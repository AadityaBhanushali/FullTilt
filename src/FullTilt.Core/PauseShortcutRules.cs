using System.Numerics;

namespace FullTilt.Core;

public static class PauseShortcutRules
{
    public const ControllerButtons Default = ControllerButtons.Start | ControllerButtons.Back;
    public static IReadOnlyList<ControllerButtons> Buttons { get; } = Array.AsReadOnly(new[] {
        ControllerButtons.Start, ControllerButtons.Back, ControllerButtons.A, ControllerButtons.B,
        ControllerButtons.X, ControllerButtons.Y, ControllerButtons.LB, ControllerButtons.RB,
        ControllerButtons.LeftThumb, ControllerButtons.RightThumb, ControllerButtons.DPadUp,
        ControllerButtons.DPadDown, ControllerButtons.DPadLeft, ControllerButtons.DPadRight });
    private static readonly ControllerButtons Allowed = Buttons.Aggregate(ControllerButtons.None, (all, button) => all | button);
    public static bool IsValid(ControllerButtons buttons) =>
        (buttons & ~Allowed) == 0 && BitOperations.PopCount((uint)buttons) is >= 2 and <= 4 &&
        (buttons & (ControllerButtons.DPadUp | ControllerButtons.DPadDown)) != (ControllerButtons.DPadUp | ControllerButtons.DPadDown) &&
        (buttons & (ControllerButtons.DPadLeft | ControllerButtons.DPadRight)) != (ControllerButtons.DPadLeft | ControllerButtons.DPadRight);
    public static string Label(ControllerButtons button) => button switch {
        ControllerButtons.LeftThumb => "L3", ControllerButtons.RightThumb => "R3",
        ControllerButtons.DPadUp => "D-pad Up", ControllerButtons.DPadDown => "D-pad Down",
        ControllerButtons.DPadLeft => "D-pad Left", ControllerButtons.DPadRight => "D-pad Right", _ => button.ToString() };
    public static string Format(ControllerButtons buttons) => string.Join(" + ", Buttons.Where(b => (buttons & b) != 0).Select(Label));
}
