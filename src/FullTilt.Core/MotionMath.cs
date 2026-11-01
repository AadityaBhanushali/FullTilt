namespace FullTilt.Core;

public readonly record struct Vector(double X, double Y)
{
    public double Length => Math.Sqrt(X * X + Y * Y);
}

public static class MotionMath
{
    public static double Normalize(short value) => value < 0 ? value / 32768.0 : value / 32767.0;

    public static Vector Radial(short rawX, short rawY, double deadZone, double curve)
    {
        var x = Normalize(rawX);
        var y = Normalize(rawY);
        var length = Math.Sqrt(x * x + y * y);
        deadZone = Math.Clamp(deadZone, 0, 0.99);
        if (length <= deadZone) return default;
        var magnitude = Math.Pow(Math.Clamp((length - deadZone) / (1 - deadZone), 0, 1), curve);
        return new(x / length * magnitude, y / length * magnitude);
    }

    public static Vector DPad(ControllerButtons buttons)
    {
        var x = ((buttons & ControllerButtons.DPadRight) != 0 ? 1 : 0) - ((buttons & ControllerButtons.DPadLeft) != 0 ? 1 : 0);
        var y = ((buttons & ControllerButtons.DPadDown) != 0 ? 1 : 0) - ((buttons & ControllerButtons.DPadUp) != 0 ? 1 : 0);
        var length = Math.Sqrt(x * x + y * y);
        return length == 0 ? default : new(x / length, y / length);
    }

    // Each scroll axis has its own gate, in addition to the radial dead zone.
    // A large Y deflection cannot make a small X deflection leak horizontal scrolling.
    public static double ScrollAxis(short raw, double deadZone)
    {
        var value = Normalize(raw);
        return Math.CopySign(Math.Max(0, (Math.Abs(value) - deadZone) / (1 - deadZone)), value);
    }

    public static double Elapsed(double seconds) => double.IsFinite(seconds) ? Math.Clamp(seconds, 0, 0.032) : 0;
}

public struct FractionalMotion
{
    private double _x, _y;
    public (int X, int Y) Take(double x, double y)
    {
        _x += x; _y += y;
        var wholeX = (int)Math.Truncate(_x);
        var wholeY = (int)Math.Truncate(_y);
        _x -= wholeX; _y -= wholeY;
        return (wholeX, wholeY);
    }
    public void Reset() { _x = 0; _y = 0; }
    public (int X, int Y) TakeRounded(double x, double y)
    {
        _x += x; _y += y;
        // Ties to even keep a +/-0.5 remainder stationary when the next delta is zero.
        var wholeX = (int)Math.Round(_x);
        var wholeY = (int)Math.Round(_y);
        _x -= wholeX; _y -= wholeY;
        return (wholeX, wholeY);
    }
    public void ResetX() => _x = 0;
    public void ResetY() => _y = 0;
}
