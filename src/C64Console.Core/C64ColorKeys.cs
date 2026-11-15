namespace C64Console.Core;

/// <summary>
/// On the C64, CTRL+1…8 pick colours 0–7 (black, white, red, cyan, purple, green, blue, yellow) and
/// the Commodore key + 1…8 pick colours 8–15 (orange, brown, light red, dark grey, grey, light green,
/// light blue, light grey). Here Ctrl+F1…F8 and Ctrl+Shift+F1…F8 do the same.
/// </summary>
public static class C64ColorKeys
{
    /// <summary>The palette index for function key <paramref name="number"/> (1–8), or null for other keys.</summary>
    public static byte? ColorFor(int number, bool commodoreKey) =>
        number is >= 1 and <= 8 ? (byte)(number - 1 + (commodoreKey ? 8 : 0)) : null;
}
