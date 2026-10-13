using C64Console.Core;
using C64Console.Preview;

// Renders the boot screen to a PNG so the look can be checked on any OS (the app itself needs Windows).
// Usage: dotnet run --project tools/C64Console.Preview -- [output.png] [scale] [columns]
var output = args.Length > 0 ? args[0] : "preview.png";
var scale = args.Length > 1 ? int.Parse(args[1]) : 2;
var columns = args.Length > 2 ? int.Parse(args[2]) : 40;
const int rows = 25;

var theme = C64Theme.Default;
var screen = new ScreenBuffer(columns, rows, theme.Foreground);
BootScreen.Show(screen);

var renderer = new FrameRenderer(columns, rows);
var frame = renderer.CreateFrame();
renderer.Render(screen, theme, cursorVisible: true, frame);

var width = renderer.Width * scale;
var height = renderer.Height * scale;
var scaled = new uint[width * height];
for (var y = 0; y < height; y++)
{
    for (var x = 0; x < width; x++)
    {
        scaled[y * width + x] = frame[y / scale * renderer.Width + x / scale];
    }
}

PngWriter.Write(output, scaled, width, height);
Console.WriteLine($"Wrote {output} ({width}×{height})");
