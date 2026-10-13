using C64Console.Core;

namespace C64Console.Core.Tests;

public class ScreenBufferTests
{
    private static string RowText(ScreenBuffer screen, int row) =>
        new string(Enumerable.Range(0, screen.Columns).Select(c => screen[c, row].Char).ToArray()).TrimEnd();

    [Fact]
    public void Starts_blank_with_cursor_home()
    {
        var screen = new ScreenBuffer(40, 25, C64Palette.LightBlue);

        Assert.Equal((0, 0), (screen.CursorColumn, screen.CursorRow));
        Assert.Equal(Cell.Blank(C64Palette.LightBlue), screen[39, 24]);
    }

    [Fact]
    public void Write_places_characters_with_current_colour()
    {
        var screen = new ScreenBuffer(40, 25, C64Palette.LightBlue);
        screen.Write("HI");
        screen.Foreground = C64Palette.Yellow;
        screen.Write("!");

        Assert.Equal(new Cell('H', C64Palette.LightBlue), screen[0, 0]);
        Assert.Equal(new Cell('!', C64Palette.Yellow), screen[2, 0]);
        Assert.Equal(3, screen.CursorColumn);
    }

    [Fact]
    public void Writing_past_last_column_wraps()
    {
        var screen = new ScreenBuffer(4, 3, 0);
        screen.Write("ABCDE");

        Assert.Equal("ABCD", RowText(screen, 0));
        Assert.Equal("E", RowText(screen, 1));
        Assert.Equal((1, 1), (screen.CursorColumn, screen.CursorRow));
    }

    [Fact]
    public void New_line_on_last_row_scrolls_up()
    {
        var screen = new ScreenBuffer(4, 2, 0);
        screen.WriteLine("ONE");
        screen.WriteLine("TWO");
        screen.Write("3");

        Assert.Equal("TWO", RowText(screen, 0));
        Assert.Equal("3", RowText(screen, 1));
        Assert.Equal((1, 1), (screen.CursorColumn, screen.CursorRow));
    }

    [Fact]
    public void Carriage_return_goes_to_start_of_line()
    {
        var screen = new ScreenBuffer(10, 2, 0);
        screen.Write("ABC\rX");

        Assert.Equal("XBC", RowText(screen, 0));
    }

    [Fact]
    public void Backspace_erases_and_crosses_line_start()
    {
        var screen = new ScreenBuffer(3, 2, 0);
        screen.Write("ABCD");
        screen.Backspace();
        screen.Backspace();

        Assert.Equal("AB", RowText(screen, 0));
        Assert.Equal("", RowText(screen, 1));
        Assert.Equal((2, 0), (screen.CursorColumn, screen.CursorRow));
    }

    [Fact]
    public void Backspace_at_home_does_nothing()
    {
        var screen = new ScreenBuffer(3, 2, 0);
        var version = screen.Version;
        screen.Backspace();

        Assert.Equal((0, 0), (screen.CursorColumn, screen.CursorRow));
        Assert.Equal(version, screen.Version);
    }

    [Fact]
    public void Changes_bump_version()
    {
        var screen = new ScreenBuffer(3, 2, 0);
        var version = screen.Version;
        screen.Write('A');

        Assert.True(screen.Version > version);
    }
}
