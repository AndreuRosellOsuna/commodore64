using System.Diagnostics;
using System.IO;
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
    private const int WheelLines = 3;

    private readonly ScreenBuffer _screen;
    private readonly VtTerminal _terminal;
    private readonly Stopwatch _blinkClock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer;
    private readonly SoundEffects _sounds = new();

    /// <summary>The current settings. Changes made in the app are saved straight away.</summary>
    private AppSettings _settings;

    /// <summary>The settings screen while it is open; it is drawn instead of the terminal.</summary>
    private SettingsMenu? _menu;

    /// <summary>Shell and prompt when the settings screen opened: if either changed, the shell restarts on close.</summary>
    private (ShellKind Shell, PromptStyle Prompt) _menuShellAtOpen;

    // Shell output arrives on a background thread; it is collected here and applied on the UI thread.
    private readonly object _outputLock = new();
    private readonly StringBuilder _pendingOutput = new();
    private bool _drainQueued;

    private FrameRenderer _renderer;
    private uint[] _frame;
    private WriteableBitmap _bitmap;

    // With the CRT effect on, the frame is scaled up and filtered on the CPU into _crtOutput,
    // and the bitmap is shown at one bitmap pixel per screen pixel.
    private CrtFilter? _crt;
    private uint[]? _crtOutput;

    /// <summary>Screen pixels per C64 pixel, in physical (device) pixels.</summary>
    private int _scale;


    // Window settings to restore when leaving fullscreen.
    private bool _fullscreen;
    private WindowStyle _windowedStyle;
    private ResizeMode _windowedResizeMode;
    private WindowState _windowedState;

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

    private C64Theme Theme => _terminal.Theme;

    private Layout CurrentLayout => _settings.Layout;

    private bool CrtEnabled => _settings.Crt;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load(AppSettings.DefaultPath).WithCommandLine(Environment.GetCommandLineArgs());
        var (columns, rows) = _settings.StartGrid;
        var theme = C64Theme.Default with { Foreground = _settings.TextColour };
        _screen = new ScreenBuffer(columns, rows, theme.Foreground);
        _terminal = new VtTerminal(_screen, theme);
        _terminal.TitleChanged += title => Title = string.IsNullOrWhiteSpace(title) ? AppTitle : $"{AppTitle} - {title}";
        _terminal.Response += response => _session?.Write(response);

        // Open at the largest whole-number scale (up to 3×) that fits comfortably on screen.
        // OnContentRendered then releases the size so the user can resize freely.
        // Sizes are worked out in physical pixels and converted to WPF's device-independent units,
        // so the grid that FitScreen computes later matches the one we start with on high-DPI screens.
        var dpi = VisualTreeHelper.GetDpi(this);
        var startup = new FrameRenderer(columns, rows);
        _scale = Math.Clamp(
            (int)Math.Min(
                SystemParameters.WorkArea.Width * dpi.DpiScaleX * 0.9 / startup.Width,
                SystemParameters.WorkArea.Height * dpi.DpiScaleY * 0.9 / startup.Height),
            1,
            MaxStartupScale);
        (_renderer, _frame, _bitmap) = CreateFrame(columns, rows);
        UpdateWindowBackground();
        Root.Width = Screen.Width = _renderer.Width * _scale / dpi.DpiScaleX;
        Root.Height = Screen.Height = _renderer.Height * _scale / dpi.DpiScaleY;
        RenderIfChanged();

        // Poll often enough to catch each blink edge promptly; we only redraw when something changed.
        // This constructor overload starts the timer.
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => RenderIfChanged(), Dispatcher);

        Loaded += (_, _) =>
        {
            if (_settings.StartupSound)
            {
                _sounds.Chirp();
            }

            StartShell();
        };
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

    /// <summary>Creates the renderer and bitmap for a grid size at the current scale and CRT setting.</summary>
    private (FrameRenderer, uint[], WriteableBitmap) CreateFrame(int columns, int rows)
    {
        var renderer = new FrameRenderer(columns, rows);
        if (CrtEnabled)
        {
            _crt = new CrtFilter(renderer.Width, renderer.Height, _scale);
            _crtOutput = _crt.CreateOutput();
        }
        else
        {
            _crt = null;
            _crtOutput = null;
        }

        var bitmap = _crt is { } crt
            ? new WriteableBitmap(crt.Width, crt.Height, 96, 96, PixelFormats.Bgra32, null)
            : new WriteableBitmap(renderer.Width, renderer.Height, 96, 96, PixelFormats.Bgra32, null);
        Screen.Source = bitmap;
        _renderedVersion = -1;
        return (renderer, renderer.CreateFrame(), bitmap);
    }

    /// <summary>The space around the picture: the border colour, or black like a TV's bezel with the CRT effect.</summary>
    private void UpdateWindowBackground() =>
        Background = CrtEnabled ? Brushes.Black : new SolidColorBrush(ToColor(C64Palette.ToArgb(Theme.Border)));

    private void StartShell()
    {
        var session = new PseudoConsoleSession(
            ShellCommands.For(_settings.Shell, _screen.Columns, _settings.Prompt, Environment.GetEnvironmentVariable("ComSpec")),
            _screen.Columns,
            _screen.Rows,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        // Events from a session that has since been replaced (after a shell change) are ignored.
        session.OutputReceived += text =>
        {
            if (ReferenceEquals(Volatile.Read(ref _session), session))
            {
                OnShellOutput(text);
            }
        };

        // When the user types "exit", close the window, like other terminals do.
        session.Exited += () => Dispatcher.BeginInvoke(() =>
        {
            if (!_closed && ReferenceEquals(_session, session))
            {
                Close();
            }
        });

        Volatile.Write(ref _session, session);
        try
        {
            session.Start();
        }
        catch (Exception e)
        {
            Volatile.Write(ref _session, null);
            session.Dispose();
            ShowError("SHELL", $"{ShellCommands.DisplayName(_settings.Shell)}: {e.Message}");
        }
    }

    /// <summary>Ends the current shell and starts the chosen one on a fresh screen.</summary>
    private void RestartShell()
    {
        var old = Interlocked.Exchange(ref _session, null);
        old?.Dispose();
        lock (_outputLock)
        {
            _pendingOutput.Clear();
        }

        _terminal.Feed("\x1B" + "c"); // full reset: colours, modes, alternate screen
        _screen.ClearScrollback();
        _scrollOffset = 0;
        _selection = null;
        StartShell();
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
        _sounds.Dispose();
    }

    /// <summary>
    /// Fits the picture to the window. In the modern layout the grid grows or shrinks at the current pixel scale;
    /// in the authentic layout the grid stays 40×25 and the scale grows. The shell is told about grid changes.
    /// The frame is drawn at a whole-number scale in physical pixels, so every C64 pixel is a crisp square;
    /// leftover space shows the border colour.
    /// </summary>
    private void FitScreen(bool rebuild = false)
    {
        if (!_sizeReleased || Root.ActualWidth <= 0 || Root.ActualHeight <= 0)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var (columns, rows, scale) = ScreenLayout.Fit(
            CurrentLayout, Root.ActualWidth * dpi.DpiScaleX, Root.ActualHeight * dpi.DpiScaleY, _scale, _renderer.BorderX, _renderer.BorderY);

        var gridChanged = columns != _screen.Columns || rows != _screen.Rows;
        if (gridChanged)
        {
            _screen.Resize(columns, rows);
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

        // The CRT bitmap is drawn at the final size, so it also has to be rebuilt when the scale changes.
        if (gridChanged || rebuild || (scale != _scale && CrtEnabled))
        {
            _scale = scale;
            (_renderer, _frame, _bitmap) = CreateFrame(columns, rows);
        }

        _scale = scale;
        Screen.Width = _renderer.Width * _scale / dpi.DpiScaleX;
        Screen.Height = _renderer.Height * _scale / dpi.DpiScaleY;
        RenderIfChanged();
    }

    /// <summary>Ctrl+Plus / Ctrl+Minus (modern layout): bigger or smaller pixels. The window keeps its size; the grid adapts.</summary>
    private void ChangeScale(int delta)
    {
        var scale = Math.Clamp(_scale + delta, 1, MaxScale);
        if (scale != _scale && CurrentLayout == Layout.Modern)
        {
            _scale = scale;
            FitScreen(rebuild: true);
        }
    }

    /// <summary>Ctrl+Shift+L: switches between the fixed 40×25 screen and the resizable grid.</summary>
    private void ToggleLayout() =>
        ApplySettings(_settings with { Layout = CurrentLayout == Layout.Modern ? Layout.Authentic : Layout.Modern });

    /// <summary>Ctrl+Shift+R: turns the CRT effect on or off.</summary>
    private void ToggleCrt() => ApplySettings(_settings with { Crt = !_settings.Crt });

    /// <summary>Alt+Enter: borderless fullscreen and back.</summary>
    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            (_windowedStyle, _windowedResizeMode, _windowedState) = (WindowStyle, ResizeMode, WindowState);

            // WPF only covers the taskbar if the window style is None before it is maximised.
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = _windowedStyle;
            ResizeMode = _windowedResizeMode;
            WindowState = _windowedState;
        }

        _fullscreen = !_fullscreen;
    }

    /// <summary>The C64 colour keys: change the colour of text typed and printed from now on.</summary>
    private void SetTextColour(byte colour) => ApplySettings(_settings with { TextColour = colour });

    /// <summary>
    /// Makes new settings take effect and saves them. Layout, CRT and colour change at once;
    /// a different shell or prompt only takes effect when <paramref name="restartShell"/> is set.
    /// </summary>
    private void ApplySettings(AppSettings settings, bool restartShell = false)
    {
        var old = _settings;
        _settings = settings;

        if (settings.TextColour != old.TextColour)
        {
            _terminal.Theme = Theme with { Foreground = settings.TextColour };
        }

        if (settings.Crt != old.Crt)
        {
            UpdateWindowBackground();
        }

        if (settings.Layout != old.Layout || settings.Crt != old.Crt)
        {
            FitScreen(rebuild: true);
        }

        if (restartShell)
        {
            RestartShell();
        }

        try
        {
            settings.Save(AppSettings.DefaultPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not being able to save is no reason to stop; the settings still apply to this window.
        }

        _renderedVersion = -1;
        RenderIfChanged();
    }

    /// <summary>Ctrl+Shift+S: opens the settings screen.</summary>
    private void OpenMenu()
    {
        _menu = new SettingsMenu(_settings, typeof(MainWindow).Assembly.GetName().Version?.ToString(3));
        _menuShellAtOpen = (_settings.Shell, _settings.Prompt);
        _selection = null;
        _renderedVersion = -1;
        RenderIfChanged();
    }

    /// <summary>Keys while the settings screen is open. Every change applies at once; shell changes apply on close.</summary>
    private void HandleMenuKey(Key key)
    {
        var menu = _menu!;
        var result = key == Key.Space
            ? menu.HandleSpace()
            : KeyInput.ToTerminalKey(key) is { } terminalKey ? menu.HandleKey(terminalKey) : SettingsMenu.Result.None;

        switch (result)
        {
            case SettingsMenu.Result.Changed:
                ApplySettings(menu.Settings);
                break;
            case SettingsMenu.Result.Closed:
                _menu = null;
                var shellChanged = (menu.Settings.Shell, menu.Settings.Prompt) != _menuShellAtOpen;
                ApplySettings(menu.Settings, restartShell: shellChanged);
                break;
        }

        if (_settings.KeyClicks && result != SettingsMenu.Result.None)
        {
            _sounds.Click();
        }

        _renderedVersion = -1;
        RenderIfChanged();
    }

    private void RenderIfChanged()
    {
        var cursorVisible = _menu is null && IsActive && _terminal.CursorVisible && CursorBlink.IsVisible(_blinkClock.Elapsed);
        if (_screen.Version == _renderedVersion && cursorVisible == _renderedCursor &&
            _scrollOffset == _renderedScrollOffset && _selection == _renderedSelection)
        {
            return;
        }

        if (_menu is { } menu)
        {
            // The settings screen is drawn on its own buffer the size of the terminal, which stays untouched.
            var menuScreen = new ScreenBuffer(_screen.Columns, _screen.Rows, Theme.Foreground, scrollbackLimit: 0);
            menu.Draw(menuScreen, Theme);
            _renderer.Render(menuScreen, Theme, cursorVisible: false, _frame);
        }
        else
        {
            _renderer.Render(_screen, Theme, cursorVisible, _frame, _scrollOffset, _selection);
        }

        if (_crt is { } crt && _crtOutput is { } output)
        {
            crt.Apply(_frame, output);
            _bitmap.WritePixels(new Int32Rect(0, 0, crt.Width, crt.Height), output, crt.Width * sizeof(uint), 0);
        }
        else
        {
            _bitmap.WritePixels(new Int32Rect(0, 0, _renderer.Width, _renderer.Height), _frame, _renderer.Width * sizeof(uint), 0);
        }

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
        if (_menu is not null)
        {
            return;
        }

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

        // With the CRT effect the picture is curved: look up which frame pixel is shown under the mouse.
        if (_crt is { } crt && crt.SourcePixelAt((int)(x * crt.Scale), (int)(y * crt.Scale)) is { } source)
        {
            (x, y) = (source.X + 0.5, source.Y + 0.5);
        }

        var (column, row) = _renderer.CellAt(x, y);
        return new BufferPosition(_screen.LinesScrolledOff + row - _scrollOffset, column);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_menu is not null)
        {
            return;
        }

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
        if (_menu is not null)
        {
            return;
        }

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
        if (text.Length > 0 && _menu is null)
        {
            SendInput(text, click: true);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = KeyInput.Modifiers(Keyboard.Modifiers);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (_menu is not null)
        {
            // Alt+Enter still toggles fullscreen and Alt+F4 still closes; every other key belongs to the menu.
            if (!(modifiers == KeyModifiers.Alt && key is Key.Enter or Key.F4))
            {
                e.Handled = true;
                HandleMenuKey(key);
                return;
            }
        }

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
        SendInput(sequence, click: true);
    }

    /// <summary>Copy/paste, scrollback and scale keys. Returns true when the key was used by the app.</summary>
    private bool HandleAppShortcut(Key key, KeyModifiers modifiers)
    {
        const KeyModifiers ctrlShift = KeyModifiers.Control | KeyModifiers.Shift;
        if (key is >= Key.F1 and <= Key.F8 && modifiers is KeyModifiers.Control or ctrlShift &&
            C64ColorKeys.ColorFor(key - Key.F1 + 1, commodoreKey: modifiers == ctrlShift) is { } colour)
        {
            SetTextColour(colour);
            return true;
        }

        switch (key)
        {
            case Key.Enter when modifiers == KeyModifiers.Alt:
                ToggleFullscreen();
                return true;
            case Key.L when modifiers == ctrlShift:
                ToggleLayout();
                return true;
            case Key.R when modifiers == ctrlShift:
                ToggleCrt();
                return true;
            case Key.S when modifiers == ctrlShift:
                OpenMenu();
                return true;
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

    private void SendInput(string text, bool click = false)
    {
        _session?.Write(text);
        if (click && _settings.KeyClicks)
        {
            _sounds.Click();
        }

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
