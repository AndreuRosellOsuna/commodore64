namespace C64Console.Core;

/// <summary>
/// The settings screen, drawn in C64 style on a screen buffer: a list of options, the selected one in reverse video.
/// Up/Down choose an option, Left/Right (or Enter/Space) change it, Escape closes the screen.
/// </summary>
public sealed class SettingsMenu
{
    public const string Title = "**** SETTINGS ****";

    private const int ValueColumn = 14;
    private const int FirstItemRow = 3;

    private static readonly string[] ColourNames =
    [
        "BLACK", "WHITE", "RED", "CYAN", "PURPLE", "GREEN", "BLUE", "YELLOW",
        "ORANGE", "BROWN", "LIGHT RED", "DARK GREY", "GREY", "LIGHT GREEN", "LIGHT BLUE", "LIGHT GREY",
    ];

    private static readonly string[] Help = ["CRSR UP/DOWN  CHOOSE", "CRSR LEFT/RIGHT  CHANGE", "RETURN/ESC  DONE"];

    /// <param name="version">Shown at the bottom of the screen, e.g. "1.0.0".</param>
    public SettingsMenu(AppSettings settings, string? version = null)
    {
        Settings = settings;
        Version = version;
    }

    public string? Version { get; }

    public enum Item
    {
        Shell,
        Layout,
        Crt,
        Prompt,
        TextColour,
        KeyClicks,
        StartupSound,
    }

    public enum Result
    {
        /// <summary>The key did nothing.</summary>
        None,

        /// <summary>The selection moved; only the menu needs redrawing.</summary>
        Moved,

        /// <summary>A setting changed.</summary>
        Changed,

        /// <summary>The user closed the menu.</summary>
        Closed,
    }

    public static IReadOnlyList<Item> Items { get; } = Enum.GetValues<Item>();

    public AppSettings Settings { get; private set; }

    public Item Selected { get; private set; }

    public Result HandleKey(TerminalKey key)
    {
        switch (key)
        {
            case TerminalKey.Up:
                Selected = Items[(Items.Count + (int)Selected - 1) % Items.Count];
                return Result.Moved;
            case TerminalKey.Down:
                Selected = Items[((int)Selected + 1) % Items.Count];
                return Result.Moved;
            case TerminalKey.Right:
                Settings = Change(Settings, Selected, +1);
                return Result.Changed;
            case TerminalKey.Left:
                Settings = Change(Settings, Selected, -1);
                return Result.Changed;
            case TerminalKey.Escape or TerminalKey.Enter:
                return Result.Closed;
            default:
                return Result.None;
        }
    }

    /// <summary>A space bar press changes the selected option, like Right.</summary>
    public Result HandleSpace()
    {
        Settings = Change(Settings, Selected, +1);
        return Result.Changed;
    }

    public static string Label(Item item) => item switch
    {
        Item.Shell => "SHELL",
        Item.Layout => "LAYOUT",
        Item.Crt => "CRT EFFECT",
        Item.Prompt => "PROMPT",
        Item.TextColour => "TEXT COLOUR",
        Item.KeyClicks => "KEY CLICKS",
        _ => "START SOUND",
    };

    public static string Value(AppSettings settings, Item item) => item switch
    {
        Item.Shell => ShellCommands.DisplayName(settings.Shell),
        Item.Layout => settings.Layout == Layout.Modern ? "MODERN" : "AUTHENTIC 40X25",
        Item.Crt => OnOff(settings.Crt),
        Item.Prompt => settings.Prompt == PromptStyle.Ready ? "READY." : "CLASSIC",
        Item.TextColour => ColourNames[settings.TextColour & 0x0F],
        Item.KeyClicks => OnOff(settings.KeyClicks),
        _ => OnOff(settings.StartupSound),
    };

    /// <summary>Draws the menu over the whole screen, in the theme's colours.</summary>
    public void Draw(ScreenBuffer screen, C64Theme theme)
    {
        screen.Reverse = false;
        screen.Background = Cell.DefaultBackground;
        screen.Foreground = theme.Foreground;
        screen.Clear();

        // Leave an empty line between options when the screen is tall enough, which reads much more easily.
        var spacing = screen.Rows >= ScreenLayout.AuthenticRows ? 2 : 1;

        WriteAt(screen, Math.Max(0, (screen.Columns - Title.Length) / 2), 1, Title);
        for (var i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            var row = FirstItemRow + i * spacing;
            screen.Reverse = item == Selected;
            WriteAt(screen, 1, row, Label(item).PadRight(ValueColumn - 1));

            // The colour option is shown in its own colour, unless that would vanish into the background.
            var colour = Settings.TextColour;
            screen.Foreground = item == Item.TextColour && colour != theme.Background ? colour : theme.Foreground;
            WriteAt(screen, ValueColumn, row, Value(Settings, item));
            screen.Foreground = theme.Foreground;
            screen.Reverse = false;
        }

        var helpRow = FirstItemRow + (Items.Count - 1) * spacing + 2;
        for (var i = 0; i < Help.Length; i++)
        {
            WriteAt(screen, 1, helpRow + i, Help[i]);
        }

        if (Selected is Item.Shell or Item.Prompt)
        {
            WriteAt(screen, 1, helpRow + Help.Length + 1, "SHELL RESTARTS ON RETURN");
        }

        if (Version is not null)
        {
            WriteAt(screen, 1, screen.Rows - 2, $"C64 CONSOLE V{Version}");
        }

        screen.MoveCursorTo(0, 0);
    }

    private static AppSettings Change(AppSettings settings, Item item, int step) => item switch
    {
        Item.Shell => settings with { Shell = Cycle(settings.Shell, step) },
        Item.Layout => settings with { Layout = Cycle(settings.Layout, step) },
        Item.Crt => settings with { Crt = !settings.Crt },
        Item.Prompt => settings with { Prompt = Cycle(settings.Prompt, step) },
        Item.TextColour => settings with { TextColour = (byte)((settings.TextColour + step + C64Palette.Count) % C64Palette.Count) },
        Item.KeyClicks => settings with { KeyClicks = !settings.KeyClicks },
        _ => settings with { StartupSound = !settings.StartupSound },
    };

    private static T Cycle<T>(T value, int step)
        where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        var index = Array.IndexOf(values, value);
        return values[(index + step + values.Length) % values.Length];
    }

    private static string OnOff(bool on) => on ? "ON" : "OFF";

    /// <summary>Writes text at a position, cut off at the right edge rather than wrapping.</summary>
    private static void WriteAt(ScreenBuffer screen, int column, int row, string text)
    {
        if (row >= screen.Rows || column >= screen.Columns)
        {
            return;
        }

        screen.MoveCursorTo(column, row);
        screen.Write(text.Length > screen.Columns - column ? text[..(screen.Columns - column)] : text);
    }
}
