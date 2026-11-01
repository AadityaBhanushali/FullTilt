using System;
using System.IO;
using FullTilt.Core;

namespace FullTilt;

internal static class AppPreferencesDiagnostics
{
    public static string Verify(string output)
    {
        var store = new AppPreferencesStore(Path.Combine(output, "app-preferences"));
        var startup = new FakeStartup();
        var exe = Path.Combine(output, "FullTilt.exe");
        var model = new AppPreferencesViewModel(store, startup, exe);
        if (startup.Writes != 0 || model.StartWithWindows || model.Theme != "Dark") throw new InvalidOperationException("Default preferences registered startup.");
        model.Theme = "Light"; model.Theme = "Dark";
        model.StartWithWindows = true;
        if (startup.Command != StartupCommand.ForExecutable(exe) || !model.StartWithWindows) throw new InvalidOperationException("Startup enable failed.");
        var reopened = new AppPreferencesViewModel(new AppPreferencesStore(Path.Combine(output, "app-preferences")), startup, exe);
        if (!reopened.StartWithWindows || reopened.Theme != "Dark" || startup.Writes != 1) throw new InvalidOperationException("Preferences failed restart or rewrote startup automatically.");
        reopened.StartWithWindows = false;
        if (startup.Command is not null || reopened.StartWithWindows) throw new InvalidOperationException("Startup disable failed.");
        startup.Fail = true; reopened.StartWithWindows = true;
        if (reopened.StartWithWindows || reopened.Error is null) throw new InvalidOperationException("Failed startup write reported success.");
        startup.Fail = false; model.StartWithWindows = false;
        startup.Command = "another executable";
        var another = new AppPreferencesViewModel(store, startup, exe);
        if (another.StartWithWindows || startup.Command != "another executable") throw new InvalidOperationException("Opening another copy changed startup.");
        var blockedStore = new AppPreferencesStore(Path.Combine(output, "blocked-app-preferences"));
        Directory.CreateDirectory(blockedStore.FilePath);
        var blocked = new AppPreferencesViewModel(blockedStore);
        blocked.Theme = "Light";
        if (blocked.Theme != "Dark" || blocked.Error is null) throw new InvalidOperationException("Failed preference save changed theme.");
        var systemDark = true;
        var following = new AppPreferencesViewModel(store, systemIsDark: () => systemDark);
        following.Theme = "System default";
        void ExpectDark(bool expected)
        {
            var color = ThemePalette.Get("#FAFCF8").Color;
            if ((color.R < 100) != expected) throw new InvalidOperationException("System theme resolution failed.");
            if (expected && (color.R != color.G || color.G != color.B)) throw new InvalidOperationException("Dark background must be neutral.");
        }
        ExpectDark(true);
        var bytes = File.ReadAllBytes(store.FilePath);
        systemDark = false; following.SystemThemeChanged(); ExpectDark(false);
        if (!System.Linq.Enumerable.SequenceEqual(bytes, File.ReadAllBytes(store.FilePath))) throw new InvalidOperationException("System event rewrote preferences.");
        systemDark = true; following.SystemThemeChanged(); ExpectDark(true);
        following.Theme = "Light"; following.SystemThemeChanged(); ExpectDark(false);
        systemDark = false; following.Theme = "Dark"; following.SystemThemeChanged(); ExpectDark(true);
        foreach (var surface in new[] { "#FFFFFF", "#FCFDFB", "#161B17", "#767D77", "#EFF3EA", "#E7EBE3" })
        {
            var c = ThemePalette.Get(surface).Color;
            if (c.R != c.G || c.G != c.B) throw new InvalidOperationException("Non-neutral dark surface: " + surface);
        }
        following.Theme = "System default";
        var restored = new AppPreferencesViewModel(new AppPreferencesStore(Path.GetDirectoryName(store.FilePath)!), systemIsDark: () => true);
        restored.ApplyTheme(); ExpectDark(true);
        if (restored.Theme != "System default") throw new InvalidOperationException("System default was not persisted.");
        ThemePalette.Apply(false);
        return "PASS: live system-theme changes, manual overrides, neutral dark surfaces, restart persistence, theme persistence, defaults, startup enable/disable, failure reporting, current-path registration and no registration on load. Fake startup adapter only; real Windows startup never modified.";
    }
    private sealed class FakeStartup : IStartupRegistration
    {
        public string? Command; public int Writes; public bool Fail;
        public string? Read() => Command;
        public void Write(string? command) { if (Fail) throw new IOException("Test failure"); Writes++; Command = command; }
    }
}
