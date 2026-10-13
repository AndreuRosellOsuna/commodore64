using System.Globalization;
using System.Text;

namespace C64Console.Core;

/// <summary>
/// Parses VT100/xterm output (as produced by Windows ConPTY) and applies it to a <see cref="ScreenBuffer"/>.
/// It covers what ConPTY needs to draw a shell: printing, cursor movement, erasing, inserting/deleting,
/// scrolling, 16-colour SGR, cursor show/hide, the window title and cursor position reports.
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
    private int? _ansiForeground; // null = default colour
    private (int Column, int Row, byte Foreground, bool Reverse)? _savedCursor;

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
            if (final is 'h' or 'l' && _params.Contains(25))
            {
                CursorVisible = final == 'h';
                Screen.MoveCursorBy(0, 0); // bump the version so the change is drawn
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
            case 'A': screen.MoveCursorBy(0, -Param(0)); break;
            case 'B': screen.MoveCursorBy(0, Param(0)); break;
            case 'C': screen.MoveCursorBy(Param(0), 0); break;
            case 'D': screen.MoveCursorBy(-Param(0), 0); break;
            case 'E': screen.MoveCursorTo(0, screen.CursorRow + Param(0)); break;
            case 'F': screen.MoveCursorTo(0, screen.CursorRow - Param(0)); break;
            case 'G' or '`': screen.MoveCursorTo(Param(0) - 1, screen.CursorRow); break;
            case 'H' or 'f': screen.MoveCursorTo(Param(1) - 1, Param(0) - 1); break;
            case 'J': screen.EraseInDisplay(EraseModeParam()); break;
            case 'K': screen.EraseInLine(EraseModeParam()); break;
            case 'L': screen.InsertLines(Param(0)); break;
            case 'M': screen.DeleteLines(Param(0)); break;
            case 'P': screen.DeleteCharacters(Param(0)); break;
            case 'S': screen.ScrollUp(Param(0)); break;
            case 'T': screen.ScrollDown(Param(0)); break;
            case 'X': screen.EraseCharacters(Param(0)); break;
            case 'd': screen.MoveCursorTo(screen.CursorColumn, Param(0) - 1); break;
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
        2 or 3 => EraseMode.All,
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
                    _ansiForeground = p - 30;
                    break;
                case >= 90 and <= 97:
                    _ansiForeground = p - 90 + 8;
                    break;
                case 39:
                    _ansiForeground = null;
                    break;
                case 38 or 48:
                    // Extended colour: 5;n (256 colours) or 2;r;g;b. Only the first 16 of the 256 are used for now.
                    var mode = i + 1 < _params.Count ? _params[i + 1] : -1;
                    if (mode == 5)
                    {
                        if (p == 38 && i + 2 < _params.Count && _params[i + 2] is >= 0 and < 16)
                        {
                            _ansiForeground = _params[i + 2];
                        }

                        i += 2;
                    }
                    else if (mode == 2)
                    {
                        i += 4;
                    }

                    break;
            }
        }

        Screen.Foreground = _ansiForeground is { } ansi
            ? AnsiColors.ToC64Color(_bold && ansi < 8 ? ansi + 8 : ansi)
            : _theme.Foreground;
    }

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
        _savedCursor = (Screen.CursorColumn, Screen.CursorRow, Screen.Foreground, Screen.Reverse);

    private void RestoreCursor()
    {
        if (_savedCursor is { } saved)
        {
            Screen.Foreground = saved.Foreground;
            Screen.Reverse = saved.Reverse;
            Screen.MoveCursorTo(saved.Column, saved.Row);
        }
    }

    private void Reset()
    {
        _bold = false;
        _ansiForeground = null;
        _savedCursor = null;
        CursorVisible = true;
        Screen.Reverse = false;
        Screen.Foreground = _theme.Foreground;
        Screen.Clear();
    }
}
