using FullTilt.Core;

static class PieRedesignTests
{
    public static void Register(TestSuite suite)
    {
        foreach (var n in Enumerable.Range(2, 7))
        {
            var count = n;
            suite.Add($"Pie redesign {count} segments share hit testing sector centers boundaries and hysteresis", () =>
            {
                for (var i = 0; i < count; i++)
                {
                    var angle = PieGeometry.Center(count, i); var width = PieGeometry.Width(count);
                    var selection = new PieSelection(count); selection.Update(0, 0);
                    var p = Stick(angle); selection.Update(p.X, p.Y); Check.Equal(i, selection.Selected);
                    Check.Equal(i, PieGeometry.HitTest(count, Math.Sin(angle * Math.PI / 180) * .7, -Math.Cos(angle * Math.PI / 180) * .7));
                    Check.Equal(i, PieGeometry.Sector(count, angle + width / 2 - .01));
                    Check.Equal((i + 1) % count, PieGeometry.Sector(count, angle + width / 2 + .01));
                    p = Stick(angle + width / 2 + 3); selection.Update(p.X, p.Y); Check.Equal(i, selection.Selected);
                    p = Stick(angle + width / 2 + 7); selection.Update(p.X, p.Y); Check.Equal((i + 1) % count, selection.Selected);
                    selection.Update(0, 0, .101); Check.Equal(-1, selection.Selected);
                }
                Check.Equal(-1, PieGeometry.HitTest(count, 0, 0)); Check.Equal(-1, PieGeometry.HitTest(count, 2, 0));
            });
            suite.Add($"Pie redesign {count} segment runtime clears at center and permits reselection", () =>
            {
                var sink = new FakeSink(); var platform = new FakePiePlatform();
                var menu = PieMenu.Create("Variable", count).Assign(count - 1, InputAction.Copy);
                var settings = new AppSettings { PieMenus = [menu], PieBindings = new() { [Control.LB] = menu.Id } };
                var engine = new InputEngine(sink, piePlatform: platform); engine.Configure(settings); engine.Tick(default, .008);
                engine.Tick(new(ControllerButtons.LB), .008); var p = Stick(PieGeometry.Center(count, count - 1));
                engine.Tick(new(ControllerButtons.LB, LeftX: p.X, LeftY: p.Y), .008); engine.Tick(new(ControllerButtons.LB), .101);
                Check.Equal(-1, engine.PieSelected);
                engine.Tick(new(ControllerButtons.LB, LeftX: p.X, LeftY: p.Y), .008);
                Check.Equal(count - 1, engine.PieSelected); engine.Tick(new(LeftX: p.X, LeftY: p.Y), .008); Check.Equal(InputAction.Copy, sink.Actions.Single());
                Check.Equal(0, sink.X); Check.Equal(0, sink.Y);
            });
        }
        suite.Add("Pie redesign two face right left and four face up right down left", () =>
        {
            Check.Near(90, PieGeometry.Center(2, 0)); Check.Near(270, PieGeometry.Center(2, 1));
            for (var i = 0; i < 4; i++) Check.Near(i * 90, PieGeometry.Center(4, i));
            for (var i = 0; i < 8; i++) Check.Near(i * 45, PieGeometry.Center(8, i));
        });
        suite.Add("Pie redesign resize preserves prefix appends empty and reports removed actions", () =>
        {
            var menu = PieMenu.Create("Custom", 4).Assign(0, InputAction.Copy).Assign(3, InputAction.Paste);
            var settings = new AppSettings { PieMenus = [menu] }; var draft = new PieDraft(menu, settings);
            draft.Resize(6); Check.Equal(InputAction.Paste, draft.Menu.Slots[3]); Check.Equal(InputAction.None, draft.Menu.Slots[5]);
            Check.Equal(InputAction.Paste, draft.RemovedByResize(3)[0]); draft.Resize(3); Check.Equal(3, draft.Menu.SegmentCount); Check.Equal(InputAction.Copy, draft.Menu.Slots[0]);
            Check.Equal(4, menu.SegmentCount); Check.Equal(InputAction.Paste, menu.Slots[3]);
            var before = draft.Menu; draft.Resize(1); Check.True(ReferenceEquals(before, draft.Menu));
        });
        suite.Add("Pie redesign drafts isolate names actions resizing and bindings until atomic apply", () =>
        {
            var menu = PieMenu.Create("Existing"); var unrelated = PieMenu.Create("Other");
            var settings = new AppSettings { PieMenus = [menu, unrelated], PieBindings = new() { [Control.LB] = menu.Id, [Control.LT] = menu.Id, [Control.RB] = unrelated.Id } }.Validated();
            var draft = new PieDraft(menu, settings); draft.Rename("Draft"); draft.Assign(0, InputAction.Copy); draft.Resize(4); draft.OpenWith = Control.RT;
            Check.Equal(Control.LB, draft.RemovedOpener!.Value); Check.True(draft.Dirty); Check.Equal("Existing", settings.PieMenus[0].Name); Check.Equal(8, settings.PieMenus[0].SegmentCount); Check.Equal(InputAction.None, settings.PieMenus[0].Slots[0]); Check.True(!settings.PieBindings.ContainsKey(Control.RT));
            var applied = draft.Apply(settings); Check.Equal("Draft", applied.PieMenus[0].Name); Check.Equal(4, applied.PieMenus[0].SegmentCount); Check.Equal(menu.Id, applied.PieBindings[Control.RT]);
            Check.True(!applied.PieBindings.ContainsKey(Control.LB)); Check.Equal(menu.Id, applied.PieBindings[Control.LT]); Check.Equal(unrelated.Id, applied.PieBindings[Control.RB]);
            Check.True(!new PieDraft(menu, settings).Dirty);
        });
        suite.Add("Pie redesign conflict disclosure and apply preserve newer unrelated binding changes", () =>
        {
            var menu = PieMenu.Create(); var settings = new AppSettings { PieMenus = [menu], PieBindings = new() { [Control.LB] = menu.Id } }.Validated();
            var draft = new PieDraft(menu, settings) { OpenWith = Control.A }; Check.Equal("Left Click", draft.BindingConflict(settings));
            var latest = settings with { Mappings = new(settings.Mappings) { [Control.RB] = InputAction.Paste } };
            var result = draft.Apply(latest); Check.Equal(InputAction.Paste, result.Mappings[Control.RB]); Check.Equal(InputAction.None, result.Mappings[Control.A]); Check.Equal(menu.Id, result.PieBindings[Control.A]);
            draft = new(menu, settings); draft.Assign(0, InputAction.Copy);
            latest = settings with { PieBindings = [], Mappings = new(settings.Mappings) { [Control.LB] = InputAction.NextTrack } };
            result = draft.Apply(latest); Check.Equal(InputAction.NextTrack, result.Mappings[Control.LB]); Check.True(!result.PieBindings.ContainsKey(Control.LB));
        });
        suite.Add("Pie redesign draft deletion clears references only on Apply", () =>
        {
            var menu = PieMenu.Create(); var settings = new AppSettings { PieMenus = [menu], PieBindings = new() { [Control.A] = menu.Id, [Control.RT] = menu.Id } }.Validated();
            var draft = new PieDraft(menu, settings) { Deleted = true }; Check.Equal(1, settings.PieMenus.Count); Check.Equal(2, settings.PieBindings.Count);
            var result = draft.Apply(settings); Check.Equal(0, result.PieMenus.Count); Check.Equal(0, result.PieBindings.Count); Check.Equal(InputAction.None, result.Mappings[Control.A]); Check.Equal(InputAction.RightClick, result.Mappings[Control.X]);
        });
        suite.Add("Fresh installation uses the captured five-menu profile including recorded keys", () =>
        {
            using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path); var settings = store.Load().Settings;
            var baseline = FactoryDefaults.Create();
            Check.Equal(5, settings.PieMenus.Count); Check.Equal(1, settings.PiePresetMigration);
            Check.Equal(InputAction.LeftClick, settings.Mappings[Control.A]); Check.Equal(InputAction.Escape, settings.Mappings[Control.B]);
            Check.Equal(6, settings.PieMenus[0].SegmentCount); Check.Equal(8, settings.PieMenus[1].SegmentCount);
            Check.Equal(120d, settings.PieMenuSizePercent); Check.Equal(600d, settings.CursorSpeed);
            Check.True(store.Save(settings) is null);
            foreach (var tab in Enum.GetValues<EditTab>()) Check.True(EditFields.Equal(EditFields.Read(tab, baseline), EditFields.Read(tab, store.Load().Settings)));
        });
        suite.Add("Pie redesign existing v4 gets unbound starters once preserves same-name custom and exact backup", () =>
        {
            using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path); var id = Guid.NewGuid();
            var json = $$$"""{"Version":4,"CursorSpeed":1780,"DPadTapDistance":17,"PieMenus":[{"Id":"{{{id}}}","Name":"Media","Slots":["Copy","Paste","NextTrack","None","None","None","None","Undo"]}],"PieBindings":{"LB":"{{{id}}}"},"Mappings":{"RB":"ToggleKeyboard"}}""";
            File.WriteAllText(store.FilePath, json); var load = store.Load(); Check.True(load.Warning is null); var settings = load.Settings;
            Check.Equal(5, settings.PieMenus.Count); Check.Equal(1, settings.PieBindings.Count); Check.Equal(id, settings.PieBindings[Control.LB]); Check.Equal(InputAction.ToggleKeyboard, settings.Mappings[Control.RB]);
            var custom = settings.PieMenus.Single(m => m.Id == id); Check.Equal(8, custom.SegmentCount); Check.Equal(InputAction.Undo, custom.Slots[7]); Check.True(custom.PresetId is null); Check.Near(1780, settings.CursorSpeed);
            Check.Equal(json, File.ReadAllText(store.FilePath + ".v4-backup")); Check.Equal(json, File.ReadAllText(store.FilePath));
            settings = PieMenu.Delete(settings, settings.PieMenus.Single(m => m.PresetId == "media").Id); Check.True(store.Save(settings) is null);
            var again = store.Load().Settings; Check.Equal(4, again.PieMenus.Count); Check.True(again.PieMenus.All(m => m.PresetId != "media")); Check.Equal(id, again.PieBindings[Control.LB]);
        });
        suite.Add("Pie redesign preset identity avoids duplicates independently of rename and migration records deletion", () =>
        {
            var media = PiePresets.Find("media")!.Create().Rename("Music");
            var settings = PiePresets.Initialize(new AppSettings { PieMenus = [media] }); Check.Equal(4, settings.PieMenus.Count); Check.Equal(media.Id, settings.PieMenus.Single(m => m.PresetId == "media").Id);
            settings = settings with { PieMenus = [] }; Check.Equal(0, PiePresets.Initialize(settings).PieMenus.Count);
        });
        suite.Add("Pie redesign Restore defaults changes only draft actions and count; custom clears in place", () =>
        {
            var menu = PiePresets.Find("media")!.Create().Rename("Music").Resize(8).Assign(6, InputAction.VolumeUp); var settings = new AppSettings { PieMenus = [menu], PieBindings = new() { [Control.RB] = menu.Id } };
            var draft = new PieDraft(menu, settings); draft.Restore(); Check.Equal(6, draft.Menu.SegmentCount); Check.Equal(menu.Id, draft.Menu.Id); Check.Equal("Music", draft.Menu.Name); Check.Equal(Control.RB, draft.OpenWith!.Value); Check.Equal(8, menu.SegmentCount);
            var custom = PieMenu.Create("Custom", 3).Assign(2, InputAction.Copy); draft = new(custom, new()); draft.Restore(); Check.Equal(3, draft.Menu.SegmentCount); Check.True(draft.Menu.Slots.All(a => a == InputAction.None)); Check.Equal(custom.Id, draft.Menu.Id);
            var duplicate = menu.Duplicate(); Check.True(duplicate.Id != menu.Id); Check.Equal(menu.PresetId, duplicate.PresetId);
        });
        suite.Add("Pie redesign malformed variable fields recover locally and future schema remains protected", () =>
        {
            using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path); var id = Guid.NewGuid();
            File.WriteAllText(store.FilePath, $$$"""{"Version":5,"CursorSpeed":1550,"PiePresetMigration":1,"PieMenus":[{"Id":"{{{id}}}","Name":"Odd","SegmentCount":99,"PresetId":"unknown","Slots":["Copy","Bogus"]}],"PieBindings":{"LT":"{{{id}}}"}}""");
            var load = store.Load(); Check.True(load.Warning is null); Check.Near(1550, load.Settings.CursorSpeed); Check.Equal(8, load.Settings.PieMenus.Single().SegmentCount); Check.True(load.Settings.PieMenus.Single().PresetId is null); Check.Equal(InputAction.None, load.Settings.PieMenus.Single().Slots[1]);
            File.WriteAllText(store.FilePath, "{\"Version\":99,\"Mappings\":{\"A\":\"Future\"}}"); store = new(dir.Path); Check.True(store.Load().Warning is not null); Check.True(store.Save(new()) is not null);
        });
    }
    private static (short X, short Y) Stick(double degrees) => ((short)(Math.Sin(degrees * Math.PI / 180) * 32767), (short)(Math.Cos(degrees * Math.PI / 180) * 32767));
}
