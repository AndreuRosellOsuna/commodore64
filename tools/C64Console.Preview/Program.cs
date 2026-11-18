using C64Console.Core;
using C64Console.Preview;

// Renders the boot screen to a PNG so the look can be checked on any OS (the app itself needs Windows).
// Usage: dotnet run --project tools/C64Console.Preview -- [output.png] [scale] [columns] [--crt] [--settings]
//    or: dotnet run --project tools/C64Console.Preview -- --icon src/C64Console.App/Assets/app.ico
// --settings draws the settings screen instead of the boot screen.
// --icon out.ico writes the app icon instead (see AppIcon).
if (args.Length == 2 && args[0] == "--icon")
{
    AppIcon.WriteIco(args[1]);
    Console.WriteLine($"Wrote {args[1]} ({string.Join(", ", AppIcon.Sizes)} px)");
    return;
}

var crt = args.Contains("--crt");
var settingsScreen = args.Contains("--settings");
var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
var output = positional.Length > 0 ? positional[0] : "preview.png";
var scale = positional.Length > 1 ? int.Parse(positional[1]) : 2;
var columns = positional.Length > 2 ? int.Parse(positional[2]) : 40;
const int rows = 25;

var theme = C64Theme.Default;
var screen = new ScreenBuffer(columns, rows, theme.Foreground);
if (settingsScreen)
{
    var version = typeof(SettingsMenu).Assembly.GetName().Version?.ToString(3);
    new SettingsMenu(new AppSettings { TextColour = C64Palette.Yellow }, version).Draw(screen, theme);
}
else
{
    BootScreen.Show(screen);
}

var renderer = new FrameRenderer(columns, rows);
var frame = renderer.CreateFrame();
renderer.Render(screen, theme, cursorVisible: !settingsScreen, frame);

uint[] pixels;
int width, height;
if (crt)
{
    var filter = new CrtFilter(renderer.Width, renderer.Height, scale);
    pixels = filter.CreateOutput();
    filter.Apply(frame, pixels);
    (width, height) = (filter.Width, filter.Height);
}
else
{
    (width, height) = (renderer.Width * scale, renderer.Height * scale);
    pixels = new uint[width * height];
    for (var y = 0; y < height; y++)
    {
        for (var x = 0; x < width; x++)
        {
            pixels[y * width + x] = frame[y / scale * renderer.Width + x / scale];
        }
    }
}

PngWriter.Write(output, pixels, width, height);
Console.WriteLine($"Wrote {output} ({width}×{height})");
