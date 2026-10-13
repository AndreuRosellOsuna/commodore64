using C64Console.Core;

namespace C64Console.Core.Tests;

public class ScreenBufferTests
{
    private static ScreenBuffer WithRows(int columns, params string[] rows)
    {
        var screen = new ScreenBuffer(columns, rows.Length, 0);
        for (var i = 0; i < rows.Length; i++)
        {
            screen.MoveCursorTo(0, i);
            screen.Write(rows[i]);
        }

        return screen;
    }

    [Fact]
    public void Starts_blank_with_cursor_home()
    {
        var screen = new ScreenBuffer(40, 25, C64Palette.LightBlue);

        Assert.Equal((0, 0), screen.Cursor());
        Assert.Equal(Cell.Blank(C64Palette.LightBlue), screen[39, 24]);
    }

    [Fact]
    public void Write_places_characters_with_current_colour_and_reverse()
    {
        var screen = new ScreenBuffer(40, 25, C64Palette.LightBlue);
        screen.Write("HI");
        screen.Foreground = C64Palette.Yellow;
        screen.Reverse = true;
        screen.Write("!");

        Assert.Equal(new Cell('H', C64Palette.LightBlue), screen[0, 0]);
        Assert.Equal(new Cell('!', C64Palette.Yellow, Reverse: true), screen[2, 0]);
        Assert.Equal((3, 0), screen.Cursor());
    }

    [Fact]
    public void Writing_the_last_column_defers_the_wrap()
    {
        var screen = new ScreenBuffer(4, 3, 0);
        screen.Write("ABCD");

        Assert.Equal((3, 0), screen.Cursor());
        Assert.True(screen.WrapPending);

        screen.Write('E');
        Assert.Equal("ABCD", screen.RowText(0));
        Assert.Equal("E", screen.RowText(1));
        Assert.Equal((1, 1), screen.Cursor());
    }

    [Fact]
    public void Full_line_followed_by_new_line_does_not_leave_a_blank_line()
    {
        var screen = new ScreenBuffer(4, 3, 0);
        screen.Write("ABCD");
        screen.NewLine();
        screen.Write("E");

        Assert.Equal("E", screen.RowText(1));
    }

    [Fact]
    public void Cursor_movement_cancels_a_pending_wrap()
    {
        var screen = new ScreenBuffer(4, 3, 0);
        screen.Write("ABCD");
        screen.MoveCursorBy(-1, 0);
        screen.Write("x");

        Assert.Equal("ABxD", screen.RowText(0));
        Assert.Equal("", screen.RowText(1));
    }

    [Fact]
    public void Line_feed_on_last_row_scrolls_up_and_keeps_column()
    {
        var screen = new ScreenBuffer(4, 2, 0);
        screen.Write("ONE");
        screen.NewLine();
        screen.Write("TWO");
        screen.LineFeed();

        Assert.Equal("TWO", screen.RowText(0));
        Assert.Equal("", screen.RowText(1));
        Assert.Equal((3, 1), screen.Cursor());
    }

    [Fact]
    public void Reverse_index_on_first_row_scrolls_down()
    {
        var screen = WithRows(3, "AAA", "BBB");
        screen.MoveCursorTo(1, 0);
        screen.ReverseIndex();

        Assert.Equal("", screen.RowText(0));
        Assert.Equal("AAA", screen.RowText(1));
        Assert.Equal((1, 0), screen.Cursor());
    }

    [Fact]
    public void Move_cursor_is_clamped_to_the_screen()
    {
        var screen = new ScreenBuffer(10, 5, 0);
        screen.MoveCursorTo(99, -3);

        Assert.Equal((9, 0), screen.Cursor());
    }

    [Theory]
    [InlineData(EraseMode.ToEnd, "AB")]
    [InlineData(EraseMode.ToStart, "   DE")]
    [InlineData(EraseMode.All, "")]
    public void Erase_in_line(EraseMode mode, string expected)
    {
        var screen = WithRows(5, "ABCDE");
        screen.MoveCursorTo(2, 0);
        screen.EraseInLine(mode);

        Assert.Equal(expected, screen.RowText(0));
    }

    [Theory]
    [InlineData(EraseMode.ToEnd, "AAA", "B", "")]
    [InlineData(EraseMode.ToStart, "", "  B", "CCC")]
    [InlineData(EraseMode.All, "", "", "")]
    public void Erase_in_display(EraseMode mode, string row0, string row1, string row2)
    {
        var screen = WithRows(3, "AAA", "BBB", "CCC");
        screen.MoveCursorTo(1, 1);
        screen.EraseInDisplay(mode);

        Assert.Equal([row0, row1, row2], [screen.RowText(0), screen.RowText(1), screen.RowText(2)]);
        Assert.Equal((1, 1), screen.Cursor());
    }

    [Fact]
    public void Insert_and_delete_characters_shift_the_rest_of_the_line()
    {
        var screen = WithRows(5, "ABCDE");
        screen.MoveCursorTo(1, 0);
        screen.InsertCharacters(2);
        Assert.Equal("A  BC", screen.RowText(0));

        screen.DeleteCharacters(3);
        Assert.Equal("AC", screen.RowText(0));
    }

    [Fact]
    public void Erase_characters_stops_at_end_of_line()
    {
        var screen = WithRows(5, "ABCDE", "FGHIJ");
        screen.MoveCursorTo(3, 0);
        screen.EraseCharacters(10);

        Assert.Equal("ABC", screen.RowText(0));
        Assert.Equal("FGHIJ", screen.RowText(1));
    }

    [Fact]
    public void Insert_and_delete_lines_work_from_the_cursor_row()
    {
        var screen = WithRows(3, "AAA", "BBB", "CCC");
        screen.MoveCursorTo(0, 1);
        screen.InsertLines(1);
        Assert.Equal(["AAA", "", "BBB"], [screen.RowText(0), screen.RowText(1), screen.RowText(2)]);

        screen.DeleteLines(2);
        Assert.Equal(["AAA", "", ""], [screen.RowText(0), screen.RowText(1), screen.RowText(2)]);
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
