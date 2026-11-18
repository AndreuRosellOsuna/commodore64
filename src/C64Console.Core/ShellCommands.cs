using System.Text;

namespace C64Console.Core;

/// <summary>Which shell runs inside the console.</summary>
public enum ShellKind
{
    /// <summary>cmd.exe (<c>%ComSpec%</c>).</summary>
    Cmd,

    /// <summary>Windows PowerShell 5 (<c>powershell.exe</c>).</summary>
    PowerShell,

    /// <summary>PowerShell 7 (<c>pwsh.exe</c>), if installed.</summary>
    Pwsh,

    /// <summary>The default WSL distribution (<c>wsl.exe</c>), if installed.</summary>
    Wsl,
}

/// <summary>What the shell prompt looks like.</summary>
public enum PromptStyle
{
    /// <summary>The C64's <c>READY.</c> on its own line, with the cursor below it.</summary>
    Ready,

    /// <summary>The shell's normal prompt (e.g. <c>C:\Users\me&gt;</c>).</summary>
    Classic,
}

/// <summary>
/// Builds the command lines used to start a shell inside the console. Each shell prints the C64 boot banner
/// itself, so it survives ConPTY's screen repaints and scrolls away naturally.
/// </summary>
public static class ShellCommands
{
    /// <summary>
    /// cmd's PROMPT for <see cref="PromptStyle.Ready"/>: <c>READY.</c> then a line break (<c>$_</c>).
    /// cmd already prints an empty line before each prompt, which puts <c>READY.</c> where the C64 has it.
    /// </summary>
    public const string CmdReadyPrompt = "READY.$_";

    /// <summary>PowerShell's prompt function for <see cref="PromptStyle.Ready"/>: an empty line, <c>READY.</c>, then the cursor below.</summary>
    public const string PowerShellReadyPrompt = "function global:prompt { \"`nREADY.`n\" }";

    /// <summary>The command line for <paramref name="shell"/>. <paramref name="comSpec"/> is cmd's path (<c>%ComSpec%</c>).</summary>
    public static string For(ShellKind shell, int columns, PromptStyle prompt, string? comSpec = null) => shell switch
    {
        ShellKind.PowerShell => PowerShellWithBanner("powershell.exe", columns, prompt),
        ShellKind.Pwsh => PowerShellWithBanner("pwsh.exe", columns, prompt),
        ShellKind.Wsl => WslWithBanner(columns),
        _ => CmdWithBanner(comSpec ?? "cmd.exe", columns, prompt),
    };

    /// <summary>The name shown in the settings screen.</summary>
    public static string DisplayName(ShellKind shell) => shell switch
    {
        ShellKind.PowerShell => "POWERSHELL",
        ShellKind.Pwsh => "PWSH 7",
        ShellKind.Wsl => "WSL",
        _ => "CMD",
    };

    /// <summary>
    /// Starts cmd.exe and has it print the banner first.
    /// With <see cref="PromptStyle.Ready"/> cmd's prompt prints the <c>READY.</c>, so the banner leaves it out.
    /// </summary>
    public static string CmdWithBanner(string cmdPath, int columns, PromptStyle prompt = PromptStyle.Classic)
    {
        var commands = new List<string>();
        if (prompt == PromptStyle.Ready)
        {
            commands.Add("prompt " + CmdReadyPrompt);
        }

        // "echo(" prints its text verbatim, including an empty line. The banner contains no cmd special characters.
        commands.AddRange(BannerLines(columns, prompt).Select(line => "echo(" + line));

        // /s /k "...": cmd strips the outer quotes and runs the rest, then stays open.
        return $"\"{cmdPath}\" /s /k \"{string.Join("&", commands)}\"";
    }

    /// <summary>
    /// Starts PowerShell with the banner and, for <see cref="PromptStyle.Ready"/>, the READY. prompt.
    /// The script is passed with -EncodedCommand (Base64 of UTF-16), so it needs no quoting at all.
    /// </summary>
    public static string PowerShellWithBanner(string executable, int columns, PromptStyle prompt)
    {
        var script = new StringBuilder();
        foreach (var line in BannerLines(columns, prompt))
        {
            script.Append("Write-Host '").Append(line.Replace("'", "''")).Append("'\n");
        }

        if (prompt == PromptStyle.Ready)
        {
            script.Append(PowerShellReadyPrompt).Append('\n');
        }

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script.ToString()));
        return $"{executable} -NoLogo -NoExit -EncodedCommand {encoded}";
    }

    /// <summary>
    /// Starts the default WSL distribution in the Linux home directory: <c>printf</c> shows the banner, then the
    /// user's own login shell takes over. That shell sets its own prompt, so the banner always ends with READY.
    /// </summary>
    public static string WslWithBanner(int columns)
    {
        // Inside single quotes nothing is special to sh; printf turns \n into line breaks.
        var banner = string.Concat(BootScreen.Lines(columns).Select(line => line + "\\n"));
        return $"wsl.exe --cd ~ -e sh -c \"printf '{banner}'; exec \\\"${{SHELL:-/bin/sh}}\\\" -l\"";
    }

    /// <summary>The banner, without its final empty line and READY. when the prompt prints those.</summary>
    private static IReadOnlyList<string> BannerLines(int columns, PromptStyle prompt)
    {
        var lines = BootScreen.Lines(columns);
        return prompt == PromptStyle.Ready ? lines.Take(lines.Count - 2).ToList() : lines;
    }
}
