using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace C64Console.App;

/// <summary>
/// Catches any error the app doesn't handle itself, so a failure never just makes the window vanish:
/// the details are written to <c>%LOCALAPPDATA%\C64Console\crash.log</c> and shown in a message box.
/// </summary>
public partial class App : Application
{
    private int _reported;

    public static string CrashLogPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "C64Console", "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Report(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => Report(args.Exception);
        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Report(e.Exception);
        e.Handled = true;
        Shutdown(1);
    }

    private void Report(Exception? exception)
    {
        // Several handlers can fire for the same failure; one report is enough.
        if (Interlocked.Exchange(ref _reported, 1) != 0)
        {
            return;
        }

        var details = new StringBuilder()
            .AppendLine($"C64 Console {typeof(App).Assembly.GetName().Version?.ToString(3)} crashed at {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"Windows {Environment.OSVersion.Version}, {(Environment.Is64BitProcess ? "64" : "32")}-bit, .NET {Environment.Version}")
            .AppendLine($"Command line: {Environment.CommandLine}")
            .AppendLine()
            .AppendLine(exception?.ToString() ?? "Unknown error")
            .ToString();

        var logNote = "";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.WriteAllText(CrashLogPath, details);
            logNote = $"\n\nThe details were saved to:\n{CrashLogPath}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Still show the message box below.
        }

        MessageBox.Show(
            $"?CRASH  ERROR\n\n{exception?.GetType().Name}: {exception?.Message}{logNote}",
            "C64 Console",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
