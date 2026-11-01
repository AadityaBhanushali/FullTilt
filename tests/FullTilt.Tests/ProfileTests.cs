using System.Text.Json;
using FullTilt.Core;
using FullTilt.Windows;

static class ProfileTests
{
    private static string Snapshot(AppSettings settings) => JsonSerializer.Serialize(ProfileLibrary.Snapshot(settings));
    public static void Register(TestSuite suite)
    {
        suite.Add("Profile rename preserves identity configuration and restart persistence", () =>
        {
            using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path);
            var initial = ProfileLibrary.Create(ProfileLibrary.Initialize(new() { CursorSpeed = 1725 }), "Editing");
            var renamed = ProfileLibrary.Rename(initial, initial.ActiveProfileId!.Value, "  Drawing  ");
            Check.Equal(initial.ActiveProfileId.Value, renamed.ActiveProfileId!.Value);
            Check.Equal(Snapshot(initial), Snapshot(renamed));
            Check.True(store.Save(renamed) is null);
            var reopened = store.Load().Settings;
            Check.Equal("Drawing", reopened.Profiles!.Single(p => p.Id == reopened.ActiveProfileId).Name);
        });
        suite.Add("Profile deletion loads fallback and preserves remaining snapshots on restart", () =>
        {
            using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path);
            var initial = ProfileLibrary.Initialize(new() { CursorSpeed = 1725 }); var first = initial.ActiveProfileId!.Value;
            var second = ProfileLibrary.Create(initial, "Other") with { CursorSpeed = 750 };
            var deleted = ProfileLibrary.Delete(second, second.ActiveProfileId!.Value);
            Check.Equal(first, deleted.ActiveProfileId!.Value); Check.Near(1725, deleted.CursorSpeed); Check.Equal(1, deleted.Profiles!.Count);
            Check.True(store.Save(deleted) is null); Check.Equal(first, store.Load().Settings.ActiveProfileId!.Value);
            var inactiveDeleted = ProfileLibrary.Delete(second, first);
            Check.Near(750, inactiveDeleted.CursorSpeed); Check.Equal(second.ActiveProfileId!.Value, inactiveDeleted.ActiveProfileId!.Value);
        });
        suite.Add("Profile names and last-profile deletion are guarded", () =>
        {
            var initial = ProfileLibrary.Initialize(new()); var id = initial.ActiveProfileId!.Value;
            var second = ProfileLibrary.Create(initial, "Other");
            void Reject(Action action) { var rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } Check.True(rejected); }
            Reject(() => ProfileLibrary.Rename(initial, id, "  "));
            Reject(() => ProfileLibrary.Rename(second, id, "OTHER"));
            Reject(() => ProfileLibrary.Rename(initial, Guid.NewGuid(), "Name"));
            Reject(() => ProfileLibrary.Rename(initial, id, new string('a', 81)));
            Reject(() => ProfileLibrary.Delete(initial, id));
            Reject(() => ProfileLibrary.Delete(second, Guid.NewGuid()));
            Check.Equal("Default", initial.Profiles!.Single().Name);
        });
        suite.Add("Portable profiles import once preserve legacy bytes and travel with the folder", () =>
        {
            using var dir = new TestDirectory();
            var local = Path.Combine(dir.Path, "local"); var app = Path.Combine(dir.Path, "app");
            var legacy = new SettingsStore(Path.Combine(local, "FullTilt"));
            var config = ProfileLibrary.Create(ProfileLibrary.Initialize(new() { CursorSpeed = 1234 }), "Drawing");
            Check.True(legacy.Save(config) is null); var original = File.ReadAllBytes(legacy.FilePath);
            var opened = SettingsStore.OpenDefault(app, local); Check.True(opened.Warning is null);
            Check.True(original.SequenceEqual(File.ReadAllBytes(opened.Store.FilePath)));
            var loaded = opened.Store.Load().Settings;
            Check.Equal(config.ActiveProfileId!.Value, loaded.ActiveProfileId!.Value); Check.Equal(2, loaded.Profiles!.Count);
            Check.True(opened.Store.Save(loaded with { CursorSpeed = 1800 }) is null);
            Check.True(original.SequenceEqual(File.ReadAllBytes(legacy.FilePath)));
            Check.Near(1800, SettingsStore.OpenDefault(app, local).Store.Load().Settings.CursorSpeed);
            var moved = Path.Combine(dir.Path, "moved"); Directory.Move(app, moved);
            var restart = SettingsStore.OpenDefault(moved, Path.Combine(dir.Path, "different-computer")).Store.Load().Settings;
            Check.Near(1800, restart.CursorSpeed); Check.Equal(config.ActiveProfileId.Value, restart.ActiveProfileId!.Value);
            Check.Equal(2, restart.Profiles!.Count);
        });
        suite.Add("Adjacent legacy settings take precedence and are never written back", () =>
        {
            using var dir = new TestDirectory(); var local = Path.Combine(dir.Path, "local");
            var adjacent = new SettingsStore(dir.Path); Check.True(adjacent.Save(new() { CursorSpeed = 1400 }) is null);
            Check.True(new SettingsStore(Path.Combine(local, "FullTilt")).Save(new() { CursorSpeed = 800 }) is null);
            var original = File.ReadAllBytes(adjacent.FilePath);
            var opened = SettingsStore.OpenDefault(dir.Path, local); Check.True(opened.Warning is null);
            Check.Near(1400, opened.Store.Load().Settings.CursorSpeed);
            Check.True(opened.Store.Save(new() { CursorSpeed = 1600 }) is null);
            Check.True(original.SequenceEqual(File.ReadAllBytes(adjacent.FilePath)));
        });
        suite.Add("Blocked Profiles folder disables saving without a LocalAppData fallback", () =>
        {
            using var dir = new TestDirectory(); File.WriteAllText(Path.Combine(dir.Path, "Profiles"), "blocked");
            var local = Path.Combine(dir.Path, "local");
            var opened = SettingsStore.OpenDefault(dir.Path, local);
            Check.True(opened.Warning is not null); Check.True(opened.Store.Save(new()) is not null);
            Check.True(!Directory.Exists(local));
        });
        suite.Add("Failed legacy import cannot overwrite or silently replace existing configuration", () =>
        {
            using var dir = new TestDirectory(); Directory.CreateDirectory(Path.Combine(dir.Path, "settings.json"));
            var opened = SettingsStore.OpenDefault(dir.Path, Path.Combine(dir.Path, "local"));
            Check.True(opened.Warning is not null); Check.True(opened.Store.Save(new()) is not null);
            Check.True(!File.Exists(opened.Store.FilePath));
            Check.Equal(0, Directory.GetFiles(opened.Store.DirectoryPath).Length);
        });
        suite.Add("Arrow Up remains usable inside the user's D-pad Up B Y pause chord", () =>
        {
            var keys = new FakeKeys(); var ownership = new KeyboardOwnership(keys);
            var engine = new InputEngine(new ShortcutSink(ownership, new(ownership, new FakeDesktop())));
            var chord = ControllerButtons.DPadUp | ControllerButtons.B | ControllerButtons.Y;
            engine.Configure(new() { PauseShortcut = chord, DPadMode = DPadMode.AssignedActions,
                Mappings = new() { [Control.DPadUp] = InputAction.ArrowUp, [Control.L3] = InputAction.ArrowUp, [Control.Y] = InputAction.StickyAlt } });
            engine.Tick(default, .01);
            engine.Tick(new(ControllerButtons.DPadUp), .01); engine.Tick(new(ControllerButtons.DPadUp), .01);
            Check.Equal(1, keys.Events.Count); Check.Equal((ushort)0x26, keys.Events[0].Key); Check.True(keys.Events[0].Down && keys.Down.Contains(0x26));
            engine.Tick(new(chord), .01); Check.True(engine.Paused); Check.Equal(2, keys.Events.Count); Check.Equal(0, keys.Down.Count);
            engine.Tick(default, .01); engine.Tick(new(chord), .01); Check.True(!engine.Paused);
            engine.Tick(new(ControllerButtons.DPadUp), .01); Check.Equal(2, keys.Events.Count); // Full release is required after recognized chord.
            engine.Tick(default, .01); engine.Tick(new(ControllerButtons.LeftThumb), .01); Check.Equal(3, keys.Events.Count); Check.True(keys.Down.Contains(0x26));
            engine.Tick(default, .01); engine.Tick(new(ControllerButtons.DPadUp), .01); Check.Equal(5, keys.Events.Count); Check.True(keys.Down.Contains(0x26));
            engine.Tick(default, .01); Check.Equal(6, keys.Events.Count); Check.Equal(0, keys.Down.Count);
            Check.True(keys.Events.All(k => k.Key == 0x26));
        });
        suite.Add("Profile Default seeds the existing complete configuration without aliases", () =>
        {
            var source = AppSettings.FreshInstallation() with { CursorSpeed = 1725, SpeedBoostMultiplier = 3.25, DPadMode = DPadMode.AssignedActions, ShowStateIndicator = false };
            var seeded = ProfileLibrary.Initialize(source);
            Check.Equal(1, seeded.Profiles!.Count); Check.Equal("Default", seeded.Profiles[0].Name); Check.Equal(seeded.Profiles[0].Id, seeded.ActiveProfileId!.Value);
            Check.Equal(Snapshot(source), Snapshot(seeded.Profiles[0].Configuration));
            seeded.Mappings[Control.A] = InputAction.Backspace; seeded.PieMenus[0].Slots[0] = InputAction.ArrowUp;
            Check.Equal(InputAction.LeftClick, seeded.Profiles[0].Configuration.Mappings[Control.A]);
            Check.Equal(InputAction.PlayPause, seeded.Profiles[0].Configuration.PieMenus[0].Slots[0]);
        });
        suite.Add("Profile create save switch and restart restore every configuration field", () =>
        {
            using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path);
            var first = ProfileLibrary.Initialize(AppSettings.FreshInstallation()); var firstId = first.ActiveProfileId!.Value;
            first = first with { CursorSpeed = 1900, PrecisionSpeed = 420, PrecisionPercent = 35, SpeedBoostMultiplier = 2.75,
                DPadMode = DPadMode.AssignedActions, CursorStick = CursorStick.Right, ScrollSpeed = 4, DPadTapDistance = 12,
                LeftDeadZone = .25, RightDeadZone = .3, ResponseCurve = 2.2, TriggerPressThreshold = 180, TriggerReleaseThreshold = 90,
                ControllerSlot = 2, ShowStateIndicator = false, TrayHintShown = true, PauseShortcut = ControllerButtons.LB | ControllerButtons.RB,
                Mappings = new(first.Mappings) { [Control.DPadUp] = InputAction.ArrowUp, [Control.A] = InputAction.Backspace } };
            var expectedFirst = Snapshot(first);
            var second = ProfileLibrary.Create(first, "Drawing"); var secondId = second.ActiveProfileId!.Value;
            second = second with { CursorSpeed = 750, SpeedBoostMultiplier = 1.25, PrecisionPercent = 60, PauseShortcut = PauseShortcutRules.Default,
                PieMenus = [], PieBindings = [], Mappings = new(second.Mappings) { [Control.A] = InputAction.Enter }, PiePresetMigration = 1 };
            var expectedSecond = Snapshot(second);
            var restored = ProfileLibrary.Switch(second, firstId); Check.Equal(expectedFirst, Snapshot(restored));
            Check.True(store.Save(restored) is null); var loaded = new SettingsStore(dir.Path).Load(); Check.True(loaded.Warning is null);
            Check.Equal(firstId, loaded.Settings.ActiveProfileId!.Value); Check.Equal(expectedFirst, Snapshot(loaded.Settings));
            var back = ProfileLibrary.Switch(loaded.Settings, secondId); Check.Equal(expectedSecond, Snapshot(back));
            Check.True(store.Save(back) is null); var restart = new SettingsStore(dir.Path).Load().Settings;
            Check.Equal(secondId, restart.ActiveProfileId!.Value); Check.Equal(expectedSecond, Snapshot(restart));
            Check.Equal(2, restart.Profiles!.Count); Check.True(restart.Profiles.All(p => p.Configuration.Profiles is null && p.Configuration.ActiveProfileId is null));
        });
        suite.Add("Profile names reject empty duplicate and overlong values", () =>
        {
            var settings = ProfileLibrary.Initialize(new());
            foreach (var name in new[] { "", "  ", "default", new string('x', 81) })
            { try { ProfileLibrary.Create(settings, name); throw new Exception("Invalid name accepted"); } catch (ArgumentException) { } }
            Check.Equal("Drawing", ProfileLibrary.Create(settings, "  Drawing  ").Profiles!.Last().Name);
        });
        suite.Add("Profile migration preserves exact v9 file and existing pause chord", () =>
        {
            using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path);
            const string json = "{\"Version\":9,\"CursorSpeed\":1731,\"PauseShortcut\":\"DPadUp, B, Y\",\"DPadMode\":\"AssignedActions\",\"Mappings\":{\"DPadUp\":\"ArrowUp\"}}";
            File.WriteAllText(store.FilePath, json); var load = store.Load(); Check.True(load.Warning is null);
            Check.Equal(json, File.ReadAllText(store.FilePath + ".v9-backup")); Check.Equal("Default", load.Settings.Profiles![0].Name);
            Check.Equal(ControllerButtons.DPadUp | ControllerButtons.B | ControllerButtons.Y, load.Settings.PauseShortcut); Check.Near(1731, load.Settings.CursorSpeed);
        });
        suite.Add("Malformed optional profiles preserve current settings and exact recovery copy", () =>
        {
            using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path);
            const string json = "{\"Version\":10,\"CursorSpeed\":1822,\"SpeedBoostMultiplier\":2.25,\"Profiles\":[null,{},123],\"ActiveProfileId\":\"bad\"}";
            File.WriteAllText(store.FilePath, json); var loaded = store.Load(); Check.True(loaded.Warning is not null);
            Check.Near(1822, loaded.Settings.CursorSpeed); Check.Near(2.25, loaded.Settings.SpeedBoostMultiplier); Check.Equal(1, loaded.Settings.Profiles!.Count);
            Check.Equal(json, File.ReadAllText(Directory.GetFiles(dir.Path, "settings.json.profiles-backup-*").Single()));
        });
        suite.Add("Profile save cannot overwrite files from newer schemas", () =>
        {
            using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path);
            const string future = "{\"Version\":999,\"Profiles\":[{\"Name\":\"Future data\"}]}";
            File.WriteAllText(store.FilePath, future); var loaded = store.Load();
            Check.True(store.Save(ProfileLibrary.Create(loaded.Settings, "Other")) is not null);
            Check.Equal(future, File.ReadAllText(store.FilePath));
        });
        suite.Add("Profile migration backup failure blocks writes and preserves v9", () =>
        {
            using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path);
            const string json = "{\"Version\":9,\"CursorSpeed\":1735}";
            File.WriteAllText(store.FilePath, json); Directory.CreateDirectory(store.FilePath + ".v9-backup");
            var loaded = store.Load(); Check.Near(1735, loaded.Settings.CursorSpeed); Check.True(loaded.Warning is not null);
            Check.True(store.Save(ProfileLibrary.Create(loaded.Settings, "Other")) is not null); Check.Equal(json, File.ReadAllText(store.FilePath));
        });
        suite.Add("Profile identity switch clears all modes even with identical mappings and rearms", () =>
        {
            var first = ProfileLibrary.Initialize(new() { Mappings = new() { [Control.A] = InputAction.LeftClick, [Control.X] = InputAction.StickyAlt, [Control.Y] = InputAction.SpeedBoostToggle } });
            var next = ProfileLibrary.Create(first, "Other"); var h = Harness.New(first);
            var held = new ControllerState(ControllerButtons.A | ControllerButtons.X | ControllerButtons.Y);
            h.Engine.Tick(held, .01); Check.True(h.Sink.Held[0] && h.Engine.AltActive && h.Engine.SpeedBoostActive);
            h.Engine.Configure(next); Check.True(!h.Sink.Held[0] && !h.Engine.AltActive && !h.Engine.SpeedBoostActive);
            h.Engine.Tick(held, .01); Check.True(!h.Sink.Held[0]); h.Engine.Tick(default, .01); h.Engine.Tick(held, .01); Check.True(h.Sink.Held[0]);
        });
    }
}
