using C64Console.Core;

namespace C64Console.Core.Tests;

public class BootScreenTests
{
    private static string RowText(ScreenBuffer screen, int row) =>
        new string(Enumerable.Range(0, screen.Columns).Select(c => screen[c, row].Char).ToArray()).TrimEnd();

    [Fact]
    public void Shows_banner_like_a_real_c64()
    {
        var screen = new ScreenBuffer(40, 25, C64Palette.LightBlue);
        BootScreen.Show(screen);

        Assert.Equal("", RowText(screen, 0));
        Assert.Equal("    **** COMMODORE 64 BASIC V2 ****", RowText(screen, 1));
        Assert.Equal("", RowText(screen, 2));
        Assert.Equal(" 64K RAM SYSTEM  38911 BASIC BYTES FREE", RowText(screen, 3));
        Assert.Equal("", RowText(screen, 4));
        Assert.Equal("READY.", RowText(screen, 5));
        Assert.Equal((0, 6), (screen.CursorColumn, screen.CursorRow));
    }

    [Fact]
    public void Centres_banner_on_wide_screens()
    {
        var screen = new ScreenBuffer(80, 25, C64Palette.LightBlue);
        BootScreen.Show(screen);

        Assert.StartsWith(new string(' ', 24) + "****", RowText(screen, 1));
        Assert.Equal("READY.", RowText(screen, 5));
    }
}
