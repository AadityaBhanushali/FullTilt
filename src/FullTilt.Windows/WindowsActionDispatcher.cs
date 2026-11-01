using FullTilt.Core;

namespace FullTilt.Windows;

public enum WindowCommand { Minimize, Maximize, Restore, Close }
public interface IDesktopActions : IDisposable
{
    void Window(WindowCommand command);
    void Navigate(bool forward);
    void ToggleKeyboard();
    void CenterCursor();
    void Scroll(int horizontal, int vertical);
    string? Error { get; }
    void CancelPending();
    void IDisposable.Dispose() { }
}

// Shared one-shot entry point: assignment controls do not contain platform behavior.
public sealed class WindowsActionDispatcher(KeyboardOwnership keyboard, IDesktopActions desktop) : IDisposable
{
    public void ExecuteKeyboard(KeyboardChord chord) => keyboard.Play(chord);
    public void BeginKeyboardFrame() => keyboard.BeginFrame();
    public void SetKeyboardHolds(ReadOnlySpan<InputAction> actions, IReadOnlyList<KeyboardChord> recordings)
    {
        var chords = new List<KeyboardChord>(recordings);
        foreach (var action in actions) if (KeyboardActions.Chord(action) is { } chord) chords.Add(chord);
        keyboard.SetHolds(chords);
    }
    public string? Error => desktop.Error;
    public void SetStickyModifier(StickyModifier modifier, bool active) => keyboard.SetLatch(modifier, active);
    public void ReleaseAll() { desktop.CancelPending(); keyboard.ReleaseAll(); }
    public void CancelPending() => desktop.CancelPending();
    public void Dispose() => desktop.Dispose();
    public void Execute(InputAction action)
    {
        switch (action)
        {
            case InputAction.PageUp: keyboard.Shortcut(0x21); break;
            case InputAction.PageDown: keyboard.Shortcut(0x22); break;
            case InputAction.JumpTop: keyboard.Shortcut(0x24, KeyboardOwnership.Ctrl); break;
            case InputAction.JumpBottom: keyboard.Shortcut(0x23, KeyboardOwnership.Ctrl); break;
            case InputAction.Escape: keyboard.Shortcut(0x1B); break;
            case InputAction.Tab: keyboard.Shortcut(0x09); break;
            case InputAction.PreviousField: keyboard.Shortcut(0x09, KeyboardOwnership.Shift); break;
            case InputAction.Home: keyboard.Shortcut(0x24); break;
            case InputAction.End: keyboard.Shortcut(0x23); break;
            case InputAction.ClipboardHistory: keyboard.Shortcut(0x56, KeyboardOwnership.Win); break;
            case InputAction.EmojiSymbols: keyboard.Shortcut(0xBE, KeyboardOwnership.Win); break;
            case InputAction.VoiceTyping: keyboard.Shortcut(0x48, KeyboardOwnership.Win); break;
            case InputAction.Save: keyboard.Shortcut(0x53, KeyboardOwnership.Ctrl); break;
            case InputAction.SaveAs: keyboard.Shortcut(0x53, KeyboardOwnership.Ctrl, KeyboardOwnership.Shift); break;
            case InputAction.Find: keyboard.Shortcut(0x46, KeyboardOwnership.Ctrl); break;
            case InputAction.OpenItem: keyboard.Shortcut(0x0D); break;
            case InputAction.DeleteItem: keyboard.Shortcut(0x2E); break;
            case InputAction.NewFolder: keyboard.Shortcut(0x4E, KeyboardOwnership.Ctrl, KeyboardOwnership.Shift); break;
            case InputAction.Properties: keyboard.Shortcut(0x0D, KeyboardOwnership.Alt); break;
            case InputAction.ContextMenu: keyboard.Shortcut(0x79, KeyboardOwnership.Shift); break;
            case InputAction.SnapTop: keyboard.Shortcut(0x26, KeyboardOwnership.Win, KeyboardOwnership.Alt); break;
            case InputAction.SnapBottom: keyboard.Shortcut(0x28, KeyboardOwnership.Win, KeyboardOwnership.Alt); break;
            case InputAction.NextApp: keyboard.Shortcut(0x09, KeyboardOwnership.Alt, KeyboardOwnership.Shift); break;
            case InputAction.ShowDesktop: keyboard.Shortcut(0x44, KeyboardOwnership.Win); break;
            case InputAction.StartMenu: keyboard.Shortcut(0x5B); break;
            case InputAction.WindowsSearch: keyboard.Shortcut(0x53, KeyboardOwnership.Win); break;
            case InputAction.NextDesktop: keyboard.Shortcut(0x27, KeyboardOwnership.Win, KeyboardOwnership.Ctrl); break;
            case InputAction.PreviousDesktop: keyboard.Shortcut(0x25, KeyboardOwnership.Win, KeyboardOwnership.Ctrl); break;
            case InputAction.CreateDesktop: keyboard.Shortcut(0x44, KeyboardOwnership.Win, KeyboardOwnership.Ctrl); break;
            case InputAction.CloseDesktop: keyboard.Shortcut(0x73, KeyboardOwnership.Win, KeyboardOwnership.Ctrl); break;
            case InputAction.FileExplorer: keyboard.Shortcut(0x45, KeyboardOwnership.Win); break;
            case InputAction.QuickSettings: keyboard.Shortcut(0x41, KeyboardOwnership.Win); break;
            case InputAction.Notifications: keyboard.Shortcut(0x4E, KeyboardOwnership.Win); break;
            case InputAction.Refresh: keyboard.Shortcut(0x74); break;
            case InputAction.FocusAddressBar: keyboard.Shortcut(0x4C, KeyboardOwnership.Ctrl); break;
            case InputAction.FindOnPage: keyboard.Shortcut(0x46, KeyboardOwnership.Ctrl); break;
            case InputAction.BrowserZoomIn: keyboard.Shortcut(0xBB, KeyboardOwnership.Ctrl); break;
            case InputAction.BrowserZoomOut: keyboard.Shortcut(0xBD, KeyboardOwnership.Ctrl); break;
            case InputAction.ResetZoom: keyboard.Shortcut(0x30, KeyboardOwnership.Ctrl); break;
            case InputAction.OpenDownloads: keyboard.Shortcut(0x4A, KeyboardOwnership.Ctrl); break;
            case InputAction.TakeScreenshot: keyboard.Shortcut(0x2C, KeyboardOwnership.Win); break;
            case InputAction.SnippingTool: keyboard.Shortcut(0x53, KeyboardOwnership.Win, KeyboardOwnership.Shift); break;
            case InputAction.ScreenRecording: keyboard.Shortcut(0x52, KeyboardOwnership.Win, KeyboardOwnership.Shift); break;
            case InputAction.Magnifier: keyboard.Shortcut(0x6B, KeyboardOwnership.Win); break;
            case InputAction.MagnifierZoomIn: keyboard.Shortcut(0x6B, KeyboardOwnership.Win); break;
            case InputAction.MagnifierZoomOut: keyboard.Shortcut(0x6D, KeyboardOwnership.Win); break;
            case InputAction.CloseMagnifier: keyboard.Shortcut(0x1B, KeyboardOwnership.Win); break;
            case InputAction.CenterCursor: desktop.CenterCursor(); break;
            case InputAction.ScrollUp: desktop.Scroll(0, 120); break;
            case InputAction.ScrollDown: desktop.Scroll(0, -120); break;
            case InputAction.ScrollLeft: desktop.Scroll(-120, 0); break;
            case InputAction.ScrollRight: desktop.Scroll(120, 0); break;
            case InputAction.MinimizeWindow: desktop.Window(WindowCommand.Minimize); break;
            case InputAction.MaximizeWindow: desktop.Window(WindowCommand.Maximize); break;
            case InputAction.RestoreWindow: desktop.Window(WindowCommand.Restore); break;
            case InputAction.CloseWindow: desktop.Window(WindowCommand.Close); break;
            case InputAction.ToggleKeyboard: desktop.ToggleKeyboard(); break;
            case InputAction.Back: desktop.Navigate(false); break;
            case InputAction.Forward: desktop.Navigate(true); break;
            case InputAction.TaskView: keyboard.Shortcut(0x09, KeyboardOwnership.Win); break;
            case InputAction.SnapLeft: keyboard.Shortcut(0x25, KeyboardOwnership.Win); break;
            case InputAction.SnapRight: keyboard.Shortcut(0x27, KeyboardOwnership.Win); break;
            case InputAction.PreviousApp: keyboard.Shortcut(0x09, KeyboardOwnership.Alt); break;
            case InputAction.NextTab: keyboard.Shortcut(0x09, KeyboardOwnership.Ctrl); break;
            case InputAction.PreviousTab: keyboard.Shortcut(0x09, KeyboardOwnership.Ctrl, KeyboardOwnership.Shift); break;
            case InputAction.NewTab: keyboard.Shortcut(0x54, KeyboardOwnership.Ctrl); break;
            case InputAction.CloseTab: keyboard.Shortcut(0x57, KeyboardOwnership.Ctrl); break;
            case InputAction.ReopenTab: keyboard.Shortcut(0x54, KeyboardOwnership.Ctrl, KeyboardOwnership.Shift); break;
            case InputAction.SelectAll: keyboard.Shortcut(0x41, KeyboardOwnership.Ctrl); break;
            case InputAction.Copy: keyboard.Shortcut(0x43, KeyboardOwnership.Ctrl); break;
            case InputAction.Cut: keyboard.Shortcut(0x58, KeyboardOwnership.Ctrl); break;
            case InputAction.Paste: keyboard.Shortcut(0x56, KeyboardOwnership.Ctrl); break;
            case InputAction.Delete: keyboard.Shortcut(0x2E); break;
            case InputAction.Space: keyboard.Shortcut(0x20); break;
            case InputAction.Enter: keyboard.Shortcut(0x0D); break;
            case InputAction.Backspace: keyboard.Shortcut(0x08); break;
            case InputAction.ArrowUp: keyboard.Shortcut(0x26); break;
            case InputAction.ArrowDown: keyboard.Shortcut(0x28); break;
            case InputAction.ArrowLeft: keyboard.Shortcut(0x25); break;
            case InputAction.ArrowRight: keyboard.Shortcut(0x27); break;
            case InputAction.Rename: keyboard.Shortcut(0x71); break;
            case InputAction.Undo: keyboard.Shortcut(0x5A, KeyboardOwnership.Ctrl); break;
            case InputAction.Redo: keyboard.Shortcut(0x59, KeyboardOwnership.Ctrl); break;
            case InputAction.VolumeUp: keyboard.Shortcut(0xAF); break;
            case InputAction.VolumeDown: keyboard.Shortcut(0xAE); break;
            case InputAction.Mute: keyboard.Shortcut(0xAD); break;
            case InputAction.PlayPause: keyboard.Shortcut(0xB3); break;
            case InputAction.NextTrack: keyboard.Shortcut(0xB0); break;
            case InputAction.PreviousTrack: keyboard.Shortcut(0xB1); break;
        }
    }
}
