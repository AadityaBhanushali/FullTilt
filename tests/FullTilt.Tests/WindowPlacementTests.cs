using FullTilt.Core;

static class WindowPlacementTests
{
    public static void Register(TestSuite suite)
    {
        suite.Add("Windowed size trial increases both dimensions by twenty percent when space permits", () =>
        {
            var size = WindowModes.WindowedSize(new(0, 0, 3840, 2112), 1);
            Check.Equal(1536, size.Width); Check.Equal(864, size.Height);
        });
        suite.Add("Windowed size trial grows capped laptop windows without exceeding the work area", () =>
        {
            var work = new WindowBounds(0, 0, 1920, 1032);
            var size = WindowModes.WindowedSize(work, 1.5);
            Check.True(size.Width > 1680 && size.Height > 945);
            Check.True(size.Width <= work.Width && size.Height <= work.Height);
            Check.Equal(size.Width * 9, size.Height * 16);
        });
        foreach (var screen in new[] { (1280, 720), (1366, 768), (1920, 1080), (2560, 1440), (3440, 1440), (3840, 2160) })
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2, 2.5, 3 })
            suite.Add($"Two window modes retain 16:9 inside {screen} at DPI {scale}", () =>
            {
                var work = new WindowBounds(-screen.Item1, 40, screen.Item1, screen.Item2 - 88);
                var fitted = WindowModes.Windowed(new(9000, -5000, 400, 900), work, scale);
                Check.Equal(fitted.Width * 9, fitted.Height * 16);
                Check.True(fitted.Width <= work.Width && fitted.Height <= work.Height);
                Check.True(fitted.X >= work.X && fitted.Y >= work.Y && fitted.X + fitted.Width <= work.X + work.Width && fitted.Y + fitted.Height <= work.Y + work.Height);
                Check.Equal(fitted, WindowModes.Windowed(fitted, work, scale));
                var normal = WindowModes.Windowed(fitted with { Width = 100, Height = 100 }, work, scale);
                Check.Equal(fitted.Width, normal.Width); Check.Equal(fitted.Height, normal.Height);
            });
        foreach (var origin in new[] { (0, 0), (-1920, 0), (1920, 0), (0, -1080), (-1280, -720) })
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2d })
        {
            var (x, y) = origin; var dpiScale = scale;
            suite.Add($"Window placement preserves native frame at ({x},{y}) scale {scale}", () =>
            {
                var work = new WindowBounds(x, y + 40, 1280, 680); // Also covers a top taskbar.
                var oversize = new WindowBounds(x - 300, y - 500, (int)(880 * dpiScale), (int)(880 * dpiScale));
                var fit = WindowPlacement.Fit(oversize, work);
                Check.True(fit.X >= work.X && fit.Y >= work.Y);
                Check.True(fit.X + fit.Width <= work.X + work.Width && fit.Y + fit.Height <= work.Y + work.Height);
                Check.Equal(Math.Min(oversize.Width, work.Width), fit.Width);
                Check.Equal(Math.Min(oversize.Height, work.Height), fit.Height);
                Check.Equal(fit, WindowPlacement.Fit(fit, work)); // Reopen/restored bounds are stable.
                var inside = new WindowBounds(x + 20, y + 60, 800, 600);
                Check.Equal(inside, WindowPlacement.Fit(inside, work)); // No needless repositioning.
            });
        }
        suite.Add("Window placement recovers a removed monitor and keeps right-side controls visible", () =>
        {
            var work = new WindowBounds(0, 0, 1920, 1040);
            Check.Equal(new WindowBounds(0, 0, 880, 880), WindowPlacement.Fit(new(-1900, -900, 880, 880), work));
            Check.Equal(new WindowBounds(1040, 160, 880, 880), WindowPlacement.Fit(new(3000, 2000, 880, 880), work));
        });
        suite.Add("Window placement ignores transient empty monitor work areas", () =>
        {
            var original = new WindowBounds(-200, -300, 880, 880);
            Check.Equal(original, WindowPlacement.Fit(original, new(0, 0, 0, 0)));
        });
    }
}
