using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using FullTilt.Core;
using Control = FullTilt.Core.Control;

namespace FullTilt;

internal enum TourSide { Right, Left, Below, Above, AnchorBottom, Center }

// One highlighted area; Elements are merged into a single cutout. ConnectTo draws a leader line to a controller dot.
internal sealed record TourSpot(IReadOnlyList<FrameworkElement> Elements, FrameworkElement? ConnectTo = null);

internal sealed record TourStep(string Id, int? Tab, Func<TourContext, (string Title, string Body)> Text,
    Func<TourContext, IReadOnlyList<TourSpot>>? Spots, TourSide[] Prefer, Func<TourContext, FrameworkElement?>? Anchor = null, bool Welcome = false, bool FileMenu = false);

// Read-only view of the live window and profile; the tour never edits either.
internal sealed class TourContext(MainWindow window, SettingsViewModel model)
{
    internal MainWindow Window { get; } = window;
    internal SettingsViewModel Model { get; } = model;
    internal DependencyObject? Root { get; init; }
    internal KeybindingsEditor? Keybindings => Window.KeybindingsHost.Content as KeybindingsEditor;
    internal static FrameworkElement? Named(DependencyObject? root, string name) => Find(root, element => AutomationProperties.GetName(element) == name);
    internal static T? OfType<T>(DependencyObject? root) where T : FrameworkElement => Find(root, element => element is T) as T;
    // Layout-visible without needing a live window, so offscreen diagnostics resolve the same targets.
    internal static bool Shown(UIElement element)
    {
        for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }
    private static FrameworkElement? Find(DependencyObject? node, Func<FrameworkElement, bool> match)
    {
        if (node is null) return null;
        if (node is FrameworkElement { ActualWidth: > 0 } element && Shown(element) && match(element)) return element;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (Find(VisualTreeHelper.GetChild(node, i), match) is { } found) return found;
        return null;
    }
    internal string Label(Control control) => EditorSession.BindingLabel(Model.Settings, control);
    internal TourSpot? Row(string row, string? dot = null)
    {
        var host = Window.KeybindingsHost;
        return Named(host, row) is { } element ? new([element], dot is null ? null : Named(host, dot)) : null;
    }
}

internal static class TourSteps
{
    private static TourSpot[] Spots(params TourSpot?[] spots) => spots.OfType<TourSpot>().ToArray();
    private static string Stick(CursorStick stick) => stick == CursorStick.Left ? "left" : "right";

    internal static IReadOnlyList<TourStep> Create() =>
    [
        new("welcome", null, _ => ("Welcome to FullTilt", "Use your controller as a mouse and keyboard on Windows.\nThis short tour covers the essentials. Skip it any time and replay it from **File → Take the tour**."),
            null, [TourSide.Center], Welcome: true),
        new("move", 1, context =>
        {
            var cursor = context.Model.CursorStick;
            var a = context.Label(Control.A);
            var click = a == "Left Click" ? "Press **A** to click." : $"**A** is set to {a}.";
            return ("Move and click", $"Use the **{Stick(cursor)} stick** to move the cursor.\n{click} Use the **{Stick(cursor == CursorStick.Left ? CursorStick.Right : CursorStick.Left)} stick** to scroll.");
        }, context => Spots(context.Row("Select Left joystick travel", "Press Left joystick travel target"), context.Row("Select Right joystick travel", "Press Right joystick travel target"),
            context.Row("Select A keybinding", "Press A diagram target")),
            [TourSide.AnchorBottom, TourSide.Below], context => context.Keybindings is { } editor ? editor.FindName("ControllerBoardHost") as FrameworkElement : null),
        new("speed", 1, context =>
        {
            var lt = context.Label(Control.LT);
            return lt == "Speed Boost While Held"
                ? ("Hold LT for speed boost", "Hold **LT** to move the cursor faster.\nRelease it to return to normal speed for precise selection.")
                : ("Triggers are buttons too", $"**LT** is set to {lt}. Choose it to assign any action, such as **Speed Boost While Held** to move the cursor faster.");
        }, context => Spots(context.Row("Select LT keybinding", "Press LT diagram target")),
            [TourSide.AnchorBottom, TourSide.Below], context => context.Keybindings is { } editor ? editor.FindName("ControllerBoardHost") as FrameworkElement : null),
        new("assign", 1, _ => ("Assign any action", "Click a button, then pick an action here, or choose **Record key / shortcut** to capture your own.\nUse search to find actions quickly."),
            context => context.Keybindings?.FindName("AssignmentPane") is FrameworkElement pane ? [new([pane])] : [], [TourSide.Left, TourSide.Right]),
        new("save", 1, _ => ("Edits stay drafts until you save", "Binding changes are drafts until you press **Save changes**.\n**Undo**, **Redo** and **Discard** work on those drafts.\nMovement and Settings save automatically."),
            context => TourContext.Named(context.Window.KeybindingsHost, "Save changes") is { } save
                ? [new(new[] { "Undo", "Redo", "Discard" }.Select(name => TourContext.Named(context.Window.KeybindingsHost, name)).OfType<FrameworkElement>().Append(save).ToList())] : [],
            [TourSide.Above]),
        new("movement", 0, _ => ("Tune how it moves", "Set cursor, scroll and response speeds here.\nMovement changes apply and save **immediately**, so you can feel each change right away."),
            context => context.Window.MovementSections.Content is MovementEditor movement ? [new([movement.CursorCard])] : [], [TourSide.Right, TourSide.Below]),
        new("pie", 2, _ => ("Hold a button to open a pie menu", "Choose which controller button opens each menu.\nHold it, aim the stick at a segment, then release to run its action."),
            context => Spots(TourContext.Named(context.Window.PieEditorHost, "Open with controller binding") is { } open ? new TourSpot([open]) : null,
                TourContext.OfType<PieDiagram>(context.Window.PieEditorHost) is { } diagram ? new TourSpot([diagram]) : null), [TourSide.Right, TourSide.Left]),
        new("dpad", 2, _ => ("D-pad actions change with the menu", "These actions work only while this pie menu is open.\nEach menu can have different D-pad actions.\nClose the menu to use your Keybindings defaults."),
            context => TourContext.OfType<PieDPadEditor>(context.Window.PieEditorHost) is { } pad ? [new([pad])] : [], [TourSide.Left, TourSide.Right]),
        new("pause", 4, context => ("Pause before playing a game",
            $"!!**Pause FullTilt when you play a game that uses a controller**, so it does not move the cursor or press keys during play.\nHold **{context.Model.PauseShortcutText}** together to pause or resume. The on-screen indicator shows when FullTilt is paused."),
            context => [new([context.Window.PauseShortcutCard])], [TourSide.Left, TourSide.Right]),
        new("file", null, _ => ("Replay the tour any time", "Open the **File** menu at the top left and choose **Take the tour** to see this walkthrough again."),
            context => TourContext.Named(context.Root, "File") is { } file ? [new([file])] : [], [TourSide.Right, TourSide.Below], FileMenu: true),
        new("done", null, _ => ("You're all set", "Use the profile bar to keep separate setups.\nFullTilt starts with Windows in the system tray; turn that off in **Settings → General**."),
            context => [new([context.Window.ProfileControls])], [TourSide.Below, TourSide.Center]),
    ];
}
