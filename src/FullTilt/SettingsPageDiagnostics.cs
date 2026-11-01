using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FullTilt.Core;
using FullTilt.Windows;

namespace FullTilt;

internal static class SettingsPageDiagnostics
{
    public static string Verify(string output)
    {
        var source = new Source(); var sink = new Sink();
        using var service = new ControllerService(new(), source, sink); service.Start();
        using var model = new SettingsViewModel(new(), new SettingsStore(Path.Combine(output, "settings-page-isolated")), service, null);
        Wait(() => service.Status.ActiveSlot == 0); model.UpdateStatus(service.Status);
        var window = new MainWindow(model) { AllowClose = true };
        var tabs = (TabControl)window.FindName("SettingsTabs"); tabs.SelectedIndex = 4;
        var root = (FrameworkElement)window.Content;
        var mouseKey = typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as DependencyPropertyKey;
        if (mouseKey is null) throw new InvalidOperationException("WPF hover diagnostic key unavailable.");
        void Render(string name, double width = 1700, double height = 1080)
        {
            root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
            using var stream = File.Create(Path.Combine(output, name + ".png")); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
        }
        ThemePalette.Apply(false); Render("settings-4.1-light");
        var left = (FrameworkElement)window.FindName("GeneralSettings"); var right = (FrameworkElement)window.FindName("PauseShortcutCard");
        if (right.TranslatePoint(new Point(), root).X <= left.TranslatePoint(new Point(left.ActualWidth, 0), root).X - 1) throw new InvalidOperationException("Wide Settings cards overlap instead of using two columns.");
        if (Descendants<Button>(root).Any(button => button.Content is "Record shortcut" or "Record again" && button.Visibility != Visibility.Collapsed)) throw new InvalidOperationException("Recording controls must stay hidden.");
        var saved = model.Settings.PauseShortcut;
        foreach (var option in model.PauseButtons) option.Selected = option.Button is ControllerButtons.LB or ControllerButtons.RB;
        if (!model.CanApplyPauseShortcut || model.Settings.PauseShortcut != saved || service.ShortcutCapture.Active) throw new InvalidOperationException("Manual shortcut selection must edit only the draft.");
        var theme = (ListBox)window.FindName("ThemePicker"); theme.SelectedItem = "Dark";
        Render("settings-4.1-manual-dark");
        var addButton = Descendants<Button>(root).Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "Add pause shortcut button");
        UiChromeDiagnostics.Verify(addButton.ContextMenu, model, output);
        addButton.ApplyTemplate(); var actionFrame = (Border)addButton.Template.FindName("ActionFrame", addButton);
        var borderBefore = ((SolidColorBrush)actionFrame.BorderBrush).Color;
        addButton.SetValue(mouseKey, true); root.UpdateLayout();
        if (((SolidColorBrush)actionFrame.BorderBrush).Color == borderBefore) throw new InvalidOperationException("Add button hover did not outline the action.");
        Render("settings-4.1-add-button-hover"); addButton.SetValue(mouseKey, false);
        model.ApplyPauseShortcut(); Wait(() => !service.ShortcutCapture.Active);
        if (model.Settings.PauseShortcut != (ControllerButtons.LB | ControllerButtons.RB)) throw new InvalidOperationException("Manual selection did not save through the existing model.");
        tabs.SelectedIndex = 0;
        tabs.SelectedIndex = 4;
        model.PauseButtons.Single(option => option.Button == ControllerButtons.A).Selected = true;
        model.DiscardPauseShortcutDraft();
        if (model.HasPauseDraft) throw new InvalidOperationException("Cancel failed to restore saved shortcut chips.");
        theme.SelectedItem = "Light";
        Render("settings-4.1-compact", 864);
        foreach (var option in model.PauseButtons) option.Selected = option.Button is ControllerButtons.LB or ControllerButtons.RB or ControllerButtons.LeftThumb or ControllerButtons.RightThumb;
        model.ApplyPauseShortcut();
        foreach (var option in model.PauseButtons) option.Selected = option.Button is ControllerButtons.Start or ControllerButtons.Back or ControllerButtons.DPadUp or ControllerButtons.DPadLeft;
        foreach (var size in new[] { new Size(1920, 1080), new Size(1700, 1080), new Size(1390, 900), new Size(1080, 760), new Size(864, 640), new Size(760, 620) })
        {
            Render($"settings-responsive-{size.Width}x{size.Height}", size.Width, size.Height);
            var viewport = (FrameworkElement)window.FindName("SettingsFitViewport");
            var page = (FrameworkElement)window.FindName("SettingsPage");
            if (Descendants<ScrollViewer>(page).Any(scroll => scroll.TemplatedParent is not TextBox)) throw new InvalidOperationException("Settings page contains a scroll viewer.");
            if (Descendants<Viewbox>(page).Any()) throw new InvalidOperationException("Settings must reflow without scaling its controls.");
            foreach (var name in new[] { "GeneralSettings", "AppearanceSettings", "ControllerCard", "PauseShortcutCard", "SharedSettingsFooter" })
            {
                var card = (FrameworkElement)window.FindName(name);
                File.AppendAllText(Path.Combine(output, "responsive-bounds.txt"), size + " " + name + " desired=" + card.DesiredSize + " actual=" + card.RenderSize + " margin=" + card.Margin + Environment.NewLine);
                var bounds = card.TransformToAncestor(viewport).TransformBounds(new Rect(new Size(card.ActualWidth, card.ActualHeight)));
                if (bounds.Left < -1 || bounds.Top < -1 || bounds.Right > viewport.ActualWidth + 1 || bounds.Bottom > viewport.ActualHeight + 1) throw new InvalidOperationException("Settings card does not fit entirely on a single page: " + name + " at " + size + " bounds=" + bounds + " viewport=" + viewport.RenderSize);
            }
        }
        model.DiscardPauseShortcutDraft();
        // Real resize sequences exercise layout, without displaying a window or touching settings.
        foreach (var tabIndex in new[] { 0, 1, 2 })
        {
            tabs.SelectedIndex = tabIndex;
            foreach (var size in new[] { new Size(1920, 1080), new Size(1390, 900), new Size(1080, 760), new Size(864, 640), new Size(760, 620) })
            {
                Render($"page-{tabIndex}-responsive-{size.Width}x{size.Height}", size.Width, size.Height);
                if (tabIndex == 0)
                {
                    var movementPanel = (MovementEditor)((ContentControl)window.FindName("MovementSections")).Content;
                    var a = movementPanel.CursorCard.TransformToAncestor(movementPanel).TransformBounds(new Rect(movementPanel.CursorCard.RenderSize));
                    var b = movementPanel.ScrollCard.TransformToAncestor(movementPanel).TransformBounds(new Rect(movementPanel.ScrollCard.RenderSize));
                    if (movementPanel.ActualWidth >= 980 ? b.Left < a.Right || Math.Abs(a.Top - b.Top) > 1 : b.Top < a.Bottom)
                        throw new InvalidOperationException("Movement cards must reflow without overlap: " + size + " a=" + a + " b=" + b);
                }
                if (tabIndex == 1)
                {
                    var editor = (KeybindingsEditor)((ContentControl)window.FindName("KeybindingsHost")).Content;
                    var board = (ContentControl)editor.FindName("ControllerBoardHost");
                    if (board.Visibility == Visibility.Visible)
                    {
                        var rows = Descendants<Button>(board).Where(button => System.Windows.Automation.AutomationProperties.GetName(button).StartsWith("Select ", StringComparison.Ordinal)).ToArray();
                        foreach (var column in rows.GroupBy(Canvas.GetLeft))
                        {
                            var ordered = column.OrderBy(Canvas.GetTop).ToArray();
                            for (var i = 1; i < ordered.Length; i++)
                                if (Canvas.GetTop(ordered[i]) < Canvas.GetTop(ordered[i - 1]) + ordered[i - 1].ActualHeight - 1)
                                    throw new InvalidOperationException("Controller assignment rows overlap after resizing.");
                        }
                    }
                    else if (((ComboBox)editor.FindName("ControlPicker")).Items.Count != 16)
                        throw new InvalidOperationException("Compact picker lost a controller target.");
                }
            }
        }
        tabs.SelectedIndex = 4;
        // Synthetic property state exercises actual WPF triggers; it never moves a real pointer.
        var movement = (TabItem)tabs.Items[0]; movement.ApplyTemplate();
        var frame = (Border)movement.Template.FindName("TabFrame", movement);
        var before = ((SolidColorBrush)frame.Background).Color;
        movement.SetValue(mouseKey, true); root.UpdateLayout();
        if (((SolidColorBrush)frame.Background).Color == before || movement.IsSelected) throw new InvalidOperationException("Hover must highlight without selecting a tab.");
        Render("settings-4.1-navigation-hover"); movement.SetValue(mouseKey, false);
        model.StopShortcutRecording(); window.Close(); ThemePalette.Apply(false);
        if (sink.Actions != 0) throw new InvalidOperationException("Recording emitted fake normal input.");
        return "PASS: responsive native-size controls (no Viewbox), maximum saved/draft chord wrapping; all visible pages rendered at full, restored and compact sizes; non-overlapping controller/movement rows; hidden recording controls; manual shortcut draft/save/cancel without capture; all five cards fit the non-scrolling Settings viewport at 1700x1080, 1390x900, 1080x760, 864x640 and 760x620; theme selection and synthetic tab/Add-button hover triggers. No physical controller, native input, clipboard, startup registration or real folder actions exercised.";
    }
    private static void Wait(Func<bool> predicate)
    {
        for (var i = 0; i < 300; i++) { if (predicate()) return; Thread.Sleep(10); }
        throw new InvalidOperationException("Fake controller diagnostic timed out.");
    }
    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i); if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private sealed class Source : IControllerSource
    {
        private int _buttons;
        public void Set(ControllerButtons buttons) => Volatile.Write(ref _buttons, (int)buttons);
        public bool TryGetState(int slot, out ControllerState state) { state = new((ControllerButtons)Volatile.Read(ref _buttons)); return slot == 0; }
    }
    private sealed class Sink : IInputSink
    {
        public int Actions;
        public void Move(int x, int y) => Interlocked.Increment(ref Actions);
        public void Scroll(int x, int y) => Interlocked.Increment(ref Actions);
        public void SetMouseButton(MouseButton button, bool down) { if (down) Interlocked.Increment(ref Actions); }
        public void Execute(InputAction action) => Interlocked.Increment(ref Actions);
        public void SetStickyModifier(StickyModifier modifier, bool active) { if (active) Interlocked.Increment(ref Actions); }
        public void ReleaseAll() { }
    }
}
