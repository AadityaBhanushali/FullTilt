using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FullTilt.Core;

namespace FullTilt;

// Offscreen only: renders the real window tree with fake input and isolated settings; no live emulation.
internal static class TourDiagnostics
{
    private static void Fail(string message) => throw new InvalidOperationException("Tour diagnostics: " + message);

    internal static string Verify(string output)
    {
        var steps = 0;
        foreach (var (dark, width, height) in new[] { (true, 1536, 864), (false, 1536, 864), (true, 1024, 720) })
        {
            ThemePalette.Apply(dark);
            using var model = new SettingsViewModel(FactoryDefaults.Create(), new SettingsStore(Path.Combine(output, "tour-settings")), null, null);
            var window = new MainWindow(model, new AppPreferencesViewModel()) { AllowClose = true, ShowActivated = false };
            try
            {
                var tabs = window.SettingsTabs; tabs.SelectedIndex = 1;
                var content = (UIElement)window.Content; window.Content = null;
                var host = new AdornerDecorator { Child = content };
                void Layout() { host.Measure(new Size(width, height)); host.Arrange(new Rect(0, 0, width, height)); host.UpdateLayout(); host.Dispatcher.Invoke(() => { }, DispatcherPriority.Background); host.UpdateLayout(); }
                Layout();
                var settings = model.Settings; var started = tabs.SelectedIndex; bool? completed = null;
                var tour = new OnboardingTour(window, model, done => completed = done, content);
                tour.Start(host.AdornerLayer); Layout();
                if (tour.Count != 11) Fail("expected eleven steps, found " + tour.Count);
                for (var i = 0; i < tour.Count; i++)
                {
                    tour.GoTo(i); Layout(); tour.GoTo(i); Layout();
                    var bubble = tour.BubbleBounds ?? throw new InvalidOperationException("Tour diagnostics: step " + (i + 1) + " has no bubble.");
                    if (bubble.Left < 0 || bubble.Top < 0 || bubble.Right > width + .5 || bubble.Bottom > height + .5) Fail($"step {i + 1} bubble leaves the window at {width}x{height}: {bubble}");
                    var spotlight = i is not (0);
                    if (spotlight && tour.HoleBounds.Count == 0) Fail($"step {i + 1} has no highlighted area at {width}x{height}.");
                    if (i is >= 1 and <= 8 && !tour.HoleBounds.Any(hole => hole.Right < 150)) Fail($"step {i + 1} does not light its navigation tab at {width}x{height}.");
                    if (tour.HoleBounds.Any(hole => hole.IntersectsWith(bubble))) Fail($"step {i + 1} bubble covers its highlighted area at {width}x{height}.");
                    var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(host);
                    using var stream = File.Create(Path.Combine(output, $"tour-{(dark ? "dark" : "light")}-{width}-step{i + 1:00}.png"));
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream); steps++;
                    if (i == 10 && !tour.IsOpen) Fail("tour closed early.");
                }
                tour.Close(true); host.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
                if (completed != true || tour.IsOpen) Fail("finishing did not report completion.");
                if (!ReferenceEquals(settings, model.Settings) || model.Edits.HasDrafts || tabs.SelectedIndex != started || model.HasPauseDraft) Fail("the tour changed settings, drafts or the selected page.");
                if (host.AdornerLayer.GetAdorners(content) is { Length: > 0 }) Fail("the overlay was not removed.");
            }
            finally { window.Close(); ThemePalette.Apply(false); }
        }
        // Pause step wording: the game warning is required and the live shortcut is shown.
        var text = TourSteps.Create().Single(step => step.Id == "pause");
        using (var model = new SettingsViewModel(FactoryDefaults.Create(), new SettingsStore(Path.Combine(output, "tour-settings")), null, null))
        {
            var window = new MainWindow(model, new AppPreferencesViewModel()) { AllowClose = true, ShowActivated = false };
            try
            {
                var (title, body) = text.Text(new TourContext(window, model));
                if (!body.Contains("game that uses a controller", StringComparison.Ordinal) || !body.Contains(model.PauseShortcutText, StringComparison.Ordinal) || !title.Contains("Pause", StringComparison.Ordinal)) Fail("pause step must warn about controller games and show the shortcut.");
            }
            finally { window.Close(); }
        }
        return $"PASS: {steps} tour renders (11 steps, dark/light 1536x864 and dark 1024x720). Every step resolved its highlighted area, kept the bubble inside the window and off the highlight, restored the page, left settings and drafts untouched and removed the overlay. The pause step warns about controller games and shows the live shortcut. Offscreen render with isolated settings; no input service or live emulation.";
    }
}
