using System.Text;
using C64Console.Core;

namespace C64Console.Core.Tests;

/// <summary>Sounds, settings, the settings screen and the choice of shell.</summary>
public class Milestone5Tests
{
    [Fact]
    public void Wav_header_describes_16_bit_mono_pcm()
    {
        var wav = Wave.ToWav([0, 1000, -1000], 22050);

        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(36 + 6, BitConverter.ToInt32(wav, 4));
        Assert.Equal("WAVEfmt ", Encoding.ASCII.GetString(wav, 8, 8));
        Assert.Equal(1, BitConverter.ToInt16(wav, 20));       // PCM
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));       // mono
        Assert.Equal(22050, BitConverter.ToInt32(wav, 24));
        Assert.Equal(16, BitConverter.ToInt16(wav, 34));
        Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));
        Assert.Equal(6, BitConverter.ToInt32(wav, 40));
        Assert.Equal(1000, BitConverter.ToInt16(wav, 46));
        Assert.Equal(Wave.HeaderSize + 6, wav.Length);
    }

    [Fact]
    public void Key_click_is_short_audible_and_fades_out()
    {
        var samples = C64Sounds.KeyClickSamples();

        Assert.InRange(samples.Length, C64Sounds.SampleRate / 100, C64Sounds.SampleRate / 40);
        Assert.Contains(samples, s => Math.Abs((int)s) > 3000);
        Assert.True(Math.Abs((int)samples[^1]) < 500);
    }

    [Fact]
    public void Startup_chirp_is_about_half_a_second_and_never_clips()
    {
        var samples = C64Sounds.StartupChirpSamples();

        Assert.InRange(samples.Length / (double)C64Sounds.SampleRate, 0.4, 0.6);
        Assert.All(samples, s => Assert.InRange((int)s, -short.MaxValue / 3, short.MaxValue / 3));
        Assert.Equal(Wave.HeaderSize + samples.Length * 2, C64Sounds.StartupChirp().Length);
    }

    [Fact]
    public void Settings_round_trip_through_json()
    {
        var settings = new AppSettings
        {
            Shell = ShellKind.Wsl, Layout = Layout.Authentic, Crt = true, Prompt = PromptStyle.Classic,
            TextColour = C64Palette.Yellow, KeyClicks = true, StartupSound = false, Columns = 100, Rows = 30,
        };

        var json = settings.ToJson();

        Assert.Contains("\"shell\": \"wsl\"", json);
        Assert.Equal(settings, AppSettings.Parse(json));
    }

    [Fact]
    public void Bad_or_missing_settings_fall_back_to_defaults()
    {
        Assert.Equal(new AppSettings(), AppSettings.Parse("not json"));
        Assert.Equal(new AppSettings(), AppSettings.Parse("null"));
        Assert.Equal(new AppSettings(), AppSettings.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "missing.json")));

        var odd = AppSettings.Parse("""{ "columns": 5000, "rows": 1, "textColour": 200, "layout": "authentic", "unknown": 1 }""");
        Assert.Equal((ScreenLayout.MaxColumns, ScreenLayout.MinRows), (odd.Columns, odd.Rows));
        Assert.Equal(200 & 0x0F, odd.TextColour);
        Assert.Equal(Layout.Authentic, odd.Layout);
    }

    [Fact]
    public void Settings_save_and_load_a_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "c64console-tests-" + Guid.NewGuid(), "settings.json");
        try
        {
            var settings = new AppSettings { Shell = ShellKind.Pwsh, TextColour = C64Palette.Cyan };
            settings.Save(path);
            settings.Save(path); // overwriting works too

            Assert.Equal(settings, AppSettings.Load(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Command_line_overrides_saved_settings()
    {
        var saved = new AppSettings { Shell = ShellKind.Cmd, Crt = true, KeyClicks = true };

        var run = saved.WithCommandLine(["C64Console.exe", "--shell", "PowerShell", "--no-crt", "--layout", "authentic", "--columns", "999", "--no-sound"]);

        Assert.Equal(ShellKind.PowerShell, run.Shell);
        Assert.False(run.Crt);
        Assert.False(run.KeyClicks);
        Assert.False(run.StartupSound);
        Assert.Equal(Layout.Authentic, run.Layout);
        Assert.Equal(ScreenLayout.MaxColumns, run.Columns);
        Assert.Equal((40, 25), run.StartGrid);
    }

    [Fact]
    public void Unknown_command_line_values_are_ignored()
    {
        var run = new AppSettings().WithCommandLine(["--shell", "bash", "--layout", "7", "--rows", "lots"]);

        Assert.Equal(new AppSettings(), run);
    }

    [Fact]
    public void Menu_moves_wraps_and_changes_settings()
    {
        var menu = new SettingsMenu(new AppSettings());

        Assert.Equal(SettingsMenu.Result.Moved, menu.HandleKey(TerminalKey.Up));
        Assert.Equal(SettingsMenu.Item.StartupSound, menu.Selected);
        Assert.Equal(SettingsMenu.Result.Changed, menu.HandleKey(TerminalKey.Right));
        Assert.False(menu.Settings.StartupSound);

        menu.HandleKey(TerminalKey.Down); // wraps to the top
        Assert.Equal(SettingsMenu.Item.Shell, menu.Selected);
        menu.HandleKey(TerminalKey.Left);
        Assert.Equal(ShellKind.Wsl, menu.Settings.Shell);
        menu.HandleSpace();
        Assert.Equal(ShellKind.Cmd, menu.Settings.Shell);

        Assert.Equal(SettingsMenu.Result.Closed, menu.HandleKey(TerminalKey.Escape));
        Assert.Equal(SettingsMenu.Result.None, menu.HandleKey(TerminalKey.F5));
    }

    [Fact]
    public void Menu_colour_option_cycles_through_all_16_colours()
    {
        var menu = new SettingsMenu(new AppSettings { TextColour = C64Palette.LightGrey });
        while (menu.Selected != SettingsMenu.Item.TextColour)
        {
            menu.HandleKey(TerminalKey.Down);
        }

        menu.HandleKey(TerminalKey.Right);
        Assert.Equal(C64Palette.Black, menu.Settings.TextColour);
        menu.HandleKey(TerminalKey.Left);
        Assert.Equal(C64Palette.LightGrey, menu.Settings.TextColour);
    }

    [Fact]
    public void Menu_draws_options_with_the_selected_one_reversed()
    {
        var screen = new ScreenBuffer(40, 25, C64Palette.LightBlue);
        var menu = new SettingsMenu(new AppSettings { TextColour = C64Palette.Yellow });

        menu.Draw(screen, C64Theme.Default);

        Assert.Equal(SettingsMenu.Title, screen.RowText(1).Trim());
        Assert.Equal(" SHELL        CMD", screen.RowText(3));
        Assert.Equal("", screen.RowText(4)); // options are double-spaced on a full-height screen
        Assert.Equal(" LAYOUT       MODERN", screen.RowText(5));
        Assert.Equal(" TEXT COLOUR  YELLOW", screen.RowText(11));
        Assert.True(screen[1, 3].Reverse);
        Assert.False(screen[1, 5].Reverse);
        Assert.Equal(C64Palette.Yellow, screen[14, 11].Foreground);
        Assert.Contains(Enumerable.Range(0, 25), row => screen.RowText(row).Contains("SHELL RESTARTS"));
    }

    [Fact]
    public void Menu_fits_the_narrowest_screen_without_wrapping()
    {
        var screen = new ScreenBuffer(ScreenLayout.MinColumns, ScreenLayout.MinRows, C64Palette.LightBlue);

        new SettingsMenu(new AppSettings()).Draw(screen, C64Theme.Default);

        Assert.Equal(" SHELL        CMD", screen.RowText(3));
        Assert.Equal(" LAYOUT       MODERN", screen.RowText(4));
    }

    [Fact]
    public void Each_shell_gets_its_own_command_line()
    {
        Assert.StartsWith("\"C:\\cmd.exe\" /s /k \"prompt READY.$_&", ShellCommands.For(ShellKind.Cmd, 40, PromptStyle.Ready, @"C:\cmd.exe"));
        Assert.StartsWith("powershell.exe -NoLogo -NoExit -EncodedCommand ", ShellCommands.For(ShellKind.PowerShell, 40, PromptStyle.Ready));
        Assert.StartsWith("pwsh.exe -NoLogo -NoExit -EncodedCommand ", ShellCommands.For(ShellKind.Pwsh, 40, PromptStyle.Ready));
        Assert.StartsWith("wsl.exe --cd ~ -e sh -c ", ShellCommands.For(ShellKind.Wsl, 40, PromptStyle.Ready));
    }

    [Fact]
    public void PowerShell_script_prints_banner_and_sets_ready_prompt()
    {
        var command = ShellCommands.PowerShellWithBanner("pwsh.exe", 40, PromptStyle.Ready);
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(command.Split(' ')[^1]));

        Assert.Contains("Write-Host '    **** COMMODORE 64 BASIC V2 ****'", script);
        Assert.DoesNotContain("READY.'", script); // the prompt prints READY., not the banner
        Assert.Contains(ShellCommands.PowerShellReadyPrompt, script);

        var classic = ShellCommands.PowerShellWithBanner("pwsh.exe", 40, PromptStyle.Classic);
        var classicScript = Encoding.Unicode.GetString(Convert.FromBase64String(classic.Split(' ')[^1]));
        Assert.Contains("Write-Host 'READY.'", classicScript);
        Assert.DoesNotContain("function global:prompt", classicScript);
    }

    [Fact]
    public void Wsl_prints_the_whole_banner_then_runs_the_login_shell()
    {
        var command = ShellCommands.WslWithBanner(40);

        Assert.Equal(
            "wsl.exe --cd ~ -e sh -c \"printf '\\n    **** COMMODORE 64 BASIC V2 ****\\n\\n 64K RAM SYSTEM  38911 BASIC BYTES FREE\\n\\nREADY.\\n'; " +
            "exec \\\"${SHELL:-/bin/sh}\\\" -l\"",
            command);
    }
}
