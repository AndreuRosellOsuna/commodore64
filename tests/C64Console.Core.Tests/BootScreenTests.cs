using C64Console.Core;

namespace C64Console.Core.Tests;

public class BootScreenTests
{
    [Fact]
    public void Shows_banner_like_a_real_c64()
    {
        var screen = new ScreenBuffer(40, 25, C64Palette.LightBlue);
        BootScreen.Show(screen);

        Assert.Equal("", screen.RowText(0));
        Assert.Equal("    **** COMMODORE 64 BASIC V2 ****", screen.RowText(1));
        Assert.Equal("", screen.RowText(2));
        Assert.Equal(" 64K RAM SYSTEM  38911 BASIC BYTES FREE", screen.RowText(3));
        Assert.Equal("", screen.RowText(4));
        Assert.Equal("READY.", screen.RowText(5));
        Assert.Equal((0, 6), screen.Cursor());
    }

    [Fact]
    public void Centres_banner_on_wide_screens()
    {
        var screen = new ScreenBuffer(80, 25, C64Palette.LightBlue);
        BootScreen.Show(screen);

        Assert.StartsWith(new string(' ', 24) + "****", screen.RowText(1));
        Assert.Equal("READY.", screen.RowText(5));
    }
}
