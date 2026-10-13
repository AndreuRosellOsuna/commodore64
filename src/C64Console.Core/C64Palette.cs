namespace C64Console.Core;

/// <summary>
/// The 16 Commodore 64 colours (Colodore palette), stored as 0xAARRGGBB.
/// In little-endian memory that is B, G, R, A, which matches WPF's Bgra32 pixel format.
/// </summary>
public static class C64Palette
{
    public const byte Black = 0;
    public const byte White = 1;
    public const byte Red = 2;
    public const byte Cyan = 3;
    public const byte Purple = 4;
    public const byte Green = 5;
    public const byte Blue = 6;
    public const byte Yellow = 7;
    public const byte Orange = 8;
    public const byte Brown = 9;
    public const byte LightRed = 10;
    public const byte DarkGrey = 11;
    public const byte Grey = 12;
    public const byte LightGreen = 13;
    public const byte LightBlue = 14;
    public const byte LightGrey = 15;

    private static readonly uint[] Argb =
    [
        0xFF000000, // black
        0xFFFFFFFF, // white
        0xFF813338, // red
        0xFF75CEC8, // cyan
        0xFF8E3C97, // purple
        0xFF56AC4D, // green
        0xFF2E2C9B, // blue
        0xFFEDF171, // yellow
        0xFF8E5029, // orange
        0xFF553800, // brown
        0xFFC46C71, // light red
        0xFF4A4A4A, // dark grey
        0xFF7B7B7B, // grey
        0xFFA9FF9F, // light green
        0xFF706DEB, // light blue
        0xFFB2B2B2, // light grey
    ];

    public const int Count = 16;

    public static uint ToArgb(byte index) => Argb[index & 0x0F];
}
