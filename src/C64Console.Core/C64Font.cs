using System.Reflection;

namespace C64Console.Core;

/// <summary>
/// An original 8×8 bitmap font in the style of the C64 character set (2-pixel-wide strokes,
/// bottom row left free for spacing). It is drawn from scratch, so no Commodore ROM data is used.
/// The glyphs live in <c>Assets/font.txt</c> as ASCII art and are parsed on first use.
/// Each glyph is 8 bytes, one per row from the top; the most significant bit is the left pixel.
/// </summary>
public static class C64Font
{
    public const int GlyphSize = 8;

    private static readonly byte[] Space = new byte[GlyphSize];

    /// <summary>Drawn for any character the font does not have: a hollow box.</summary>
    public static readonly byte[] Fallback = [0x7E, 0x66, 0x66, 0x66, 0x66, 0x66, 0x7E, 0x00];

    private static readonly Lazy<Dictionary<char, byte[]>> Glyphs = new(Load);

    public static bool Contains(char c) => c == ' ' || Glyphs.Value.ContainsKey(c);

    /// <summary>Returns the 8 row bitmaps for <paramref name="c"/>, or <see cref="Fallback"/>.</summary>
    public static ReadOnlySpan<byte> GetGlyph(char c) =>
        c == ' ' ? Space : Glyphs.Value.TryGetValue(c, out var glyph) ? glyph : Fallback;

    private static Dictionary<char, byte[]> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("C64Console.Core.Assets.font.txt")
            ?? throw new InvalidOperationException("Embedded font resource is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Parses the ASCII-art font format described at the top of <c>Assets/font.txt</c>.</summary>
    public static Dictionary<char, byte[]> Parse(string text)
    {
        var glyphs = new Dictionary<char, byte[]>();
        var lines = text.ReplaceLineEndings("\n").Split('\n')
            .Where(line => line.Length > 0 && !line.StartsWith("# ", StringComparison.Ordinal))
            .ToArray();

        for (var i = 0; i < lines.Length; i += GlyphSize + 1)
        {
            var key = lines[i];
            if (key.Length != 1)
            {
                throw new FormatException($"Expected a single character, found \"{key}\".");
            }

            if (i + GlyphSize >= lines.Length)
            {
                throw new FormatException($"Glyph '{key}' has fewer than {GlyphSize} rows.");
            }

            var glyph = new byte[GlyphSize];
            for (var row = 0; row < GlyphSize; row++)
            {
                var pixels = lines[i + 1 + row];
                if (pixels.Length != GlyphSize || pixels.Any(p => p is not ('#' or '.')))
                {
                    throw new FormatException($"Glyph '{key}' row {row} must be 8 of '#' or '.', found \"{pixels}\".");
                }

                for (var x = 0; x < GlyphSize; x++)
                {
                    if (pixels[x] == '#')
                    {
                        glyph[row] |= (byte)(0x80 >> x);
                    }
                }
            }

            if (!glyphs.TryAdd(key[0], glyph))
            {
                throw new FormatException($"Glyph '{key}' is defined twice.");
            }
        }

        return glyphs;
    }
}
