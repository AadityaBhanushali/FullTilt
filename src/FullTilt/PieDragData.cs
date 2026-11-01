using System;
using System.Globalization;
using System.Windows;
using FullTilt.Core;

namespace FullTilt;

// Use plain text across OLE instead of serializing CLR enum/record instances.
internal static class PieDragData
{
    private const string ActionFormat = "FullTilt.PieAction", SlotFormat = "FullTilt.PieSlot";
    public static DataObject ForAction(InputAction action) => new(ActionFormat, ActionCatalog.Get(action).Id);
    public static DataObject ForSlot(Guid menu, int slot) => new(SlotFormat, menu.ToString("D") + "/" + slot.ToString(CultureInfo.InvariantCulture));
    public static bool TryAction(IDataObject data, out InputAction action)
    {
        action = InputAction.None;
        var value = data.GetData(ActionFormat, false);
        if (value is string id && ActionCatalog.TryFromId(id, out var definition)) { action = definition!.Action; return true; }
        if (value is InputAction legacy && ActionCatalog.Contains(legacy)) { action = legacy; return true; }
        return false;
    }
    public static bool TrySlot(IDataObject data, out PieSlotDrag slot)
    {
        slot = new(Guid.Empty, -1);
        var value = data.GetData(SlotFormat, false);
        if (value is PieSlotDrag legacy) { slot = legacy; return true; }
        if (value is not string text) return false;
        var fields = text.Split('/');
        if (fields.Length != 2 || !Guid.TryParseExact(fields[0], "D", out var id) || id == Guid.Empty ||
            !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var index)) return false;
        slot = new(id, index); return true;
    }
}
