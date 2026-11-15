using System.Diagnostics;
using System.Runtime.InteropServices;
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
    private const int MaxStartupScale = 3;
    private const int MaxScale = 8;
    private const int MinColumns = 20;
    private const int MinRows = 5;
    private const int WheelLines = 3;

    private readonly C64Theme _theme = C64Theme.Default;
    private readonly ScreenBuffer _screen;
    private readonly VtTerminal _terminal;
    private readonly Stopwatch _blinkClock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer;

    // Shell output arrives on a background thread; it is collected here and applied on the UI thread.
    private readonly object _outputLock = new();
    private readonly StringBuilder _pendingOutput = new();
    private bool _drainQueued;

    private FrameRenderer _renderer;
    private uint[] _frame;
    private WriteableBitmap _bitmap;

    /// <summary>Screen pixels per C64 pixel, in physical (device) pixels.</summary>
    private int _scale;

    /// <summary>How many lines the view is scrolled back into the scrollback; 0 shows the live screen.</summary>
    private int _scrollOffset;

    private TextSelection? _selection;
    private bool _selecting;

    private PseudoConsoleSession? _session;
    private bool _closed;
    private bool _sizeReleased;

    // What was drawn last, to skip frames where nothing changed.
    private int _renderedVersion = -1;
    private bool _renderedCursor;
    private int _renderedScrollOffset;
    private TextSelection? _renderedSelection;

    public MainWindow()
    {
        InitializeComponent();

        var args = Environment.GetCommandLineArgs();
        var columns = CommandLine.Columns(args);
        var rows = CommandLine.Rows(args);
        _screen = new ScreenBuffer(columns, rows, _theme.Foreground);
        _terminal = new VtTerminal(_screen, _theme);
        _terminal.TitleChanged += title => Title = string.IsNullOrWhiteSpace(title) ? AppTitle : $"{AppTitle} - {title}";
        _terminal.Response += response => _session?.Write(response);

        (_renderer, _frame, _bitmap) = CreateFrame(columns, rows);
        Background = new SolidColorBrush(ToColor(C64Palette.ToArgb(_theme.Border)));

        // Open at the largest whole-number scale (up to 3×) that fits comfortably on screen.
        // OnContentRendered then releases the size so the user can resize freely.
        _scale = Math.Clamp(
            (int)Math.Min(SystemParameters.WorkArea.Width * 0.9 / _renderer.Width, SystemParameters.WorkArea.Height * 0.9 / _renderer.Height),
            1,
            MaxStartupScale);
        Root.Width = Screen.Width = _renderer.Width * _scale;
        Root.Height = Screen.Height = _renderer.Height * _scale;
        RenderIfChanged();

        // Poll often enough to catch each blink edge promptly; we only redraw when something changed.
        // This constructor overload starts the timer.
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => RenderIfChanged(), Dispatcher);

        Loaded += (_, _) => StartShell();
        ContentRendered += OnContentRendered;
        SizeChanged += (_, _) => FitScreen();
        DpiChanged += (_, _) => FitScreen();
        TextInput += OnTextInput;
        PreviewKeyDown += OnPreviewKeyDown;
        MouseWheel += OnMouseWheel;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseRightButtonUp += OnMouseRightButtonUp;
        Closed += OnClosed;
    }

    private (FrameRenderer, uint[], WriteableBitmap) CreateFrame(int columns, int rows)
    {
        var renderer = new FrameRenderer(columns, rows);
        var bitmap = new WriteableBitmap(renderer.Width, renderer.Height, 96, 96, PixelFormats.Bgra32, null);
        Screen.Source = bitmap;
        _renderedVersion = -1;
        return (renderer, renderer.CreateFrame(), bitmap);
    }

    private void StartShell()
    {
        var cmd = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        var session = new PseudoConsoleSession(
            ShellCommands.CmdWithBanner(cmd, _screen.Columns),
            _screen.Columns,
            _screen.Rows,
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

        var linesBefore = _screen.LinesScrolledOff;
        _terminal.Feed(text);

        // Keep a scrolled-back view on the same text while new output pushes lines into the scrollback.
        if (_scrollOffset > 0)
        {
            SetScrollOffset(_scrollOffset + (int)(_screen.LinesScrolledOff - linesBefore));
        }

        RenderIfChanged();
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        SizeToContent = SizeToContent.Manual;
        Root.Width = double.NaN;
        Root.Height = double.NaN;
        _sizeReleased = true;
        FitScreen();
        Focus();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _timer.Stop();
        _session?.Dispose();
    }

    /// <summary>
    /// Fits the text grid to the window: at the current pixel scale, as many columns and rows as fit,
    /// and tells the shell about the new size. The frame is drawn at a whole-number scale in physical
    /// pixels, so every C64 pixel is a crisp square; leftover space shows the border colour.
    /// </summary>
    private void FitScreen()
    {
        if (!_sizeReleased || Root.ActualWidth <= 0 || Root.ActualHeight <= 0)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var devicePixelsWide = Root.ActualWidth * dpi.DpiScaleX;
        var devicePixelsHigh = Root.ActualHeight * dpi.DpiScaleY;

        // Small epsilon: the window size round-trips through DIPs and may come back a hair short.
        var columns = Math.Max(MinColumns, (int)((devicePixelsWide / _scale + 0.01 - 2 * _renderer.BorderX) / C64Font.GlyphSize));
        var rows = Math.Max(MinRows, (int)((devicePixelsHigh / _scale + 0.01 - 2 * _renderer.BorderY) / C64Font.GlyphSize));
        if (columns != _screen.Columns || rows != _screen.Rows)
        {
            _screen.Resize(columns, rows);
            (_renderer, _frame, _bitmap) = CreateFrame(columns, rows);
            _selection = null;
            SetScrollOffset(_scrollOffset);
            try
            {
                _session?.Resize(columns, rows);
            }
            catch (Exception)
            {
                // The shell may have just exited; the window closes shortly.
            }
        }

        Screen.Width = _renderer.Width * _scale / dpi.DpiScaleX;
        Screen.Height = _renderer.Height * _scale / dpi.DpiScaleY;
        RenderIfChanged();
    }

    /// <summary>Ctrl+Plus / Ctrl+Minus: bigger or smaller pixels. The window keeps its size; the grid adapts.</summary>
    private void ChangeScale(int delta)
    {
        var scale = Math.Clamp(_scale + delta, 1, MaxScale);
        if (scale != _scale)
        {
            _scale = scale;
            FitScreen();
        }
    }

    private void RenderIfChanged()
    {
        var cursorVisible = IsActive && _terminal.CursorVisible && CursorBlink.IsVisible(_blinkClock.Elapsed);
        if (_screen.Version == _renderedVersion && cursorVisible == _renderedCursor &&
            _scrollOffset == _renderedScrollOffset && _selection == _renderedSelection)
        {
            return;
        }

        _renderer.Render(_screen, _theme, cursorVisible, _frame, _scrollOffset, _selection);
        _bitmap.WritePixels(new Int32Rect(0, 0, _renderer.Width, _renderer.Height), _frame, _renderer.Width * sizeof(uint), 0);
        _renderedVersion = _screen.Version;
        _renderedCursor = cursorVisible;
        _renderedScrollOffset = _scrollOffset;
        _renderedSelection = _selection;
    }

    private void SetScrollOffset(int offset)
    {
        _scrollOffset = _screen.IsAlternateScreen ? 0 : Math.Clamp(offset, 0, _screen.ScrollbackCount);
        RenderIfChanged();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var lines = e.Delta / 120 * WheelLines;
        if (lines == 0)
        {
            lines = Math.Sign(e.Delta);
        }

        if (_screen.IsAlternateScreen)
        {
            // Full-screen programs (less, vim) have no scrollback here: scroll them with the arrow keys instead.
            var arrow = KeyEncoder.Encode(lines > 0 ? TerminalKey.Up : TerminalKey.Down, applicationCursorKeys: _terminal.ApplicationCursorKeys);
            _session?.Write(string.Concat(Enumerable.Repeat(arrow, Math.Abs(lines))));
        }
        else
        {
            SetScrollOffset(_scrollOffset + lines);
        }

        e.Handled = true;
    }

    /// <summary>The buffer position under the mouse, taking the scrolled-back view into account.</summary>
    private BufferPosition PositionAt(MouseEventArgs e)
    {
        var point = e.GetPosition(Screen);
        var x = point.X * _renderer.Width / Math.Max(1, Screen.ActualWidth);
        var y = point.Y * _renderer.Height / Math.Max(1, Screen.ActualHeight);
        var (column, row) = _renderer.CellAt(x, y);
        return new BufferPosition(_screen.LinesScrolledOff + row - _scrollOffset, column);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var position = PositionAt(e);
        _selection = new TextSelection(position, position);
        _selecting = true;
        CaptureMouse();
        RenderIfChanged();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_selecting && _selection is { } selection)
        {
            _selection = selection with { Active = PositionAt(e) };
            RenderIfChanged();
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_selecting)
        {
            return;
        }

        _selecting = false;
        ReleaseMouseCapture();

        // A plain click (no drag) just clears the selection.
        if (_selection is { } selection && selection.Anchor == selection.Active)
        {
            _selection = null;
        }

        RenderIfChanged();
    }

    /// <summary>Right click copies the selection if there is one, otherwise pastes, like the classic Windows console.</summary>
    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!CopySelection())
        {
            Paste();
        }

        e.Handled = true;
    }

    /// <summary>Copies the selected text to the clipboard and clears the selection. Returns false when nothing is selected.</summary>
    private bool CopySelection()
    {
        if (_selection is not { } selection)
        {
            return false;
        }

        var text = _screen.GetText(selection.Start, selection.End);
        _selection = null;
        RenderIfChanged();
        try
        {
            Clipboard.SetText(text);
        }
        catch (ExternalException)
        {
            // Another program has the clipboard open; nothing sensible to do but skip.
        }

        return true;
    }

    private void Paste()
    {
        string text;
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
        }
        catch (ExternalException)
        {
            return;
        }

        if (text.Length > 0)
        {
            SendInput(KeyEncoder.EncodePaste(text, _terminal.BracketedPaste));
        }
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

        if (HandleAppShortcut(key, modifiers))
        {
            e.Handled = true;
            return;
        }

        string? sequence = null;
        if (KeyInput.ToTerminalKey(key) is { } terminalKey)
        {
            sequence = KeyEncoder.Encode(terminalKey, modifiers, _terminal.ApplicationCursorKeys);
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

    /// <summary>Copy/paste, scrollback and scale keys. Returns true when the key was used by the app.</summary>
    private bool HandleAppShortcut(Key key, KeyModifiers modifiers)
    {
        const KeyModifiers ctrlShift = KeyModifiers.Control | KeyModifiers.Shift;
        switch (key)
        {
            // Ctrl+C copies when something is selected, and is sent to the shell as an interrupt otherwise.
            case Key.C when modifiers == ctrlShift || (modifiers == KeyModifiers.Control && _selection is not null):
                CopySelection();
                return true;
            case Key.V when modifiers is KeyModifiers.Control or ctrlShift:
            case Key.Insert when modifiers == KeyModifiers.Shift:
                Paste();
                return true;
            case Key.PageUp when modifiers == KeyModifiers.Shift && !_screen.IsAlternateScreen:
                SetScrollOffset(_scrollOffset + _screen.Rows - 1);
                return true;
            case Key.PageDown when modifiers == KeyModifiers.Shift && !_screen.IsAlternateScreen:
                SetScrollOffset(_scrollOffset - (_screen.Rows - 1));
                return true;
            case Key.OemPlus or Key.Add when modifiers == KeyModifiers.Control:
                ChangeScale(1);
                return true;
            case Key.OemMinus or Key.Subtract when modifiers == KeyModifiers.Control:
                ChangeScale(-1);
                return true;
            default:
                return false;
        }
    }

    private void SendInput(string text)
    {
        _session?.Write(text);

        // Typing jumps back to the live screen and drops the selection, as in other terminals.
        _selection = null;
        _scrollOffset = 0;

        // Like the C64, show the cursor straight away after a key press.
        _blinkClock.Restart();
        RenderIfChanged();
    }

    private static Color ToColor(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}
