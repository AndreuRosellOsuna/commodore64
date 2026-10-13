using System.Windows.Input;
using C64Console.Core;

namespace C64Console.App;

/// <summary>Translates WPF keys into the platform-independent <see cref="TerminalKey"/>.</summary>
internal static class KeyInput
{
    public static TerminalKey? ToTerminalKey(Key key) => key switch
    {
        Key.Enter => TerminalKey.Enter,
        Key.Back => TerminalKey.Backspace,
        Key.Tab => TerminalKey.Tab,
        Key.Escape => TerminalKey.Escape,
        Key.Up => TerminalKey.Up,
        Key.Down => TerminalKey.Down,
        Key.Right => TerminalKey.Right,
        Key.Left => TerminalKey.Left,
        Key.Home => TerminalKey.Home,
        Key.End => TerminalKey.End,
        Key.Insert => TerminalKey.Insert,
        Key.Delete => TerminalKey.Delete,
        Key.PageUp => TerminalKey.PageUp,
        Key.PageDown => TerminalKey.PageDown,
        >= Key.F1 and <= Key.F12 => TerminalKey.F1 + (key - Key.F1),
        _ => null,
    };

    public static KeyModifiers Modifiers(ModifierKeys keys) =>
        (keys.HasFlag(ModifierKeys.Shift) ? KeyModifiers.Shift : KeyModifiers.None) |
        (keys.HasFlag(ModifierKeys.Alt) ? KeyModifiers.Alt : KeyModifiers.None) |
        (keys.HasFlag(ModifierKeys.Control) ? KeyModifiers.Control : KeyModifiers.None);
}
