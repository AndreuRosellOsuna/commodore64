namespace C64Console.Core;

/// <summary>
/// The C64 KERNAL flips the cursor every 20 jiffies (1/60 s each), so it is on for 1/3 s
/// and off for 1/3 s.
/// </summary>
public static class CursorBlink
{
    public static readonly TimeSpan Phase = TimeSpan.FromSeconds(20.0 / 60.0);

    /// <summary>Whether the cursor is shown, given the time since it was last reset (e.g. by a key press).</summary>
    public static bool IsVisible(TimeSpan sinceReset) => sinceReset.Ticks / Phase.Ticks % 2 == 0;
}
