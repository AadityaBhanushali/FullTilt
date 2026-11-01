using System.Text.Json;

namespace FullTilt.Core;

public enum EditTab { Keybindings, Movement, PieMenus }

// Histories contain field patches, rather than entire profiles. Replaying one tab
// must not roll back a later edit committed by another tab.
public sealed class EditHistory
{
    private sealed record Step(Dictionary<string, string> Before, Dictionary<string, string> After);
    private readonly List<Step> _undo = [], _redo = [];
    public bool CanUndo => _undo.Count != 0;
    public bool CanRedo => _redo.Count != 0;
    public int UndoCount => _undo.Count;
    public void Record(Dictionary<string, string> before, Dictionary<string, string> after)
    {
        if (EditFields.Equal(before, after)) return;
        _undo.Add(new(new(before), new(after))); _redo.Clear();
        if (_undo.Count > 100) _undo.RemoveAt(0);
    }
    public Dictionary<string, string> Undo(Dictionary<string, string> current) => Replay(_undo, _redo, current, true);
    public Dictionary<string, string> Redo(Dictionary<string, string> current) => Replay(_redo, _undo, current, false);
    private static Dictionary<string, string> Replay(List<Step> from, List<Step> to, Dictionary<string, string> current, bool undo)
    {
        if (from.Count == 0) return current;
        var step = from[^1]; from.RemoveAt(from.Count - 1); to.Add(step);
        return undo ? EditFields.Merge(step.After, step.Before, current) : EditFields.Merge(step.Before, step.After, current);
    }
}

public static class EditFields
{
    private static readonly string[] Movement = [nameof(AppSettings.CursorSpeed), nameof(AppSettings.PrecisionCursorSpeed), nameof(AppSettings.BoostedCursorSpeed), nameof(AppSettings.PrecisionPercent),
        nameof(AppSettings.SpeedBoostMultiplier), nameof(AppSettings.ScrollSpeed), nameof(AppSettings.ScrollBoostMultiplier),
        nameof(AppSettings.ReverseScroll), nameof(AppSettings.CursorStick), nameof(AppSettings.ResponseCurve),
        nameof(AppSettings.LeftDeadZone), nameof(AppSettings.RightDeadZone), nameof(AppSettings.PrecisionSpeed),
        nameof(AppSettings.DPadTapDistance), nameof(AppSettings.DPadMode)];
    public static Dictionary<string, string> Read(EditTab tab, AppSettings settings)
    {
        var result = new Dictionary<string, string>();
        var json = JsonSerializer.SerializeToElement(settings);
        var properties = tab == EditTab.Movement ? Movement : tab == EditTab.Keybindings
            ? new[] { nameof(AppSettings.CursorStick), nameof(AppSettings.DPadMode) } : new[] { nameof(AppSettings.PieMenuSizePercent) };
        foreach (var property in properties) result[property] = json.GetProperty(property).GetRawText();
        if (tab != EditTab.Movement)
            foreach (var control in Enum.GetValues<Control>()) result["Binding/" + control] = settings.PieBindings.TryGetValue(control, out var id)
                ? "pie:" + id : settings.RecordedBindings.TryGetValue(control, out var chord) ? "keys:" + JsonSerializer.Serialize(chord) : "action:" + (int)settings.Mappings.GetValueOrDefault(control);
        if (tab == EditTab.PieMenus)
        {
            foreach (var menu in settings.PieMenus) result["Menu/" + menu.Id] = JsonSerializer.Serialize(menu);
            result["MenuOrder"] = JsonSerializer.Serialize(settings.PieMenus.Select(m => m.Id));
        }
        return result;
    }
    public static bool Equal(Dictionary<string, string> a, Dictionary<string, string> b) => a.Count == b.Count && a.All(p => b.GetValueOrDefault(p.Key) == p.Value);
    public static int Differences(Dictionary<string, string> a, Dictionary<string, string> b) => a.Keys.Union(b.Keys).Count(k => a.GetValueOrDefault(k) != b.GetValueOrDefault(k));
    public static Dictionary<string, string> Merge(Dictionary<string, string> baseline, Dictionary<string, string> draft, Dictionary<string, string> latest)
    {
        var result = new Dictionary<string, string>(latest);
        foreach (var key in baseline.Keys.Union(draft.Keys))
            if (baseline.GetValueOrDefault(key) != draft.GetValueOrDefault(key))
            { if (draft.TryGetValue(key, out var value)) result[key] = value; else result.Remove(key); }
        return result;
    }
    public static AppSettings Apply(EditTab tab, Dictionary<string, string> fields, AppSettings latest)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(latest))!.AsObject();
        foreach (var pair in fields.Where(p => !p.Key.Contains('/') && p.Key != "MenuOrder")) json[pair.Key] = System.Text.Json.Nodes.JsonNode.Parse(pair.Value);
        var next = json.Deserialize<AppSettings>()!;
        if (tab != EditTab.Movement)
        {
            var mappings = new Dictionary<Control, InputAction>(latest.Mappings);
            var bindings = new Dictionary<Control, Guid>(latest.PieBindings);
            var recorded = new Dictionary<Control, KeyboardChord>(latest.RecordedBindings);
            foreach (var control in Enum.GetValues<Control>())
                if (fields.TryGetValue("Binding/" + control, out var target))
                {
                    recorded.Remove(control);
                    if (target.StartsWith("pie:", StringComparison.Ordinal)) { bindings[control] = Guid.Parse(target[4..]); mappings[control] = InputAction.None; }
                    else if (target.StartsWith("keys:", StringComparison.Ordinal)) { bindings.Remove(control); mappings[control] = InputAction.None; recorded[control] = JsonSerializer.Deserialize<KeyboardChord>(target[5..])!; }
                    else { bindings.Remove(control); mappings[control] = (InputAction)int.Parse(target[7..], System.Globalization.CultureInfo.InvariantCulture); }
                }
            next = next with { Mappings = mappings, PieBindings = bindings, RecordedBindings = recorded };
        }
        if (tab == EditTab.PieMenus)
        {
            // Keep existing menu ordering; deleted and recreated menus retain their slot.
            var menus = latest.PieMenus.Where(m => fields.ContainsKey("Menu/" + m.Id)).Select(m => JsonSerializer.Deserialize<PieMenu>(fields["Menu/" + m.Id])!).ToList();
            menus.AddRange(fields.Where(p => p.Key.StartsWith("Menu/", StringComparison.Ordinal) && !menus.Any(m => "Menu/" + m.Id == p.Key)).Select(p => JsonSerializer.Deserialize<PieMenu>(p.Value)!));
            var order = fields.TryGetValue("MenuOrder", out var orderJson) ? JsonSerializer.Deserialize<Guid[]>(orderJson)! : [];
            menus = menus.OrderBy(m => { var index = Array.IndexOf(order, m.Id); return index < 0 ? int.MaxValue : index; }).ToList();
            next = next with { PieMenus = menus };
        }
        return next.Validated();
    }
}
