using C64Console.Core;

namespace C64Console.Core.Tests;

public class CursorBlinkTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(300, true)]
    [InlineData(350, false)]
    [InlineData(650, false)]
    [InlineData(700, true)]
    public void Blinks_every_third_of_a_second(int milliseconds, bool visible)
    {
        Assert.Equal(visible, CursorBlink.IsVisible(TimeSpan.FromMilliseconds(milliseconds)));
    }
}
