using C64Console.Core;

namespace C64Console.Core.Tests;

public class FrameRendererTests
{
    private static readonly C64Theme Theme = C64Theme.Default;
    private static readonly uint Border = C64Palette.ToArgb(Theme.Border);
    private static readonly uint Background = C64Palette.ToArgb(Theme.Background);
    private static readonly uint Text = C64Palette.ToArgb(Theme.Foreground);

    [Fact]
    public void C64_screen_is_384_by_272()
    {
        var renderer = new FrameRenderer(40, 25);

        Assert.Equal((384, 272), (renderer.Width, renderer.Height));
    }

    [Fact]
    public void Draws_border_background_and_glyph_pixels()
    {
        var renderer = new FrameRenderer(40, 25);
        var screen = new ScreenBuffer(40, 25, Theme.Foreground);
        screen.Write('|'); // column 3-4 of each row is lit
        var frame = renderer.CreateFrame();

        renderer.Render(screen, Theme, cursorVisible: false, frame);

        uint At(int x, int y) => frame[y * renderer.Width + x];
        var x0 = FrameRenderer.DefaultBorderX;
        var y0 = FrameRenderer.DefaultBorderY;
        Assert.Equal(Border, At(0, 0));
        Assert.Equal(Border, At(x0 - 1, y0));
        Assert.Equal(Background, At(x0, y0));
        Assert.Equal(Text, At(x0 + 3, y0));
        Assert.Equal(Background, At(x0 + 3, y0 + 7));
    }

    [Fact]
    public void Visible_cursor_inverts_its_cell()
    {
        var renderer = new FrameRenderer(40, 25);
        var screen = new ScreenBuffer(40, 25, Theme.Foreground);
        var frame = renderer.CreateFrame();

        renderer.Render(screen, Theme, cursorVisible: true, frame);
        var cursorPixel = frame[FrameRenderer.DefaultBorderY * renderer.Width + FrameRenderer.DefaultBorderX];
        renderer.Render(screen, Theme, cursorVisible: false, frame);
        var hiddenPixel = frame[FrameRenderer.DefaultBorderY * renderer.Width + FrameRenderer.DefaultBorderX];

        Assert.Equal(Text, cursorPixel);
        Assert.Equal(Background, hiddenPixel);
    }

    [Fact]
    public void Rejects_mismatched_screen()
    {
        var renderer = new FrameRenderer(40, 25);
        var screen = new ScreenBuffer(80, 25, 0);

        Assert.Throws<ArgumentException>(() => renderer.Render(screen, Theme, false, renderer.CreateFrame()));
    }
}
