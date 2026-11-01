using System.ComponentModel;
using System.Runtime.InteropServices;
using FullTilt.Core;

namespace FullTilt.Windows;

public sealed class WindowsInputSink : IInputSink, IDisposable
{
    private readonly bool[] _mouseOwned = new bool[3];
    private readonly WindowsActionDispatcher _actions;
    private readonly bool[] _navigationOwned = new bool[2];
    public WindowsInputSink() => _actions = new(new KeyboardOwnership(new NativeKeyInput()), new NativeDesktopActions(Navigate, Scroll, MoveTo));
    public string? ActionError => _actions.Error;
    public void Dispose() => _actions.Dispose();
    public void CancelPendingActions() => _actions.CancelPending();
    public void SetStickyModifier(StickyModifier modifier, bool active) => _actions.SetStickyModifier(modifier, active);
    private static readonly uint[] DownFlags = [0x0002, 0x0008, 0x0020];
    private static readonly uint[] UpFlags = [0x0004, 0x0010, 0x0040];
    public static int NativeInputSize => Marshal.SizeOf<NativeMethods.Input>();

    public static int AbsoluteCoordinate(int coordinate, int origin, int size)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        var pixel = Math.Clamp((long)coordinate - origin, 0, size - 1);
        return (int)Math.Clamp((pixel * 65536 + 32768) / size, 0, 65535);
    }

    public void Move(int dx, int dy)
    {
        if (!NativeMethods.GetCursorPos(out var position)) throw ApiError("GetCursorPos");
        MoveTo(position.X + dx, position.Y + dy);
    }
    private void MoveTo(int x, int y)
    {
        var left = NativeMethods.GetSystemMetrics(76);
        var top = NativeMethods.GetSystemMetrics(77);
        var width = NativeMethods.GetSystemMetrics(78);
        var height = NativeMethods.GetSystemMetrics(79);
        if (width <= 0 || height <= 0) throw new Win32Exception("Virtual desktop is unavailable.");
        var input = Mouse(0x0001 | 0x8000 | 0x4000);
        input.Data.Mouse.Dx = AbsoluteCoordinate(x, left, width);
        input.Data.Mouse.Dy = AbsoluteCoordinate(y, top, height);
        SendOne(input);
    }

    // Metadata identifies our wheel packets inside FullTilt; flags, deltas and timing stay unchanged.
    public const nuint WheelInputTag = 0x46545748;
    public static NativeMethods.Input WheelInput(bool horizontal, int delta)
    {
        var input = Mouse(horizontal ? 0x1000u : 0x0800u, unchecked((uint)delta));
        input.Data.Mouse.ExtraInfo = WheelInputTag;
        return input;
    }

    public void Scroll(int horizontal, int vertical)
    {
        // Windows requires separate events because the axes share the mouseData field.
        if (vertical != 0) SendOne(WheelInput(false, vertical));
        if (horizontal != 0) SendOne(WheelInput(true, horizontal));
    }

    public void SetMouseButton(MouseButton button, bool down)
    {
        var index = (int)button;
        if (_mouseOwned[index] == down) return;
        SendOne(Mouse(down ? DownFlags[index] : UpFlags[index]));
        _mouseOwned[index] = down;
    }

    public void Execute(InputAction action) => _actions.Execute(action);
    public void BeginKeyboardFrame() => _actions.BeginKeyboardFrame();
    public void ExecuteKeyboard(KeyboardChord chord) => _actions.ExecuteKeyboard(chord);
    public void SetKeyboardHolds(ReadOnlySpan<InputAction> actions, IReadOnlyList<KeyboardChord> recordings) => _actions.SetKeyboardHolds(actions, recordings);
    private void Navigate(bool forward)
    {
        var index = forward ? 1 : 0;
        // Do not release a navigation button already held on the physical mouse.
        if ((NativeMethods.GetAsyncKeyState(forward ? 0x06 : 0x05) & 0x8000) != 0) return;
        SendOne(Mouse(0x0080, (uint)index + 1)); _navigationOwned[index] = true;
        SendOne(Mouse(0x0100, (uint)index + 1)); _navigationOwned[index] = false;
    }
    public void ReleaseAll()
    {
        Exception? failure = null;
        for (var i = 0; i < _mouseOwned.Length; i++)
            if (_mouseOwned[i])
                try { SetMouseButton((MouseButton)i, false); }
                catch (Win32Exception ex) { failure = ex; }
        for (var i = 0; i < _navigationOwned.Length; i++)
            if (_navigationOwned[i])
                try { SendOne(Mouse(0x0100, (uint)i + 1)); _navigationOwned[i] = false; }
                catch (Win32Exception ex) { failure = ex; }
        try { _actions.ReleaseAll(); }
        catch (Win32Exception ex) { failure = ex; }
        if (failure is not null) throw failure;
    }

    private static NativeMethods.Input Mouse(uint flags, uint data = 0) => new()
    {
        Type = 0, Data = new NativeMethods.InputUnion { Mouse = new NativeMethods.MouseInput { Flags = flags, MouseData = data } }
    };
    private static unsafe void SendOne(NativeMethods.Input input)
    {
        var sent = NativeMethods.SendInput(1, &input, NativeInputSize);
        if (sent != 1) throw ApiError($"SendInput accepted {sent}/1 events; Windows does not identify the cause uniquely. Check the active desktop and target permissions");
    }
    private static Win32Exception ApiError(string operation) => new(Marshal.GetLastWin32Error(), operation + " failed.");
}
