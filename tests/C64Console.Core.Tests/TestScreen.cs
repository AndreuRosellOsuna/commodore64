using C64Console.Core;

namespace C64Console.Core.Tests;

internal static class TestScreen
{
    public static string RowText(this ScreenBuffer screen, int row) =>
        new string(Enumerable.Range(0, screen.Columns).Select(c => screen[c, row].Char).ToArray()).TrimEnd();

    public static (int Column, int Row) Cursor(this ScreenBuffer screen) => (screen.CursorColumn, screen.CursorRow);
}
