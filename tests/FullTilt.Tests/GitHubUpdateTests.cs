using System.Net;
using System.Text;
using System.Text.Json;
using FullTilt.Core;

static class GitHubUpdateTests
{
    public static void Register(TestSuite suite)
    {
        suite.Add("GitHub public initial release compares normally and development users require manual install", () =>
        {
            var initial = Run(() => Reply("v1.0.0"), new Version(1, 0, 0));
            Check.Equal(UpdateState.Current, initial.State); Check.True(!ReleaseVersions.ShouldNotify(initial, new(1, 0, 0), true, null));
            Check.Equal(UpdateState.Available, Run(() => Reply("v1.1.0"), new Version(1, 0, 0)).State);
            Check.Equal(UpdateState.Current, Run(() => Reply("v1.0.0"), new Version(1, 4, 8)).State);
            Check.Equal(UpdateState.Current, Run(() => Reply("v1.0.0"), new Version(4, 8, 0)).State);
        });
        suite.Add("GitHub numeric versions support v tags, padding and multi-digit comparison", () =>
        {
            Check.True(ReleaseVersions.TryParse("v4.8.0", out var first)); Check.True(ReleaseVersions.TryParse("4.9.0", out var next)); Check.True(next > first);
            Check.True(ReleaseVersions.TryParse("4.10.0", out var ten)); Check.True(ten > next);
            Check.True(ReleaseVersions.TryParse("4.8", out var shortTag)); Check.Equal(first, shortTag);
            foreach (var tag in new[] { "", "abc", "v4.8.0-rc1", "4.8.0+abc", "4..8", "4.8.0.0.1", "-1.4", "4" }) Check.True(!ReleaseVersions.TryParse(tag, out _));
        });
        suite.Add("GitHub latest stable uses anonymous REST headers and official release URL", () =>
        {
            using var handler = new Fake(request =>
            {
                Check.Equal("https://api.github.com/repos/creator/FullTilt/releases/latest", request.RequestUri!.AbsoluteUri);
                Check.True(request.Headers.UserAgent.ToString().Contains("FullTilt/1.4.8")); Check.True(request.Headers.Authorization is null);
                Check.True(request.Headers.Contains("X-GitHub-Api-Version")); return Reply("v1.4.9");
            });
            using var http = new HttpClient(handler); var result = new GitHubUpdates(http, "creator", "FullTilt", new(1, 4, 8)).CheckAsync().GetAwaiter().GetResult();
            Check.Equal(UpdateState.Available, result.State); Check.Equal("Changes\n- Fix", result.Release!.Notes); Check.Equal("v1.4.9", result.Release.Tag);
            Check.True(ReleaseVersions.ShouldNotify(result, new(1, 4, 8), true, null));
            Check.True(!ReleaseVersions.ShouldNotify(result, new(1, 4, 8), false, null));
            Check.True(!ReleaseVersions.ShouldNotify(result, new(1, 4, 8), true, "1.4.9.0"));
            Check.True(!ReleaseVersions.ShouldNotify(result, new(1, 4, 8), true, "v1.4.10"));
            Check.True(ReleaseVersions.ShouldNotify(result, new(1, 4, 8), true, "v1.4.8"));
        });
        suite.Add("GitHub old and equal releases are current; drafts and prereleases are ignored", () =>
        {
            foreach (var tag in new[] { "1.4.7", "v1.4.8", "1.4.8.0" }) Check.Equal(UpdateState.Current, Run(() => Reply(tag)).State);
            Check.Equal(UpdateState.NoRelease, Run(() => Reply("v1.4.9", draft: true)).State);
            Check.Equal(UpdateState.NoRelease, Run(() => Reply("v1.4.9", prerelease: true)).State);
        });
        suite.Add("GitHub failures and missing releases never report up to date", () =>
        {
            foreach (var code in new[] { 403, 429, 500, 301 }) Check.Equal(UpdateState.Failed, Run(() => new((HttpStatusCode)code)).State);
            Check.Equal(UpdateState.NoRelease, Run(() => new(HttpStatusCode.NotFound)).State);
            foreach (var json in new[] { "{", "null", "[]", "{}", "{\"draft\":\"no\"}", new string('x', 1_048_577) })
                Check.Equal(UpdateState.Failed, Run(() => new(HttpStatusCode.OK) { Content = new StringContent(json) }).State);
            Check.Equal(UpdateState.Failed, Run(() => throw new HttpRequestException("offline")).State);
            Check.Equal(UpdateState.Failed, Run(() => Reply("v1.4.9-rc1")).State);
            foreach (var url in new[] { "https://example.com/creator/FullTilt/releases/tag/v1.4.9", "http://github.com/creator/FullTilt/releases/tag/v1.4.9", "https://github.com/other/FullTilt/releases/tag/v1.4.9", "https://github.com/creator/FullTilt/issues/1" })
                Check.Equal(UpdateState.Failed, Run(() => Reply("v1.4.9", url: url)).State);
        });
        suite.Add("GitHub unset repository makes no requests and lifecycle cancellation propagates", () =>
        {
            using var handler = new Fake(_ => throw new InvalidOperationException("No request expected")); using var http = new HttpClient(handler);
            Check.Equal(UpdateState.NotConfigured, new GitHubUpdates(http, "", "", new(1, 4, 8)).CheckAsync().GetAwaiter().GetResult().State);
            using var canceled = new CancellationTokenSource(); canceled.Cancel(); var observed = false;
            try { new GitHubUpdates(http, "creator", "FullTilt", new(1, 4, 8)).CheckAsync(canceled.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { observed = true; }
            Check.True(observed);
        });
        suite.Add("GitHub preferences persist independently and preserve profiles and unknown preferences", () =>
        {
            using var dir = new TestDirectory(); var store = new AppPreferencesStore(dir.Path);
            File.WriteAllText(store.FilePath, "{\"Version\":1,\"Theme\":\"Light\",\"FutureFlag\":42}");
            var loaded = store.Load(); Check.True(loaded.AutomaticallyCheckForUpdates); Check.True(loaded.AcknowledgedRelease is null);
            var profiles = new SettingsStore(dir.Path); var saveError = profiles.Save(ProfileLibrary.Create(new(), "Existing"));
            if (saveError is not null) throw new InvalidOperationException(saveError);
            var bytes = File.ReadAllBytes(profiles.FilePath);
            Check.True(store.Save(loaded with { AutomaticallyCheckForUpdates = false, AcknowledgedRelease = "v1.4.9" }) is null);
            var reopened = new AppPreferencesStore(dir.Path).Load(); Check.True(!reopened.AutomaticallyCheckForUpdates); Check.Equal("v1.4.9", reopened.AcknowledgedRelease!);
            Check.Equal("Light", reopened.Theme); Check.Equal(42, reopened.Additional!["FutureFlag"].GetInt32()); Check.True(bytes.SequenceEqual(File.ReadAllBytes(profiles.FilePath)));
        });
        suite.Add("GitHub malformed optional preferences retain unrelated data after exact backup", () =>
        {
            using var dir = new TestDirectory(); var store = new AppPreferencesStore(dir.Path);
            var original = "{\"Version\":1,\"Theme\":\"Light\",\"AutomaticallyCheckForUpdates\":\"invalid\",\"AcknowledgedRelease\":{},\"FutureOption\":42}";
            File.WriteAllText(store.FilePath, original); var loaded = store.Load();
            Check.Equal("Light", loaded.Theme); Check.True(loaded.AutomaticallyCheckForUpdates); Check.True(loaded.AcknowledgedRelease is null);
            Check.Equal(42, loaded.Additional!["FutureOption"].GetInt32()); Check.Equal(original, File.ReadAllText(Directory.GetFiles(dir.Path, "*.recovered-*.bak").Single()));
            Check.True(store.Save(loaded) is null); Check.Equal("Light", new AppPreferencesStore(dir.Path).Load().Theme);
        });
    }
    private static UpdateResult Run(Func<HttpResponseMessage> reply, Version? installed = null)
    {
        using var handler = new Fake(_ => reply()); using var http = new HttpClient(handler);
        return new GitHubUpdates(http, "creator", "FullTilt", installed ?? new(1, 4, 8)).CheckAsync().GetAwaiter().GetResult();
    }
    private static HttpResponseMessage Reply(string tag, bool draft = false, bool prerelease = false, string? url = null) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { tag_name = tag, draft, prerelease, body = "Changes\n- Fix", html_url = url ?? "https://github.com/creator/FullTilt/releases/tag/" + tag }), Encoding.UTF8, "application/json")
    };
    private sealed class Fake(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(reply(request)); }
    }
}
