namespace C64Console.Core;

/// <summary>Keys that don't produce text and need an escape sequence (or control code) instead.</summary>
public enum TerminalKey
{
    Enter,
    Backspace,
    Tab,
    Escape,
    Up,
    Down,
    Right,
    Left,
    Home,
    End,
    Insert,
    Delete,
    PageUp,
    PageDown,
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12,
}

[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1,
    Alt = 2,
    Control = 4,
}

/// <summary>Turns key presses into the xterm-style input sequences that ConPTY understands.</summary>
public static class KeyEncoder
{
    public static string Encode(TerminalKey key, KeyModifiers modifiers = KeyModifiers.None)
    {
        // xterm adds a modifier parameter: 1 + (Shift=1, Alt=2, Ctrl=4).
        var m = (int)modifiers;
        var altPrefix = modifiers.HasFlag(KeyModifiers.Alt) ? "\x1B" : "";

        return key switch
        {
            TerminalKey.Enter => altPrefix + "\r",
            TerminalKey.Backspace => altPrefix + (modifiers.HasFlag(KeyModifiers.Control) ? "\b" : "\x7F"),
            TerminalKey.Tab => modifiers.HasFlag(KeyModifiers.Shift) ? "\x1B[Z" : altPrefix + "\t",
            TerminalKey.Escape => altPrefix + "\x1B",
            TerminalKey.Up => Cursor('A', m),
            TerminalKey.Down => Cursor('B', m),
            TerminalKey.Right => Cursor('C', m),
            TerminalKey.Left => Cursor('D', m),
            TerminalKey.Home => Cursor('H', m),
            TerminalKey.End => Cursor('F', m),
            TerminalKey.F1 => Ss3('P', m),
            TerminalKey.F2 => Ss3('Q', m),
            TerminalKey.F3 => Ss3('R', m),
            TerminalKey.F4 => Ss3('S', m),
            TerminalKey.Insert => Tilde(2, m),
            TerminalKey.Delete => Tilde(3, m),
            TerminalKey.PageUp => Tilde(5, m),
            TerminalKey.PageDown => Tilde(6, m),
            TerminalKey.F5 => Tilde(15, m),
            TerminalKey.F6 => Tilde(17, m),
            TerminalKey.F7 => Tilde(18, m),
            TerminalKey.F8 => Tilde(19, m),
            TerminalKey.F9 => Tilde(20, m),
            TerminalKey.F10 => Tilde(21, m),
            TerminalKey.F11 => Tilde(23, m),
            TerminalKey.F12 => Tilde(24, m),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
        };
    }

    /// <summary>Ctrl + a letter gives control codes 1–26 (Ctrl+C = 0x03). Returns null for anything else.</summary>
    public static string? EncodeControlLetter(char letter) =>
        char.ToUpperInvariant(letter) is var upper and >= 'A' and <= 'Z' ? ((char)(upper - 'A' + 1)).ToString() : null;

    private static string Cursor(char final, int m) => m == 0 ? $"\x1B[{final}" : $"\x1B[1;{m + 1}{final}";

    private static string Ss3(char final, int m) => m == 0 ? $"\x1BO{final}" : $"\x1B[1;{m + 1}{final}";

    private static string Tilde(int code, int m) => m == 0 ? $"\x1B[{code}~" : $"\x1B[{code};{m + 1}~";
}
