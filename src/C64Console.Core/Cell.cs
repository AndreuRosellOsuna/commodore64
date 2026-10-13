namespace C64Console.Core;

/// <summary>One character position on the screen.</summary>
public readonly record struct Cell(char Char, byte Foreground, bool Reverse = false)
{
    public static Cell Blank(byte foreground) => new(' ', foreground);
}
