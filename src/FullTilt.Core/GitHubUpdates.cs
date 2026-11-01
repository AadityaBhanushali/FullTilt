using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FullTilt.Core;

public sealed record ReleaseInfo(string Tag, Version Version, string Notes, Uri Url);
public enum UpdateState { Available, Current, NoRelease, Failed, NotConfigured }
public sealed record UpdateResult(UpdateState State, string Message, ReleaseInfo? Release = null);

public static class ReleaseVersions
{
    public static bool TryParse(string? tag, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        var text = tag?.Trim();
        if (string.IsNullOrEmpty(text)) return false;
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
        var parts = text.Split('.');
        if (parts.Length is < 2 or > 4 || parts.Any(p => p.Length == 0 || p.Any(c => c is < '0' or > '9'))) return false;
        if (!Version.TryParse(text, out var parsed)) return false;
        version = Normalize(parsed); return true;
    }
    public static Version Normalize(Version value) => new(value.Major, value.Minor, Math.Max(0, value.Build), Math.Max(0, value.Revision));
    public static bool ShouldNotify(UpdateResult result, Version current, bool enabled, string? acknowledged) =>
        enabled && result.State == UpdateState.Available && result.Release is { } release && release.Version > Normalize(current) &&
        (!TryParse(acknowledged, out var previous) || release.Version > previous);
}

// Inject HttpClient in tests. No credentials, controller dependencies, downloads or installation.
public sealed class GitHubUpdates(HttpClient client, string owner, string repository, Version current)
{
    public bool Configured => ValidPart(owner) && ValidPart(repository);
    private static bool ValidPart(string text) => !string.IsNullOrWhiteSpace(text) && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
    public async Task<UpdateResult> CheckAsync(CancellationToken cancellation = default)
    {
        if (!Configured) return new(UpdateState.NotConfigured, "The GitHub repository has not been configured yet.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{owner}/{repository}/releases/latest");
            request.Headers.UserAgent.ParseAdd("FullTilt/" + current.ToString(3));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return new(UpdateState.NoRelease, "No public stable release is available yet. The repository may also be unavailable.");
            if ((int)response.StatusCode is 403 or 429) return new(UpdateState.Failed, "GitHub is limiting update checks. Please try again later.");
            if (!response.IsSuccessStatusCode) return new(UpdateState.Failed, "GitHub could not complete the update check. Please try again later.");
            // Bound the response even if the server omits Content-Length.
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var bytes = new MemoryStream(); var buffer = new byte[8192]; int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (bytes.Length + count > 1_048_576) return new(UpdateState.Failed, "GitHub returned an unexpected release response.");
                bytes.Write(buffer, 0, count);
            }
            using var json = JsonDocument.Parse(bytes.ToArray()); var root = json.RootElement;
            if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return new(UpdateState.NoRelease, "No public stable release is available yet.");
            var tag = root.GetProperty("tag_name").GetString();
            if (!ReleaseVersions.TryParse(tag, out var version)) return new(UpdateState.Failed, "The release has an unsupported version tag.");
            var urlText = root.GetProperty("html_url").GetString();
            if (!Uri.TryCreate(urlText, UriKind.Absolute, out var url) || url.Scheme != "https" || url.Host != "github.com" ||
                !string.IsNullOrEmpty(url.UserInfo) || !url.IsDefaultPort || !url.AbsolutePath.StartsWith($"/{owner}/{repository}/releases/tag/", StringComparison.OrdinalIgnoreCase))
                return new(UpdateState.Failed, "GitHub returned an unexpected release link.");
            var notes = root.TryGetProperty("body", out var body) && body.ValueKind != JsonValueKind.Null ? body.GetString() ?? "" : "";
            var release = new ReleaseInfo(tag!, version, notes, url);
            return new(version > ReleaseVersions.Normalize(current) ? UpdateState.Available : UpdateState.Current,
                "You're up to date! You're running the latest version of FullTilt.", release);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return new(UpdateState.Failed, "The update check timed out. Check your connection and try again."); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { return new(UpdateState.Failed, "Could not check for updates. Check your connection and try again."); }
    }
}
