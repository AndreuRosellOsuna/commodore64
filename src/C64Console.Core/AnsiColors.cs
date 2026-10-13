namespace C64Console.Core;

/// <summary>Maps the 16 ANSI terminal colours to the closest-looking readable C64 colours.</summary>
public static class AnsiColors
{
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

    public static byte ToC64Color(int ansiIndex) => ToC64[ansiIndex & 0x0F];
}
