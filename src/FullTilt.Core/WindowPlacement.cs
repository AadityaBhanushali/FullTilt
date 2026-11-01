namespace FullTilt.Core;

// Physical desktop pixels throughout, including negative monitor origins.
public readonly record struct WindowBounds(int X, int Y, int Width, int Height);

public static class WindowPlacement
{
    public static WindowBounds Fit(WindowBounds window, WindowBounds workArea)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0) return window;
        var width = Math.Clamp(window.Width, 1, workArea.Width);
        var height = Math.Clamp(window.Height, 1, workArea.Height);
        return new(Math.Clamp(window.X, workArea.X, workArea.X + workArea.Width - width),
            Math.Clamp(window.Y, workArea.Y, workArea.Y + workArea.Height - height), width, height);
    }
}
