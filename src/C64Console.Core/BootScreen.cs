namespace C64Console.Core;

/// <summary>Writes the C64 power-on banner and leaves the cursor below <c>READY.</c>.</summary>
public static class BootScreen
{
    // Laid out for the 40-column screen, as on the real machine.
    private const int OriginalColumns = 40;
    private const string Title = "    **** COMMODORE 64 BASIC V2 ****";
    private const string Memory = " 64K RAM SYSTEM  38911 BASIC BYTES FREE";

    public const string Ready = "READY.";

    /// <summary>The banner lines, from the top of the screen down to <c>READY.</c>.</summary>
    public static IReadOnlyList<string> Lines(int columns)
    {
        // On wider screens, shift the banner so it stays centred.
        var indent = new string(' ', Math.Max(0, (columns - OriginalColumns) / 2));
        return ["", indent + Title, "", indent + Memory, "", Ready];
    }

    public static void Show(ScreenBuffer screen)
    {
        screen.Clear();
        foreach (var line in Lines(screen.Columns))
        {
            screen.WriteLine(line);
        }
    }
}
