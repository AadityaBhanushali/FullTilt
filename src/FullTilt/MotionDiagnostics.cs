using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FullTilt.Core;

namespace FullTilt;

internal static class MotionDiagnostics
{
    internal static string Verify(string output)
    {
        void Wait(int ms = 220) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) }; timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
        void Layout(FrameworkElement element, double width, double height) { element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout(); }
        void Render(string name, FrameworkElement element, double width, double height)
        {
            Layout(element, width, height); var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        }
        try
        {
            UiMotion.DiagnosticAnimationOverride = true;
            var scale = new ScaleTransform(1, 1); UiMotion.AnimateScale(scale, .985);
            if (!scale.HasAnimatedProperties) throw new InvalidOperationException("Interactive motion did not start.");
            Wait(65); if (scale.ScaleX is <= .985 or >= 1) throw new InvalidOperationException("Motion skipped the intermediate state.");
            Wait(); if (Math.Abs(scale.ScaleX - .985) > .0001) throw new InvalidOperationException("Motion did not settle.");
            UiMotion.AnimateScale(scale, 1); Wait(); if (Math.Abs(scale.ScaleX - 1) > .0001) throw new InvalidOperationException("Motion did not restore resting state.");
            using var model = new SettingsViewModel(PiePresets.Initialize(new(), bind: true), new SettingsStore(Path.Combine(output, "motion-isolated")), null, null);
            var editor = new PieEditor(model) { Confirm = (_, _) => MessageBoxResult.Yes }; Layout(editor, 1500, 850); Wait();
            var add = Descendants<Button>(editor).Single(b => Equals(b.Content, "New pie menu")); add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!model.Edits.Dirty(EditTab.PieMenus) || !((Canvas)editor.Diagram.Child).HasAnimatedProperties) throw new InvalidOperationException("New menu did not edit immediately and animate its presentation.");
            Wait(); editor.Diagram.SetDropHover(2);
            if (editor.Diagram.DropHover != 2 || editor.Draft!.Menu.Slots.Any(a => a != InputAction.None)) throw new InvalidOperationException("Drag hover assigned an action.");
            editor.Diagram.SetDropHover(-1); if (editor.Diagram.DropHover != -1) throw new InvalidOperationException("Canceled drag left a target highlight.");
            editor.Diagram.TryDrop(2, new DataObject("FullTilt.PieAction", InputAction.Copy));
            if (!model.Edits.Dirty(EditTab.PieMenus) || editor.SelectedAction != InputAction.Copy || model.Settings.PieMenus.Any(m => m.Id == editor.Draft.Menu.Id)) throw new InvalidOperationException("Drop motion delayed editing or changed selection.");
            Wait();
            foreach (var dark in new[] { false, true })
            {
                ThemePalette.Apply(dark); editor.Refresh(); Layout(editor, 1500, 850); Wait();
                var surface = new Grid { Background = ThemePalette.Get("#FFFFFF") }; var ghost = new PieDragVisual(surface, InputAction.Copy);
                var layer = new System.Windows.Documents.AdornerDecorator { Child = surface }; Layout(layer, 500, 240); layer.AdornerLayer.Add(ghost);
                ghost.Follow(new Point(40, 45)); Layout(layer, 500, 240);
                if (ghost.Position != new Point(56, 63) || ghost.IsHitTestVisible || ghost.Focusable) throw new InvalidOperationException("Drag card position or non-interactive state is incorrect.");
                ghost.Follow(new Point(150, 85)); if (ghost.Position != new Point(166, 103)) throw new InvalidOperationException("Drag card did not follow cursor updates.");
                Render("drag-card-" + (dark ? "dark" : "light"), layer, 500, 240); layer.AdornerLayer.Remove(ghost);
                editor.Diagram.SetDropHover(1); Render("pie-drop-target-" + (dark ? "dark" : "light"), editor, 1500, 850); editor.Diagram.SetDropHover(-1);
            }
            var expander = new Expander { Style = (Style)Application.Current.FindResource("PieCategory"), Header = "Animated category", Content = new TextBlock { Text = "Content", Height = 120 } }; Layout(expander, 320, 200); expander.IsExpanded = true; Wait(); Layout(expander, 320, 200);
            var body = (FrameworkElement)expander.Template.FindName("Body", expander); if (body.Visibility != Visibility.Visible || body.ActualHeight < 100) throw new InvalidOperationException("Expanded category did not settle to full size.");
            expander.IsExpanded = false; Wait(); Layout(expander, 320, 200); if (body.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Collapsed category remained interactive or visible.");
            var thumb = new Border(); UiMotion.SetOffsetX(thumb, 22); if (!thumb.RenderTransform.HasAnimatedProperties) throw new InvalidOperationException("Switch thumb did not animate."); Wait(); if (((TranslateTransform)thumb.RenderTransform).X != 22) throw new InvalidOperationException("Switch thumb did not settle.");
            UiMotion.DiagnosticAnimationOverride = false; UiMotion.AnimateScale(scale, .985); if (scale.HasAnimatedProperties || scale.ScaleX != .985) throw new InvalidOperationException("Reduced-motion mode retained animation.");
            UiMotion.SetOffsetX(thumb, 0); if (thumb.RenderTransform.HasAnimatedProperties || ((TranslateTransform)thumb.RenderTransform).X != 0) throw new InvalidOperationException("Reduced-motion switch did not update immediately.");
            editor.BeginNew(null); if (((Canvas)editor.Diagram.Child).HasAnimatedProperties || !model.Edits.Dirty(EditTab.PieMenus)) throw new InvalidOperationException("Reduced-motion new menu animated or failed to edit.");
            return "PASS: eased intermediate/end states, resting-state recovery, immediate draft new-menu/drop behavior, non-assigning target hover and cancellation cleanup, cursor-offset drag card updates in both themes, expansion/collapse and switch settling, reduced-motion immediate states. Isolated settings/offscreen timelines only. Real OLE pointer drag, multi-monitor DPI and hardware acceptance remain manual; no live input emulation.";
        }
        finally { UiMotion.DiagnosticAnimationOverride = false; ThemePalette.Apply(false); }
    }
    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T found) yield return found; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
}
