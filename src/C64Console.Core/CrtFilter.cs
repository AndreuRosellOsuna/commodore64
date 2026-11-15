namespace C64Console.Core;

/// <summary>
/// Scales a frame up by a whole number and makes it look like an old CRT television:
/// a slightly curved picture (barrel distortion) with black corners, dark scanlines between pixel rows,
/// and a vignette that darkens the edges. Runs on the CPU and is cheap: the per-pixel work is looked up
/// in tables that are built once for each frame size.
/// </summary>
public sealed class CrtFilter
{
    /// <summary>How strongly the picture bulges; 0 is flat.</summary>
    public const double Curvature = 0.035;

    /// <summary>Brightness of the dark gap between two pixel rows (1 = no scanlines).</summary>
    public const double ScanlineBrightness = 0.62;

    /// <summary>How much the edges are darkened (0 = no vignette, 1 = black at the very edge).</summary>
    public const double VignetteStrength = 0.6;

    private const uint Black = 0xFF000000;

    private readonly int[] _source;      // source pixel index for each output pixel, or -1 outside the tube
    private readonly ushort[] _brightness; // 0…256 brightness multiplier for each output pixel

    public CrtFilter(int sourceWidth, int sourceHeight, int scale)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceHeight, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(scale, 1);

        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        Scale = scale;
        Width = sourceWidth * scale;
        Height = sourceHeight * scale;
        _source = new int[Width * Height];
        _brightness = new ushort[Width * Height];
        BuildTables();
    }

    public int SourceWidth { get; }
    public int SourceHeight { get; }
    public int Scale { get; }

    /// <summary>Output width in pixels (source width × scale).</summary>
    public int Width { get; }

    /// <summary>Output height in pixels (source height × scale).</summary>
    public int Height { get; }

    public uint[] CreateOutput() => new uint[Width * Height];

    /// <summary>The source pixel shown at an output pixel, or null where the curved picture leaves a black corner.</summary>
    public (int X, int Y)? SourcePixelAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return null;
        }

        var index = _source[y * Width + x];
        return index < 0 ? null : (index % SourceWidth, index / SourceWidth);
    }

    public void Apply(uint[] source, uint[] output)
    {
        if (source.Length < SourceWidth * SourceHeight)
        {
            throw new ArgumentException($"Source needs {SourceWidth * SourceHeight} pixels.", nameof(source));
        }

        if (output.Length < Width * Height)
        {
            throw new ArgumentException($"Output needs {Width * Height} pixels.", nameof(output));
        }

        Parallel.For(0, Height, y =>
        {
            var row = y * Width;
            for (var i = row; i < row + Width; i++)
            {
                var index = _source[i];
                output[i] = index < 0 ? Black : Dim(source[index], _brightness[i]);
            }
        });
    }

    private static uint Dim(uint argb, uint brightness)
    {
        var r = ((argb >> 16) & 0xFF) * brightness >> 8;
        var g = ((argb >> 8) & 0xFF) * brightness >> 8;
        var b = (argb & 0xFF) * brightness >> 8;
        return 0xFF000000 | (r << 16) | (g << 8) | b;
    }

    private void BuildTables()
    {
        for (var y = 0; y < Height; y++)
        {
            // Output pixel centre in -1…1 coordinates.
            var v = (y + 0.5) / Height * 2 - 1;
            for (var x = 0; x < Width; x++)
            {
                var u = (x + 0.5) / Width * 2 - 1;
                var i = y * Width + x;

                // Barrel distortion: points further from the centre sample from further out.
                var su = u * (1 + Curvature * v * v);
                var sv = v * (1 + Curvature * u * u);
                if (Math.Abs(su) > 1 || Math.Abs(sv) > 1)
                {
                    _source[i] = -1;
                    continue;
                }

                var sx = (su + 1) / 2 * SourceWidth;
                var sy = (sv + 1) / 2 * SourceHeight;
                _source[i] = Math.Min((int)sy, SourceHeight - 1) * SourceWidth + Math.Min((int)sx, SourceWidth - 1);

                // Scanline: the last output row of every pixel row is darker (needs at least 2 rows per pixel).
                // It follows the output rows rather than the curved picture: curved scanlines sampled at this
                // resolution make a wavy moiré pattern.
                var scanline = Scale >= 2 && y % Scale == Scale - 1 ? ScanlineBrightness : 1.0;

                // Vignette: gently darker towards the edges and much darker in the corners.
                var (px, py) = ((su + 1) / 2, (sv + 1) / 2);
                var vignette = 1 - VignetteStrength * (1 - Math.Pow(16 * px * py * (1 - px) * (1 - py), 0.25));
                _brightness[i] = (ushort)Math.Clamp(Math.Round(256 * scanline * vignette), 0, 256);
            }
        }
    }
}
