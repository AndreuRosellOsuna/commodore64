namespace C64Console.Core;

/// <summary>
/// Draws a <see cref="ScreenBuffer"/> into a 32-bit pixel array (0xAARRGGBB per pixel):
/// the border, the background, every character, and the cursor as a reverse-video cell.
/// </summary>
public sealed class FrameRenderer
{
    /// <summary>Border width on the left and right, in pixels (about what a PAL TV shows).</summary>
    public const int DefaultBorderX = 32;

    /// <summary>Border height on the top and bottom, in pixels.</summary>
    public const int DefaultBorderY = 36;

    public FrameRenderer(int columns, int rows, int borderX = DefaultBorderX, int borderY = DefaultBorderY)
    {
        Columns = columns;
        Rows = rows;
        BorderX = borderX;
        BorderY = borderY;
        Width = columns * C64Font.GlyphSize + 2 * borderX;
        Height = rows * C64Font.GlyphSize + 2 * borderY;
    }

    public int Columns { get; }
    public int Rows { get; }
    public int BorderX { get; }
    public int BorderY { get; }

    /// <summary>Frame width in pixels, border included. 384 for the 40×25 C64 screen.</summary>
    public int Width { get; }

    /// <summary>Frame height in pixels, border included. 272 for the 40×25 C64 screen.</summary>
    public int Height { get; }

    public uint[] CreateFrame() => new uint[Width * Height];

    public void Render(ScreenBuffer screen, C64Theme theme, bool cursorVisible, Span<uint> pixels)
    {
        if (screen.Columns != Columns || screen.Rows != Rows)
        {
            throw new ArgumentException($"Screen is {screen.Columns}×{screen.Rows}, renderer expects {Columns}×{Rows}.", nameof(screen));
        }

        if (pixels.Length < Width * Height)
        {
            throw new ArgumentException($"Frame needs {Width * Height} pixels.", nameof(pixels));
        }

        pixels[..(Width * Height)].Fill(C64Palette.ToArgb(theme.Border));

        var background = C64Palette.ToArgb(theme.Background);
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                var cell = screen[column, row];
                var reverse = cell.Reverse;
                if (cursorVisible && column == screen.CursorColumn && row == screen.CursorRow)
                {
                    reverse = !reverse;
                }

                DrawCell(pixels, column, row, cell.Char, C64Palette.ToArgb(cell.Foreground), background, reverse);
            }
        }
    }

    private void DrawCell(Span<uint> pixels, int column, int row, char c, uint foreground, uint background, bool reverse)
    {
        var glyph = C64Font.GetGlyph(c);
        var left = BorderX + column * C64Font.GlyphSize;
        var top = BorderY + row * C64Font.GlyphSize;

        for (var y = 0; y < C64Font.GlyphSize; y++)
        {
            var bits = reverse ? (byte)~glyph[y] : glyph[y];
            var line = pixels.Slice((top + y) * Width + left, C64Font.GlyphSize);
            for (var x = 0; x < C64Font.GlyphSize; x++)
            {
                line[x] = (bits & (0x80 >> x)) != 0 ? foreground : background;
            }
        }
    }
}
