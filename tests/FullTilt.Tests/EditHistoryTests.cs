using FullTilt.Core;

static class EditHistoryTests
{
    public static void Register(TestSuite suite)
    {
        suite.Add("Edit history retains 100 steps and clears redo on branching", () =>
        {
            var history = new EditHistory(); var state = new Dictionary<string, string> { ["value"] = "0" };
            for (var i = 1; i <= 120; i++) { var next = new Dictionary<string, string> { ["value"] = i.ToString() }; history.Record(state, next); state = next; }
            Check.Equal(100, history.UndoCount);
            for (var i = 0; i < 100; i++) state = history.Undo(state);
            Check.Equal("20", state["value"]); Check.True(!history.CanUndo && history.CanRedo);
            state = history.Redo(state); Check.Equal("21", state["value"]);
            var branch = new Dictionary<string, string> { ["value"] = "500" }; history.Record(state, branch); Check.True(!history.CanRedo);
            history.Record(branch, new(branch)); Check.Equal(2, history.UndoCount);
        });
        suite.Add("Edit history replays only edited fields across tabs and saves", () =>
        {
            var saved = AppSettings.FreshInstallation(); var history = new EditHistory();
            var before = EditFields.Read(EditTab.Movement, saved); var after = EditFields.Read(EditTab.Movement, saved with { CursorSpeed = 777 });
            history.Record(before, after);
            var later = saved with { CursorSpeed = 777, ScrollSpeed = 21, CursorStick = CursorStick.Right };
            var undone = EditFields.Apply(EditTab.Movement, history.Undo(EditFields.Read(EditTab.Movement, later)), later);
            Check.Near(saved.CursorSpeed, undone.CursorSpeed); Check.Near(21, undone.ScrollSpeed); Check.Equal(CursorStick.Right, undone.CursorStick);
            var redone = EditFields.Apply(EditTab.Movement, history.Redo(EditFields.Read(EditTab.Movement, undone)), undone);
            Check.Near(777, redone.CursorSpeed); Check.Near(21, redone.ScrollSpeed);
        });
        suite.Add("Edit history restores deleted menu ordering and activation bindings", () =>
        {
            var saved = AppSettings.FreshInstallation(); var menu = saved.PieMenus[1]; var draft = new PieDraft(menu, saved) { Deleted = true };
            var deleted = draft.Apply(saved); var history = new EditHistory(); history.Record(EditFields.Read(EditTab.PieMenus, saved), EditFields.Read(EditTab.PieMenus, deleted));
            var restored = EditFields.Apply(EditTab.PieMenus, history.Undo(EditFields.Read(EditTab.PieMenus, deleted)), deleted);
            Check.True(saved.PieMenus.Select(m => m.Id).SequenceEqual(restored.PieMenus.Select(m => m.Id)));
            Check.True(saved.PieBindings.OrderBy(p => p.Key).SequenceEqual(restored.PieBindings.OrderBy(p => p.Key)));
        });
        suite.Add("Edit fields keep keybindings drafts detached and pie size global", () =>
        {
            var saved = ProfileLibrary.Initialize(AppSettings.FreshInstallation());
            var maps = new Dictionary<Control, InputAction>(saved.Mappings) { [Control.A] = InputAction.Copy };
            var fields = EditFields.Read(EditTab.Keybindings, saved with { Mappings = maps });
            var view = EditFields.Apply(EditTab.Keybindings, fields, saved);
            Check.Equal(InputAction.Copy, view.Mappings[Control.A]); Check.Equal(InputAction.LeftClick, saved.Mappings[Control.A]);
            var other = saved with { ScrollSpeed = 25, PieMenuSizePercent = 180 };
            var merged = EditFields.Apply(EditTab.Keybindings, EditFields.Merge(EditFields.Read(EditTab.Keybindings, saved), fields, EditFields.Read(EditTab.Keybindings, other)), other);
            Check.Near(25, merged.ScrollSpeed); Check.Near(180, merged.PieMenuSizePercent);
            var second = ProfileLibrary.Create(merged, "Other"); Check.Near(180, second.PieMenuSizePercent);
        });
    }
}
