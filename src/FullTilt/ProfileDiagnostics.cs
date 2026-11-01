using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FullTilt.Core;
using FullTilt.Windows;

namespace FullTilt;

internal static class ProfileDiagnostics
{
    public static string Verify(string output, ControlTemplate textBoxTemplate)
    {
        var store = new SettingsStore(Path.Combine(output, "profile-ui-settings"));
        Guid savedId; string expected;
        using (var model = new SettingsViewModel(PiePresets.Initialize(new(), bind: true), store, null, null))
        {
            var window = new MainWindow(model) { AllowClose = true, RequestProfileName = () => "Alternate" };
            var root = (FrameworkElement)window.Content; Layout(root);
            var picker = (ComboBox)window.FindName("ProfilePicker");
            Button Button(string label) => Descendants<Button>(root).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == label);
            savedId = model.ActiveProfileId;
            if (model.Profiles.Single().Name != "Default" || !Equals(picker.SelectedValue, savedId)) throw new InvalidOperationException("Default profile selection missing.");
            model.CursorSpeed = 1777; model.SpeedBoostMultiplier = 2.75; model.PrecisionPercent = 35; model.DPadMode = DPadMode.AssignedActions;
            model.DPadBindings.Single(b => b.Control == FullTilt.Core.Control.DPadUp).Action = InputAction.ArrowUp;
            model.Bindings.Single(b => b.Control == FullTilt.Core.Control.A).Action = InputAction.Backspace;
            var editor = (PieEditor)((ContentControl)window.FindName("PieEditorHost")).Content;
            editor.Confirm = (_, _) => MessageBoxResult.Yes; editor.BeginNew(null); editor.Draft!.Assign(0, InputAction.Enter); editor.Apply();
            foreach (var option in model.PauseButtons) option.Selected = option.Button is ControllerButtons.DPadUp or ControllerButtons.B or ControllerButtons.Y;
            model.ApplyPauseShortcut();
            Button("Save FullTilt profile").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            expected = Snapshot(model.Settings);
            Button("New FullTilt profile").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Layout(root);
            var alternate = model.ActiveProfileId;
            if (alternate == savedId || model.Profiles.Count != 2 || !Equals(picker.SelectedValue, alternate)) throw new InvalidOperationException($"New profile did not become selected: active={alternate}, old={savedId}, count={model.Profiles.Count}, picker={picker.SelectedValue}, warning={model.Warning}.");
            model.CursorSpeed = 825; model.SpeedBoostMultiplier = 1.25; model.ShowStateIndicator = false;
            var alternateExpected = Snapshot(model.Settings);
            picker.SetCurrentValue(Selector.SelectedValueProperty, savedId); Layout(root);
            if (model.ActiveProfileId != savedId || Snapshot(model.Settings) != expected || model.CursorSpeed != 1777) throw new InvalidOperationException("Profile selector did not restore original settings.");
            picker.SetCurrentValue(Selector.SelectedValueProperty, alternate); Layout(root);
            if (Snapshot(model.Settings) != alternateExpected) throw new InvalidOperationException("Outgoing edits were lost during switch.");
            picker.SetCurrentValue(Selector.SelectedValueProperty, savedId); Layout(root);
            // Pending pie edits require an explicit Save/Discard/Cancel decision before switching.
            editor.BeginNew(null); var newMenuId = editor.Draft!.Menu.Id; editor.Draft.Rename("Saved before profile switch");
            editor.Confirm = (_, _) => MessageBoxResult.Yes;
            picker.SetCurrentValue(Selector.SelectedValueProperty, alternate); Layout(root);
            if (model.ActiveProfileId != alternate || !Equals(picker.SelectedValue, alternate)) throw new InvalidOperationException("Autosaved pie prevented profile switch.");
            picker.SetCurrentValue(Selector.SelectedValueProperty, savedId); Layout(root);
            if (model.Settings.PieMenus.Single(m => m.Id == newMenuId).Name != "Saved before profile switch") throw new InvalidOperationException("Profile switch lost pending pie edits.");
            expected = Snapshot(model.Settings);
            var beforeRename = Snapshot(model.Settings);
            window.RequestRenameProfile = _ => "Renamed Default";
            Button("Rename FullTilt profile").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Layout(root);
            if (model.ActiveProfileId != savedId || Snapshot(model.Settings) != beforeRename || model.Profiles.Single(p => p.Id == savedId).Name != "Renamed Default") throw new InvalidOperationException("Rename changed configuration or failed.");
            window.ConfirmProfile = (_, _) => MessageBoxResult.No;
            Button("Delete FullTilt profile").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (model.Profiles.Count != 2) throw new InvalidOperationException("Canceled deletion removed a profile.");
            picker.SetCurrentValue(Selector.SelectedValueProperty, alternate); Layout(root);
            window.ConfirmProfile = (_, _) => MessageBoxResult.Yes;
            Button("Delete FullTilt profile").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Layout(root);
            if (model.ActiveProfileId != savedId || model.Profiles.Count != 1 || Snapshot(model.Settings) != expected || Button("Delete FullTilt profile").IsEnabled) throw new InvalidOperationException("Delete failed to restore fallback or protect last profile.");
            var image = new RenderTargetBitmap(864, 840, 96, 96, PixelFormats.Pbgra32);
            var background = new DrawingVisual(); using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, 864, 840));
            image.Render(background); image.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var file = File.Create(Path.Combine(output, "profiles.png"))) encoder.Save(file);
            window.Close();
        }
        using (var reopened = new SettingsViewModel(new SettingsStore(store.DirectoryPath).Load().Settings, new SettingsStore(store.DirectoryPath), null, null))
            if (reopened.ActiveProfileId != savedId || reopened.Profiles.Count != 1 || Snapshot(reopened.Settings) != expected || reopened.Profiles.Single().Name != "Renamed Default") throw new InvalidOperationException("Profiles did not survive restart.");
        var blockedStore = new SettingsStore(Path.Combine(output, "blocked-profile-settings"));
        Directory.CreateDirectory(blockedStore.FilePath);
        using (var blocked = new SettingsViewModel(PiePresets.Initialize(new(), bind: true), blockedStore, null, null))
        {
            var before = blocked.ActiveProfileId;
            if (blocked.RenameProfile("Cannot rename") || blocked.Profiles.Single().Name != "Default") throw new InvalidOperationException("Failed rename changed the profile.");
            if (blocked.CreateProfile("Cannot save") || blocked.ActiveProfileId != before || blocked.Profiles.Count != 1 || blocked.Warning is null)
                throw new InvalidOperationException("Failed profile save changed active configuration.");
        }
        using (var blocked = new SettingsViewModel(ProfileLibrary.Create(new(), "Second"), blockedStore, null, null))
        {
            var before = blocked.Settings;
            if (blocked.DeleteProfile() || !ReferenceEquals(before, blocked.Settings) || blocked.Profiles.Count != 2) throw new InvalidOperationException("Failed delete changed profile settings.");
        }
        var box = new TextBox { Template = textBoxTemplate, Text = "abc\ndef", AcceptsReturn = true, Width = 240, Height = 100 };
        box.Measure(new Size(240, 100)); box.Arrange(new Rect(0, 0, 240, 100)); box.UpdateLayout();
        var keys = new UpCaretInput(box); var keyboard = new KeyboardOwnership(keys); var dispatcher = new WindowsActionDispatcher(keyboard, new NoDesktop());
        var engine = new InputEngine(new KeyboardSink(keyboard, dispatcher));
        engine.Configure(new() { DPadMode = DPadMode.AssignedActions, PauseShortcut = ControllerButtons.DPadUp | ControllerButtons.B | ControllerButtons.Y,
            Mappings = new() { [FullTilt.Core.Control.DPadUp] = InputAction.ArrowUp, [FullTilt.Core.Control.L3] = InputAction.ArrowUp } });
        engine.Tick(default, .01);
        foreach (var button in new[] { ControllerButtons.DPadUp, ControllerButtons.LeftThumb })
        {
            // Establish the offscreen text view's preferred horizontal caret position
            // through normal editing commands; there is no focused native text window.
            box.CaretIndex = 2;
            EditingCommands.MoveRightByCharacter.Execute(null, box); EditingCommands.MoveLeftByCharacter.Execute(null, box);
            EditingCommands.MoveDownByLine.Execute(null, box); box.UpdateLayout();
            if (box.CaretIndex != 6) throw new InvalidOperationException("Text caret preparation failed.");
            engine.Tick(new(button), .01); box.UpdateLayout();
            box.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            if (box.CaretIndex != 2) throw new InvalidOperationException("Arrow Up failed in offscreen text field from " + button + ": caret " + box.CaretIndex + ", lines " + box.LineCount);
            engine.Tick(default, .01);
        }
        return "PASS: Default, New, Save, Rename, confirmed/canceled Delete, last-profile protection and selector handlers restore full settings, preserve outgoing edits and save pending pie edits after explicit confirmation before switching; a reopened store/view model retains profiles and active selection. Arrow Up from D-pad Up and L3 moves a real offscreen text caret through an adapter with the user's exact shortcut. No live input or user settings used.";
    }
    private static string Snapshot(AppSettings settings) => JsonSerializer.Serialize(ProfileLibrary.Snapshot(settings));
    private static void Layout(FrameworkElement root) { root.Measure(new Size(864, 840)); root.Arrange(new Rect(0, 0, 864, 840)); root.UpdateLayout(); root.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background); root.UpdateLayout(); }
    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); if (child is T found) yield return found; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
    private sealed class UpCaretInput(TextBox box) : IKeyInput
    {
        public bool IsDown(ushort key) => false;
        public int Send(ReadOnlySpan<KeyTransition> transitions)
        { foreach (var key in transitions) { if (key.Key != 0x26) throw new InvalidOperationException("Unexpected key"); if (key.Down) EditingCommands.MoveUpByLine.Execute(null, box); } return transitions.Length; }
    }
    private sealed class KeyboardSink(KeyboardOwnership keyboard, WindowsActionDispatcher dispatcher) : IInputSink
    {
        public void BeginKeyboardFrame() => dispatcher.BeginKeyboardFrame();
        public void SetKeyboardHolds(ReadOnlySpan<InputAction> actions, IReadOnlyList<KeyboardChord> recordings) => dispatcher.SetKeyboardHolds(actions, recordings);
        public void Move(int dx, int dy) { }
        public void Scroll(int horizontal, int vertical) { }
        public void SetMouseButton(FullTilt.Core.MouseButton button, bool down) { }
        public void Execute(InputAction action) => dispatcher.Execute(action);
        public void SetStickyModifier(StickyModifier modifier, bool active) => keyboard.SetLatch(modifier, active);
        public void ReleaseAll() => keyboard.ReleaseAll();
    }
    private sealed class NoDesktop : IDesktopActions
    {
        public string? Error => null;
        public void Window(WindowCommand command) => throw new InvalidOperationException();
        public void Navigate(bool forward) => throw new InvalidOperationException();
        public void ToggleKeyboard() => throw new InvalidOperationException();
        public void CenterCursor() => throw new InvalidOperationException();
        public void Scroll(int horizontal, int vertical) => throw new InvalidOperationException();
        public void CancelPending() { }
    }
}
