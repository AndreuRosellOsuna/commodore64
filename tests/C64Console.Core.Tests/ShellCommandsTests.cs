using C64Console.Core;

namespace C64Console.Core.Tests;

public class ShellCommandsTests
{
    [Fact]
    public void Cmd_prints_the_banner_then_stays_open()
    {
        var command = ShellCommands.CmdWithBanner(@"C:\Windows\system32\cmd.exe", 40);

        Assert.Equal(
            "\"C:\\Windows\\system32\\cmd.exe\" /s /k \"echo(&echo(    **** COMMODORE 64 BASIC V2 ****&echo(&" +
            "echo( 64K RAM SYSTEM  38911 BASIC BYTES FREE&echo(&echo(READY.\"",
            command);
    }

    [Fact]
    public void Ready_prompt_sets_cmd_prompt_and_leaves_ready_to_it()
    {
        var command = ShellCommands.CmdWithBanner("cmd.exe", 40, PromptStyle.Ready);

        Assert.Equal(
            "\"cmd.exe\" /s /k \"prompt READY.$_&echo(&echo(    **** COMMODORE 64 BASIC V2 ****&echo(&" +
            "echo( 64K RAM SYSTEM  38911 BASIC BYTES FREE\"",
            command);
    }

    [Fact]
    public void Banner_has_no_characters_cmd_would_interpret()
    {
        foreach (var line in BootScreen.Lines(80))
        {
            Assert.DoesNotContain(line, c => "&|<>^%!\"()".Contains(c));
        }
    }
}
