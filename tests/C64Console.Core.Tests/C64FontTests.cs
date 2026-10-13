using C64Console.Core;

namespace C64Console.Core.Tests;

public class C64FontTests
{
    [Fact]
    public void Has_every_printable_ascii_character()
    {
        var missing = Enumerable.Range(' ', '~' - ' ' + 1).Select(c => (char)c).Where(c => !C64Font.Contains(c));
        Assert.Empty(missing);
    }

    [Fact]
    public void Space_is_blank()
    {
        Assert.All(C64Font.GetGlyph(' ').ToArray(), row => Assert.Equal(0, row));
    }

    [Fact]
    public void Unknown_character_uses_fallback_box()
    {
        Assert.Equal(C64Font.Fallback, C64Font.GetGlyph('€').ToArray());
    }

    [Fact]
    public void Glyphs_leave_the_bottom_row_free_except_descenders()
    {
        var descenders = "gjpqy,;_";
        var offenders = Enumerable.Range('!', '~' - '!' + 1)
            .Select(c => (char)c)
            .Where(c => !descenders.Contains(c) && C64Font.GetGlyph(c)[7] != 0);
        Assert.Empty(offenders);
    }

    [Fact]
    public void Parse_reads_ascii_art()
    {
        var glyphs = C64Font.Parse("""
            # comment
            X
            #.......
            .#......
            ..#.....
            ...#....
            ....#...
            .....#..
            ......#.
            .......#
            """);

        Assert.Equal([0x80, 0x40, 0x20, 0x10, 0x08, 0x04, 0x02, 0x01], glyphs['X']);
    }

    [Theory]
    [InlineData("AB\n........\n")]
    [InlineData("A\n........\n")]
    [InlineData("A\n.......\n........\n........\n........\n........\n........\n........\n........\n")]
    public void Parse_rejects_malformed_glyphs(string text)
    {
        Assert.Throws<FormatException>(() => C64Font.Parse(text));
    }
}
