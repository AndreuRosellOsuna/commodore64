namespace C64Console.Core;

/// <summary>Writes the C64 power-on banner and leaves the cursor below <c>READY.</c>.</summary>
public static class BootScreen
{
    // Laid out for the 40-column screen, as on the real machine.
    private const int OriginalColumns = 40;
    private const string Title = "    **** COMMODORE 64 BASIC V2 ****";
    private const string Memory = " 64K RAM SYSTEM  38911 BASIC BYTES FREE";

    public const string Ready = "READY.";

    public static void Show(ScreenBuffer screen)
    {
        // On wider screens, shift the banner so it stays centred.
        var indent = new string(' ', Math.Max(0, (screen.Columns - OriginalColumns) / 2));

        screen.Clear();
        screen.NewLine();
        screen.WriteLine(indent + Title);
        screen.NewLine();
        screen.WriteLine(indent + Memory);
        screen.NewLine();
        screen.WriteLine(Ready);
    }
}
