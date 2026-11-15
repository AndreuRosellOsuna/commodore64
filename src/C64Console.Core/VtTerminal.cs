using System.Globalization;
using System.Text;

namespace C64Console.Core;

/// <summary>
/// Parses VT100/xterm output (as produced by Windows ConPTY) and applies it to a <see cref="ScreenBuffer"/>.
/// It covers what ConPTY and full-screen programs need: printing, cursor movement, erasing, inserting/deleting,
/// scroll regions, the alternate screen, SGR colours (16, 256 and 24-bit, mapped to the C64 palette),
/// cursor show/hide, application cursor keys, bracketed paste, the window title and cursor position reports.
/// Anything else is parsed and ignored. Sequences may be split across <see cref="Feed"/> calls.
/// </summary>
public sealed class VtTerminal
{
    private const int MaxParams = 32;
    private const int MaxParamValue = 65535;
    private const int MaxOscLength = 4096;
    private const int TabWidth = 8;

    private readonly C64Theme _theme;
    private readonly List<int> _params = new(MaxParams);
    private readonly StringBuilder _osc = new();

    private State _state = State.Ground;
    private int _currentParam = -1; // -1 = no digits yet for the current parameter
    private char _privateMarker;
    private bool _hasIntermediate;

    private bool _bold;
    private int? _ansiForeground; // 0-15 = basic ANSI colour (bold may brighten it), null = default colour
    private byte? _fixedForeground; // a colour from 256-colour or 24-bit SGR, already mapped to the C64 palette
    private SavedCursor? _savedCursor;

    public VtTerminal(ScreenBuffer screen, C64Theme theme)
    {
        Screen = screen;
        _theme = theme;
        screen.Foreground = theme.Foreground;
    }

    private enum State
    {
        Ground,
        Escape,
        EscapeIntermediate,
        Csi,
        Osc,
        OscEscape,
        IgnoredString,
        IgnoredStringEscape,
    }

    public ScreenBuffer Screen { get; }

    /// <summary>Set by DECTCEM (<c>ESC [ ? 25 h/l</c>).</summary>
    public bool CursorVisible { get; private set; } = true;

    /// <summary>DECCKM (<c>ESC [ ? 1 h</c>): arrow keys send <c>ESC O A</c> instead of <c>ESC [ A</c>.</summary>
    public bool ApplicationCursorKeys { get; private set; }

    /// <summary>Set by <c>ESC [ ? 2004 h</c>: pasted text must be wrapped in <c>ESC [ 200 ~</c> … <c>ESC [ 201 ~</c>.</summary>
    public bool BracketedPaste { get; private set; }

    /// <summary>DECOM (<c>ESC [ ? 6 h</c>): cursor positions are relative to the scroll region.</summary>
    public bool OriginMode { get; private set; }

    public string? Title { get; private set; }

    /// <summary>Raised when the window title changes (OSC 0 or 2).</summary>
    public event Action<string>? TitleChanged;

    /// <summary>Raised with bytes the terminal must send back to the program, e.g. a cursor position report.</summary>
    public event Action<string>? Response;

    public void Feed(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            Feed(c);
        }
    }

    private void Feed(char c)
    {
        // CAN and SUB abort any sequence; ESC starts a new one (except inside strings, where it may begin ST).
        if (c is '\x18' or '\x1A')
        {
            _state = State.Ground;
            return;
        }

        switch (_state)
        {
            case State.Ground:
                Ground(c);
                break;
            case State.Escape:
                Escape(c);
                break;
            case State.EscapeIntermediate:
                if (c == '\x1B')
                {
                    _state = State.Escape;
                }
                else if (c >= '\x30')
                {
                    _state = State.Ground; // e.g. ESC ( B: character set selection, ignored
                }

                break;
            case State.Csi:
                Csi(c);
                break;
            case State.Osc:
                if (c == '\x07')
                {
                    DispatchOsc();
                }
                else if (c == '\x1B')
                {
                    _state = State.OscEscape;
                }
                else if (_osc.Length < MaxOscLength)
                {
                    _osc.Append(c);
                }

                break;
            case State.OscEscape:
                if (c == '\\')
                {
                    DispatchOsc();
                }
                else
                {
                    EnterEscape();
                    Escape(c);
                }

                break;
            case State.IgnoredString:
                if (c == '\x1B')
                {
                    _state = State.IgnoredStringEscape;
                }
                else if (c == '\x07')
                {
                    _state = State.Ground;
                }

                break;
            case State.IgnoredStringEscape:
                _state = c == '\\' ? State.Ground : State.IgnoredString;
                break;
        }
    }

    private void Ground(char c)
    {
        if (c == '\x1B')
        {
            EnterEscape();
        }
        else if (c < ' ')
        {
            Execute(c);
        }
        else if (c is '\x7F' or (>= '\x80' and <= '\x9F') || char.IsLowSurrogate(c))
        {
            // DEL and C1 controls draw nothing; a low surrogate belongs to the high surrogate already drawn.
        }
        else
        {
            Screen.Write(c);
        }
    }

    private void Execute(char c)
    {
        switch (c)
        {
            case '\b':
                Screen.MoveCursorBy(-1, 0);
                break;
            case '\t':
                Screen.MoveCursorTo((Screen.CursorColumn / TabWidth + 1) * TabWidth, Screen.CursorRow);
                break;
            case '\n' or '\v' or '\f':
                Screen.LineFeed();
                break;
            case '\r':
                Screen.CarriageReturn();
                break;
        }
    }

    private void EnterEscape()
    {
        _state = State.Escape;
        _hasIntermediate = false;
    }

    private void Escape(char c)
    {
        _state = State.Ground;
        switch (c)
        {
            case '[':
                _state = State.Csi;
                _params.Clear();
                _currentParam = -1;
                _privateMarker = '\0';
                _hasIntermediate = false;
                break;
            case ']':
                _state = State.Osc;
                _osc.Clear();
                break;
            case 'P' or 'X' or '^' or '_':
                _state = State.IgnoredString; // DCS, SOS, PM, APC
                break;
            case '7':
                SaveCursor();
                break;
            case '8':
                RestoreCursor();
                break;
            case 'D':
                Screen.LineFeed();
                break;
            case 'E':
                Screen.NewLine();
                break;
            case 'M':
                Screen.ReverseIndex();
                break;
            case 'c':
                Reset();
                break;
            case '\x1B':
                EnterEscape();
                break;
            case >= ' ' and <= '/':
                _state = State.EscapeIntermediate;
                break;
            case < ' ':
                Execute(c);
                _state = State.Escape;
                break;
        }
    }

    private void Csi(char c)
    {
        switch (c)
        {
            case >= '0' and <= '9':
                _currentParam = Math.Min((_currentParam < 0 ? 0 : _currentParam) * 10 + (c - '0'), MaxParamValue);
                break;
            case ';' or ':':
                PushParam();
                break;
            case >= '<' and <= '?':
                if (_params.Count == 0 && _currentParam < 0)
                {
                    _privateMarker = c;
                }

                break;
            case >= ' ' and <= '/':
                _hasIntermediate = true;
                break;
            case >= '@' and <= '~':
                PushParam();
                _state = State.Ground;
                if (!_hasIntermediate)
                {
                    DispatchCsi(c);
                }

                break;
            case '\x1B':
                EnterEscape();
                break;
            case < ' ':
                Execute(c);
                break;
        }
    }

    private void PushParam()
    {
        if (_params.Count < MaxParams)
        {
            _params.Add(_currentParam);
        }

        _currentParam = -1;
    }

    /// <summary>Parameter <paramref name="index"/>, or <paramref name="fallback"/> when missing or zero.</summary>
    private int Param(int index, int fallback = 1) =>
        index < _params.Count && _params[index] > 0 ? _params[index] : fallback;

    private void DispatchCsi(char final)
    {
        if (_privateMarker == '?')
        {
            if (final is 'h' or 'l')
            {
                foreach (var mode in _params)
                {
                    SetPrivateMode(mode, final == 'h');
                }
            }

            return;
        }

        if (_privateMarker != '\0')
        {
            return;
        }

        var screen = Screen;
        switch (final)
        {
            case '@': screen.InsertCharacters(Param(0)); break;
            case 'A': MoveCursorVertically(-Param(0)); break;
            case 'B': MoveCursorVertically(Param(0)); break;
            case 'C': screen.MoveCursorBy(Param(0), 0); break;
            case 'D': screen.MoveCursorBy(-Param(0), 0); break;
            case 'E': MoveCursorVertically(Param(0)); screen.CarriageReturn(); break;
            case 'F': MoveCursorVertically(-Param(0)); screen.CarriageReturn(); break;
            case 'G' or '`': screen.MoveCursorTo(Param(0) - 1, screen.CursorRow); break;
            case 'H' or 'f': SetCursorPosition(Param(1) - 1, Param(0) - 1); break;
            case 'J' when Param(0, 0) == 3: screen.ClearScrollback(); break;
            case 'J': screen.EraseInDisplay(EraseModeParam()); break;
            case 'K': screen.EraseInLine(EraseModeParam()); break;
            case 'L': screen.InsertLines(Param(0)); break;
            case 'M': screen.DeleteLines(Param(0)); break;
            case 'P': screen.DeleteCharacters(Param(0)); break;
            case 'S': screen.ScrollUp(Param(0)); break;
            case 'T': screen.ScrollDown(Param(0)); break;
            case 'X': screen.EraseCharacters(Param(0)); break;
            case 'd': SetCursorPosition(screen.CursorColumn, Param(0) - 1); break;
            case 'r': SetScrollRegion(); break;
            case 'm': SelectGraphicRendition(); break;
            case 'n': DeviceStatusReport(); break;
            case 'c': Response?.Invoke("\x1B[?1;0c"); break; // primary device attributes: a plain VT100
            case 's': SaveCursor(); break;
            case 'u': RestoreCursor(); break;
        }
    }

    private EraseMode EraseModeParam() => Param(0, 0) switch
    {
        1 => EraseMode.ToStart,
        2 => EraseMode.All,
        _ => EraseMode.ToEnd,
    };

    private void DeviceStatusReport()
    {
        switch (Param(0, 0))
        {
            case 5:
                Response?.Invoke("\x1B[0n");
                break;
            case 6:
                Response?.Invoke(string.Create(CultureInfo.InvariantCulture, $"\x1B[{Screen.CursorRow + 1};{Screen.CursorColumn + 1}R"));
                break;
        }
    }

    private void SetPrivateMode(int mode, bool on)
    {
        switch (mode)
        {
            case 1:
                ApplicationCursorKeys = on;
                break;
            case 6:
                OriginMode = on;
                SetCursorPosition(0, 0);
                break;
            case 7:
                Screen.AutoWrap = on;
                break;
            case 25:
                CursorVisible = on;
                Screen.MoveCursorBy(0, 0); // bump the version so the change is drawn
                break;
            case 47 or 1047:
                Screen.UseAlternateScreen(on);
                break;
            case 1049:
                // Save the cursor, switch to a blank alternate screen; on exit switch back and restore the cursor.
                if (on)
                {
                    if (!Screen.IsAlternateScreen)
                    {
                        SaveCursor();
                        Screen.UseAlternateScreen(true);
                    }
                }
                else if (Screen.IsAlternateScreen)
                {
                    Screen.UseAlternateScreen(false);
                    RestoreCursor();
                }

                break;
            case 2004:
                BracketedPaste = on;
                break;
        }
    }

    /// <summary>CUP/VPA: moves to a 0-based position, relative to the scroll region in origin mode.</summary>
    private void SetCursorPosition(int column, int row)
    {
        if (OriginMode)
        {
            Screen.MoveCursorTo(column, Math.Min(Screen.ScrollTop + row, Screen.ScrollBottom));
        }
        else
        {
            Screen.MoveCursorTo(column, row);
        }
    }

    /// <summary>CUU/CUD: like a VT100, stops at the scroll margins when starting inside the region.</summary>
    private void MoveCursorVertically(int rows)
    {
        var screen = Screen;
        var row = screen.CursorRow + rows;
        if (rows < 0 && screen.CursorRow >= screen.ScrollTop)
        {
            row = Math.Max(row, screen.ScrollTop);
        }
        else if (rows > 0 && screen.CursorRow <= screen.ScrollBottom)
        {
            row = Math.Min(row, screen.ScrollBottom);
        }

        screen.MoveCursorTo(screen.CursorColumn, row);
    }

    private void SetScrollRegion()
    {
        var top = Param(0) - 1;
        var bottom = Param(1, Screen.Rows) - 1;
        if (top == 0 && bottom == Screen.Rows - 1)
        {
            Screen.ResetScrollRegion();
            SetCursorPosition(0, 0);
        }
        else if (bottom > top)
        {
            Screen.SetScrollRegion(top, bottom);
            SetCursorPosition(0, 0);
        }
    }

    private void SelectGraphicRendition()
    {
        for (var i = 0; i < Math.Max(1, _params.Count); i++)
        {
            var p = i < _params.Count ? Math.Max(0, _params[i]) : 0;
            switch (p)
            {
                case 0:
                    _bold = false;
                    _ansiForeground = null;
                    _fixedForeground = null;
                    Screen.Background = Cell.DefaultBackground;
                    Screen.Reverse = false;
                    break;
                case 1:
                    _bold = true;
                    break;
                case 22:
                    _bold = false;
                    break;
                case 7:
                    Screen.Reverse = true;
                    break;
                case 27:
                    Screen.Reverse = false;
                    break;
                case >= 30 and <= 37:
                    SetForeground(p - 30);
                    break;
                case >= 90 and <= 97:
                    SetForeground(p - 90 + 8);
                    break;
                case 39:
                    SetForeground(null);
                    break;
                case >= 40 and <= 47:
                    Screen.Background = AnsiColors.ToC64Background(p - 40);
                    break;
                case >= 100 and <= 107:
                    Screen.Background = AnsiColors.ToC64Background(p - 100 + 8);
                    break;
                case 49:
                    Screen.Background = Cell.DefaultBackground;
                    break;
                case 38 or 48:
                    i = ExtendedColour(i, background: p == 48);
                    break;
            }
        }

        UpdateForeground();
    }

    /// <summary>Reads <c>38;5;n</c> (256 colours) or <c>38;2;r;g;b</c> (24-bit) starting at <paramref name="i"/>; returns the last index used.</summary>
    private int ExtendedColour(int i, bool background)
    {
        int Arg(int index) => index < _params.Count ? Math.Max(0, _params[index]) : 0;

        byte colour;
        switch (Arg(i + 1))
        {
            case 5 when i + 2 < _params.Count:
                colour = AnsiColors.FromXterm256(Math.Min(Arg(i + 2), 255), background);
                i += 2;
                break;
            case 2 when i + 4 < _params.Count:
                colour = C64Palette.Nearest((byte)Math.Min(Arg(i + 2), 255), (byte)Math.Min(Arg(i + 3), 255), (byte)Math.Min(Arg(i + 4), 255));
                i += 4;
                break;
            default:
                return _params.Count; // malformed: ignore the rest, as xterm does
        }

        if (background)
        {
            Screen.Background = colour;
        }
        else
        {
            _ansiForeground = null;
            _fixedForeground = colour;
        }

        return i;
    }

    private void SetForeground(int? ansi)
    {
        _ansiForeground = ansi;
        _fixedForeground = null;
    }

    private void UpdateForeground() =>
        Screen.Foreground = _fixedForeground
            ?? (_ansiForeground is { } ansi ? AnsiColors.ToC64Color(_bold && ansi < 8 ? ansi + 8 : ansi) : _theme.Foreground);

    private void DispatchOsc()
    {
        _state = State.Ground;
        var text = _osc.ToString();
        var separator = text.IndexOf(';');
        if (separator > 0 && text[..separator] is "0" or "2")
        {
            Title = text[(separator + 1)..];
            TitleChanged?.Invoke(Title);
        }
    }

    private void SaveCursor() =>
        _savedCursor = new SavedCursor(
            Screen.CursorColumn, Screen.CursorRow, _bold, _ansiForeground, _fixedForeground, Screen.Background, Screen.Reverse, OriginMode);

    private void RestoreCursor()
    {
        var saved = _savedCursor ?? new SavedCursor(0, 0, false, null, null, Cell.DefaultBackground, false, false);
        _bold = saved.Bold;
        _ansiForeground = saved.AnsiForeground;
        _fixedForeground = saved.FixedForeground;
        Screen.Background = saved.Background;
        Screen.Reverse = saved.Reverse;
        OriginMode = saved.OriginMode;
        UpdateForeground();
        Screen.MoveCursorTo(saved.Column, saved.Row);
    }

    private void Reset()
    {
        _bold = false;
        _ansiForeground = null;
        _fixedForeground = null;
        _savedCursor = null;
        CursorVisible = true;
        ApplicationCursorKeys = false;
        BracketedPaste = false;
        OriginMode = false;
        Screen.UseAlternateScreen(false);
        Screen.ResetScrollRegion();
        Screen.AutoWrap = true;
        Screen.Reverse = false;
        Screen.Background = Cell.DefaultBackground;
        Screen.Foreground = _theme.Foreground;
        Screen.Clear();
    }

    private readonly record struct SavedCursor(
        int Column, int Row, bool Bold, int? AnsiForeground, byte? FixedForeground, byte Background, bool Reverse, bool OriginMode);
}
