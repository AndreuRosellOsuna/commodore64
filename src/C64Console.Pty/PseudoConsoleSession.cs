using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;
using static C64Console.Pty.NativeMethods;

namespace C64Console.Pty;

/// <summary>
/// Runs a console program (cmd.exe, PowerShell…) attached to a Windows pseudo console (ConPTY).
/// We write keyboard input as VT sequences and receive the screen as VT output.
/// Subscribe to the events, then call <see cref="Start"/>. Events are raised on background threads.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class PseudoConsoleSession : IDisposable
{
    private readonly string _commandLine;
    private readonly string? _workingDirectory;
    private readonly object _lock = new();

    private IntPtr _pseudoConsole;
    private SafeProcessHandle? _process;
    private FileStream? _input;
    private FileStream? _output;
    private bool _disposed;

    public PseudoConsoleSession(string commandLine, int columns, int rows, string? workingDirectory = null)
    {
        _commandLine = commandLine;
        _workingDirectory = workingDirectory;
        Columns = columns;
        Rows = rows;
    }

    public int Columns { get; private set; }
    public int Rows { get; private set; }

    /// <summary>Output from the pseudo console, decoded from UTF-8. Raised on a background thread.</summary>
    public event Action<string>? OutputReceived;

    /// <summary>Raised once, on a background thread, when the shell process exits.</summary>
    public event Action? Exited;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pseudoConsole != IntPtr.Zero)
        {
            throw new InvalidOperationException("The session has already started.");
        }

        if (!CreatePipe(out var inputRead, out var inputWrite, IntPtr.Zero, 0) ||
            !CreatePipe(out var outputRead, out var outputWrite, IntPtr.Zero, 0))
        {
            throw new Win32Exception();
        }

        // ConPTY duplicates the handles it is given, so our copies of its ends can be closed straight away.
        using (inputRead)
        using (outputWrite)
        {
            var result = CreatePseudoConsole(ToCoord(Columns, Rows), inputRead, outputWrite, 0, out _pseudoConsole);
            if (result != 0)
            {
                inputWrite.Dispose();
                outputRead.Dispose();
                Marshal.ThrowExceptionForHR(result);
            }
        }

        _input = new FileStream(inputWrite, FileAccess.Write, bufferSize: 0);
        _output = new FileStream(outputRead, FileAccess.Read, bufferSize: 0);

        // Start reading before the process starts so no output is missed, and so ConPTY never blocks on a full pipe.
        new Thread(ReadOutput) { IsBackground = true, Name = "ConPTY output" }.Start();

        _process = StartProcess();
        new Thread(WaitForExit) { IsBackground = true, Name = "ConPTY process wait" }.Start();
    }

    /// <summary>Sends keyboard input (text or VT sequences) to the shell.</summary>
    public void Write(string text)
    {
        var input = _input;
        if (input is null || text.Length == 0)
        {
            return;
        }

        try
        {
            lock (_lock)
            {
                input.Write(Encoding.UTF8.GetBytes(text));
                input.Flush();
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // The shell has gone; Exited reports that.
        }
    }

    public void Resize(int columns, int rows)
    {
        if (_pseudoConsole == IntPtr.Zero || _disposed)
        {
            return;
        }

        var result = ResizePseudoConsole(_pseudoConsole, ToCoord(columns, rows));
        if (result != 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }

        Columns = columns;
        Rows = rows;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Closing the pseudo console ends the shell and makes the output pipe report end-of-file.
        // On older Windows versions this call blocks until the output has been drained, which the reader thread does.
        if (_pseudoConsole != IntPtr.Zero)
        {
            ClosePseudoConsole(_pseudoConsole);
            _pseudoConsole = IntPtr.Zero;
        }

        lock (_lock)
        {
            _input?.Dispose();
        }

        _process?.Dispose();
    }

    private SafeProcessHandle StartProcess()
    {
        var size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size); // asks for the required size
        var attributeList = Marshal.AllocHGlobal(size);
        try
        {
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref size))
            {
                throw new Win32Exception();
            }

            try
            {
                if (!UpdateProcThreadAttribute(attributeList, 0, ProcThreadAttributePseudoConsole, _pseudoConsole, IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                {
                    throw new Win32Exception();
                }

                var startupInfo = new StartupInfoEx { AttributeList = attributeList };
                startupInfo.StartupInfo.Cb = Marshal.SizeOf<StartupInfoEx>();

                // Pass empty std handles explicitly, so the shell talks to the pseudo console and not to whatever
                // handles this process happens to have.
                startupInfo.StartupInfo.Flags = StartfUseStdHandles;

                var commandLine = new StringBuilder(_commandLine); // CreateProcessW may modify this buffer
                if (!CreateProcessW(
                        null,
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                        IntPtr.Zero,
                        _workingDirectory,
                        ref startupInfo,
                        out var processInfo))
                {
                    throw new Win32Exception();
                }

                CloseHandle(processInfo.Thread);
                return new SafeProcessHandle(processInfo.Process, ownsHandle: true);
            }
            finally
            {
                DeleteProcThreadAttributeList(attributeList);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(attributeList);
        }
    }

    private void ReadOutput()
    {
        var output = _output!;
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[16 * 1024];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];

        try
        {
            int read;
            while ((read = output.Read(bytes, 0, bytes.Length)) > 0)
            {
                // The decoder keeps any incomplete UTF-8 sequence for the next read.
                var count = decoder.GetChars(bytes, 0, read, chars, 0);
                if (count > 0)
                {
                    OutputReceived?.Invoke(new string(chars, 0, count));
                }
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // Pipe closed.
        }
        finally
        {
            output.Dispose();
        }
    }

    private void WaitForExit()
    {
        WaitForSingleObject(_process!, Infinite);
        Exited?.Invoke();
    }

    private static Coord ToCoord(int columns, int rows) =>
        new((short)Math.Clamp(columns, 1, short.MaxValue), (short)Math.Clamp(rows, 1, short.MaxValue));
}
