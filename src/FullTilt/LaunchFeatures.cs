using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using FullTilt.Core;

namespace FullTilt;

internal sealed class LaunchFeatures : IDisposable
{
    private readonly MainWindow _owner;
    private readonly AppPreferencesViewModel _preferences;
    private readonly Action<string> _warning;
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false });
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromHours(6) };
    private readonly GitHubUpdates _updates;
    private Task<UpdateResult>? _flight;
    private UpdateResult? _pending;
    private bool _dialogOpen, _disposed;
    private string? _sessionAcknowledged;
    internal LaunchFeatures(MainWindow owner, AppPreferencesViewModel preferences, Action<string> warning)
    {
        _owner = owner; _preferences = preferences; _warning = warning;
        var links = LaunchLinks.Current; _updates = new(_http, links.GitHubOwner, links.GitHubRepository, AppPreferencesViewModel.ApplicationVersion);
        owner.FileAction += HandleAction; owner.Activated += OnActivated;
        _timer.Tick += OnTick;
    }
    internal void Start() { _timer.Start(); _ = AutomaticCheckAsync(); }
    private void OnTick(object? sender, EventArgs e) => _ = AutomaticCheckAsync();
    private void OnActivated(object? sender, EventArgs e) => _owner.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(TryNotify));
    private Task<UpdateResult> CheckAsync() => _flight is { IsCompleted: false } ? _flight : _flight = _updates.CheckAsync(_lifetime.Token);
    private async Task AutomaticCheckAsync()
    {
        if (_disposed || !_preferences.AutomaticallyCheckForUpdates || !_updates.Configured) return;
        try { _pending = await CheckAsync(); TryNotify(); }
        catch (OperationCanceledException) when (_disposed) { }
    }
    private void TryNotify()
    {
        if (_disposed || _dialogOpen || !_owner.IsVisible || !_owner.IsActive || _owner.WindowState == WindowState.Minimized || _owner.OwnedWindows.Count != 0) return;
        if (_pending is not { } result || !ReleaseVersions.ShouldNotify(result, AppPreferencesViewModel.ApplicationVersion, _preferences.AutomaticallyCheckForUpdates, _sessionAcknowledged ?? _preferences.AcknowledgedRelease)) return;
        var release = result.Release!;
        Show(LaunchDialog.Automatic(release, _preferences, Open)); Acknowledge(release); _pending = null;
    }
    private void Acknowledge(ReleaseInfo release)
    {
        _sessionAcknowledged = release.Tag;
        if (!_preferences.AcknowledgeRelease(release.Tag)) _warning(_preferences.Error ?? "Could not save update preference.");
    }
    private void HandleAction(string action)
    {
        if (_disposed || _dialogOpen) return;
        switch (action)
        {
            case "updates": ManualCheck(); break;
            case "about": Show(LaunchDialog.About(Open)); break;
            case "issues":
                if (LaunchLinks.Current.Issues is { } issues) Open(issues);
                else { var dialog = new LaunchDialog("Report a Problem"); dialog.Text("The public GitHub repository has not been configured yet.", 18); dialog.Button(dialog.Actions(), "Close", dialog.Close); Show(dialog); }
                break;
        }
    }
    private void ManualCheck()
    {
        var dialog = new LaunchDialog("Check for Updates");
        using var closed = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var closedFlag = false; ReleaseInfo? acknowledged = null;
        dialog.Closed += (_, _) => { closedFlag = true; closed.Cancel(); };
        async Task Run()
        {
            RenderChecking(dialog);
            UpdateResult result;
            try { result = await CheckAsync().WaitAsync(closed.Token); }
            catch (OperationCanceledException) { return; }
            if (closedFlag || _disposed) return;
            RenderResult(dialog, result, () => _ = Run());
            if (result.State == UpdateState.Available) acknowledged = result.Release;
        }
        dialog.Loaded += (_, _) => _ = Run(); Show(dialog);
        if (acknowledged is not null) Acknowledge(acknowledged);
    }
    internal static void RenderChecking(LaunchDialog dialog)
    {
        dialog.Body.Children.Clear(); dialog.Text("Check for Updates", 25).FontWeight = FontWeights.SemiBold;
        dialog.Text("Checking for updates…", 16);
        var progress = new ProgressBar { IsIndeterminate = UiMotion.Enabled, Value = UiMotion.Enabled ? 0 : 35, Height = 4, Margin = new Thickness(0, 4, 0, 18) };
        progress.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Accent"); dialog.Body.Children.Add(progress);
        dialog.Button(dialog.Actions(), "Cancel", dialog.Close);
    }
    internal void RenderResult(LaunchDialog dialog, UpdateResult result, Action retry)
    {
        dialog.Body.Children.Clear();
        dialog.Text(result.State == UpdateState.Available ? "New update available" : result.State == UpdateState.Current ? "You're up to date!" : "Check for Updates", 25).FontWeight = FontWeights.SemiBold;
        if (result.State == UpdateState.Available && result.Release is { } release)
        {
            dialog.Versions(release); dialog.Text("What's New", 17).FontWeight = FontWeights.SemiBold;
            var notes = new TextBox { Text = string.IsNullOrWhiteSpace(release.Notes) ? "No release notes were provided." : release.Notes, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 14) };
            System.Windows.Automation.AutomationProperties.SetName(notes, "What's New"); dialog.Body.Children.Add(notes);
            dialog.Text("Download from GitHub, exit FullTilt, then replace the application files. Keep your Profiles folder.", muted: true);
        }
        else dialog.Text(result.Message, 14, true);
        if (result.State == UpdateState.Current) dialog.Text("Installed version  " + AppPreferencesViewModel.BuildVersion, muted: true);
        dialog.Preference(_preferences); var row = dialog.Actions(); dialog.Button(row, result.State == UpdateState.Available ? "Later" : "Close", dialog.Close);
        if (result.State == UpdateState.Available && result.Release is { } latest) dialog.Button(row, "Open Release Page", () => { Open(latest.Url); dialog.Close(); }, true);
        if (result.State == UpdateState.Failed) dialog.Button(row, "Retry", retry, true);
    }
    private void Show(LaunchDialog dialog)
    {
        if (_disposed || _dialogOpen) return;
        _dialogOpen = true; dialog.Owner = _owner;
        var root = (UIElement)_owner.Content; var layer = AdornerLayer.GetAdornerLayer(root);
        var dimmer = new DialogDimmer(root) { IsHitTestVisible = false };
        layer?.Add(dimmer);
        try { dialog.ShowDialog(); }
        finally { layer?.Remove(dimmer); _dialogOpen = false; }
    }
    private void Open(Uri uri)
    {
        if (uri.Scheme != "https") return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { _warning("Could not open the browser. " + ex.Message); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _timer.Stop(); _lifetime.Cancel();
        _owner.FileAction -= HandleAction; _owner.Activated -= OnActivated; _timer.Tick -= OnTick;
        _http.Dispose(); _lifetime.Dispose();
    }
}
