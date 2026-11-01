namespace FullTilt.Core;

[Flags]
public enum ControllerButtons : ushort
{
    None = 0, DPadUp = 1, DPadDown = 2, DPadLeft = 4, DPadRight = 8,
    Start = 0x10, Back = 0x20, LeftThumb = 0x40, RightThumb = 0x80,
    LB = 0x100, RB = 0x200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000
}

public enum Control { A = 0, X = 1, Y = 2, B = 3, LB = 4, RB = 5, LT = 6, RT = 7, L3 = 8, R3 = 9, DPadUp = 10, DPadDown = 11, DPadLeft = 12, DPadRight = 13 }
// Explicit legacy values also preserve old numeric JSON. Persisted strings live in ActionCatalog.
public enum InputAction
{
    None = 0, LeftClick = 1, RightClick = 2, MiddleClick = 3, TaskView = 4,
    MinimizeWindow = 5, CloseWindow = 6, SnapLeft = 7, SnapRight = 8,
    PrecisionHold = 20, PrecisionToggle = 21, DoubleClick = 22, DragLock = 23, ReleaseCancel = 24,
    MaximizeWindow = 30, RestoreWindow = 31, PreviousApp = 32, ToggleKeyboard = 33,
    VolumeUp = 40, VolumeDown = 41, Mute = 42, PlayPause = 43, NextTrack = 44, PreviousTrack = 45,
    Back = 50, Forward = 51, NextTab = 52, PreviousTab = 53, NewTab = 54, CloseTab = 55, ReopenTab = 56,
    SelectAll = 60, Copy = 61, Cut = 62, Paste = 63, Rename = 64, Undo = 65, Redo = 66,
    StickyCtrl = 70, StickyShift = 71, SpeedBoostHold = 72, SpeedBoostToggle = 73,
    StickyAlt = 74, Delete = 75, Space = 76, Enter = 77,
    Backspace = 78, ArrowUp = 79, ArrowDown = 80, ArrowLeft = 81, ArrowRight = 82,
    CenterCursor = 83, ScrollUp = 84, ScrollDown = 85, ScrollLeft = 86,
    ScrollRight = 87, PageUp = 88, PageDown = 89, JumpTop = 90,
    JumpBottom = 91, Escape = 92, Tab = 93, PreviousField = 94,
    Home = 95, End = 96, ReleaseModifiers = 97, ClipboardHistory = 98,
    EmojiSymbols = 99, VoiceTyping = 100, Save = 101, SaveAs = 102,
    Find = 103, OpenItem = 104, DeleteItem = 105, NewFolder = 106,
    Properties = 107, ContextMenu = 108, SnapTop = 109, SnapBottom = 110,
    NextApp = 111, ShowDesktop = 112, StartMenu = 113, WindowsSearch = 114,
    NextDesktop = 115, PreviousDesktop = 116, CreateDesktop = 117, CloseDesktop = 118,
    FileExplorer = 119, QuickSettings = 120, Notifications = 121, Refresh = 122,
    FocusAddressBar = 123, FindOnPage = 124, BrowserZoomIn = 125, BrowserZoomOut = 126,
    ResetZoom = 127, OpenDownloads = 128, TakeScreenshot = 129, SnippingTool = 130,
    ScreenRecording = 131, Magnifier = 132, MagnifierZoomIn = 133, MagnifierZoomOut = 134,
    CloseMagnifier = 135
}
public enum StickyModifier { Ctrl, Shift, Alt }
public enum CursorStick { Left = 0, Right = 1 }
public enum DPadMode { CursorMovement = 0, AssignedActions = 1 }
public enum MouseButton { Left, Right, Middle }

public readonly record struct ControllerState(
    ControllerButtons Buttons = ControllerButtons.None,
    short LeftX = 0, short LeftY = 0, short RightX = 0, short RightY = 0,
    byte LeftTrigger = 0, byte RightTrigger = 0)
{
    public bool Has(ControllerButtons button) => (Buttons & button) == button;
}

public interface IInputSink
{
    void Move(int dx, int dy);
    void Scroll(int horizontal, int vertical);
    void SetMouseButton(MouseButton button, bool down);
    void Execute(InputAction action);
    void BeginKeyboardFrame() { }
    void ExecuteKeyboard(KeyboardChord chord) => throw new InvalidOperationException("Keyboard recording output is unavailable.");
    void SetKeyboardHolds(ReadOnlySpan<InputAction> actions, IReadOnlyList<KeyboardChord> recordings)
    {
        if (actions.Length != 0 || recordings.Count != 0) throw new InvalidOperationException("Held keyboard output is unavailable.");
    }
    void SetStickyModifier(StickyModifier modifier, bool active);
    string? ActionError => null;
    // Stop asynchronous requests without releasing latched mouse or keyboard ownership.
    void CancelPendingActions() { }
    // Releases only input owned by this application. Implementations must retain failed releases for retry.
    void ReleaseAll();
}
