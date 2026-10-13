using System.Globalization;

namespace C64Console.App;

/// <summary>Command-line options. <c>--columns N</c> sets the screen width (default 40, like the C64).</summary>
internal static class CommandLine
{
    public const int DefaultColumns = 40;

    public static int Columns(string[] args)
    {
        var index = Array.IndexOf(args, "--columns");
        return index >= 0 && index + 1 < args.Length &&
               int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var columns)
            ? Math.Clamp(columns, 20, 250)
            : DefaultColumns;
    }
}
