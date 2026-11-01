using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using FullTilt.Core;
using Control = FullTilt.Core.Control;

namespace FullTilt;

internal static class IndependentActionsDiagnostics
{
    public static string Verify(string output)
    {
        static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Layout(FrameworkElement root)
        {
            root.Measure(new Size(1500, 850)); root.Arrange(new Rect(0, 0, 1500, 850)); root.UpdateLayout();
            root.Dispatcher.Invoke(() => { }, DispatcherPriority.Background); root.UpdateLayout();
        }
        using var model = new SettingsViewModel(FactoryDefaults.Create(), new SettingsStore(Path.Combine(output, "independent-actions-settings")), null, null);
        var original = model.Settings;
        var pie = new PieEditor(model);
        Layout(pie);
        static int LitSegments(PieEditor editor) => Descendants<System.Windows.Shapes.Path>(editor.Diagram).Count(p => p.Stroke is SolidColorBrush stroke && stroke.Color == ThemePalette.Get("#A4EB42").Color);
        static void ClickOutside(UIElement element) => element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
        Assert(LitSegments(pie) == 0, "Editor automatically highlighted the top segment on load.");
        var actions = Descendants<ListBox>(pie).Single(l => l.ItemsSource is not null && l.Items.OfType<ActionDefinition>().Any());
        var categories = Descendants<Expander>(actions).ToArray();
        categories[0].IsExpanded = false; Layout(pie);
        var source = actions.ItemsSource;
        var scroll = Descendants<ScrollViewer>(actions).First();
        scroll.ScrollToVerticalOffset(300); Layout(pie); var offset = scroll.VerticalOffset;
        foreach (var menu in original.PieMenus)
        {
            pie.SelectMenu(menu.Id);
            Assert(LitSegments(pie) == 0, "Menu switch automatically highlighted a segment.");
            for (var direction = 0; direction < 4; direction++)
            {
                pie.SelectDPad(direction); Layout(pie);
                Assert(ReferenceEquals(source, actions.ItemsSource) && categories.SequenceEqual(Descendants<Expander>(actions)) && !categories[0].IsExpanded &&
                    Math.Abs(scroll.VerticalOffset - offset) < .1 && actions.SelectedItem is null, "Pie target change reset the palette.");
            }
            Descendants<Button>(pie.Diagram).First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(pie);
            Assert(LitSegments(pie) == 1, "Explicit segment selection did not highlight its target.");
            Assert(!pie.IsDPadTarget && ReferenceEquals(source, actions.ItemsSource) && !categories[0].IsExpanded && Math.Abs(scroll.VerticalOffset - offset) < .1,
                "Returning from D-pad to segment reset the palette.");
        }
        Descendants<Button>(pie.Diagram).ElementAt(2).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var targetAction = pie.SelectedAction;
        ClickOutside(pie.SearchBox); Layout(pie);
        Assert(LitSegments(pie) == 0 && pie.SelectedAction == targetAction && ReferenceEquals(original, model.Settings), "Outside click changed the assignment target/settings or left the wheel lit.");
        pie.AssignSelected(InputAction.Copy);
        Assert(pie.Draft!.Menu.Slots[2] == InputAction.Copy && LitSegments(pie) == 0 && ReferenceEquals(original, model.Settings), "Action assignment after clearing the glow used another segment or applied live.");
        model.Edits.Replay(EditTab.PieMenus, true);
        Descendants<Button>(pie.Diagram).First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var directionButton = Descendants<Button>(pie.DPadEditor).First();
        ClickOutside(directionButton);
        Assert(LitSegments(pie) == 0 && ReferenceEquals(directionButton, Descendants<Button>(pie.DPadEditor).First()), "Clearing the wheel replaced the clicked D-pad control.");
        directionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(pie.IsDPadTarget, "Outside click prevented selecting a D-pad direction.");
        pie.SearchBox.Text = "copy"; Layout(pie); source = actions.ItemsSource;
        pie.SelectMenu(original.PieMenus[0].Id); pie.SelectDPad(2); Layout(pie);
        Assert(pie.SearchBox.Text == "copy" && ReferenceEquals(source, actions.ItemsSource), "Pie menu/direction changed the search.");
        var copy = actions.Items.OfType<ActionDefinition>().Single(a => a.Action == InputAction.Copy);
        actions.SelectedItem = copy;
        pie.SelectDPad(3); actions.SelectedItem = copy;
        Assert(pie.Draft!.Menu.DPadActions[2] == InputAction.Copy && pie.Draft.Menu.DPadActions[3] == InputAction.Copy && ReferenceEquals(original, model.Settings), "Repeated palette action failed or applied before Save.");
        model.Edits.Replay(EditTab.PieMenus, true); model.Edits.Replay(EditTab.PieMenus, false); pie.Discard();
        Assert(ReferenceEquals(source, actions.ItemsSource) && pie.SearchBox.Text == "copy", "History or Discard reset the palette.");
        var key = new KeybindingsEditor(model); Layout(key);
        var choices = (ListBox)key.FindName("Actions"); var search = (TextBox)key.FindName("Search");
        search.Text = "copy"; source = choices.ItemsSource;
        foreach (var control in new[] { Control.A, Control.B })
        {
            key.SelectControl(control); choices.SelectedItem = choices.Items.OfType<BindingChoice>().Single(c => Equals(c.Value, InputAction.Copy));
        }
        Assert(ReferenceEquals(source, choices.ItemsSource) && search.Text == "copy" && model.Edits.View(EditTab.Keybindings).Mappings[Control.A] == InputAction.Copy &&
            model.Edits.View(EditTab.Keybindings).Mappings[Control.B] == InputAction.Copy && ReferenceEquals(original, model.Settings), $"Repeated controller assignment failed or reset its palette: source={ReferenceEquals(source, choices.ItemsSource)}, search={search.Text}, A={model.Edits.View(EditTab.Keybindings).Mappings[Control.A]}, B={model.Edits.View(EditTab.Keybindings).Mappings[Control.B]}, runtime={ReferenceEquals(original, model.Settings)}.");
        model.Edits.Discard(EditTab.Keybindings);
        model.CursorSpeed = 1700; model.ScrollSpeed = 7; model.DPadMode = DPadMode.CursorMovement; model.ResetMovement();
        Assert(EditFields.Equal(EditFields.Read(EditTab.Movement, model.Settings), EditFields.Read(EditTab.Movement, FactoryDefaults.Create())), "Movement reset missed captured defaults.");
        var other = ProfileLibrary.Create(model.Settings, "Other"); model.SetMenus(other);
        var profile = model.ActiveProfileId; var names = model.Profiles.Select(p => p.Name).ToArray();
        model.ResetDefaults();
        foreach (var tab in new[] { EditTab.Keybindings, EditTab.PieMenus })
            Assert(EditFields.Equal(EditFields.Read(tab, model.Settings), EditFields.Read(tab, FactoryDefaults.Create())), "Profile reset missed captured keys or menus.");
        Assert(model.ActiveProfileId == profile && model.Profiles.Select(p => p.Name).SequenceEqual(names), "Reset changed the profile library.");
        var preferences = new AppPreferencesStore(Path.Combine(output, "independent-window-preferences"));
        var app = new AppPreferencesViewModel(preferences); Assert(app.Theme == "Dark" && app.Maximized, "First-run app defaults failed.");
        var window = new MainWindow(model, app) { AllowClose = true, ShowActivated = false };
        var hostedPie = (PieEditor)((ContentControl)window.FindName("PieEditorHost")).Content;
        hostedPie.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Descendants<Button>(hostedPie.Diagram).ElementAt(1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(LitSegments(hostedPie) == 1, "Hosted segment did not highlight.");
        ClickOutside(window);
        Assert(LitSegments(hostedPie) == 0, "Clicking the window outside Pie Menus left a segment lit.");
        hostedPie.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Assert(window.WindowState == WindowState.Maximized && !window.IsVisible, "New main window did not default to maximized."); window.Close();
        app.Theme = "Light"; app.Maximized = false;
        var reopened = new AppPreferencesViewModel(new AppPreferencesStore(Path.GetDirectoryName(preferences.FilePath)!));
        var normal = new MainWindow(model, reopened) { AllowClose = true, ShowActivated = false };
        Assert(normal.WindowState == WindowState.Normal && reopened.Theme == "Light" && !normal.IsVisible, "Remembered window/theme selection failed."); normal.Close();
        return "PASS: stable pie action-list/category instances, expansion, scroll and search across every menu/direction/segment; repeated same-action assignments on controller buttons and D-pad, detached drafts and history; captured Movement/profile resets, profiles retained; dark/maximized first-run defaults and light/windowed restart. Isolated stores, hidden unshown main windows and routed/offscreen UI only. Physical drag/drop and visible maximize/minimize/tray behavior remain manual.";
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
