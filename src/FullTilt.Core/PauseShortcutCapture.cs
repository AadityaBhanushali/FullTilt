namespace FullTilt.Core;

// Capture a simultaneous chord after an initial release; never union sequential presses.
public sealed class PauseShortcutCapture
{
    private bool _armed;
    private bool _invalid;
    public ControllerButtons Buttons { get; private set; }
    public bool Complete { get; private set; }
    public void Tick(ControllerButtons buttons)
    {
        if (Complete) return;
        if (!_armed) { if (buttons == ControllerButtons.None) _armed = true; return; }
        if (buttons == ControllerButtons.None)
        {
            if (Buttons != ControllerButtons.None) Complete = true;
            return;
        }
        if (System.Numerics.BitOperations.PopCount((uint)buttons) > 4) _invalid = true;
        if (System.Numerics.BitOperations.PopCount((uint)buttons) > System.Numerics.BitOperations.PopCount((uint)Buttons)) Buttons = buttons;
    }
    public bool Valid => Complete && !_invalid && PauseShortcutRules.IsValid(Buttons);
}
