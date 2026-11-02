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
        if (startup.Writes != 1 || !model.StartWithWindows || startup.Command != StartupCommand.ForExecutable(exe) || model.Theme != "Dark" || !model.OnboardingPending)
            throw new InvalidOperationException("A fresh install must enable startup once and queue the tour.");
        model.Theme = "Light"; model.Theme = "Dark";
        model.StartWithWindows = true;
        if (startup.Writes != 1 || startup.Command != StartupCommand.ForExecutable(exe) || !model.StartWithWindows) throw new InvalidOperationException("Startup enable failed.");
        var reopened = new AppPreferencesViewModel(new AppPreferencesStore(Path.Combine(output, "app-preferences")), startup, exe);
        if (!reopened.StartWithWindows || reopened.Theme != "Dark" || startup.Writes != 1) throw new InvalidOperationException("Preferences failed restart or rewrote startup automatically.");
        if (!reopened.OnboardingPending) throw new InvalidOperationException("The queued tour was lost on restart.");
        reopened.StartWithWindows = false;
        if (startup.Command is not null || reopened.StartWithWindows) throw new InvalidOperationException("Startup disable failed.");
        var afterDisable = new AppPreferencesViewModel(new AppPreferencesStore(Path.Combine(output, "app-preferences")), startup, exe);
        if (afterDisable.StartWithWindows || startup.Command is not null || startup.Writes != 2) throw new InvalidOperationException("Disabled startup was re-enabled on a later launch.");
        if (!afterDisable.CompleteOnboarding() || afterDisable.OnboardingPending || new AppPreferencesViewModel(new AppPreferencesStore(Path.Combine(output, "app-preferences"))).OnboardingPending) throw new InvalidOperationException("Finishing the tour was not remembered.");
        var oldCopy = new FakeStartup { Command = "\"C:/Old/FullTilt.exe\" --startup" };
        var repointed = new AppPreferencesViewModel(new AppPreferencesStore(Path.Combine(output, "repoint-app-preferences")), oldCopy, exe);
        if (!repointed.StartWithWindows || oldCopy.Command != StartupCommand.ForExecutable(exe)) throw new InvalidOperationException("A fresh install must take over an older FullTilt copy's startup entry.");
        var unrelated = new FakeStartup { Command = "\"C:/Tools/Other.exe\"" };
        _ = new AppPreferencesViewModel(new AppPreferencesStore(Path.Combine(output, "unrelated-app-preferences")), unrelated, exe);
        if (unrelated.Writes != 0 || unrelated.Command != "\"C:/Tools/Other.exe\"") throw new InvalidOperationException("A fresh install changed an unrelated startup entry.");
        var existingDirectory = Path.Combine(output, "existing-app-preferences"); Directory.CreateDirectory(existingDirectory);
        File.WriteAllText(Path.Combine(existingDirectory, "app-preferences.json"), "{\"Version\":1,\"Theme\":\"Light\"}");
        var existingBytes = File.ReadAllBytes(Path.Combine(existingDirectory, "app-preferences.json")); var existingStartup = new FakeStartup();
        var existing = new AppPreferencesViewModel(new AppPreferencesStore(existingDirectory), existingStartup, exe);
        if (existingStartup.Writes != 0 || existing.StartWithWindows || existing.OnboardingPending || !System.Linq.Enumerable.SequenceEqual(existingBytes, File.ReadAllBytes(Path.Combine(existingDirectory, "app-preferences.json"))))
            throw new InvalidOperationException("An existing install must not get startup or the tour, and its file must not be rewritten.");
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
