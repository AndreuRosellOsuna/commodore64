namespace C64Console.Core;

/// <summary>What the shell prompt looks like.</summary>
public enum PromptStyle
{
    /// <summary>The C64's <c>READY.</c> on its own line, with the cursor below it.</summary>
    Ready,

    /// <summary>The shell's normal prompt (e.g. <c>C:\Users\me&gt;</c>).</summary>
    Classic,
}

/// <summary>Builds the command lines used to start a shell inside the console.</summary>
public static class ShellCommands
{
    /// <summary>
    /// cmd's PROMPT for <see cref="PromptStyle.Ready"/>: <c>READY.</c> then a line break (<c>$_</c>).
    /// cmd already prints an empty line before each prompt, which puts <c>READY.</c> where the C64 has it.
    /// </summary>
    public const string CmdReadyPrompt = "READY.$_";

    /// <summary>
    /// Starts cmd.exe and has it print the C64 boot banner first. The shell prints the banner itself,
    /// so it survives ConPTY's screen repaints and scrolls away naturally.
    /// With <see cref="PromptStyle.Ready"/> cmd's prompt prints the <c>READY.</c>, so the banner leaves it out.
    /// </summary>
    public static string CmdWithBanner(string cmdPath, int columns, PromptStyle prompt = PromptStyle.Classic)
    {
        var lines = BootScreen.Lines(columns);
        var commands = new List<string>();
        if (prompt == PromptStyle.Ready)
        {
            // The banner ends with an empty line and READY.; cmd's empty line and prompt provide both.
            lines = lines.Take(lines.Count - 2).ToList();
            commands.Add("prompt " + CmdReadyPrompt);
        }

        // "echo(" prints its text verbatim, including an empty line. The banner contains no cmd special characters.
        commands.AddRange(lines.Select(line => "echo(" + line));

        // /s /k "...": cmd strips the outer quotes and runs the rest, then stays open.
        return $"\"{cmdPath}\" /s /k \"{string.Join("&", commands)}\"";
    }
}
