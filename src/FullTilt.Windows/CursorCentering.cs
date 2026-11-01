namespace FullTilt.Windows;

public static class CursorCentering
{
    // Full monitor bounds in physical pixels, including negative monitor origins.
    public static (int X, int Y) Center(int left, int top, int right, int bottom)
    {
        if (right <= left || bottom <= top) throw new ArgumentException("Monitor bounds must have positive dimensions.");
        return ((int)(left + ((long)right - left) / 2), (int)(top + ((long)bottom - top) / 2));
    }
}
