using C64Console.Core;

namespace C64Console.Core.Tests;

/// <summary>Layouts, the CRT filter, the C64 colour keys and theme changes.</summary>
public class Milestone4Tests
{
    [Fact]
    public void Authentic_layout_is_always_40_by_25_with_the_largest_scale_that_fits()
    {
        Assert.Equal((40, 25, 3), ScreenLayout.Fit(Layout.Authentic, 384 * 3 + 50, 272 * 3 + 10, scale: 1));
        Assert.Equal((40, 25, 1), ScreenLayout.Fit(Layout.Authentic, 100, 100, scale: 4));
    }

    [Fact]
    public void Modern_layout_keeps_the_scale_and_fills_the_window_with_cells()
    {
        // 80×25 at scale 2 needs (80*8 + 64) × (25*8 + 72) = 704 × 272 frame pixels.
        Assert.Equal((80, 25, 2), ScreenLayout.Fit(Layout.Modern, 704 * 2, 272 * 2, scale: 2));
        Assert.Equal((80, 25, 2), ScreenLayout.Fit(Layout.Modern, 704 * 2 + 15, 272 * 2 + 15, scale: 2));
        Assert.Equal((81, 26, 2), ScreenLayout.Fit(Layout.Modern, 704 * 2 + 16, 272 * 2 + 16, scale: 2));
    }

    [Fact]
    public void Modern_layout_survives_rounding_and_has_a_minimum_size()
    {
        Assert.Equal((40, 25, 1), ScreenLayout.Fit(Layout.Modern, 383.999, 271.999, scale: 1));
        Assert.Equal((ScreenLayout.MinColumns, ScreenLayout.MinRows, 3), ScreenLayout.Fit(Layout.Modern, 10, 10, scale: 3));
    }

    [Theory]
    [InlineData(1, false, C64Palette.Black)]
    [InlineData(2, false, C64Palette.White)]
    [InlineData(7, false, C64Palette.Blue)]
    [InlineData(8, false, C64Palette.Yellow)]
    [InlineData(1, true, C64Palette.Orange)]
    [InlineData(7, true, C64Palette.LightBlue)]
    [InlineData(8, true, C64Palette.LightGrey)]
    public void Colour_keys_follow_the_c64_keyboard(int key, bool commodore, byte expected)
    {
        Assert.Equal(expected, C64ColorKeys.ColorFor(key, commodore));
    }

    [Fact]
    public void Colour_keys_ignore_other_function_keys()
    {
        Assert.Null(C64ColorKeys.ColorFor(9, false));
        Assert.Null(C64ColorKeys.ColorFor(0, true));
    }

    [Fact]
    public void Changing_the_theme_colours_new_default_text_only()
    {
        var screen = new ScreenBuffer(10, 2, C64Theme.Default.Foreground);
        var terminal = new VtTerminal(screen, C64Theme.Default);
        terminal.Feed("A");

        terminal.Theme = C64Theme.Default with { Foreground = C64Palette.Yellow };
        terminal.Feed("B\x1B[31mC\x1B[0mD");

        Assert.Equal(C64Palette.LightBlue, screen[0, 0].Foreground);
        Assert.Equal(C64Palette.Yellow, screen[1, 0].Foreground);
        Assert.Equal(C64Palette.Red, screen[2, 0].Foreground);
        Assert.Equal(C64Palette.Yellow, screen[3, 0].Foreground);
    }

    [Fact]
    public void Changing_the_theme_keeps_a_pending_wrap()
    {
        var screen = new ScreenBuffer(3, 2, C64Theme.Default.Foreground);
        var terminal = new VtTerminal(screen, C64Theme.Default);
        terminal.Feed("ABC");

        terminal.Theme = C64Theme.Default with { Foreground = C64Palette.White };
        terminal.Feed("D");

        Assert.Equal("ABC", screen.RowText(0));
        Assert.Equal("D", screen.RowText(1));
    }

    [Fact]
    public void Crt_output_is_the_source_scaled_up()
    {
        var crt = new CrtFilter(384, 272, 3);

        Assert.Equal((1152, 816), (crt.Width, crt.Height));
        Assert.Equal(1152 * 816, crt.CreateOutput().Length);
    }

    [Fact]
    public void Crt_centre_is_undistorted_and_full_brightness()
    {
        var source = Enumerable.Repeat(0xFFFFFFFFu, 100 * 100).ToArray();
        var crt = new CrtFilter(100, 100, 3);
        var output = crt.CreateOutput();

        crt.Apply(source, output);

        // The top row of a pixel at the centre is a bright (non-scanline) row.
        Assert.Equal((50, 50), crt.SourcePixelAt(150, 150));
        Assert.Equal(0xFFFFFFFFu, output[150 * crt.Width + 150]);
    }

    [Fact]
    public void Crt_draws_darker_scanlines_between_rows()
    {
        var source = Enumerable.Repeat(0xFFFFFFFFu, 100 * 100).ToArray();
        var crt = new CrtFilter(100, 100, 3);
        var output = crt.CreateOutput();

        crt.Apply(source, output);

        var bright = output[150 * crt.Width + 150] & 0xFF;
        var gap = output[152 * crt.Width + 150] & 0xFF; // third output row of source row 50
        Assert.True(gap < bright * 0.7, $"scanline {gap} vs {bright}");
    }

    [Fact]
    public void Crt_corners_are_black_and_edges_are_darker()
    {
        var source = Enumerable.Repeat(0xFFFFFFFFu, 100 * 100).ToArray();
        var crt = new CrtFilter(100, 100, 2);
        var output = crt.CreateOutput();

        crt.Apply(source, output);

        Assert.Null(crt.SourcePixelAt(0, 0));
        Assert.Equal(0xFF000000u, output[0]);
        var centre = output[100 * crt.Width + 100] & 0xFF;
        var edge = output[100 * crt.Width + 3] & 0xFF;
        Assert.True(edge < centre, $"edge {edge} vs centre {centre}");
    }

    [Fact]
    public void Crt_at_scale_1_has_no_scanlines()
    {
        var source = Enumerable.Repeat(0xFFFFFFFFu, 10 * 10).ToArray();
        var crt = new CrtFilter(10, 10, 1);
        var output = crt.CreateOutput();

        crt.Apply(source, output);

        Assert.Equal(output[5 * 10 + 5], output[4 * 10 + 5]);
    }
}
