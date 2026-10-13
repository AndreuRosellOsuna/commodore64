using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using C64Console.Core;
using C64Console.Pty;

namespace C64Console.App;

public partial class MainWindow : Window
{
    private const string AppTitle = "C64 Console";
    private const int Rows = 25;
    private const int MaxStartupScale = 3;

    private readonly C64Theme _theme = C64Theme.Default;
    private readonly int _columns;
    private readonly ScreenBuffer _screen;
    private readonly VtTerminal _terminal;
    private readonly FrameRenderer _renderer;
    private readonly uint[] _frame;
    private readonly WriteableBitmap _bitmap;
    private readonly Stopwatch _blinkClock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer;

    // Shell output arrives on a background thread; it is collected here and applied on the UI thread.
    private readonly object _outputLock = new();
    private readonly StringBuilder _pendingOutput = new();
    private bool _drainQueued;

    private PseudoConsoleSession? _session;
    private bool _closed;
    private int _renderedVersion = -1;
    private bool _renderedCursor;

    public MainWindow()
    {
        InitializeComponent();

        _columns = CommandLine.Columns(Environment.GetCommandLineArgs());
        _screen = new ScreenBuffer(_columns, Rows, _theme.Foreground);
        _terminal = new VtTerminal(_screen, _theme);
        _terminal.TitleChanged += title => Title = string.IsNullOrWhiteSpace(title) ? AppTitle : $"{AppTitle} - {title}";
        _terminal.Response += response => _session?.Write(response);

        _renderer = new FrameRenderer(_columns, Rows);
        _frame = _renderer.CreateFrame();
        _bitmap = new WriteableBitmap(_renderer.Width, _renderer.Height, 96, 96, PixelFormats.Bgra32, null);
        Screen.Source = _bitmap;
        Background = new SolidColorBrush(ToColor(C64Palette.ToArgb(_theme.Border)));

        // Open at the largest whole-number scale (up to 3×) that fits comfortably on screen.
        // OnContentRendered then releases the size so the user can resize freely.
        var scale = Math.Clamp(
            (int)Math.Min(SystemParameters.WorkArea.Width * 0.9 / _renderer.Width, SystemParameters.WorkArea.Height * 0.9 / _renderer.Height),
            1,
            MaxStartupScale);
        Root.Width = Screen.Width = _renderer.Width * scale;
        Root.Height = Screen.Height = _renderer.Height * scale;
        RenderIfChanged();

        // Poll often enough to catch each blink edge promptly; we only redraw when something changed.
        // This constructor overload starts the timer.
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => RenderIfChanged(), Dispatcher);

        Loaded += (_, _) => StartShell();
        ContentRendered += OnContentRendered;
        SizeChanged += (_, _) => FitScreen();
        TextInput += OnTextInput;
        PreviewKeyDown += OnPreviewKeyDown;
        Closed += OnClosed;
    }

    private void StartShell()
    {
        var cmd = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        var session = new PseudoConsoleSession(
            ShellCommands.CmdWithBanner(cmd, _columns),
            _columns,
            Rows,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        session.OutputReceived += OnShellOutput;
        // When the user types "exit", close the window, like other terminals do.
        session.Exited += () => Dispatcher.BeginInvoke(() =>
        {
            if (!_closed)
            {
                Close();
            }
        });

        try
        {
            session.Start();
            _session = session;
        }
        catch (Exception e)
        {
            session.Dispose();
            ShowError("SHELL", e.Message);
        }
    }

    /// <summary>Shows a failure the C64 way, e.g. <c>?SHELL  ERROR</c>, followed by the details.</summary>
    private void ShowError(string what, string details)
    {
        _screen.NewLine();
        _screen.WriteLine($"?{what.ToUpperInvariant()}  ERROR");
        _screen.WriteLine(details);
        _screen.WriteLine(BootScreen.Ready);
        RenderIfChanged();
    }

    private void OnShellOutput(string text)
    {
        lock (_outputLock)
        {
            _pendingOutput.Append(text);
            if (_drainQueued)
            {
                return;
            }

            _drainQueued = true;
        }

        // Background priority lets key presses and repaints go first even when the shell prints a lot.
        Dispatcher.BeginInvoke(DrainShellOutput, DispatcherPriority.Background);
    }

    private void DrainShellOutput()
    {
        string text;
        lock (_outputLock)
        {
            text = _pendingOutput.ToString();
            _pendingOutput.Clear();
            _drainQueued = false;
        }

        _terminal.Feed(text);
        RenderIfChanged();
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        SizeToContent = SizeToContent.Manual;
        Root.Width = double.NaN;
        Root.Height = double.NaN;
        FitScreen();
        Focus();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _timer.Stop();
        _session?.Dispose();
    }

    /// <summary>Scales the frame by the largest whole number that fits, in physical pixels, so every C64 pixel is a crisp square.</summary>
    private void FitScreen()
    {
        if (Root.ActualWidth <= 0 || Root.ActualHeight <= 0)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var devicePixelsWide = Root.ActualWidth * dpi.DpiScaleX;
        var devicePixelsHigh = Root.ActualHeight * dpi.DpiScaleY;
        var scale = Math.Max(1, (int)Math.Min(devicePixelsWide / _renderer.Width, devicePixelsHigh / _renderer.Height));

        Screen.Width = _renderer.Width * scale / dpi.DpiScaleX;
        Screen.Height = _renderer.Height * scale / dpi.DpiScaleY;
    }

    private void RenderIfChanged()
    {
        var cursorVisible = IsActive && _terminal.CursorVisible && CursorBlink.IsVisible(_blinkClock.Elapsed);
        if (_screen.Version == _renderedVersion && cursorVisible == _renderedCursor)
        {
            return;
        }

        _renderer.Render(_screen, _theme, cursorVisible, _frame);
        _bitmap.WritePixels(new Int32Rect(0, 0, _renderer.Width, _renderer.Height), _frame, _renderer.Width * sizeof(uint), 0);
        _renderedVersion = _screen.Version;
        _renderedCursor = cursorVisible;
    }

    private void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        var text = new string(e.Text.Where(c => !char.IsControl(c)).ToArray());
        if (text.Length > 0)
        {
            SendInput(text);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = KeyInput.Modifiers(Keyboard.Modifiers);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        string? sequence = null;
        if (KeyInput.ToTerminalKey(key) is { } terminalKey)
        {
            sequence = KeyEncoder.Encode(terminalKey, modifiers);
        }
        else if (modifiers == KeyModifiers.Control && key is >= Key.A and <= Key.Z)
        {
            sequence = KeyEncoder.EncodeControlLetter((char)('A' + (key - Key.A)));
        }

        // Leave Alt+F4 and other Alt shortcuts to Windows; everything else we recognise goes to the shell.
        if (sequence is null || (modifiers.HasFlag(KeyModifiers.Alt) && key == Key.F4))
        {
            return;
        }

        e.Handled = true;
        SendInput(sequence);
    }

    private void SendInput(string text)
    {
        _session?.Write(text);

        // Like the C64, show the cursor straight away after a key press.
        _blinkClock.Restart();
        RenderIfChanged();
    }

    private static Color ToColor(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}
