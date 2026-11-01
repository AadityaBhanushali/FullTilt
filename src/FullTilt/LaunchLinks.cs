using System;
using System.Text.Json;
using System.Windows;

namespace FullTilt;

internal sealed record LaunchLinks(string GitHubOwner, string GitHubRepository, string LinkedIn, string Behance)
{
    public static LaunchLinks Current { get; } = Load();
    private static LaunchLinks Load()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/LaunchLinks.json")).Stream;
        return JsonSerializer.Deserialize<LaunchLinks>(stream) ?? throw new InvalidOperationException("Missing launch links.");
    }
    public Uri? Issues => string.IsNullOrWhiteSpace(GitHubOwner) || string.IsNullOrWhiteSpace(GitHubRepository) ? null : new Uri($"https://github.com/{Uri.EscapeDataString(GitHubOwner)}/{Uri.EscapeDataString(GitHubRepository)}/issues");
}
