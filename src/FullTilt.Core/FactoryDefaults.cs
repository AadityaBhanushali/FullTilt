using System.Text.Json;
using System.Text.Json.Serialization;

namespace FullTilt.Core;

// Captured, reviewed configuration. Never read mutable user files during a reset.
public static class FactoryDefaults
{
    public static AppSettings Create()
    {
        using var stream = typeof(FactoryDefaults).Assembly.GetManifestResourceStream("FullTilt.Core.Defaults.DefaultProfile.json")
            ?? throw new InvalidOperationException("The default profile is missing.");
        return (JsonSerializer.Deserialize<AppSettings>(stream, new JsonSerializerOptions
        {
            Converters = { new ActionIdJsonConverter(), new JsonStringEnumConverter() }
        }) ?? throw new InvalidOperationException("The default profile is empty.")).Validated();
    }
    public static PieMenu? Menu(PieMenu menu)
    {
        var baseline = Create();
        return baseline.PieMenus.FirstOrDefault(m => m.Id == menu.Id)
            ?? baseline.PieMenus.FirstOrDefault(m => menu.PresetId is not null && m.PresetId == menu.PresetId);
    }
}
