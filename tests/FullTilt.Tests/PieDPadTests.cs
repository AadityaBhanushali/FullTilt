using FullTilt.Core;

static class PieDPadTests
{
    private static readonly ControllerButtons[] Directions = [ControllerButtons.DPadUp, ControllerButtons.DPadDown, ControllerButtons.DPadLeft, ControllerButtons.DPadRight];
    private static PieHarness Create(InputAction?[] actions, bool movement = false, Control opener = Control.LB)
    {
        var h = new PieHarness(opener: opener, dpadMovement: movement);
        h.Settings = h.Settings with { PieMenus = [h.Menu with { DPadActions = actions }] };
        foreach (var control in new[] { Control.DPadUp, Control.DPadDown, Control.DPadLeft, Control.DPadRight }) h.Settings.Mappings[control] = InputAction.Backspace;
        h.Engine.Configure(h.Settings); h.Tick(default); return h;
    }
    public static void Register(TestSuite suite)
    {
        foreach (var movement in new[] { false, true })
        for (var index = 0; index < 4; index++)
        {
            var direction = index; var cursor = movement;
            suite.Add($"Context D-pad direction {index} executes directly and restores defaults movement={movement}", () =>
            {
                var actions = new InputAction?[4]; actions[direction] = InputAction.Copy; var h = Create(actions, cursor);
                h.Tick(new(ControllerButtons.LB));
                h.Tick(new(ControllerButtons.LB | Directions[direction]));
                Check.True(h.Engine.PieOpen); Check.Equal(-1, h.Engine.PieSelected); Check.Equal(InputAction.Copy, h.Sink.Actions.Single());
                for (var i = 0; i < 70; i++) h.Tick(new(ControllerButtons.LB | Directions[direction]));
                Check.Equal(1, h.Sink.Actions.Count); Check.Equal(Directions[direction], h.Platform.Last!.DPadPressed);
                h.Tick(new(Directions[direction])); h.Tick(new(Directions[direction])); Check.Equal(1, h.Sink.Actions.Count); Check.Equal(0, h.Sink.X); Check.Equal(0, h.Sink.Y);
                h.Tick(default); h.Tick(new(Directions[direction]));
                if (cursor) Check.True(h.Sink.X != 0 || h.Sink.Y != 0); else Check.Equal(InputAction.Backspace, h.Sink.Actions.Last());
                Check.True(h.Settings.Mappings.Values.Contains(InputAction.Backspace));
            });
        }
        foreach (var action in ActionCatalog.All)
        {
            var assigned = action.Action;
            suite.Add($"Context D-pad supports catalog action {action.Id}", () =>
            {
                var h = Create([assigned, null, null, null]); h.Tick(new(ControllerButtons.LB)); h.Tick(new(ControllerButtons.LB | ControllerButtons.DPadUp));
                Check.True(h.Engine.PieOpen == (assigned != InputAction.ReleaseCancel));
                if (assigned != InputAction.Backspace) Check.True(!h.Sink.Actions.Contains(InputAction.Backspace));
                else Check.Equal(1, h.Sink.Actions.Count);
                h.Tick(default); if (assigned == InputAction.DragLock) h.Engine.Reset(); Check.True(h.Sink.Held.All(held => !held));
            });
        }
        suite.Add("Context D-pad independent diagonal presses preserve pie selection", () =>
        {
            var h = Create([InputAction.Copy, null, InputAction.Paste, null]); h.Tick(new(ControllerButtons.LB, LeftY: 32767));
            h.Tick(new(ControllerButtons.LB | ControllerButtons.DPadUp | ControllerButtons.DPadLeft, LeftY: 32767));
            Check.Equal(2, h.Sink.Actions.Count); Check.Equal(0, h.Engine.PieSelected); Check.True(h.Engine.PieOpen);
            h.Tick(new(LeftY: 32767)); Check.Equal(3, h.Sink.Actions.Count); Check.Equal(InputAction.Copy, h.Sink.Actions.Last());
        });
        suite.Add("Context D-pad held precision and boost release before default rearming", () =>
        {
            foreach (var action in new[] { InputAction.PrecisionHold, InputAction.SpeedBoostHold })
            {
                var h = Create([action, null, null, null]); h.Tick(new(ControllerButtons.LB)); h.Tick(new(ControllerButtons.LB | ControllerButtons.DPadUp));
                Check.True(h.Engine.PrecisionActive || h.Engine.SpeedBoostActive);
                h.Tick(new(ControllerButtons.DPadUp)); Check.True(!h.Engine.PrecisionActive && !h.Engine.SpeedBoostActive);
                h.Tick(new(ControllerButtons.DPadUp)); Check.Equal(0, h.Sink.Actions.Count);
            }
        });
        suite.Add("Context D-pad respects mouse hold conflicts and ignores a close-sample press", () =>
        {
            var h = Create([InputAction.LeftClick, InputAction.Copy, InputAction.RightClick, InputAction.Paste]); h.Tick(new(ControllerButtons.LB));
            h.Tick(new(ControllerButtons.LB | ControllerButtons.DPadUp)); Check.True(h.Sink.Held[0]);
            h.Tick(new(ControllerButtons.LB | ControllerButtons.DPadUp | ControllerButtons.DPadDown | ControllerButtons.DPadLeft));
            Check.Equal(0, h.Sink.Actions.Count); Check.True(!h.Sink.Held[1]);
            h.Tick(new(ControllerButtons.DPadRight)); Check.Equal(0, h.Sink.Actions.Count); Check.True(h.Sink.Held.All(held => !held));
        });
        suite.Add("Context D-pad ignores entry holds and other menu bindings until fresh release", () =>
        {
            var h = Create([InputAction.Copy, null, null, null]);
            h.Tick(new(ControllerButtons.DPadUp)); var before = h.Sink.Actions.Count;
            h.Tick(new(ControllerButtons.LB | ControllerButtons.DPadUp)); h.Tick(new(ControllerButtons.LB | ControllerButtons.DPadUp)); Check.Equal(before, h.Sink.Actions.Count);
            h.Tick(new(ControllerButtons.LB)); h.Tick(new(ControllerButtons.LB | ControllerButtons.DPadUp)); Check.Equal(InputAction.Copy, h.Sink.Actions.Last());
            h.Tick(default);
            var other = PieMenu.Create("Other").AssignDPad(0, InputAction.Paste);
            h.Settings = h.Settings with { PieMenus = [h.Settings.PieMenus[0], other] }; h.Settings.PieBindings[Control.RB] = other.Id; h.Engine.Configure(h.Settings); h.Tick(default);
            h.Tick(new(ControllerButtons.RB)); h.Tick(new(ControllerButtons.RB | ControllerButtons.DPadUp)); Check.Equal(InputAction.Paste, h.Sink.Actions.Last());
        });
        suite.Add("Context D-pad null and None retain suppression and override default cancel", () =>
        {
            var h = Create([InputAction.Copy, InputAction.None, null, null]); h.Map(Control.DPadUp, InputAction.ReleaseCancel);
            h.Tick(new(ControllerButtons.LB)); h.Tick(new(ControllerButtons.LB | ControllerButtons.DPadUp)); Check.True(h.Engine.PieOpen); Check.Equal(InputAction.Copy, h.Sink.Actions.Single());
            h.Tick(new(ControllerButtons.LB | ControllerButtons.DPadDown | ControllerButtons.DPadRight)); Check.Equal(1, h.Sink.Actions.Count);
        });
        foreach (var lifecycle in new[] { "close", "pause", "disconnect", "focus", "configure", "reset" })
        {
            var change = lifecycle;
            suite.Add("Context D-pad held ownership cleans up on " + change, () =>
            {
                var h = Create([InputAction.LeftClick, null, null, null]); h.Tick(new(ControllerButtons.LB)); h.Tick(new(ControllerButtons.LB | ControllerButtons.DPadUp)); Check.True(h.Sink.Held[0]);
                switch (change)
                {
                    case "close": h.Tick(new(ControllerButtons.DPadUp)); break;
                    case "pause": h.Engine.SetPaused(true); break;
                    case "disconnect": h.Engine.Disconnect(); break;
                    case "focus": h.Platform.Foreground = 2; h.Tick(new(ControllerButtons.LB | ControllerButtons.DPadUp)); break;
                    case "configure": h.Engine.Configure(h.Settings with { PieMenus = [h.Settings.PieMenus[0].AssignDPad(0, InputAction.Copy)] }); break;
                    default: h.Engine.Reset(); break;
                }
                Check.True(!h.Sink.Held[0]); Check.True(!h.Engine.PieOpen);
                h.Engine.SetPaused(false); h.Tick(new(ControllerButtons.DPadUp)); Check.True(!h.Sink.Actions.Contains(InputAction.Backspace));
            });
        }
        suite.Add("Context D-pad repeat ends on close and double click can progress while open", () =>
        {
            var h = Create([InputAction.VolumeUp, InputAction.DoubleClick, null, null]); h.Tick(new(ControllerButtons.LB));
            for (var i = 0; i < 60; i++) h.Tick(new(ControllerButtons.LB | ControllerButtons.DPadUp));
            Check.True(h.Sink.Actions.Count > 1); h.Tick(new(ControllerButtons.DPadUp)); var count = h.Sink.Actions.Count;
            for (var i = 0; i < 80; i++) h.Tick(new(ControllerButtons.DPadUp)); Check.Equal(count, h.Sink.Actions.Count);
            h.Tick(default); h.Tick(new(ControllerButtons.LB)); h.Tick(new(ControllerButtons.LB | ControllerButtons.DPadDown));
            for (var i = 0; i < 8; i++) h.Tick(new(ControllerButtons.LB)); Check.Equal(4, h.Sink.ButtonEvents.Count); Check.True(h.Engine.PieOpen);
        });
        suite.Add("Context D-pad opener stays reserved and does not execute override", () =>
        {
            var h = Create([InputAction.Copy, InputAction.Paste, null, null], opener: Control.DPadUp);
            h.Tick(new(ControllerButtons.DPadUp)); Check.True(h.Engine.PieOpen); Check.Equal(0, h.Sink.Actions.Count);
            h.Tick(new(ControllerButtons.DPadUp | ControllerButtons.DPadDown)); Check.Equal(InputAction.Paste, h.Sink.Actions.Single());
            h.Tick(default); Check.True(!h.Engine.PieOpen);
        });
        suite.Add("Context D-pad drafts histories clones profiles and persistence are isolated", () =>
        {
            using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path);
            var settings = ProfileLibrary.Initialize(PiePresets.Initialize(new(), bind: true)); var menu = settings.PieMenus[0]; var before = EditFields.Read(EditTab.PieMenus, settings);
            var draft = new PieDraft(menu, settings); draft.AssignDPad(2, InputAction.PrecisionHold); Check.True(draft.Dirty); Check.True(menu.DPadActions[2] is null);
            var edited = draft.Apply(settings); var after = EditFields.Read(EditTab.PieMenus, edited); var history = new EditHistory(); history.Record(before, after);
            var undone = EditFields.Apply(EditTab.PieMenus, history.Undo(after), edited); Check.True(undone.PieMenus[0].DPadActions[2] is null);
            edited = EditFields.Apply(EditTab.PieMenus, history.Redo(EditFields.Read(EditTab.PieMenus, undone)), undone);
            Check.True(store.Save(edited) is null); var loaded = store.Load().Settings; Check.Equal(InputAction.PrecisionHold, loaded.PieMenus[0].DPadActions[2]!.Value);
            var copy = loaded.PieMenus[0].Duplicate(); copy.DPadActions[2] = InputAction.Copy; Check.Equal(InputAction.PrecisionHold, loaded.PieMenus[0].DPadActions[2]!.Value);
            var snapshot = ProfileLibrary.Snapshot(loaded); snapshot.PieMenus[0].DPadActions[2] = InputAction.Copy; Check.Equal(InputAction.PrecisionHold, loaded.PieMenus[0].DPadActions[2]!.Value);
            draft.Restore(); Check.True(draft.Menu.DPadActions.SequenceEqual(FactoryDefaults.Create().PieMenus[0].DPadActions));
        });
        suite.Add("Context D-pad v14 migration preserves exact file and defaults, backup failure blocks saving", () =>
        {
            using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path);
            const string original = """{"Version":14,"CursorSpeed":1573,"Mappings":{"DPadUp":"Backspace"},"PieMenus":[{"Id":"098db238-091f-4507-bbb5-54447e9c7ec9","Name":"Kept","SegmentCount":4,"Slots":["Copy"]}]}""";
            File.WriteAllText(store.FilePath, original); var loaded = store.Load(); Check.Equal(original, File.ReadAllText(store.FilePath + ".v14-backup")); Check.Equal(original, File.ReadAllText(store.FilePath));
            Check.True(loaded.Settings.PieMenus.All(m => m.DPadActions.All(a => a is null))); Check.Equal(InputAction.Backspace, loaded.Settings.Mappings[Control.DPadUp]);
            File.Delete(store.FilePath + ".v14-backup"); Directory.CreateDirectory(store.FilePath + ".v14-backup"); loaded = store.Load(); Check.True(loaded.Warning is not null); Check.True(store.Save(loaded.Settings) is not null); Check.Equal(original, File.ReadAllText(store.FilePath));
        });
        suite.Add("Context D-pad malformed optional entries recover independently with exact backup", () =>
        {
            using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path);
            const string json = """{"Version":15,"CursorSpeed":1573,"PieMenus":[{"Id":"098db238-091f-4507-bbb5-54447e9c7ec9","Name":"Kept","SegmentCount":4,"Slots":["Copy"],"DPadActions":["VolumeUp",{},"Copy","FutureAction"]}]}""";
            File.WriteAllText(store.FilePath, json); var loaded = store.Load(); Check.True(loaded.Warning is not null); Check.Near(1573, loaded.Settings.CursorSpeed);
            var menu = loaded.Settings.PieMenus[0]; Check.Equal(InputAction.Copy, menu.Slots[0]); Check.Equal(InputAction.VolumeUp, menu.DPadActions[0]!.Value); Check.True(menu.DPadActions[1] is null); Check.Equal(InputAction.Copy, menu.DPadActions[2]!.Value);
            Check.Equal(json, File.ReadAllText(Directory.GetFiles(dir.Path, "*.profiles-backup-*").Single())); Check.True(store.Save(loaded.Settings) is null);
        });
        suite.Add("Context D-pad hint placement fits whole strip at monitor edges and DPI", () =>
        {
            foreach (var dpi in new[] { 96d, 144, 192 }) foreach (var x in new[] { -1920, -1 }) foreach (var y in new[] { 0, 400 })
            {
                var p = PiePlacement.Fit(x, y, -1920, 0, 0, 400, dpi, 200, 480d / 440);
                Check.True(p.X >= -1920 && p.X + p.Size <= 0 && p.Y >= 0 && p.Y + Math.Ceiling(p.Size * 480d / 440) <= 400);
            }
        });
    }
}
