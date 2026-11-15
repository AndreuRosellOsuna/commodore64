using System.Globalization;
using C64Console.Core;

namespace C64Console.App;

/// <summary>
/// Command-line options:
/// <list type="bullet">
/// <item><c>--layout modern|authentic</c>: a resizable grid (default) or a fixed 40×25 screen.</item>
/// <item><c>--columns N</c>, <c>--rows N</c>: the starting grid in the modern layout (default 80×25).</item>
/// <item><c>--crt</c>: start with the CRT effect on.</item>
/// <item><c>--prompt ready|classic</c>: the C64 <c>READY.</c> prompt (default) or cmd's usual one.</item>
/// </list>
/// </summary>
internal sealed record CommandLine(Layout Layout, int Columns, int Rows, bool Crt, PromptStyle Prompt)
{
    public const int DefaultColumns = 80;

    public static CommandLine Parse(string[] args)
    {
        var layout = Value(args, "--layout") is { } l && l.Equals("authentic", StringComparison.OrdinalIgnoreCase)
            ? Layout.Authentic
            : Layout.Modern;
        var prompt = Value(args, "--prompt") is { } p && p.Equals("classic", StringComparison.OrdinalIgnoreCase)
            ? PromptStyle.Classic
            : PromptStyle.Ready;
        var columns = Number(args, "--columns", DefaultColumns, ScreenLayout.MinColumns, ScreenLayout.MaxColumns);
        var rows = Number(args, "--rows", ScreenLayout.AuthenticRows, ScreenLayout.MinRows, ScreenLayout.MaxRows);
        if (layout == Layout.Authentic)
        {
            (columns, rows) = (ScreenLayout.AuthenticColumns, ScreenLayout.AuthenticRows);
        }

        return new CommandLine(layout, columns, rows, args.Contains("--crt", StringComparer.OrdinalIgnoreCase), prompt);
    }

    private static string? Value(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int Number(string[] args, string name, int fallback, int min, int max) =>
        int.TryParse(Value(args, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;
}
