using System.Text.Json;
using System.Text.Json.Serialization;

namespace FullTilt.Core;

[Flags]
public enum ActivationCapabilities { OneShot = 1, Held = 2, Toggle = 4, Repeat = 8 }
public sealed record ActionDefinition(InputAction Action, string Id, string Label, string Category,
    ActivationCapabilities Capabilities, MouseButton? MouseButton = null, string Description = "");

// One catalog for assignment choices, persistence and dispatch metadata. IDs are never display labels or indexes.
public static class ActionCatalog
{
    public static IReadOnlyList<ActionDefinition> All { get; } = Array.AsReadOnly<ActionDefinition>([
        new(InputAction.LeftClick, "LeftClick", "Left Click", "Pointer & Click", ActivationCapabilities.Held, MouseButton.Left, "Hold the left mouse button while pressed."),
        new(InputAction.RightClick, "RightClick", "Right Click", "Pointer & Click", ActivationCapabilities.Held, MouseButton.Right, "Hold the right mouse button while pressed."),
        new(InputAction.MiddleClick, "MiddleClick", "Middle Click", "Pointer & Click", ActivationCapabilities.Held, MouseButton.Middle, "Hold the middle mouse button while pressed."),
        new(InputAction.DoubleClick, "DoubleClick", "Double Click", "Pointer & Click", ActivationCapabilities.OneShot, Description: "Two left-click pairs; canceled by held left click or Release/cancel."),
        new(InputAction.DragLock, "DragLock", "Drag Lock", "Pointer & Click", ActivationCapabilities.Toggle, Description: "Toggle left-button ownership until pressed again or canceled."),
        One(InputAction.CenterCursor, "CenterCursor", "Move Cursor to Screen Center", "Pointer & Click", "Move the pointer to the center of the monitor containing the active window; does not activate a window."),
        One(InputAction.ScrollUp, "ScrollUp", "Scroll Up", "Cursor & Scroll", "Send one 120-unit mouse-wheel step up per activation. Does not change joystick scroll speed."),
        One(InputAction.ScrollDown, "ScrollDown", "Scroll Down", "Cursor & Scroll", "Send one 120-unit mouse-wheel step down per activation. Does not change joystick scroll speed."),
        One(InputAction.ScrollLeft, "ScrollLeft", "Scroll Left", "Cursor & Scroll", "Send one 120-unit mouse-wheel step left per activation. Does not change joystick scroll speed."),
        One(InputAction.ScrollRight, "ScrollRight", "Scroll Right", "Cursor & Scroll", "Send one 120-unit mouse-wheel step right per activation. Does not change joystick scroll speed."),
        One(InputAction.PageUp, "PageUp", "Page Up", "Cursor & Scroll", "PgUp. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.PageDown, "PageDown", "Page Down", "Cursor & Scroll", "PgDn. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.JumpTop, "JumpTop", "Jump to Top", "Cursor & Scroll", "Ctrl+Home. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.JumpBottom, "JumpBottom", "Jump to Bottom", "Cursor & Scroll", "Ctrl+End. Acts on the focused app; support depends on that app and Windows configuration."),
        new(InputAction.PrecisionHold, "PrecisionHold", "Precision While Held", "Speed & Precision", ActivationCapabilities.Held, Description: "Apply the precision cursor speed while held."),
        new(InputAction.PrecisionToggle, "PrecisionToggle", "Precision Toggle", "Speed & Precision", ActivationCapabilities.Toggle, Description: "Toggle precision cursor speed until pressed again or canceled."),
        new(InputAction.SpeedBoostHold, "SpeedBoostHold", "Speed Boost While Held", "Speed & Precision", ActivationCapabilities.Held, Description: "Multiply cursor-stick speed by the configured Speed Boost value while held. Precision takes priority."),
        new(InputAction.SpeedBoostToggle, "SpeedBoostToggle", "Speed Boost Toggle", "Speed & Precision", ActivationCapabilities.Toggle, Description: "Toggle Speed Boost until pressed again or canceled. Precision takes priority; boosts do not stack."),
        One(InputAction.Enter, "Enter", "Enter", "Keyboard & Navigation", "Press Enter once."),
        One(InputAction.Escape, "Escape", "Escape", "Keyboard & Navigation", "Esc. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.Tab, "Tab", "Tab", "Keyboard & Navigation", "Tab. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.PreviousField, "PreviousField", "Previous Field / Shift + Tab", "Keyboard & Navigation", "Shift+Tab. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.Space, "Space", "Space", "Keyboard & Navigation", "Press Space once."),
        One(InputAction.Backspace, "Backspace", "Backspace", "Keyboard & Navigation", "Press Backspace once."),
        One(InputAction.Delete, "Delete", "Delete", "Keyboard & Navigation", "Press Delete once."),
        One(InputAction.ArrowUp, "ArrowUp", "Arrow Up", "Keyboard & Navigation", "Press the Up arrow once to move the caret or navigate."),
        One(InputAction.ArrowDown, "ArrowDown", "Arrow Down", "Keyboard & Navigation", "Press the Down arrow once to move the caret or navigate."),
        One(InputAction.ArrowLeft, "ArrowLeft", "Arrow Left", "Keyboard & Navigation", "Press the Left arrow once to move the caret or navigate."),
        One(InputAction.ArrowRight, "ArrowRight", "Arrow Right", "Keyboard & Navigation", "Press the Right arrow once to move the caret or navigate."),
        One(InputAction.Home, "Home", "Home", "Keyboard & Navigation", "Home. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.End, "End", "End", "Keyboard & Navigation", "End. Acts on the focused app; support depends on that app and Windows configuration."),
        new(InputAction.StickyCtrl, "StickyCtrl", "Sticky Ctrl", "Modifiers", ActivationCapabilities.Toggle, Description: "Toggle Ctrl until pressed again or canceled. Useful for selecting separate files."),
        new(InputAction.StickyShift, "StickyShift", "Sticky Shift", "Modifiers", ActivationCapabilities.Toggle, Description: "Toggle Shift until pressed again or canceled. Useful for range selection."),
        new(InputAction.StickyAlt, "StickyAlt", "Sticky Alt", "Modifiers", ActivationCapabilities.Toggle, Description: "Toggle Alt until pressed again or canceled."),
        One(InputAction.ReleaseModifiers, "ReleaseModifiers", "Release All Modifiers", "Modifiers", "Release only FullTilt-owned Sticky Ctrl, Shift and Alt. Leave mouse holds, speed modes and physical keyboard state unchanged."),
        One(InputAction.ToggleKeyboard, "ToggleKeyboard", "Show / Hide Touch Keyboard", "Text & Input", "Toggle the Windows touch keyboard. Select a text field first. Typing support must be verified; Windows permissions and version can limit interaction."),
        One(InputAction.ClipboardHistory, "ClipboardHistory", "Clipboard History", "Text & Input", "Win+V. Open Windows clipboard history; enable history yourself if prompted."),
        One(InputAction.EmojiSymbols, "EmojiSymbols", "Emoji & Symbols", "Text & Input", "Win+.. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.VoiceTyping, "VoiceTyping", "Voice Typing", "Text & Input", "Win+H. Open Windows voice typing for the focused text field; Windows manages microphone access and availability."),
        One(InputAction.SelectAll, "SelectAll", "Select All", "Selection & Editing", "Ctrl + A in the foreground application."),
        One(InputAction.Copy, "Copy", "Copy", "Selection & Editing", "Ctrl + C in the foreground application."),
        One(InputAction.Cut, "Cut", "Cut", "Selection & Editing", "Ctrl + X in the foreground application."),
        One(InputAction.Paste, "Paste", "Paste", "Selection & Editing", "Ctrl + V in the foreground application."),
        One(InputAction.Undo, "Undo", "Undo", "Selection & Editing", "Ctrl + Z in the foreground application."),
        One(InputAction.Redo, "Redo", "Redo", "Selection & Editing", "Ctrl + Y in the foreground application; support varies."),
        One(InputAction.Save, "Save", "Save", "Selection & Editing", "Ctrl+S. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.SaveAs, "SaveAs", "Save As", "Selection & Editing", "Ctrl+Shift+S. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.Find, "Find", "Find", "Selection & Editing", "Ctrl+F. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.Rename, "Rename", "Rename", "Files & Items", "F2 in the foreground application; support varies."),
        One(InputAction.OpenItem, "OpenItem", "Open", "Files & Items", "Enter. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.DeleteItem, "DeleteItem", "Delete Item", "Files & Items", "Del. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.NewFolder, "NewFolder", "New Folder", "Files & Items", "Ctrl+Shift+N. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.Properties, "Properties", "Properties", "Files & Items", "Alt+Enter. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.ContextMenu, "ContextMenu", "Open Context Menu", "Files & Items", "Shift+F10. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.MinimizeWindow, "MinimizeWindow", "Minimize Window", "Window Controls", "Minimize the foreground window, including snapped windows."),
        One(InputAction.MaximizeWindow, "MaximizeWindow", "Maximize Window", "Window Controls", "Maximize the foreground window, including from a snapped state."),
        One(InputAction.RestoreWindow, "RestoreWindow", "Restore Window", "Window Controls", "Request the foreground window's normal restored state."),
        One(InputAction.CloseWindow, "CloseWindow", "Close Window", "Window Controls", "Request a normal close; unsaved-document prompts remain available."),
        One(InputAction.SnapLeft, "SnapLeft", "Snap Left", "Window Controls", "Windows + Left in the foreground application."),
        One(InputAction.SnapRight, "SnapRight", "Snap Right", "Window Controls", "Windows + Right in the foreground application."),
        One(InputAction.SnapTop, "SnapTop", "Snap Top", "Window Controls", "Win+Alt+Up. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.SnapBottom, "SnapBottom", "Snap Bottom", "Window Controls", "Win+Alt+Down. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.TaskView, "TaskView", "Task View", "Apps & Workspace", "Open Windows Task View."),
        One(InputAction.PreviousApp, "PreviousApp", "Previous App", "Apps & Workspace", "Alt + Tab; preserves an active Sticky Alt, otherwise releases temporary Alt ownership."),
        One(InputAction.NextApp, "NextApp", "Next App", "Apps & Workspace", "Alt+Shift+Tab. Switch in reverse task-switcher order; Previous App keeps its existing Alt+Tab behavior."),
        One(InputAction.ShowDesktop, "ShowDesktop", "Show Desktop", "Apps & Workspace", "Win+D. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.StartMenu, "StartMenu", "Start Menu", "Apps & Workspace", "Win. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.WindowsSearch, "WindowsSearch", "Windows Search", "Apps & Workspace", "Win+S. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.NextDesktop, "NextDesktop", "Next Virtual Desktop", "Apps & Workspace", "Win+Ctrl+Right. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.PreviousDesktop, "PreviousDesktop", "Previous Virtual Desktop", "Apps & Workspace", "Win+Ctrl+Left. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.CreateDesktop, "CreateDesktop", "Create Virtual Desktop", "Apps & Workspace", "Win+Ctrl+D. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.CloseDesktop, "CloseDesktop", "Close Virtual Desktop", "Apps & Workspace", "Win+Ctrl+F4. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.FileExplorer, "FileExplorer", "File Explorer", "Apps & Workspace", "Win+E. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.QuickSettings, "QuickSettings", "Quick Settings", "Apps & Workspace", "Win+A. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.Notifications, "Notifications", "Notifications", "Apps & Workspace", "Win+N. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.Back, "Back", "Back", "Browser", "Mouse navigation button Back in supporting foreground applications."),
        One(InputAction.Forward, "Forward", "Forward", "Browser", "Mouse navigation button Forward in supporting foreground applications."),
        One(InputAction.Refresh, "Refresh", "Refresh", "Browser", "F5. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.NewTab, "NewTab", "New Tab", "Browser", "Ctrl + T in the foreground application."),
        One(InputAction.CloseTab, "CloseTab", "Close Tab", "Browser", "Ctrl + W in the foreground application."),
        One(InputAction.ReopenTab, "ReopenTab", "Reopen Closed Tab", "Browser", "Ctrl + Shift + T in the foreground application."),
        One(InputAction.NextTab, "NextTab", "Next Tab", "Browser", "Ctrl + Tab in the foreground application."),
        One(InputAction.PreviousTab, "PreviousTab", "Previous Tab", "Browser", "Ctrl + Shift + Tab in the foreground application."),
        One(InputAction.FocusAddressBar, "FocusAddressBar", "Focus Address Bar", "Browser", "Ctrl+L. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.FindOnPage, "FindOnPage", "Find on Page", "Browser", "Ctrl+F. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.BrowserZoomIn, "BrowserZoomIn", "Zoom In", "Browser", "Ctrl++. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.BrowserZoomOut, "BrowserZoomOut", "Zoom Out", "Browser", "Ctrl+-. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.ResetZoom, "ResetZoom", "Reset Zoom", "Browser", "Ctrl+0. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.OpenDownloads, "OpenDownloads", "Open Downloads", "Browser", "Ctrl+J. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.PlayPause, "PlayPause", "Play / Pause", "Media", "System media command; receiving player support determines the response."),
        One(InputAction.NextTrack, "NextTrack", "Next Track", "Media", "System media command; receiving player support determines the response."),
        One(InputAction.PreviousTrack, "PreviousTrack", "Previous Track", "Media", "System media command; receiving player support determines the response."),
        new(InputAction.VolumeUp, "VolumeUp", "Volume Up", "Media", ActivationCapabilities.OneShot | ActivationCapabilities.Repeat, Description: "System output volume: immediate step, then repeat while held (300 ms delay / 80 ms interval)."),
        new(InputAction.VolumeDown, "VolumeDown", "Volume Down", "Media", ActivationCapabilities.OneShot | ActivationCapabilities.Repeat, Description: "System output volume: immediate step, then repeat while held (300 ms delay / 80 ms interval)."),
        One(InputAction.Mute, "Mute", "Mute / Unmute", "Media", "Toggle system output mute; does not change microphone mute."),
        One(InputAction.TakeScreenshot, "TakeScreenshot", "Take Screenshot", "Capture & Utilities", "Win+PrtScn. Ask Windows to save a full-screen screenshot in Pictures/Screenshots."),
        One(InputAction.SnippingTool, "SnippingTool", "Open Snipping Tool", "Capture & Utilities", "Win+Shift+S. Open the Windows region screenshot selector."),
        One(InputAction.ScreenRecording, "ScreenRecording", "Screen Recording", "Capture & Utilities", "Win+Shift+R. Open Snipping Tool recording selection on supported Windows 11 versions. Choose a region and start recording in Windows."),
        One(InputAction.Magnifier, "Magnifier", "Magnifier", "Capture & Utilities", "Win+Plus. Open Windows Magnifier; also zooms in if already open."),
        One(InputAction.MagnifierZoomIn, "MagnifierZoomIn", "Zoom In", "Capture & Utilities", "Win++. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.MagnifierZoomOut, "MagnifierZoomOut", "Zoom Out", "Capture & Utilities", "Win+-. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.CloseMagnifier, "CloseMagnifier", "Close Magnifier", "Capture & Utilities", "Win+Esc. Acts on the focused app; support depends on that app and Windows configuration."),
        One(InputAction.ReleaseCancel, "ReleaseCancel", "Release All Held Inputs", "FullTilt", "Release FullTilt-owned input, clear modes and repeats, and require neutral input. Does not hide the keyboard or undo changes."),
        One(InputAction.None, "None", "None", "None", "Do nothing.")
    ]);
    // Picker aliases share persisted IDs and execution, while preserving the requested headings.
    public static IReadOnlyList<string> Categories { get; } = Array.AsReadOnly(new[] { "Pointer & Click", "Cursor & Scroll", "Speed & Precision", "Keyboard & Navigation", "Modifiers", "Text & Input", "Selection & Editing", "Files & Items", "Window Controls", "Apps & Workspace", "Browser", "Media", "Capture & Utilities", "FullTilt", "None" });
    public static IReadOnlyList<ActionDefinition> PickerEntries { get; } = Array.AsReadOnly(All
        .Concat(All.Where(a => a.Action is InputAction.PageUp or InputAction.PageDown)
            .Select(a => a with { Category = "Keyboard & Navigation" }))
        .OrderBy(a => Categories.ToList().IndexOf(a.Category)).ToArray());
    private static ActionDefinition One(InputAction action, string id, string label, string category, string description) =>
        new(action, id, label, category, ActivationCapabilities.OneShot, Description: KeyboardActions.SingleKey(action) is not null
            ? "Hold this key while the controller button or contextual D-pad direction is pressed. Release it to let go. Pie segments tap the key." : description);
    private static readonly Dictionary<InputAction, ActionDefinition> ByAction = All.ToDictionary(a => a.Action);
    private static readonly Dictionary<string, ActionDefinition> ById = All.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
    public static ActionDefinition Get(InputAction action) => ByAction[action];
    public static bool Contains(InputAction action) => ByAction.ContainsKey(action);
    public static bool SupportsPie(InputAction action) => Contains(action) &&
        (Get(action).MouseButton is not null || (Get(action).Capabilities & (ActivationCapabilities.OneShot | ActivationCapabilities.Toggle)) != 0);
    public static string? PieDisabledReason(InputAction action, bool mouseHeld) => !SupportsPie(action) ? "Requires a physical hold; choose its toggle action for a pie menu." :
        mouseHeld && action is not (InputAction.None or InputAction.DragLock or InputAction.PrecisionToggle or InputAction.SpeedBoostToggle or InputAction.ReleaseCancel or InputAction.StickyCtrl or InputAction.StickyShift or InputAction.StickyAlt or InputAction.ReleaseModifiers or InputAction.VolumeUp or InputAction.VolumeDown or InputAction.Mute or InputAction.PlayPause or InputAction.NextTrack or InputAction.PreviousTrack)
            ? "Release the mouse hold or drag lock first." : null;
    public static bool TryFromId(string id, out ActionDefinition? definition) => ById.TryGetValue(id, out definition);
}
public sealed class ActionIdJsonConverter : JsonConverter<InputAction>
{
    public override InputAction Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && ActionCatalog.TryFromId(reader.GetString()!, out var action)) return action!.Action;
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var legacy) && ActionCatalog.Contains((InputAction)legacy)) return (InputAction)legacy;
        throw new JsonException("Unknown action identifier.");
    }
    public override void Write(Utf8JsonWriter writer, InputAction value, JsonSerializerOptions options) => writer.WriteStringValue(ActionCatalog.Get(value).Id);
}
