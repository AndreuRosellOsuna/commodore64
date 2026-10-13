namespace C64Console.Core;

/// <summary>Builds the command lines used to start a shell inside the console.</summary>
public static class ShellCommands
{
    /// <summary>
    /// Starts cmd.exe and has it print the C64 boot banner first. The shell prints the banner itself,
    /// so it survives ConPTY's screen repaints and scrolls away naturally.
    /// </summary>
    public static string CmdWithBanner(string cmdPath, int columns)
    {
        // "echo(" prints its text verbatim, including an empty line. The banner contains no cmd special characters.
        var echoes = string.Join("&", BootScreen.Lines(columns).Select(line => "echo(" + line));

        // /s /k "...": cmd strips the outer quotes and runs the rest, then stays open.
        return $"\"{cmdPath}\" /s /k \"{echoes}\"";
    }
}
