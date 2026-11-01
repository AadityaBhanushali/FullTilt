using FullTilt.Core;

namespace FullTilt.Windows;

public sealed class NativeKeyInput : IKeyInput
{
    public bool IsDown(ushort key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
    public unsafe int Send(ReadOnlySpan<KeyTransition> transitions)
    {
        Span<NativeMethods.Input> inputs = stackalloc NativeMethods.Input[transitions.Length];
        for (var i = 0; i < transitions.Length; i++) inputs[i] = Encode(transitions[i]);
        fixed (NativeMethods.Input* pointer = inputs)
            return (int)NativeMethods.SendInput((uint)inputs.Length, pointer, WindowsInputSink.NativeInputSize);
    }
    public static NativeMethods.Input Encode(KeyTransition transition) => new()
    {
        Type = 1,
        Data = new NativeMethods.InputUnion
        {
            Keyboard = new NativeMethods.KeyboardInput
            {
                VirtualKey = transition.Key,
                Scan = transition.ScanCode,
                Flags = (transition.Down ? 0u : 2u) | (transition.IsExtended ? 1u : 0)
            }
        }
    };

}
