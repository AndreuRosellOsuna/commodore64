namespace C64Console.Core;

/// <summary>How the text grid relates to the window size.</summary>
public enum Layout
{
    /// <summary>Always 40×25, like the real machine. A bigger window gets bigger pixels.</summary>
    Authentic,

    /// <summary>The pixel size stays put and the grid grows or shrinks to fill the window.</summary>
    Modern,
}

/// <summary>Works out the grid size and pixel scale that fit a window.</summary>
public static class ScreenLayout
{
    public const int AuthenticColumns = 40;
    public const int AuthenticRows = 25;
    public const int MinColumns = 20;
    public const int MinRows = 5;
    public const int MaxColumns = 250;
    public const int MaxRows = 100;

    /// <summary>
    /// The grid and whole-number scale for a window of <paramref name="width"/>×<paramref name="height"/> physical pixels.
    /// In <see cref="Layout.Modern"/> the scale stays <paramref name="scale"/>; in <see cref="Layout.Authentic"/> it is
    /// the largest that fits a 40×25 frame.
    /// </summary>
    public static (int Columns, int Rows, int Scale) Fit(
        Layout layout, double width, double height, int scale,
        int borderX = FrameRenderer.DefaultBorderX, int borderY = FrameRenderer.DefaultBorderY)
    {
        // Small epsilon: window sizes round-trip through device-independent units and may come back a hair short.
        const double epsilon = 0.01;
        var glyph = C64Font.GlyphSize;

        if (layout == Layout.Authentic)
        {
            var frameWidth = AuthenticColumns * glyph + 2 * borderX;
            var frameHeight = AuthenticRows * glyph + 2 * borderY;
            var fitted = (int)Math.Min((width + epsilon) / frameWidth, (height + epsilon) / frameHeight);
            return (AuthenticColumns, AuthenticRows, Math.Max(1, fitted));
        }

        scale = Math.Max(1, scale);
        var columns = (int)((width / scale + epsilon - 2 * borderX) / glyph);
        var rows = (int)((height / scale + epsilon - 2 * borderY) / glyph);
        return (Math.Clamp(columns, MinColumns, MaxColumns), Math.Clamp(rows, MinRows, MaxRows), scale);
    }
}
