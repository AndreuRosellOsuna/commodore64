namespace C64Console.Core;

/// <summary>Maps ANSI terminal colours (16, 256 and 24-bit) to C64 colours.</summary>
public static class AnsiColors
{
    // The 16 basic colours, chosen by eye rather than by distance so text stays readable on the blue screen.
    private static readonly byte[] ToC64 =
    [
        C64Palette.Black,      // 0 black
        C64Palette.Red,        // 1 red
        C64Palette.Green,      // 2 green
        C64Palette.Orange,     // 3 yellow (dark)
        C64Palette.LightBlue,  // 4 blue: C64 blue would vanish on the blue background
        C64Palette.Purple,     // 5 magenta
        C64Palette.Cyan,       // 6 cyan
        C64Palette.LightGrey,  // 7 white (light grey)
        C64Palette.DarkGrey,   // 8 bright black
        C64Palette.LightRed,   // 9 bright red
        C64Palette.LightGreen, // 10 bright green
        C64Palette.Yellow,     // 11 bright yellow
        C64Palette.LightBlue,  // 12 bright blue
        C64Palette.Purple,     // 13 bright magenta
        C64Palette.Cyan,       // 14 bright cyan
        C64Palette.White,      // 15 bright white
    ];

    private static readonly byte[] CubeLevels = [0, 95, 135, 175, 215, 255];

    /// <summary>Text colour for one of the 16 basic ANSI colours.</summary>
    public static byte ToC64Color(int ansiIndex) => ToC64[ansiIndex & 0x0F];

    /// <summary>Background colour for one of the 16 basic ANSI colours. Unlike text, a blue background is shown as C64 blue.</summary>
    public static byte ToC64Background(int ansiIndex) => (ansiIndex & 0x0F) == 4 ? C64Palette.Blue : ToC64Color(ansiIndex);

    /// <summary>Nearest C64 colour for an entry of the xterm 256-colour palette (0–15 use <see cref="ToC64Color"/>).</summary>
    public static byte FromXterm256(int index, bool background = false)
    {
        if (index < 16)
        {
            return background ? ToC64Background(index) : ToC64Color(index);
        }

        var (r, g, b) = Xterm256ToRgb(index);
        return C64Palette.Nearest(r, g, b);
    }

    public static (byte R, byte G, byte B) Xterm256ToRgb(int index)
    {
        index = Math.Clamp(index, 16, 255);
        if (index >= 232)
        {
            var grey = (byte)(8 + (index - 232) * 10);
            return (grey, grey, grey);
        }

        index -= 16;
        return (CubeLevels[index / 36], CubeLevels[index / 6 % 6], CubeLevels[index % 6]);
    }
}
