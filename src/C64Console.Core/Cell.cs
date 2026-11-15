namespace C64Console.Core;

/// <summary>
/// One character position on the screen. <see cref="Background"/> is a C64 palette index,
/// or <see cref="DefaultBackground"/> to use the theme's background colour.
/// </summary>
public readonly record struct Cell(char Char, byte Foreground, bool Reverse = false, byte Background = Cell.DefaultBackground)
{
    /// <summary>Marks a cell that has no background colour of its own.</summary>
    public const byte DefaultBackground = 0xFF;

    public static Cell Blank(byte foreground, byte background = DefaultBackground) => new(' ', foreground, false, background);
}
