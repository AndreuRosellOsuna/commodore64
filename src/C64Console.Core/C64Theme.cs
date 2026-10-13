namespace C64Console.Core;

/// <summary>Border, background and default text colour, as C64 palette indexes.</summary>
public sealed record C64Theme(byte Border, byte Background, byte Foreground)
{
    /// <summary>The C64 power-on colours: light blue border and text on a blue background.</summary>
    public static C64Theme Default { get; } = new(C64Palette.LightBlue, C64Palette.Blue, C64Palette.LightBlue);
}
