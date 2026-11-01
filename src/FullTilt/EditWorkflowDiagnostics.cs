using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FullTilt.Core;
using Control = FullTilt.Core.Control;

namespace FullTilt;

internal static class EditWorkflowDiagnostics
{
    public static string Verify(string output)
    {
        using var model = new SettingsViewModel(PiePresets.Initialize(new(), bind: true), new SettingsStore(Path.Combine(output, "edit-workflow-settings")), null, null);
        var window = new MainWindow(model) { AllowClose = true };
        var tabs = (TabControl)window.FindName("SettingsTabs"); tabs.SelectedIndex = 0;
        var key = (KeybindingsEditor)((ContentControl)window.FindName("KeybindingsHost")).Content;
        var pie = (PieEditor)((ContentControl)window.FindName("PieEditorHost")).Content; pie.Confirm = (_, _) => MessageBoxResult.Yes;
        var actions = (ListBox)key.FindName("Actions"); var edits = model.Edits;
        void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        void Assign(Control control, InputAction action) { key.SelectControl(control); actions.SelectedItem = actions.Items.Cast<BindingChoice>().Single(c => Equals(c.Value, action)); }
        var original = model.Settings; var profile = model.ActiveProfileId;
        Assign(Control.A, InputAction.Copy);
        Assert(ReferenceEquals(original, model.Settings) && edits.BindingDirty(Control.A) && !edits.BindingDirty(Control.B), "Draft leaked or highlighted an unchanged binding.");
        Assert(((TextBlock)key.FindName("CurrentAssignment")).Text == "Copy" && !Descendants<TextBlock>((ControllerBindingsBoard)((ContentControl)key.FindName("ControllerBoardHost")).Content).Any(t => t.Text.Contains("Unsaved") || t.Text.Contains("Saved:")), "Changed keybinding must show its assignment without inline draft annotations.");
        Assert(edits.CanUndo(EditTab.Keybindings) && !edits.CanUndo(EditTab.Movement) && !edits.CanUndo(EditTab.PieMenus), "Histories are not independent.");
        var sink = new Sink(); var engine = new InputEngine(sink); engine.Configure(model.Settings); engine.Tick(default, .02); engine.Tick(new(ControllerButtons.A), .02);
        Assert(sink.LeftHeld && sink.Actions.Count == 0, "Draft action reached runtime before Save."); engine.Tick(default, .02);
        edits.Replay(EditTab.Keybindings, true); Assert(!edits.BindingDirty(Control.A) && edits.CanRedo(EditTab.Keybindings), "Undo to saved did not clear highlights.");
        edits.Replay(EditTab.Keybindings, false); Assert(edits.BindingDirty(Control.A), "Redo did not restore the draft.");
        Assert(edits.Save(EditTab.Keybindings) && !edits.Dirty(EditTab.Keybindings) && edits.CanUndo(EditTab.Keybindings), "Save lost history or retained highlights.");
        engine.Configure(model.Settings); engine.Tick(default, .02); engine.Tick(new(ControllerButtons.A), .02);
        Assert(sink.Actions.Contains(InputAction.Copy), "Committed action was not dispatched."); engine.Tick(default, .02);
        edits.Replay(EditTab.Keybindings, true); Assert(edits.BindingDirty(Control.A) && model.Settings.Mappings[Control.A] == InputAction.Copy, "Undo after Save affected saved actions.");
        edits.Discard(EditTab.Keybindings); Assert(!edits.Dirty(EditTab.Keybindings), "Discard did not restore saved keybindings.");
        edits.Replay(EditTab.Keybindings, true); Assert(edits.BindingDirty(Control.A), "Discard was not undoable.");
        Assign(Control.X, InputAction.DoubleClick); Assert(!edits.CanRedo(EditTab.Keybindings), "New edit retained stale redo.");
        tabs.SelectedIndex = 2; var first = pie.Draft!.Menu.Id;
        pie.Diagram.TryDrop(0, new DataObject("FullTilt.PieAction", InputAction.TaskView));
        pie.SizeSlider.SetCurrentValue(RangeBase.ValueProperty, 140d);
        Assert(!Descendants<TextBlock>(pie.Diagram).Any(t => t.Text.Contains("Unsaved") || t.Text.StartsWith("Saved:")), "Changed pie segment contains inline draft annotations.");
        Assert(model.Settings.PieMenus.Single(m => m.Id == first).Slots[0] != InputAction.TaskView && model.Settings.PieMenuSizePercent == 100, "Pie action or global size leaked before Save.");
        Assert(edits.Count(EditTab.PieMenus) == 2 && edits.BindingDirty(Control.X), "Pie changed count or tab preservation is wrong.");
        var second = model.Settings.PieMenus[1].Id; pie.SelectMenu(second); pie.SelectMenu(first);
        Assert(pie.Draft!.Menu.Slots[0] == InputAction.TaskView && pie.MenuSizePercent == 140, "Menu navigation lost a draft.");
        edits.Replay(EditTab.PieMenus, true); Assert(pie.MenuSizePercent == 100 && pie.Draft.Menu.Slots[0] == InputAction.TaskView, "Pie Undo restored more than one edit.");
        edits.Replay(EditTab.PieMenus, false);
        Assert(pie.Apply() && model.Settings.PieMenuSizePercent == 140 && !edits.Dirty(EditTab.PieMenus), "Pie Save did not commit global size and slots.");
        edits.Replay(EditTab.PieMenus, true); Assert(pie.MenuSizePercent == 100 && model.Settings.PieMenuSizePercent == 140, "Pie Undo after Save changed live size.");
        edits.Discard(EditTab.PieMenus); edits.Replay(EditTab.PieMenus, true); Assert(pie.MenuSizePercent == 100, "Pie Discard was not undoable.");
        tabs.SelectedIndex = 0; Assert(edits.BindingDirty(Control.X), "Switching tabs lost key drafts.");
        var speed = model.CursorSpeed; edits.Begin(EditTab.Movement);
        model.CursorSpeed = 1000; model.CursorSpeed = 1100; model.CursorSpeed = 1400; edits.End(EditTab.Movement);
        edits.Replay(EditTab.Movement, true); Assert(model.CursorSpeed == speed && edits.BindingDirty(Control.X), "Slider gesture was multiple steps or rolled back keybindings.");
        edits.Replay(EditTab.Movement, false); Assert(model.CursorSpeed == 1400, "Movement Redo did not apply immediately.");
        model.ResetMovement(); edits.Replay(EditTab.Movement, true); Assert(model.CursorSpeed == 1400, "Reset movement was not a single undoable edit.");
        tabs.SelectedIndex = 0;
        var movement = (MovementEditor)((ContentControl)window.FindName("MovementSections")).Content;
        movement.Measure(new Size(1400, 760)); movement.Arrange(new Rect(0, 0, 1400, 760)); movement.UpdateLayout();
        var slider = (Slider)movement.DiagnosticControls["CursorSlider"];
        slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent });
        slider.SetCurrentValue(RangeBase.ValueProperty, 1600d); slider.SetCurrentValue(RangeBase.ValueProperty, 1700d);
        slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseUpEvent });
        edits.Replay(EditTab.Movement, true); Assert(model.CursorSpeed == 1400, "Routed slider drag did not group intermediate changes into one edit.");
        edits.Replay(EditTab.Movement, false); Assert(model.CursorSpeed == 1700 && model.SaveProfile(), "Routed movement redo did not save immediately.");
        pie.Confirm = (_, _) => MessageBoxResult.Cancel;
        Assert(!window.TryResolvePieDraft() && edits.HasDrafts, "Close Cancel lost drafts.");
        pie.Confirm = (_, _) => MessageBoxResult.No;
        Assert(window.TryResolvePieDraft() && !edits.HasDrafts, "Close Discard did not restore saved configuration.");
        Assert(model.CreateProfile("Second"), "Profile creation failed.");
        Assert(!edits.CanUndo(EditTab.Keybindings) && !edits.CanUndo(EditTab.Movement) && !edits.CanUndo(EditTab.PieMenus) && model.Settings.PieMenuSizePercent == 140, "History crossed profiles or size lost global scope.");
        Assert(model.SwitchProfile(profile) && edits.CanUndo(EditTab.Keybindings) && edits.CanUndo(EditTab.Movement) && edits.CanUndo(EditTab.PieMenus), "Switching back lost profile histories.");
        tabs.SelectedIndex = 2; var beforeNew = model.Settings.PieMenus.Count;
        Assert(pie.BeginNew(null) && pie.Draft!.Menu.Slots.All(a => a == InputAction.None) && model.Settings.PieMenus.Count == beforeNew, "New pie is not an empty draft.");
        var newId = pie.Draft.Menu.Id; edits.Replay(EditTab.PieMenus, true);
        Assert(!edits.View(EditTab.PieMenus).PieMenus.Any(m => m.Id == newId), "Undo New did not remove the draft menu.");
        edits.Replay(EditTab.PieMenus, false); pie.SelectMenu(newId); pie.Diagram.TryDrop(0, new DataObject("FullTilt.PieAction", InputAction.Copy));
        pie.Measure(new Size(1500, 850)); pie.Arrange(new Rect(0, 0, 1500, 850)); pie.UpdateLayout();
        var clear = Descendants<Button>(pie).Single(b => Equals(b.Content, "Clear all actions")); clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        edits.Replay(EditTab.PieMenus, true); Assert(pie.Draft!.Menu.Slots[0] == InputAction.Copy, "Clear/Restore defaults was not undoable.");
        edits.Discard(EditTab.PieMenus);
        // A conflicting shared binding needs an explicit choice; cancellation preserves both.
        Assign(Control.B, InputAction.Copy); tabs.SelectedIndex = 2; pie.SelectMenu(first); pie.Confirm = (_, _) => MessageBoxResult.Yes;
        var opener = Descendants<ComboBox>(pie).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Open with controller binding");
        opener.SelectedItem = opener.Items.Cast<PieControlOption>().Single(o => o.Control == Control.RB);
        Assign(Control.RB, InputAction.Paste); edits.ResolveBindingConflict = _ => null;
        Assert(!edits.Save(EditTab.Keybindings, EditTab.PieMenus) && edits.HasDrafts, "Conflicting drafts were silently overwritten.");
        edits.ResolveBindingConflict = _ => EditTab.PieMenus;
        Assert(edits.Save(EditTab.Keybindings, EditTab.PieMenus) && model.Settings.PieBindings[Control.RB] == first && model.Settings.Mappings[Control.B] == InputAction.Copy && !edits.HasDrafts, "Shared activation commit was inconsistent.");
        // Real selection and slider events, including intermediate values, stay detached.
        var layout = (FrameworkElement)window.Content;
        foreach (var dark in new[] { false, true })
        {
            ThemePalette.Apply(dark); Assign(Control.LT, InputAction.PrecisionHold); Assign(Control.X, InputAction.DoubleClick);
            tabs.SelectedIndex = 1; Render(layout, output, "draft-keybindings-" + (dark ? "dark" : "light"), 1536, 864);
            tabs.SelectedIndex = 2; pie.SelectMenu(first); pie.Diagram.TryDrop(0, new DataObject("FullTilt.PieAction", InputAction.Copy)); pie.MenuSizePercent = 175;
            Render(layout, output, "draft-pies-" + (dark ? "dark" : "light"), 1536, 864);
            edits.Discard(EditTab.Keybindings); edits.Discard(EditTab.PieMenus);
        }
        ThemePalette.Apply(false); window.Close();
        var protectedStore = new SettingsStore(Path.Combine(output, "draft-protected-settings")); Directory.CreateDirectory(protectedStore.DirectoryPath); File.WriteAllText(protectedStore.FilePath, "{\"Version\":99}");
        var loaded = protectedStore.Load(); using var protectedModel = new SettingsViewModel(loaded.Settings, protectedStore, null, loaded.Warning);
        var failed = new PieEditor(protectedModel); failed.BeginNew(null);
        Assert(!failed.Apply() && protectedModel.Edits.Dirty(EditTab.PieMenus) && protectedModel.Settings.PieMenus.Count == 0 && File.ReadAllText(protectedStore.FilePath) == "{\"Version\":99}", "Failed Save lost drafts or changed runtime/file.");
        return "PASS: independent per-profile histories; Undo/Redo branching, Save retains history, undoable Discard/reset/clear/new; grouped movement gesture; detached key/pie actions and global size; tab/menu/profile preservation; Save/Discard/Cancel resolution; explicit shared-binding conflict choice; atomic failure preserves drafts/runtime/file; exact changed-field count and light/dark renders. Isolated settings, fake InputEngine sink and offscreen UI only; hardware validation remains manual.";
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T found) yield return found; foreach (var item in Descendants<T>(child)) yield return item; }
    }
    private static void Render(FrameworkElement root, string output, string name, int width, int height)
    {
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(output, name + ".png")); png.Save(stream);
    }
    private sealed class Sink : IInputSink
    {
        public bool LeftHeld;
        public List<InputAction> Actions { get; } = [];
        public void Move(int dx, int dy) { }
        public void Scroll(int horizontal, int vertical) { }
        public void SetMouseButton(FullTilt.Core.MouseButton button, bool down) { if (button == FullTilt.Core.MouseButton.Left) LeftHeld = down; }
        public void Execute(InputAction action) => Actions.Add(action);
        public void SetStickyModifier(StickyModifier modifier, bool active) { }
        public void ReleaseAll() => LeftHeld = false;
    }
}
