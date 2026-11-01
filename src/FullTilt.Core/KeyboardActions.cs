namespace FullTilt.Core;

// Built-in single keyboard keys share the physical-hold path. Command shortcuts and
// volume's existing repeat clock retain their established activation behavior.
public static class KeyboardActions
{
    public static ushort? SingleKey(InputAction action) => action switch
    {
        InputAction.PageUp => 0x21, InputAction.PageDown => 0x22,
        InputAction.Escape => 0x1B, InputAction.Tab => 0x09,
        InputAction.Home => 0x24, InputAction.End => 0x23,
        InputAction.OpenItem or InputAction.Enter => 0x0D,
        InputAction.DeleteItem or InputAction.Delete => 0x2E,
        InputAction.StartMenu => 0x5B, InputAction.Refresh => 0x74,
        InputAction.Space => 0x20, InputAction.Backspace => 0x08,
        InputAction.ArrowUp => 0x26, InputAction.ArrowDown => 0x28,
        InputAction.ArrowLeft => 0x25, InputAction.ArrowRight => 0x27,
        InputAction.Rename => 0x71,
        InputAction.Mute => 0xAD, InputAction.PlayPause => 0xB3,
        InputAction.NextTrack => 0xB0, InputAction.PreviousTrack => 0xB1,
        _ => null
    };
    public static KeyboardChord? Chord(InputAction action) => SingleKey(action) is { } key
        ? new([new(key, Extended: new KeyTransition(key, true).IsExtended)]) : null;
}
