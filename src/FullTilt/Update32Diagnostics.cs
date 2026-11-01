using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Documents;
using FullTilt.Core;
using FullTilt.Windows;

namespace FullTilt;

internal static class Update32Diagnostics
{
    public static string VerifyShortcut(SettingsViewModel model, CheckBox[] boxes, Button apply, string output)
    {
        var choices = boxes.Where(b => b.DataContext is PauseButtonOption).ToArray();
        if (choices.Length != PauseShortcutRules.Buttons.Count) throw new InvalidOperationException("Shortcut picker missing buttons.");
        void Select(ControllerButtons mask)
        {
            foreach (var box in choices) box.SetCurrentValue(ToggleButton.IsCheckedProperty, (mask & ((PauseButtonOption)box.DataContext).Button) != 0);
        }
        var savedShortcut = model.Settings.PauseShortcut;
        Select(ControllerButtons.A); apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (apply.IsEnabled || model.Settings.PauseShortcut != savedShortcut) throw new InvalidOperationException("Invalid shortcut became active.");
        foreach (var mask in new[] { PauseShortcutRules.Default, PauseShortcutRules.Default | ControllerButtons.LB, PauseShortcutRules.Default | ControllerButtons.LB | ControllerButtons.RB })
        {
            Select(mask); if (!apply.IsEnabled) throw new InvalidOperationException("Valid shortcut disabled.");
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (model.Settings.PauseShortcut != mask) throw new InvalidOperationException("Shortcut binding did not apply.");
            var store = new SettingsStore(Path.Combine(output, "shortcut-settings"));
            if (store.Save(model.Settings) is not null || store.Load().Settings.PauseShortcut != mask) throw new InvalidOperationException("Shortcut did not persist.");
            model.UpdateStatus(new(0, 1, true, false, null));
            if (!model.StatusDetail.Contains(PauseShortcutRules.Format(mask))) throw new InvalidOperationException("Pause hint is stale.");
        }
        Select(PauseShortcutRules.Default); apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); model.UpdateStatus(new(0, 1, false, false, null));
        return "PASS: real checkbox/Apply bindings enforce 2–4 unique buttons, reject one button, update status hints, and persist all supported chord sizes in isolated settings.";
    }
    public static string VerifyCaret(ControlTemplate textBoxTemplate)
    {
        var box = new TextBox { Template = textBoxTemplate, Text = "abc\ndef", AcceptsReturn = true, Width = 240, Height = 100 };
        box.Measure(new Size(240, 100)); box.Arrange(new Rect(0, 0, 240, 100)); box.UpdateLayout();
        var input = new CaretInput(box); var dispatcher = new WindowsActionDispatcher(new KeyboardOwnership(input), new NoDesktop());
        box.CaretIndex = 2;
        void Execute(InputAction action, int expected)
        {
            dispatcher.Execute(action); box.UpdateLayout();
            box.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background); box.UpdateLayout();
            if (box.CaretIndex != expected) throw new InvalidOperationException($"{action} caret expected {expected}, got {box.CaretIndex}; lines {box.LineCount}.");
        }
        Execute(InputAction.ArrowRight, 3); Execute(InputAction.ArrowLeft, 2);
        Execute(InputAction.ArrowDown, 6); Execute(InputAction.ArrowUp, 2); Execute(InputAction.Backspace, 1);
        if (box.Text != "ac\ndef" || input.Down != 0) throw new InvalidOperationException("Backspace or key ownership failed.");
        return "PASS: dispatcher key pairs adapted to real offscreen WPF editing commands move the caret left/right/up/down and delete the previous character. This is not native input or hardware validation.";
    }
    private sealed class CaretInput(TextBox box) : IKeyInput
    {
        public int Down;
        public bool IsDown(ushort key) => false;
        public int Send(ReadOnlySpan<KeyTransition> transitions)
        {
            foreach (var key in transitions)
            {
                Down += key.Down ? 1 : -1;
                if (!key.Down) continue;
                RoutedUICommand command = key.Key switch {
                    0x25 => EditingCommands.MoveLeftByCharacter, 0x27 => EditingCommands.MoveRightByCharacter,
                    0x26 => EditingCommands.MoveUpByLine, 0x28 => EditingCommands.MoveDownByLine,
                    0x08 => EditingCommands.Backspace, _ => throw new InvalidOperationException("Unexpected key.") };
                command.Execute(null, box);
            }
            return transitions.Length;
        }
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
