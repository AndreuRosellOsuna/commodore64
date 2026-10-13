using C64Console.Core;

namespace C64Console.Core.Tests;

public class KeyEncoderTests
{
    [Theory]
    [InlineData(TerminalKey.Enter, KeyModifiers.None, "\r")]
    [InlineData(TerminalKey.Backspace, KeyModifiers.None, "\x7F")]
    [InlineData(TerminalKey.Backspace, KeyModifiers.Control, "\b")]
    [InlineData(TerminalKey.Tab, KeyModifiers.None, "\t")]
    [InlineData(TerminalKey.Tab, KeyModifiers.Shift, "\x1B[Z")]
    [InlineData(TerminalKey.Escape, KeyModifiers.None, "\x1B")]
    [InlineData(TerminalKey.Up, KeyModifiers.None, "\x1B[A")]
    [InlineData(TerminalKey.Left, KeyModifiers.Control, "\x1B[1;5D")]
    [InlineData(TerminalKey.Home, KeyModifiers.Shift, "\x1B[1;2H")]
    [InlineData(TerminalKey.Delete, KeyModifiers.None, "\x1B[3~")]
    [InlineData(TerminalKey.PageDown, KeyModifiers.Control, "\x1B[6;5~")]
    [InlineData(TerminalKey.F1, KeyModifiers.None, "\x1BOP")]
    [InlineData(TerminalKey.F12, KeyModifiers.None, "\x1B[24~")]
    [InlineData(TerminalKey.Enter, KeyModifiers.Alt, "\x1B\r")]
    public void Encodes_keys_like_xterm(TerminalKey key, KeyModifiers modifiers, string expected)
    {
        Assert.Equal(expected, KeyEncoder.Encode(key, modifiers));
    }

    [Fact]
    public void Every_key_has_an_encoding()
    {
        foreach (var key in Enum.GetValues<TerminalKey>())
        {
            Assert.False(string.IsNullOrEmpty(KeyEncoder.Encode(key)));
        }
    }

    [Theory]
    [InlineData('c', "\x03")]
    [InlineData('A', "\x01")]
    [InlineData('Z', "\x1A")]
    [InlineData('1', null)]
    public void Ctrl_letters_become_control_codes(char letter, string? expected)
    {
        Assert.Equal(expected, KeyEncoder.EncodeControlLetter(letter));
    }
}
