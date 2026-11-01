using System.Diagnostics;
using System.Text.Json;

namespace FullTilt.Windows;

// Same executable, same user/token, no emulation. A short-lived helper bounds potentially hung COM calls.
// On cancellation only our own helper is terminated, never TabTip/TextInputHost/Windows shell processes.
public sealed class TouchKeyboardProcessBackend : ITouchKeyboardBackend
{
    public const int HelperTimeoutMilliseconds = 3000;
    public async Task<bool> ObserveAsync(CancellationToken cancellation) => (await RunAsync("observe", cancellation).ConfigureAwait(false)).Visible;
    public async Task EnsureStartedAsync(CancellationToken cancellation) => await RunAsync("ensure", cancellation).ConfigureAwait(false);
    public async Task ToggleAsync(CancellationToken cancellation) => await RunAsync("toggle", cancellation).ConfigureAwait(false);
    private static async Task<TouchKeyboardReply> RunAsync(string command, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("FullTilt executable path is unavailable."))
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true
        };
        info.ArgumentList.Add("--touch-keyboard-helper"); info.ArgumentList.Add(command);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Keyboard helper could not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(HelperTimeoutMilliseconds);
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var text = await output.ConfigureAwait(false);
            if (process.ExitCode == 124) throw new TimeoutException("Windows touch-keyboard component stopped responding.");
            if (text.Length > 2048) throw new InvalidOperationException("Unexpected keyboard helper response.");
            var reply = JsonSerializer.Deserialize<TouchKeyboardReply>(text) ?? throw new InvalidOperationException("Keyboard helper returned no status.");
            if (process.ExitCode != 0 || reply.Error is not null) throw new InvalidOperationException(reply.Error ?? "Keyboard helper failed.");
            return reply;
        }
        catch (OperationCanceledException)
        {
            // Kill only the Process object created above, not its child Windows keyboard process.
            if (!process.HasExited) process.Kill(entireProcessTree: false);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            throw new TimeoutException("Windows touch-keyboard component stopped responding.");
        }
        catch (JsonException) { throw new InvalidOperationException("Keyboard helper returned an invalid status."); }
    }
}
