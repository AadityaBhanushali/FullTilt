namespace FullTilt.Core;

public sealed record FullTiltProfile(Guid Id, string Name, AppSettings Configuration);

// Snapshots contain configuration only, never another profile library or live input state.
public static class ProfileLibrary
{
    public static AppSettings Snapshot(AppSettings settings) => (settings with { Profiles = null, ActiveProfileId = null, PieMenuSizePercent = 100 }).Validated();
    public static string CleanName(string? name) => new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
    public static AppSettings Initialize(AppSettings settings)
    {
        var current = settings.Validated();
        var profiles = new List<FullTiltProfile>(); var ids = new HashSet<Guid>();
        foreach (var profile in current.Profiles ?? [])
            if (profile is not null && profile.Id != Guid.Empty && ids.Add(profile.Id) && profile.Configuration is not null)
            {
                var name = CleanName(profile.Name);
                if (name.Length is 0 or > 80) continue;
                profiles.Add(profile with { Name = name, Configuration = Snapshot(profile.Configuration) });
            }
        if (profiles.Count == 0) profiles.Add(new(Guid.NewGuid(), "Default", Snapshot(current)));
        var active = profiles.Any(p => p.Id == current.ActiveProfileId) ? current.ActiveProfileId : profiles[0].Id;
        return current with { Profiles = profiles, ActiveProfileId = active };
    }
    public static AppSettings Checkpoint(AppSettings settings)
    {
        var current = Initialize(settings);
        return current with { Profiles = current.Profiles!.Select(p => p.Id == current.ActiveProfileId ? p with { Configuration = Snapshot(current) } : p).ToList() };
    }
    public static AppSettings Create(AppSettings settings, string name)
    {
        var current = Checkpoint(settings); name = CleanName(name);
        if (name.Length is 0 or > 80) throw new ArgumentException("Enter a profile name with 1–80 characters.", nameof(name));
        if (current.Profiles!.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("A profile with that name already exists.", nameof(name));
        var profile = new FullTiltProfile(Guid.NewGuid(), name, Snapshot(current));
        return current with { ActiveProfileId = profile.Id, Profiles = [.. current.Profiles!, profile] };
    }
    public static AppSettings Rename(AppSettings settings, Guid id, string name)
    {
        var current = Checkpoint(settings); name = CleanName(name);
        if (name.Length is 0 or > 80) throw new ArgumentException("Enter a profile name with 1–80 characters.", nameof(name));
        if (!current.Profiles!.Any(p => p.Id == id)) throw new ArgumentException("Profile no longer exists.", nameof(id));
        if (current.Profiles!.Any(p => p.Id != id && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("A profile with that name already exists.", nameof(name));
        return current with { Profiles = current.Profiles!.Select(p => p.Id == id ? p with { Name = name } : p).ToList() };
    }
    public static AppSettings Delete(AppSettings settings, Guid id)
    {
        var current = Checkpoint(settings);
        if (!current.Profiles!.Any(p => p.Id == id)) throw new ArgumentException("Profile no longer exists.", nameof(id));
        if (current.Profiles!.Count == 1) throw new ArgumentException("Keep at least one profile.", nameof(id));
        var remaining = current.Profiles!.Where(p => p.Id != id).ToList();
        if (current.ActiveProfileId != id) return current with { Profiles = remaining };
        return Snapshot(remaining[0].Configuration) with { Profiles = remaining, ActiveProfileId = remaining[0].Id, PieMenuSizePercent = current.PieMenuSizePercent };
    }
    public static AppSettings Switch(AppSettings settings, Guid id)
    {
        var current = Checkpoint(settings);
        var profile = current.Profiles!.FirstOrDefault(p => p.Id == id) ?? throw new ArgumentException("Profile no longer exists.", nameof(id));
        return Snapshot(profile.Configuration) with { ActiveProfileId = id, Profiles = current.Profiles, PieMenuSizePercent = current.PieMenuSizePercent };
    }
}
