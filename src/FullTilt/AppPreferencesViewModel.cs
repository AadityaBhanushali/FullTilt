using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security;
using FullTilt.Core;

namespace FullTilt;

public sealed class AppPreferencesViewModel : INotifyPropertyChanged
{
    private readonly AppPreferencesStore? _store;
    private readonly IStartupRegistration? _startup;
    private readonly string _command;
    private readonly Func<bool> _systemIsDark;
    private AppPreferences _preferences;
    private bool _startWithWindows;
    public string[] ThemeOptions { get; } = ["System default", "Light", "Dark"];
    public string StoragePath { get; }
    public static string ReleaseVersion => BuildVersion;
    public static Version ApplicationVersion => typeof(AppPreferencesViewModel).Assembly.GetName().Version ?? throw new InvalidOperationException("Application version metadata is missing.");
    public static string BuildVersion => ApplicationVersion.ToString(3);
    public string? AcknowledgedRelease => _preferences.AcknowledgedRelease;
    public bool AutomaticallyCheckForUpdates
    {
        get => _preferences.AutomaticallyCheckForUpdates;
        set { SaveUpdatePreferences(_preferences with { AutomaticallyCheckForUpdates = value }); }
    }
    public bool AcknowledgeRelease(string tag) => SaveUpdatePreferences(_preferences with { AcknowledgedRelease = tag });
    private bool SaveUpdatePreferences(AppPreferences next)
    {
        Error = _store?.Save(next);
        if (Error is null) _preferences = next;
        Notify(nameof(AutomaticallyCheckForUpdates)); Notify(nameof(AcknowledgedRelease)); Notify(nameof(Error));
        return Error is null;
    }
    public string VersionLabel => $"FullTilt {BuildVersion} · Windows x64";
    public string? Error { get; private set; }
    public bool StartupAvailable => _startup is not null;
    public string StartupDescription { get; private set; } = "Starts in the system tray with controller input active. Applies to this Windows account.";
    public AppPreferencesViewModel(AppPreferencesStore? store = null, IStartupRegistration? startup = null, string? executable = null, Func<bool>? systemIsDark = null)
    {
        _systemIsDark = systemIsDark ?? (() => false);
        _store = store; _startup = startup; _preferences = store?.Load() ?? new(); Error = store?.Warning;
        StoragePath = store is null ? "Isolated diagnostic settings" : Path.GetDirectoryName(store.FilePath)!;
        _command = executable is null ? "" : StartupCommand.ForExecutable(executable);
        if (startup is not null)
            try { var registered = startup.Read(); _startWithWindows = registered == _command; if (registered is not null && !_startWithWindows) StartupDescription = "Another FullTilt copy is registered. Enable here to use this copy at sign-in."; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { Error = "Cannot read Windows startup registration. " + ex.Message; }
    }
    public bool Maximized
    {
        get => _preferences.Maximized;
        set
        {
            if (value == Maximized) return;
            var next = _preferences with { Maximized = value };
            var error = _store?.Save(next);
            if (error is null) _preferences = next;
            Error = error;
            PropertyChanged?.Invoke(this, new(nameof(Error)));
        }
    }
    public string Theme
    {
        get => _preferences.Theme;
        set
        {
            if (value is not ("System default" or "Light" or "Dark") || value == Theme) return;
            var next = _preferences with { Theme = value }; var error = _store?.Save(next);
            if (error is null) { _preferences = next; ApplyTheme(); }
            Error = error; Notify(nameof(Error)); Notify();
        }
    }
    public void ApplyTheme() => ThemePalette.Apply(Theme == "Dark" || (Theme == "System default" && _systemIsDark()));
    public void SystemThemeChanged() { if (Theme == "System default") ApplyTheme(); }
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (_startup is null || value == _startWithWindows) return;
            try
            {
                // Do not remove a different copy's registration after this window was opened.
                if (!value && _startup.Read() != _command) throw new IOException("Startup registration has changed. Reopen FullTilt settings to refresh it.");
                _startup.Write(value ? _command : null);
                if (_startup.Read() != (value ? _command : null)) throw new IOException("Windows did not retain the startup change.");
                _startWithWindows = value; Error = null;
                StartupDescription = "Starts in the system tray with controller input active. If you move FullTilt, disable and re-enable this option from the new folder.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { Error = "Could not change Windows startup. " + ex.Message; }
            Notify(); Notify(nameof(Error)); Notify(nameof(StartupDescription));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
