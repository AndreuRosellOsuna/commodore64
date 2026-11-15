using C64Console.Core;

namespace C64Console.Core.Tests;

public class VtTerminalTests
{
    private readonly ScreenBuffer _screen = new(10, 4, 0);
    private readonly VtTerminal _terminal;
    private readonly List<string> _responses = [];

    public VtTerminalTests()
    {
        _terminal = new VtTerminal(_screen, C64Theme.Default);
        _terminal.Response += _responses.Add;
    }

    private void Feed(string text) => _terminal.Feed(text);

    [Fact]
    public void Prints_text_and_handles_cr_lf()
    {
        Feed("AB\r\nCD");

        Assert.Equal("AB", _screen.RowText(0));
        Assert.Equal("CD", _screen.RowText(1));
        Assert.Equal((2, 1), _screen.Cursor());
    }

    [Fact]
    public void Line_feed_alone_keeps_the_column()
    {
        Feed("AB\nC");

        Assert.Equal("  C", _screen.RowText(1));
    }

    [Fact]
    public void Backspace_moves_left_without_erasing()
    {
        Feed("ABC\b\bx");

        Assert.Equal("AxC", _screen.RowText(0));
    }

    [Fact]
    public void Tab_moves_to_next_multiple_of_eight()
    {
        Feed("A\tB");

        Assert.Equal('B', _screen[8, 0].Char);
    }

    [Fact]
    public void Cursor_position_is_one_based_and_defaults_to_home()
    {
        Feed("\x1B[3;5H");
        Assert.Equal((4, 2), _screen.Cursor());

        Feed("\x1B[H");
        Assert.Equal((0, 0), _screen.Cursor());
    }

    [Theory]
    [InlineData("\x1B[2A", 5, 1)]
    [InlineData("\x1B[B", 5, 4)]
    [InlineData("\x1B[3C", 8, 3)]
    [InlineData("\x1B[D", 4, 3)]
    [InlineData("\x1B[2G", 1, 3)]
    [InlineData("\x1B[1d", 5, 0)]
    [InlineData("\x1B[E", 0, 4)]
    public void Relative_cursor_moves(string sequence, int column, int row)
    {
        var screen = new ScreenBuffer(10, 6, 0);
        var terminal = new VtTerminal(screen, C64Theme.Default);
        terminal.Feed("\x1B[4;6H" + sequence);

        Assert.Equal((column, row), screen.Cursor());
    }

    [Fact]
    public void Erase_sequences_clear_the_screen_and_lines()
    {
        Feed("AAAA\r\nBBBB\x1B[2D\x1B[K");
        Assert.Equal("BB", _screen.RowText(1));

        Feed("\x1B[2J");
        Assert.Equal("", _screen.RowText(0));
    }

    [Fact]
    public void Insert_delete_and_erase_characters()
    {
        Feed("ABCDEF\x1B[1;2H\x1B[2@");
        Assert.Equal("A  BCDEF", _screen.RowText(0));

        Feed("\x1B[3P");
        Assert.Equal("ACDEF", _screen.RowText(0));

        Feed("\x1B[2X");
        Assert.Equal("A  EF", _screen.RowText(0));
    }

    [Fact]
    public void Sgr_sets_c64_colours_and_reset_restores_theme_colour()
    {
        Feed("\x1B[31mR\x1B[1;32mG\x1B[0mD");

        Assert.Equal(C64Palette.Red, _screen[0, 0].Foreground);
        Assert.Equal(C64Palette.LightGreen, _screen[1, 0].Foreground);
        Assert.Equal(C64Theme.Default.Foreground, _screen[2, 0].Foreground);
    }

    [Fact]
    public void Sgr_reverse_video()
    {
        Feed("\x1B[7mR\x1B[27mN");

        Assert.True(_screen[0, 0].Reverse);
        Assert.False(_screen[1, 0].Reverse);
    }

    [Fact]
    public void Sgr_extended_colours_consume_their_arguments()
    {
        // 38;2;r;g;b must not be read as "2 = dim, 31 = red" etc.
        Feed("\x1B[38;2;31;32;33mA\x1B[38;5;9mB\x1B[48;5;1;33mC");

        Assert.Equal(C64Palette.Black, _screen[0, 0].Foreground);
        Assert.Equal(C64Palette.LightRed, _screen[1, 0].Foreground);
        Assert.Equal(C64Palette.Orange, _screen[2, 0].Foreground);
        Assert.Equal(C64Palette.Red, _screen[2, 0].Background);
    }

    [Fact]
    public void Cursor_can_be_hidden_and_shown()
    {
        Feed("\x1B[?25l");
        Assert.False(_terminal.CursorVisible);

        Feed("\x1B[?25h");
        Assert.True(_terminal.CursorVisible);
    }

    [Fact]
    public void Unknown_private_modes_are_ignored()
    {
        Feed("\x1B[?9001h\x1B[?1004hOK");

        Assert.Equal("OK", _screen.RowText(0));
    }

    [Fact]
    public void Osc_sets_title_with_bel_or_st()
    {
        string? changed = null;
        _terminal.TitleChanged += t => changed = t;

        Feed("\x1B]0;C:\\WINDOWS\\cmd.exe\x07");
        Assert.Equal("C:\\WINDOWS\\cmd.exe", changed);

        Feed("\x1B]2;Second\x1B\\X");
        Assert.Equal("Second", _terminal.Title);
        Assert.Equal("X", _screen.RowText(0));
    }

    [Fact]
    public void Ignores_dcs_strings_and_charset_selection()
    {
        Feed("\x1BPqignored\x1B\\\x1B(BOK");

        Assert.Equal("OK", _screen.RowText(0));
    }

    [Fact]
    public void Reports_cursor_position()
    {
        Feed("\x1B[2;3H\x1B[6n");

        Assert.Equal(["\x1B[2;3R"], _responses);
    }

    [Fact]
    public void Sequences_can_be_split_across_feeds()
    {
        Feed("\x1B");
        Feed("[3");
        Feed(";4");
        Feed("HZ");

        Assert.Equal('Z', _screen[3, 2].Char);
    }

    [Fact]
    public void Save_and_restore_cursor()
    {
        Feed("\x1B[2;2H\x1B" + "7\x1B[H\x1B" + "8X");

        Assert.Equal('X', _screen[1, 1].Char);
    }

    [Fact]
    public void Reverse_index_at_top_scrolls_down()
    {
        Feed("TOP\x1B[H\x1BM");

        Assert.Equal("", _screen.RowText(0));
        Assert.Equal("TOP", _screen.RowText(1));
    }

    [Fact]
    public void Full_width_line_then_crlf_matches_conpty_expectations()
    {
        Feed("0123456789\r\nNEXT");

        Assert.Equal("0123456789", _screen.RowText(0));
        Assert.Equal("NEXT", _screen.RowText(1));
    }

    [Fact]
    public void Control_characters_inside_csi_are_executed()
    {
        Feed("AB\x1B[\r1CX");

        Assert.Equal("AX", _screen.RowText(0));
    }

    [Fact]
    public void Surrogate_pair_takes_one_cell()
    {
        Feed("a\U0001F600b");

        Assert.Equal('b', _screen[2, 0].Char);
    }
}
