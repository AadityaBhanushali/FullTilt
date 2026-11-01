using System.ComponentModel;
using FullTilt.Core;

namespace FullTilt.Windows;

public interface IControllerSource
{
    bool TryGetState(int slot, out ControllerState state);
}

public sealed class XInputSource : IControllerSource
{
    public bool TryGetState(int slot, out ControllerState state)
    {
        var result = NativeMethods.XInputGetState((uint)slot, out var native);
        if (result == 1167) { state = default; return false; } // ERROR_DEVICE_NOT_CONNECTED
        if (result != 0) throw new Win32Exception((int)result, "XInput could not read the controller.");
        var pad = native.Gamepad;
        state = new((ControllerButtons)pad.Buttons, pad.LX, pad.LY, pad.RX, pad.RY, pad.LT, pad.RT);
        return true;
    }
}
