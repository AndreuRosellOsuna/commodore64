using C64Console.Core;

namespace C64Console.Core.Tests;

/// <summary>Scroll regions, scrollback, the alternate screen, resize, colours, modes and selection.</summary>
public class Milestone3Tests
{
    private static (ScreenBuffer Screen, VtTerminal Terminal) Create(int columns = 10, int rows = 5, int scrollback = 100)
    {
        var screen = new ScreenBuffer(columns, rows, C64Theme.Default.Foreground, scrollback);
        return (screen, new VtTerminal(screen, C64Theme.Default));
    }

    [Fact]
    public void Line_feed_at_bottom_margin_scrolls_only_the_region()
    {
        var (screen, terminal) = Create();
        terminal.Feed("HEAD\r\nA\r\nB\r\nC\r\nFOOT");
        terminal.Feed("\x1B[2;4r");           // region = rows 2..4 (1-based)
        terminal.Feed("\x1B[4;1H\nNEW");     // line feed on the bottom margin

        Assert.Equal("HEAD", screen.RowText(0));
        Assert.Equal("B", screen.RowText(1));
        Assert.Equal("C", screen.RowText(2));
        Assert.Equal("NEW", screen.RowText(3));
        Assert.Equal("FOOT", screen.RowText(4));
        Assert.Equal(0, screen.ScrollbackCount); // region scrolls never reach the scrollback
    }

    [Fact]
    public void Reverse_index_at_top_margin_scrolls_region_down()
    {
        var (screen, terminal) = Create();
        terminal.Feed("0\r\n1\r\n2\r\n3\r\n4\x1B[2;4r\x1B[2;1H\x1BM");

        Assert.Equal(["0", "", "1", "2", "4"], Enumerable.Range(0, 5).Select(screen.RowText));
    }

    [Fact]
    public void Insert_and_delete_lines_stay_inside_the_region()
    {
        var (screen, terminal) = Create();
        terminal.Feed("0\r\n1\r\n2\r\n3\r\n4\x1B[1;3r\x1B[1;1H\x1B[L");
        Assert.Equal(["", "0", "1", "3", "4"], Enumerable.Range(0, 5).Select(screen.RowText));

        terminal.Feed("\x1B[2M");
        Assert.Equal(["1", "", "", "3", "4"], Enumerable.Range(0, 5).Select(screen.RowText));
    }

    [Fact]
    public void Scroll_up_and_down_use_the_region()
    {
        var (screen, terminal) = Create();
        terminal.Feed("0\r\n1\r\n2\r\n3\r\n4\x1B[2;4r\x1B[S");
        Assert.Equal(["0", "2", "3", "", "4"], Enumerable.Range(0, 5).Select(screen.RowText));

        terminal.Feed("\x1B[2T");
        Assert.Equal(["0", "", "", "2", "4"], Enumerable.Range(0, 5).Select(screen.RowText));
    }

    [Fact]
    public void Setting_the_region_homes_the_cursor_and_reset_restores_full_screen()
    {
        var (screen, terminal) = Create();
        terminal.Feed("\x1B[3;3H\x1B[2;4r");
        Assert.Equal((0, 0), screen.Cursor());
        Assert.Equal((1, 3), (screen.ScrollTop, screen.ScrollBottom));

        terminal.Feed("\x1B[r");
        Assert.Equal((0, 4), (screen.ScrollTop, screen.ScrollBottom));
    }

    [Fact]
    public void Origin_mode_positions_relative_to_the_region()
    {
        var (screen, terminal) = Create();
        terminal.Feed("\x1B[2;4r\x1B[?6h\x1B[1;3H");
        Assert.Equal((2, 1), screen.Cursor());

        terminal.Feed("\x1B[9;1H"); // clamped to the bottom margin
        Assert.Equal((0, 3), screen.Cursor());
    }

    [Fact]
    public void Cursor_up_stops_at_the_top_margin()
    {
        var (screen, terminal) = Create();
        terminal.Feed("\x1B[2;4r\x1B[3;1H\x1B[9A");

        Assert.Equal(1, screen.CursorRow);
    }

    [Fact]
    public void Lines_scrolled_off_the_top_go_to_the_scrollback()
    {
        var (screen, terminal) = Create(rows: 3);
        terminal.Feed("L1\r\nL2\r\nL3\r\nL4\r\nL5");

        Assert.Equal(2, screen.ScrollbackCount);
        Assert.Equal(2, screen.LinesScrolledOff);
        Assert.Equal('L', screen.GetCell(0, 0).Char);
        Assert.Equal('1', screen.GetCell(1, 0).Char);
        Assert.Equal('3', screen.GetViewCell(1, 0, 0).Char);
        Assert.Equal('1', screen.GetViewCell(1, 0, 2).Char); // scrolled back two lines
    }

    [Fact]
    public void Scrollback_drops_the_oldest_lines_at_its_limit()
    {
        var (screen, terminal) = Create(rows: 2, scrollback: 3);
        for (var i = 0; i < 10; i++)
        {
            terminal.Feed($"L{i}\r\n");
        }

        Assert.Equal(3, screen.ScrollbackCount);
        Assert.Equal(9, screen.LinesScrolledOff);
        Assert.Equal("L6", screen.GetText(new(screen.FirstLine, 0), new(screen.FirstLine, 9)));
    }

    [Fact]
    public void Erase_display_3_clears_only_the_scrollback()
    {
        var (screen, terminal) = Create(rows: 2);
        terminal.Feed("A\r\nB\r\nC\x1B[3J");

        Assert.Equal(0, screen.ScrollbackCount);
        Assert.Equal("C", screen.RowText(1));
    }

    [Fact]
    public void Alternate_screen_1049_keeps_main_screen_and_cursor()
    {
        var (screen, terminal) = Create();
        terminal.Feed("MAIN\x1B[?1049h");
        Assert.True(screen.IsAlternateScreen);
        Assert.Equal("", screen.RowText(0));

        terminal.Feed("\x1B[3;3HVIM\r\nmore\r\nlines\r\nand\r\nmore\r\nscrolling");
        Assert.Equal(0, screen.ScrollbackCount); // the alternate screen has no scrollback

        terminal.Feed("\x1B[?1049l");
        Assert.False(screen.IsAlternateScreen);
        Assert.Equal("MAIN", screen.RowText(0));
        Assert.Equal((4, 0), screen.Cursor());
    }

    [Fact]
    public void Alternate_screen_is_blank_each_time_it_is_entered()
    {
        var (screen, terminal) = Create();
        terminal.Feed("\x1B[?1049hOLD\x1B[?1049l\x1B[?1049h");

        Assert.Equal("", screen.RowText(0));
    }

    [Fact]
    public void Resize_keeps_the_cursor_line_and_moves_lines_above_into_scrollback()
    {
        var (screen, terminal) = Create(columns: 10, rows: 5);
        terminal.Feed("0\r\n1\r\n2\r\n3\r\n4");

        screen.Resize(6, 3);

        Assert.Equal((6, 3), (screen.Columns, screen.Rows));
        Assert.Equal(["2", "3", "4"], Enumerable.Range(0, 3).Select(screen.RowText));
        Assert.Equal(2, screen.ScrollbackCount);
        Assert.Equal(2, screen.CursorRow);
    }

    [Fact]
    public void Resize_wider_pads_with_blanks_and_resets_region()
    {
        var (screen, terminal) = Create(columns: 4, rows: 3);
        terminal.Feed("ABCD\x1B[1;2r");

        screen.Resize(8, 4);

        Assert.Equal("ABCD", screen.RowText(0));
        Assert.Equal((0, 3), (screen.ScrollTop, screen.ScrollBottom));
        terminal.Feed("\x1B[1;8HZ");
        Assert.Equal('Z', screen[7, 0].Char);
    }

    [Fact]
    public void Resize_while_on_alternate_screen_keeps_both_screens()
    {
        var (screen, terminal) = Create(columns: 6, rows: 3);
        terminal.Feed("MAIN\x1B[?1049h\x1B[HALT");

        screen.Resize(8, 4);
        Assert.Equal("ALT", screen.RowText(0));

        terminal.Feed("\x1B[?1049l");
        Assert.Equal("MAIN", screen.RowText(0));
    }

    [Fact]
    public void Background_colours_and_erase_use_current_background()
    {
        var (screen, terminal) = Create();
        terminal.Feed("\x1B[41mR\x1B[K\x1B[49mD");

        Assert.Equal(C64Palette.Red, screen[0, 0].Background);
        Assert.Equal(Cell.DefaultBackground, screen[1, 0].Background);
        Assert.Equal(C64Palette.Red, screen[5, 0].Background); // erased with the red background
        Assert.Equal(C64Palette.Blue, AnsiColors.ToC64Background(4));
    }

    [Theory]
    [InlineData(21, C64Palette.Blue)]       // pure blue in the 6×6×6 cube
    [InlineData(231, C64Palette.White)]
    [InlineData(16, C64Palette.Black)]
    [InlineData(244, C64Palette.Grey)]      // mid grey ramp
    [InlineData(226, C64Palette.Yellow)]
    public void Xterm_256_colours_map_to_nearest_c64_colour(int index, byte expected)
    {
        var (screen, terminal) = Create();
        terminal.Feed($"\x1B[38;5;{index}mX");

        Assert.Equal(expected, screen[0, 0].Foreground);
    }

    [Theory]
    [InlineData(0x70, 0x6D, 0xEB, C64Palette.LightBlue)]
    [InlineData(0xFF, 0xA5, 0x00, C64Palette.Orange)]
    [InlineData(0x80, 0x80, 0x80, C64Palette.Grey)]
    public void Truecolor_maps_to_nearest_c64_colour(int r, int g, int b, byte expected)
    {
        Assert.Equal(expected, C64Palette.Nearest((byte)r, (byte)g, (byte)b));
    }

    [Fact]
    public void Bold_does_not_brighten_256_colour_text()
    {
        var (screen, terminal) = Create();
        terminal.Feed("\x1B[1;38;5;21mX\x1B[39mY");

        Assert.Equal(C64Palette.Blue, screen[0, 0].Foreground);
        Assert.Equal(C64Theme.Default.Foreground, screen[1, 0].Foreground);
    }

    [Fact]
    public void Save_and_restore_cursor_includes_colours()
    {
        var (screen, terminal) = Create();
        terminal.Feed("\x1B[31;42m\x1B" + "7\x1B[0m\x1B" + "8X");

        Assert.Equal(C64Palette.Red, screen[0, 0].Foreground);
        Assert.Equal(C64Palette.Green, screen[0, 0].Background);
    }

    [Fact]
    public void Modes_for_cursor_keys_paste_and_autowrap()
    {
        var (screen, terminal) = Create(columns: 4);
        terminal.Feed("\x1B[?1h\x1B[?2004h\x1B[?7l");
        Assert.True(terminal.ApplicationCursorKeys);
        Assert.True(terminal.BracketedPaste);

        terminal.Feed("ABCDEF");
        Assert.Equal("ABCF", screen.RowText(0)); // no wrap: the last column is overwritten
        Assert.Equal("", screen.RowText(1));

        terminal.Feed("\x1B" + "c");
        Assert.False(terminal.ApplicationCursorKeys);
        Assert.False(terminal.BracketedPaste);
        Assert.True(screen.AutoWrap);
    }

    [Fact]
    public void Application_cursor_keys_use_ss3()
    {
        Assert.Equal("\x1BOA", KeyEncoder.Encode(TerminalKey.Up, applicationCursorKeys: true));
        Assert.Equal("\x1BOH", KeyEncoder.Encode(TerminalKey.Home, applicationCursorKeys: true));
        Assert.Equal("\x1B[1;5A", KeyEncoder.Encode(TerminalKey.Up, KeyModifiers.Control, applicationCursorKeys: true));
        Assert.Equal("\x1B[A", KeyEncoder.Encode(TerminalKey.Up));
    }

    [Fact]
    public void Paste_normalises_newlines_and_supports_bracketed_mode()
    {
        Assert.Equal("a\rb\rc", KeyEncoder.EncodePaste("a\r\nb\nc", bracketed: false));
        Assert.Equal("\x1B[200~x\ry\x1B[201~", KeyEncoder.EncodePaste("x\ny\x1B", bracketed: true));
    }

    [Fact]
    public void Get_text_spans_scrollback_and_screen_and_trims_lines()
    {
        var (screen, terminal) = Create(rows: 2);
        terminal.Feed("hello\r\nworld\r\nagain");

        var text = screen.GetText(new BufferPosition(0, 1), new BufferPosition(2, 2));

        Assert.Equal("ello\r\nworld\r\naga", text);
    }

    [Fact]
    public void Selection_contains_cells_in_reading_order_either_direction()
    {
        var selection = new TextSelection(new BufferPosition(5, 3), new BufferPosition(4, 7));

        Assert.True(selection.Contains(9, 4));
        Assert.True(selection.Contains(0, 5));
        Assert.True(selection.Contains(3, 5));
        Assert.False(selection.Contains(4, 5));
        Assert.False(selection.Contains(6, 4));
    }

    [Fact]
    public void Renderer_draws_scrolled_back_view_selection_and_backgrounds()
    {
        var theme = C64Theme.Default;
        var screen = new ScreenBuffer(4, 2, theme.Foreground);
        var terminal = new VtTerminal(screen, theme);
        terminal.Feed("\x1B[41m \x1B[0m\r\nB\r\nC");
        var renderer = new FrameRenderer(4, 2, 0, 0);
        var frame = renderer.CreateFrame();

        // Scrolled back by one line: the red-background line is at the top again.
        renderer.Render(screen, theme, cursorVisible: false, frame, scrollOffset: 1);
        Assert.Equal(C64Palette.ToArgb(C64Palette.Red), frame[0]);

        // A selected blank cell is drawn in reverse: text colour instead of background.
        renderer.Render(screen, theme, cursorVisible: false, frame, selection: new TextSelection(new(1, 3), new(1, 3)));
        Assert.Equal(C64Palette.ToArgb(theme.Foreground), frame[3 * 8]);
        Assert.Equal(C64Palette.ToArgb(theme.Background), frame[2 * 8]);
    }

    [Fact]
    public void Renderer_maps_pixels_to_cells()
    {
        var renderer = new FrameRenderer(40, 25);

        Assert.Equal((0, 0), renderer.CellAt(0, 0));
        Assert.Equal((1, 2), renderer.CellAt(FrameRenderer.DefaultBorderX + 8, FrameRenderer.DefaultBorderY + 16));
        Assert.Equal((39, 24), renderer.CellAt(10_000, 10_000));
    }
}
