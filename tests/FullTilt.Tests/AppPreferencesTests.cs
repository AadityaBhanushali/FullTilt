using FullTilt.Core;

static class AppPreferencesTests
{
    public static void Register(TestSuite suite)
    {
        suite.Add("Dark and maximized defaults retain explicit old preferences and last window mode", () =>
        {
            using var dir = new TestDirectory(); var store = new AppPreferencesStore(dir.Path);
            Check.True(store.Load().Maximized); Check.Equal("Dark", store.Load().Theme);
            File.WriteAllText(store.FilePath, "{\"Version\":1,\"Theme\":\"Light\"}");
            var old = store.Load(); Check.Equal("Light", old.Theme); Check.True(old.Maximized);
            Check.True(store.Save(old with { Maximized = false }) is null);
            var loaded = new AppPreferencesStore(dir.Path).Load(); Check.True(!loaded.Maximized); Check.Equal("Light", loaded.Theme);
            Check.True(store.Save(loaded with { Theme = "System default" }) is null); Check.True(!store.Load().Maximized);
        });
        suite.Add("Tour and startup flags are optional, round trip and repair without erasing other preferences", () =>
        {
            using var dir = new TestDirectory(); var store = new AppPreferencesStore(dir.Path);
            File.WriteAllText(store.FilePath, "{\"Version\":1,\"Theme\":\"Light\"}");
            var old = store.Load(); Check.True(old.OnboardingCompleted is null && !old.StartupDefaultApplied && old.Theme == "Light");
            Check.True(store.Save(old with { OnboardingCompleted = false, StartupDefaultApplied = true }) is null);
            var saved = new AppPreferencesStore(dir.Path).Load(); Check.True(saved.OnboardingCompleted == false && saved.StartupDefaultApplied);
            File.WriteAllText(store.FilePath, "{\"Version\":1,\"Theme\":\"Light\",\"OnboardingCompleted\":\"yes\",\"StartupDefaultApplied\":3}");
            var repaired = new AppPreferencesStore(dir.Path); var result = repaired.Load();
            Check.True(result.Theme == "Light" && result.OnboardingCompleted is null && !result.StartupDefaultApplied && repaired.Warning is not null);
            Check.True(Directory.GetFiles(dir.Path, "*.recovered-*.bak").Length == 1);
        });
        suite.Add("Factory profile returns independent keyboard, movement and pie data for every reset", () =>
        {
            var first = FactoryDefaults.Create(); var second = FactoryDefaults.Create();
            Check.Equal(600d, first.CursorSpeed); Check.Equal(10d, first.ScrollSpeed); Check.Equal(2.6, first.ResponseCurve);
            Check.Equal(10d, first.SpeedBoostMultiplier); Check.Equal(4d, first.ScrollBoostMultiplier); Check.Equal(DPadMode.AssignedActions, first.DPadMode);
            Check.Equal(ControllerButtons.DPadUp | ControllerButtons.B | ControllerButtons.Y, first.PauseShortcut);
            Check.Equal(InputAction.None, first.Mappings[Control.R3]); Check.Equal(InputAction.SpeedBoostHold, first.Mappings[Control.LT]);
            Check.Equal(InputAction.Escape, first.Mappings[Control.B]); Check.True(first.RecordedBindings.Count == 0 && first.ShowStateIndicator); Check.Equal(5, first.PieMenus.Count);
            Check.True(first.Profiles is null && first.ActiveProfileId is null && !first.TrayHintShown);
            first.PieMenus[0].Slots[0] = InputAction.Copy; first.PieMenus[0].DPadActions[0] = InputAction.None;
            first.Mappings[Control.B] = InputAction.Copy; first.Mappings[Control.R3] = InputAction.Copy;
            Check.Equal(InputAction.PlayPause, second.PieMenus[0].Slots[0]); Check.Equal(InputAction.VolumeUp, second.PieMenus[0].DPadActions[0]!.Value);
            Check.Equal(InputAction.Escape, second.Mappings[Control.B]); Check.Equal(InputAction.None, FactoryDefaults.Create().Mappings[Control.R3]);
            var keyboard = second.PieMenus[4]; var draft = new PieDraft(keyboard.Assign(0, InputAction.Copy), second); draft.Restore();
            Check.True(draft.Menu.Slots.SequenceEqual(keyboard.Slots)); Check.Equal(keyboard.Id, draft.Menu.Id);
        });
        suite.Add("App-wide theme round trips separately from controller profile settings", () =>
        {
            using var dir = new TestDirectory(); var store = new AppPreferencesStore(dir.Path);
            Check.Equal("Dark", store.Load().Theme);
            var settings = new SettingsStore(dir.Path); settings.Save(ProfileLibrary.Create(new(), "Drawing"));
            var original = File.ReadAllBytes(settings.FilePath);
            Check.True(store.Save(new() { Theme = "Dark" }) is null);
            Check.Equal("Dark", new AppPreferencesStore(dir.Path).Load().Theme);
            Check.True(original.SequenceEqual(File.ReadAllBytes(settings.FilePath)));
            Check.True(store.Save(new() { Theme = "Light" }) is null);
            Check.Equal("Dark", System.Text.Json.JsonSerializer.Deserialize<AppPreferences>(File.ReadAllBytes(store.FilePath + ".bak"))!.Theme);
        });
        suite.Add("Malformed preferences preserve exact bytes before recovery", () =>
        {
            using var dir = new TestDirectory(); var store = new AppPreferencesStore(dir.Path);
            var original = new byte[] { 123, 255, 44, 125 }; File.WriteAllBytes(store.FilePath, original);
            Check.Equal("Dark", store.Load().Theme); Check.True(store.Warning is not null);
            Check.True(original.SequenceEqual(File.ReadAllBytes(Directory.GetFiles(dir.Path, "*.bak").Single())));
            Check.True(store.Save(new() { Theme = "Dark" }) is null);
        });
        suite.Add("Future preferences are read-only and optional unknown fields survive", () =>
        {
            using var dir = new TestDirectory(); var store = new AppPreferencesStore(dir.Path);
            var original = "{\"Version\":99,\"Theme\":\"Dark\"}"; File.WriteAllText(store.FilePath, original);
            store.Load(); Check.True(store.Save(new()) is not null); Check.Equal(original, File.ReadAllText(store.FilePath));
            File.WriteAllText(store.FilePath, "{\"Version\":1,\"Theme\":\"Dark\",\"FutureOption\":42}");
            var next = new AppPreferencesStore(dir.Path); var loaded = next.Load(); Check.True(next.Save(loaded with { Theme = "Light" }) is null);
            Check.Equal(42, next.Load().Additional!["FutureOption"].GetInt32());
        });
        suite.Add("Startup command quotes the portable path and requests tray startup", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "FullTilt portable", "FullTilt.exe");
            Check.Equal("\"" + path + "\" --startup", StartupCommand.ForExecutable(path));
            var rejected = false; try { StartupCommand.ForExecutable("relative.exe"); } catch (ArgumentException) { rejected = true; } Check.True(rejected);
        });
    }
}
