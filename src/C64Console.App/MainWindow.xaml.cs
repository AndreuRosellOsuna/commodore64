using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using C64Console.Core;

namespace C64Console.App;

public partial class MainWindow : Window
{
    private const int Columns = 40;
    private const int Rows = 25;
    private const int MaxStartupScale = 3;

    private readonly C64Theme _theme = C64Theme.Default;
    private readonly ScreenBuffer _screen;
    private readonly FrameRenderer _renderer = new(Columns, Rows);
    private readonly uint[] _frame;
    private readonly WriteableBitmap _bitmap;
    private readonly Stopwatch _blinkClock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer;

    private int _renderedVersion = -1;
    private bool _renderedCursor;

    public MainWindow()
    {
        InitializeComponent();

        _screen = new ScreenBuffer(Columns, Rows, _theme.Foreground);
        BootScreen.Show(_screen);

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

        ContentRendered += OnContentRendered;
        SizeChanged += (_, _) => FitScreen();
        TextInput += OnTextInput;
        PreviewKeyDown += OnPreviewKeyDown;
        Closed += (_, _) => _timer.Stop();
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        SizeToContent = SizeToContent.Manual;
        Root.Width = double.NaN;
        Root.Height = double.NaN;
        FitScreen();
        Focus();
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
        var cursorVisible = IsActive && CursorBlink.IsVisible(_blinkClock.Elapsed);
        if (_screen.Version == _renderedVersion && cursorVisible == _renderedCursor)
        {
            return;
        }

        _renderer.Render(_screen, _theme, cursorVisible, _frame);
        _bitmap.WritePixels(new Int32Rect(0, 0, _renderer.Width, _renderer.Height), _frame, _renderer.Width * sizeof(uint), 0);
        _renderedVersion = _screen.Version;
        _renderedCursor = cursorVisible;
    }

    // Milestone 1 has no shell yet, so typing is echoed locally. Milestone 2 sends keys to ConPTY instead.
    private void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        foreach (var c in e.Text.Where(c => !char.IsControl(c)))
        {
            _screen.Write(c);
        }

        OnTyped();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                _screen.NewLine();
                break;
            case Key.Back:
                _screen.Backspace();
                break;
            default:
                return;
        }

        e.Handled = true;
        OnTyped();
    }

    private void OnTyped()
    {
        // Like the C64, show the cursor straight away after a key press.
        _blinkClock.Restart();
        RenderIfChanged();
    }

    private static Color ToColor(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}
