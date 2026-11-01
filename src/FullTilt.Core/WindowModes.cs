namespace FullTilt.Core;

// Outer window bounds are physical pixels; Windows DPI affects only the preferred size.
public static class WindowModes
{
    // Tune this one multiplier for the next windowed-size trial; 1 restores 4.4's original size.
    public const double WindowedSizeMultiplier = 1.20;
    public static (int Width, int Height) WindowedSize(WindowBounds work, double dpiScale)
    {
        var scale = double.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1;
        var originalHeight = Math.Min(720 * scale, Math.Min(work.Height * .92, work.Width * .92 * 9 / 16));
        var height = Math.Min(originalHeight * WindowedSizeMultiplier, Math.Min(work.Height * .98, work.Width * .98 * 9 / 16));
        var units = Math.Max(1, (int)Math.Floor(height / 9));
        return (units * 16, units * 9);
    }
    public static WindowBounds Windowed(WindowBounds current, WindowBounds work, double dpiScale)
    {
        if (work.Width <= 0 || work.Height <= 0) return current;
        var size = WindowedSize(work, dpiScale);
        return WindowPlacement.Fit(current with { Width = size.Width, Height = size.Height }, work);
    }
}
