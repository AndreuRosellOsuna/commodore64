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

    private static readonly (double L, double A, double B)[] Lab = Argb.Select(c => ToLab((byte)(c >> 16), (byte)(c >> 8), (byte)c)).ToArray();

    public static uint ToArgb(byte index) => Argb[index & 0x0F];

    /// <summary>
    /// The C64 colour that looks closest to an RGB colour. Distance is measured in CIE Lab space with hue
    /// differences counted double, so a colour keeps its hue rather than jumping to a neighbour of similar brightness.
    /// </summary>
    public static byte Nearest(byte r, byte g, byte b)
    {
        const double HueWeight = 2;
        var (l, a, bb) = ToLab(r, g, b);
        var chroma = Math.Sqrt(a * a + bb * bb);
        var best = 0;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < Count; i++)
        {
            var dl = Lab[i].L - l;
            var da = Lab[i].A - a;
            var db = Lab[i].B - bb;
            var dc = Math.Sqrt(Lab[i].A * Lab[i].A + Lab[i].B * Lab[i].B) - chroma;
            var dh2 = Math.Max(0, da * da + db * db - dc * dc);
            var distance = dl * dl + dc * dc + HueWeight * dh2;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return (byte)best;
    }

    private static (double L, double A, double B) ToLab(byte r, byte g, byte b)
    {
        static double Linear(byte c)
        {
            var v = c / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116;

        var (lr, lg, lb) = (Linear(r), Linear(g), Linear(b));
        var x = (0.4124 * lr + 0.3576 * lg + 0.1805 * lb) / 0.95047;
        var y = 0.2126 * lr + 0.7152 * lg + 0.0722 * lb;
        var z = (0.0193 * lr + 0.1192 * lg + 0.9505 * lb) / 1.08883;
        var (fx, fy, fz) = (F(x), F(y), F(z));
        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }
}
