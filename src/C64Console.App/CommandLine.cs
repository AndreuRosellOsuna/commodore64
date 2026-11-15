using System.Globalization;

namespace C64Console.App;

/// <summary>
/// Command-line options: <c>--columns N</c> and <c>--rows N</c> set the starting screen size
/// (default 40×25, like the C64). Resizing the window changes the size afterwards.
/// </summary>
internal static class CommandLine
{
    public const int DefaultColumns = 40;
    public const int DefaultRows = 25;

    public static int Columns(string[] args) => Option(args, "--columns", DefaultColumns, 20, 250);

    public static int Rows(string[] args) => Option(args, "--rows", DefaultRows, 5, 100);

    private static int Option(string[] args, string name, int fallback, int min, int max)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length &&
               int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;
    }
}
