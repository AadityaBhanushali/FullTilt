using FullTilt.Core;

static class PieSizeTests
{
    public static void Register(TestSuite suite)
    {
        suite.Add("Pie global size stays shared through profiles, new menus and active profile deletion", () =>
        {
            var settings = ProfileLibrary.Initialize(AppSettings.FreshInstallation()); var originalId = settings.ActiveProfileId!.Value;
            var second = ProfileLibrary.Create(settings, "Second");
            var draft = new PieDraft(second.PieMenus[0], second); draft.SetSize(180);
            var applied = draft.Apply(second); var switched = ProfileLibrary.Switch(applied, originalId);
            Check.Near(180, switched.PieMenuSizePercent); Check.Near(180, new PieDraft(switched.PieMenus[1], switched).SizePercent);
            var created = new PieDraft(PieMenu.Create("New"), switched, true).Apply(switched); Check.Near(180, created.PieMenuSizePercent);
            var deleted = ProfileLibrary.Delete(created, originalId); Check.Near(180, deleted.PieMenuSizePercent);
            Check.Near(190, new PieDraft(deleted.PieMenus[0], deleted).Apply(deleted with { PieMenuSizePercent = 190 }).PieMenuSizePercent);
        });
        suite.Add("Pie global size drives every runtime menu regardless of legacy per menu sizes", () =>
        {
            var first = PieMenu.Create("First", 4) with { SizePercent = 125 }; var second = PieMenu.Create("Second", 6) with { SizePercent = 200 };
            var settings = new AppSettings { PieMenuSizePercent = 160, PieMenus = [first, second], PieBindings = new() { [Control.LB] = first.Id, [Control.RB] = second.Id } };
            var platform = new FakePiePlatform(); var engine = new InputEngine(new FakeSink(), piePlatform: platform); engine.Configure(settings);
            foreach (var button in new[] { ControllerButtons.LB, ControllerButtons.RB })
            {
                engine.Reset(); engine.Tick(default, .02); engine.Tick(new(button), .02); Check.True(engine.PieOpen); Check.Near(160, platform.Last!.SizePercent);
            }
        });
        suite.Add("Pie global size v13 migration preserves exact file and legacy menu fields", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path);
            const string original = """{"Version":13,"CursorSpeed":1387,"PieMenus":[{"Id":"098db238-091f-4507-bbb5-54447e9c7ec9","Name":"Custom","SegmentCount":4,"Slots":["Copy"],"SizePercent":175}]}""";
            File.WriteAllText(store.FilePath, original); var loaded = store.Load().Settings; Check.Near(175, loaded.PieMenuSizePercent); Check.Equal(original, File.ReadAllText(store.FilePath + ".v13-backup"));
            Check.True(store.Save(loaded with { PieMenuSizePercent = 150 }) is null); var saved = store.Load().Settings;
            Check.Near(150, saved.PieMenuSizePercent); Check.Near(175, saved.PieMenus[0].SizePercent); Check.Equal(InputAction.Copy, saved.PieMenus[0].Slots[0]); Check.Near(1387, saved.CursorSpeed);
        });
        suite.Add("Pie global size malformed optional data preserves other configuration", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path);
            const string original = """{"Version":14,"CursorSpeed":1387,"PieMenuSizePercent":"bad","PieMenus":[{"Id":"098db238-091f-4507-bbb5-54447e9c7ec9","Name":"Custom","SegmentCount":4,"Slots":["Copy"]}]}""";
            File.WriteAllText(store.FilePath, original); var result = store.Load(); Check.True(result.Warning is not null); Check.Near(100, result.Settings.PieMenuSizePercent); Check.Near(1387, result.Settings.CursorSpeed); Check.Equal(InputAction.Copy, result.Settings.PieMenus[0].Slots[0]);
            Check.Equal(original, File.ReadAllText(Directory.GetFiles(temp.Path, "settings.json.profiles-backup-*").Single()));
        });
        suite.Add("Pie global size failed v13 preservation disables saving", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path); const string original = """{"Version":13,"CursorSpeed":1387}""";
            File.WriteAllText(store.FilePath, original); Directory.CreateDirectory(store.FilePath + ".v13-backup"); Check.True(store.Load().Warning is not null); Check.True(store.Save(new()) is not null); Check.Equal(original, File.ReadAllText(store.FilePath));
        });
        foreach (var stick in new[] { CursorStick.Left, CursorStick.Right })
        foreach (var reverse in new[] { false, true })
            suite.Add($"Pie size release LT held boost scroll follows {stick} reverse={reverse}", () =>
            {
                var settings = new AppSettings { CursorStick = stick, ScrollSpeed = 10, ScrollBoostMultiplier = 3, ReverseScroll = reverse,
                    Mappings = new(AppSettings.DefaultMappings()) { [Control.LT] = InputAction.SpeedBoostHold } };
                var h = Harness.New(settings); var sign = reverse ? -1 : 1;
                var scroll = stick == CursorStick.Left ? new ControllerState(RightX: 32767, RightY: 32767) : new ControllerState(LeftX: 32767, LeftY: 32767);
                h.Engine.Tick(scroll, .02); Check.Equal(24 * sign, h.Sink.ScrollY);
                h.Engine.Tick(scroll with { LeftTrigger = 139 }, .02); Check.Equal(48 * sign, h.Sink.ScrollY);
                h.Engine.Tick(scroll with { LeftTrigger = 140 }, .02); Check.True(h.Engine.SpeedBoostActive); Check.Equal(120 * sign, h.Sink.ScrollY);
                h.Engine.Tick(scroll with { LeftTrigger = 101 }, .02); Check.Equal(192 * sign, h.Sink.ScrollX);
                h.Engine.Tick(scroll with { LeftTrigger = 100 }, .02); Check.True(!h.Engine.SpeedBoostActive); Check.Equal(216 * sign, h.Sink.ScrollY);
                h.Engine.SetPaused(true); h.Engine.SetPaused(false); h.Engine.Tick(scroll with { LeftTrigger = 255 }, .02); Check.Equal(216 * sign, h.Sink.ScrollY);
                h.Engine.Tick(default, .02); h.Engine.Tick(scroll with { LeftTrigger = 255 }, .02); Check.Equal(288 * sign, h.Sink.ScrollY);
            });
        suite.Add("Pie size release draft apply duplicate resize restore and profiles preserve size", () =>
        {
            var menu = PieMenu.Create("Media", 4); var settings = ProfileLibrary.Initialize(new AppSettings { PieMenus = [menu] });
            var draft = new PieDraft(menu, settings); draft.SetSize(175); Check.True(draft.Dirty); Check.Near(100, settings.PieMenus[0].SizePercent);
            draft.Resize(6); draft.Restore(); Check.Near(175, draft.SizePercent); Check.Near(100, draft.Menu.Duplicate().SizePercent);
            var applied = draft.Apply(settings); Check.Near(175, applied.PieMenuSizePercent); var second = ProfileLibrary.Create(applied, "Copy"); Check.Near(175, second.PieMenuSizePercent);
            Check.Near(100, PieMenu.Validate([menu with { SizePercent = double.NaN }])[0].SizePercent); Check.Near(200, PieMenu.Validate([menu with { SizePercent = 900 }])[0].SizePercent);
        });
        suite.Add("Pie size release exact v12 migration and per menu roundtrip", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path);
            const string original = """{"Version":12,"CursorSpeed":1387,"ScrollBoostMultiplier":4.64,"Mappings":{"LT":"SpeedBoostHold"},"PieMenus":[{"Id":"098db238-091f-4507-bbb5-54447e9c7ec9","Name":"Custom","SegmentCount":4,"Slots":["Copy","Undo","Paste","SelectAll"]}]}""";
            File.WriteAllText(store.FilePath, original); var loaded = store.Load().Settings; Check.Equal(original, File.ReadAllText(store.FilePath + ".v12-backup")); Check.Near(100, loaded.PieMenus[0].SizePercent);
            var draft = new PieDraft(loaded.PieMenus[0], loaded); draft.SetSize(153.5); Check.True(store.Save(draft.Apply(loaded)) is null); var saved = store.Load().Settings;
            Check.Near(153.5, saved.PieMenuSizePercent); Check.Near(4.64, saved.ScrollBoostMultiplier); Check.Equal(InputAction.SpeedBoostHold, saved.Mappings[Control.LT]); Check.Equal(original, File.ReadAllText(store.FilePath + ".v12-backup"));
        });
        suite.Add("Pie size release malformed optional size preserves menu and exact original", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path);
            const string original = """{"Version":13,"CursorSpeed":1387,"PieMenus":[{"Id":"098db238-091f-4507-bbb5-54447e9c7ec9","Name":"Custom","SegmentCount":4,"Slots":["Copy"],"SizePercent":"bad"}]}""";
            File.WriteAllText(store.FilePath, original); var result = store.Load(); Check.True(result.Warning is not null); Check.Near(1387, result.Settings.CursorSpeed); Check.Equal(InputAction.Copy, result.Settings.PieMenus[0].Slots[0]); Check.Near(100, result.Settings.PieMenus[0].SizePercent);
            Check.Equal(original, File.ReadAllText(Directory.GetFiles(temp.Path, "settings.json.profiles-backup-*").Single()));
        });
        suite.Add("Pie size release migration backup failure disables saving", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path); const string original = """{"Version":12,"CursorSpeed":1387}""";
            File.WriteAllText(store.FilePath, original); Directory.CreateDirectory(store.FilePath + ".v12-backup"); Check.True(store.Load().Warning is not null); Check.True(store.Save(new()) is not null); Check.Equal(original, File.ReadAllText(store.FilePath));
        });
        suite.Add("Pie size release DPI and percentage scale fit captured work area", () =>
        {
            foreach (var dpi in new[] { 96d, 144, 192 }) foreach (var percent in new[] { 100d, 150, 200 })
            {
                var fit = PiePlacement.Fit(1800, 1000, 0, 0, 1920, 1080, dpi, percent);
                Check.Equal(Math.Min(1080, (int)(440 * dpi / 96 * percent / 100)), fit.Size); Check.True(fit.X >= 0 && fit.Y >= 0 && fit.X + fit.Size <= 1920 && fit.Y + fit.Size <= 1080);
            }
        });
        suite.Add("Pie size release changing size cancels an open session and rearms", () =>
        {
            var menu = PieMenu.Create("Custom", 4); var settings = new AppSettings { PieMenus = [menu], PieBindings = new() { [Control.LB] = menu.Id } };
            var platform = new FakePiePlatform(); var engine = new InputEngine(new FakeSink(), piePlatform: platform); engine.Configure(settings); engine.Tick(default, .02); engine.Tick(new(ControllerButtons.LB), .02); Check.True(engine.PieOpen);
            engine.Configure(settings with { PieMenuSizePercent = 200 }); Check.True(!engine.PieOpen); engine.Tick(new(ControllerButtons.LB), .02); Check.True(!engine.PieOpen); engine.Tick(default, .02); engine.Tick(new(ControllerButtons.LB), .02); Check.True(engine.PieOpen); Check.Near(200, platform.Last!.SizePercent);
        });
    }
}
