using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Threading;
using FullTilt.Core;
using FullTilt.Windows;

namespace FullTilt;

internal sealed class KeyboardRecorder : Window
{
    private readonly SettingsViewModel _model;
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
    private readonly TextBlock _keys = new() { FontSize = 23, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 20) };
    private readonly Button _use = new() { Content = "Use recording", IsEnabled = false };
    private readonly Button _again = new() { Content = "Record again", IsEnabled = false, Margin = new Thickness(8, 0, 8, 0) };
    private readonly DispatcherTimer _ready = new() { Interval = TimeSpan.FromMilliseconds(25) };
    private KeyboardCapture? _capture;
    private DateTime _started;
    private bool _closing, _closed, _sessionEnded;
    private readonly string _playbackHint;
    internal KeyboardChord? Recording { get; private set; }
    internal KeyboardChord? Result { get; private set; }
    internal KeyboardRecorder(SettingsViewModel model, string target, bool diagnostic = false, bool holdable = true)
    {
        _model = model; Title = "Record key / shortcut"; Width = 490; SizeToContent = SizeToContent.Height;
        _playbackHint = holdable ? "Keys stay down until the controller button is released." : "Pie segments tap the recorded keys when selected.";
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        var body = new StackPanel { Margin = new Thickness(24, 12, 24, 24) };
        body.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = target, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        body.Children.Add(_hint); body.Children.Add(_keys);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel" };
        foreach (var button in new[] { cancel, _again, _use }) { buttons.Children.Add(button); AutomationProperties.SetName(button, (string)button.Content); }
        _use.Background = ThemePalette.Get("#EAF9CA");
        _use.Click += (_, _) => CloseRecorder(accept: true);
        cancel.Click += (_, _) => CloseRecorder(); _again.Click += (_, _) => Begin();
        body.Children.Add(buttons); Content = body; ThemedChrome.Apply(this, false);
        _ready.Tick += (_, _) => StartCapture();
        if (!diagnostic) Loaded += (_, _) => Begin();
        _hint.Text = "Press a key or hold a shortcut, then release all keys. " + _playbackHint;
        _keys.Text = "Waiting for keys…";
    }
    private void Begin()
    {
        if (_closing || _closed) return;
        StopCapture(); Recording = null; _use.IsEnabled = _again.IsEnabled = false; _keys.Text = "Preparing…";
        _model.BeginKeyboardRecording(); _started = DateTime.UtcNow; _ready.Start();
    }
    private void StartCapture()
    {
        if (_closing || _closed) return;
        if (!_model.KeyboardRecordingReady)
        {
            if ((DateTime.UtcNow - _started).TotalSeconds > 5) { _ready.Stop(); _keys.Text = "Could not prepare recording. Try again."; _again.IsEnabled = true; }
            return;
        }
        _ready.Stop();
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            _capture = new KeyboardCapture(() => IsActive && NativeMethods.GetForegroundWindow() == handle, Changed);
        }
        catch (Exception ex) { _keys.Text = ex.Message; _again.IsEnabled = true; }
    }
    internal void Changed(KeyboardRecording state)
    {
        if (_closing || _closed) return;
        if (state.Complete)
        {
            Recording = state.Result; _keys.Text = Recording?.Label ?? "This shortcut cannot be recorded.";
            _hint.Text = _playbackHint + " Use recording adds it to your draft; Save changes applies it.";
            _use.IsEnabled = Recording is not null; _again.IsEnabled = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(StopCapture));
        }
        else _keys.Text = state.WaitingForRelease ? "Release held keys to start…" : state.Preview.Length > 0 ? state.Preview : "Press keys, then release…";
    }
    private void StopCapture() { _capture?.Dispose(); _capture = null; }
    private void CloseRecorder(bool accept = false)
    {
        if (_closing || _closed) return;
        Result = accept ? Recording : null;
        Close();
    }
    // WPF deactivates a modal window during Close. That notification must not close it again
    // or clear a recording already accepted by Use recording.
    internal void CancelOnDeactivation() => CloseRecorder();
    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        CancelOnDeactivation();
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        _closing = true;
        base.OnClosing(e);
        if (e.Cancel) { _closing = false; return; }
        EndSession();
    }
    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        EndSession();
        base.OnClosed(e);
    }
    private void EndSession()
    {
        if (_sessionEnded) return;
        _sessionEnded = true;
        _ready.Stop(); StopCapture(); _model.EndKeyboardRecording();
    }
    internal static KeyboardChord? Ask(SettingsViewModel model, Window? owner, string target, bool holdable = true)
    {
        if (ThemedDialog.RejectDiagnosticPrompts) throw new InvalidOperationException("Unexpected keyboard recorder prompt.");
        var dialog = new KeyboardRecorder(model, target, holdable: holdable); if (owner?.IsVisible == true) dialog.Owner = owner;
        dialog.ShowDialog(); return dialog.Result;
    }
}
