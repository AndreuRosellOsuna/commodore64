using System.Buffers.Binary;
using C64Console.Core;

namespace C64Console.Preview;

/// <summary>
/// Draws the app icon: a tiny C64 screen (light-blue border, blue screen) showing "64" and the block cursor,
/// in the app's own font. Small sizes drop the text and keep just the screen and cursor, which stay readable.
/// Saved as a Windows .ico holding PNG images.
/// </summary>
internal static class AppIcon
{
    public static readonly int[] Sizes = [16, 32, 48, 64, 128, 256];

    public static void WriteIco(string path)
    {
        var images = Sizes.Select(size => PngWriter.Encode(Draw(size), size, size)).ToArray();

        using var file = File.Create(path);
        Span<byte> header = stackalloc byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(header[2..], 1); // type: icon
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], (ushort)images.Length);
        file.Write(header);

        var offset = 6 + 16 * images.Length;
        Span<byte> entry = stackalloc byte[16];
        for (var i = 0; i < images.Length; i++)
        {
            entry.Clear();
            entry[0] = (byte)(Sizes[i] == 256 ? 0 : Sizes[i]); // 0 means 256
            entry[1] = entry[0];
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 1);  // colour planes
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], 32); // bits per pixel
            BinaryPrimitives.WriteInt32LittleEndian(entry[8..], images[i].Length);
            BinaryPrimitives.WriteInt32LittleEndian(entry[12..], offset);
            file.Write(entry);
            offset += images[i].Length;
        }

        foreach (var image in images)
        {
            file.Write(image);
        }
    }

    public static uint[] Draw(int size)
    {
        var border = C64Palette.ToArgb(C64Palette.LightBlue);
        var screen = C64Palette.ToArgb(C64Palette.Blue);
        var text = C64Palette.ToArgb(C64Palette.LightBlue);

        if (size < 32)
        {
            // 16×16: a 2-pixel border and a cursor block; text would only be noise at this size.
            var small = Fill(size, border);
            Rect(small, size, 2, 2, size - 4, size - 4, screen);
            Rect(small, size, 4, size / 2, size / 3, size / 3, text);
            return small;
        }

        // Designed on a 32×32 grid and scaled up by a whole number; 48 uses the 32 design centred on a border.
        const int grid = 32;
        var scale = size / grid;
        var design = Fill(grid, border);
        Rect(design, grid, 4, 4, 24, 24, screen);
        DrawGlyph(design, grid, '6', 8, 7, text);
        DrawGlyph(design, grid, '4', 16, 7, text);
        Rect(design, grid, 8, 17, 8, 8, text); // the cursor

        var pixels = Fill(size, border);
        var inset = (size - grid * scale) / 2;
        for (var y = 0; y < grid * scale; y++)
        {
            for (var x = 0; x < grid * scale; x++)
            {
                pixels[(y + inset) * size + x + inset] = design[y / scale * grid + x / scale];
            }
        }

        return pixels;
    }

    private static uint[] Fill(int size, uint colour) => Enumerable.Repeat(colour, size * size).ToArray();

    private static void Rect(uint[] pixels, int size, int left, int top, int width, int height, uint colour)
    {
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                pixels[y * size + x] = colour;
            }
        }
    }

    private static void DrawGlyph(uint[] pixels, int size, char c, int left, int top, uint colour)
    {
        var glyph = C64Font.GetGlyph(c);
        for (var y = 0; y < C64Font.GlyphSize; y++)
        {
            for (var x = 0; x < C64Font.GlyphSize; x++)
            {
                if ((glyph[y] & (0x80 >> x)) != 0)
                {
                    pixels[(top + y) * size + left + x] = colour;
                }
            }
        }
    }
}
