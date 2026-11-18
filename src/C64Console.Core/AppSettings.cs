using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace C64Console.Core;

/// <summary>
/// Everything the user can choose, saved as JSON in <c>%APPDATA%\C64Console\settings.json</c>.
/// Command-line options (<see cref="WithCommandLine"/>) override the saved values for one run.
/// </summary>
public sealed record AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public ShellKind Shell { get; init; } = ShellKind.Cmd;
    public Layout Layout { get; init; } = Layout.Modern;

    /// <summary>Starting grid in the modern layout.</summary>
    public int Columns { get; init; } = 80;

    public int Rows { get; init; } = ScreenLayout.AuthenticRows;
    public bool Crt { get; init; }
    public PromptStyle Prompt { get; init; } = PromptStyle.Ready;

    /// <summary>The default text colour, as set with the C64 colour keys.</summary>
    public byte TextColour { get; init; } = C64Palette.LightBlue;

    public bool KeyClicks { get; init; }
    public bool StartupSound { get; init; } = true;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "C64Console", "settings.json");

    /// <summary>Reads settings, falling back to the defaults for a missing or unreadable file and for bad values.</summary>
    public static AppSettings Load(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : new AppSettings();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public static AppSettings Parse(string json)
    {
        try
        {
            return (JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings()).Normalized();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Writes the settings. A temporary file is renamed into place, so a crash never leaves half a file.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, ToJson());
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// Applies command-line options:
    /// <c>--shell cmd|powershell|pwsh|wsl</c>, <c>--layout modern|authentic</c>, <c>--columns N</c>, <c>--rows N</c>,
    /// <c>--crt</c> / <c>--no-crt</c>, <c>--prompt ready|classic</c>, <c>--sound</c> / <c>--no-sound</c>.
    /// </summary>
    public AppSettings WithCommandLine(IReadOnlyList<string> args)
    {
        var result = this;
        if (EnumOption<ShellKind>(args, "--shell") is { } shell)
        {
            result = result with { Shell = shell };
        }

        if (EnumOption<Layout>(args, "--layout") is { } layout)
        {
            result = result with { Layout = layout };
        }

        if (EnumOption<PromptStyle>(args, "--prompt") is { } prompt)
        {
            result = result with { Prompt = prompt };
        }

        if (Number(args, "--columns") is { } columns)
        {
            result = result with { Columns = columns };
        }

        if (Number(args, "--rows") is { } rows)
        {
            result = result with { Rows = rows };
        }

        if (Flag(args, "--crt"))
        {
            result = result with { Crt = true };
        }

        if (Flag(args, "--no-crt"))
        {
            result = result with { Crt = false };
        }

        if (Flag(args, "--sound"))
        {
            result = result with { KeyClicks = true, StartupSound = true };
        }

        if (Flag(args, "--no-sound"))
        {
            result = result with { KeyClicks = false, StartupSound = false };
        }

        return result.Normalized();
    }

    /// <summary>The grid to start with: the saved size in the modern layout, always 40×25 in the authentic one.</summary>
    public (int Columns, int Rows) StartGrid =>
        Layout == Layout.Authentic ? (ScreenLayout.AuthenticColumns, ScreenLayout.AuthenticRows) : (Columns, Rows);

    private AppSettings Normalized() => this with
    {
        Shell = Enum.IsDefined(Shell) ? Shell : ShellKind.Cmd,
        Layout = Enum.IsDefined(Layout) ? Layout : Layout.Modern,
        Prompt = Enum.IsDefined(Prompt) ? Prompt : PromptStyle.Ready,
        Columns = Math.Clamp(Columns, ScreenLayout.MinColumns, ScreenLayout.MaxColumns),
        Rows = Math.Clamp(Rows, ScreenLayout.MinRows, ScreenLayout.MaxRows),
        TextColour = (byte)(TextColour & 0x0F),
    };

    private static string? Value(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i + 1 < args.Count; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static bool Flag(IReadOnlyList<string> args, string name) => args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static T? EnumOption<T>(IReadOnlyList<string> args, string name)
        where T : struct, System.Enum =>
        System.Enum.TryParse<T>(Value(args, name), ignoreCase: true, out var value) && System.Enum.IsDefined(value) ? value : null;

    private static int? Number(IReadOnlyList<string> args, string name) =>
        int.TryParse(Value(args, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
}
