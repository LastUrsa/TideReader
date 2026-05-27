using System.Net.Http.Json;
using System.Reflection;
using TideReader.Backend.Models;

namespace TideReader.Backend.Services;

public sealed class AppUpdateChecker(HttpClient httpClient) : IAppUpdateChecker
{
    private const string LatestReleaseEndpoint = "https://api.github.com/repos/LastUrsa/TideReader/releases/latest";
    private const string ReleasesPageUrl = "https://github.com/LastUrsa/TideReader/releases";

    public string CurrentVersion { get; } = ResolveCurrentVersion();
    public string ReleaseUrl => ReleasesPageUrl;

    public async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseEndpoint);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd($"TideReader/{CurrentVersion}");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Update check failed: GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var release = await response.Content.ReadFromJsonAsync<GitHubRelease>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Update check failed: GitHub returned an empty response.");

        var latestVersion = NormalizeVersion(release.TagName);
        if (string.IsNullOrWhiteSpace(latestVersion))
        {
            latestVersion = CurrentVersion;
        }

        var updateAvailable = CompareVersions(latestVersion, CurrentVersion) > 0;
        return new UpdateInfo
        {
            CurrentVersion = CurrentVersion,
            LatestVersion = latestVersion,
            UpdateAvailable = updateAvailable,
            ReleaseUrl = ReleaseUrl,
            Message = updateAvailable
                ? $"Version {latestVersion} is available."
                : "You're running the latest version."
        };
    }

    internal static string NormalizeVersion(string? version)
    {
        var trimmed = (version ?? "").Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[1..];
        }

        var plusIndex = trimmed.IndexOf('+');
        if (plusIndex >= 0)
        {
            trimmed = trimmed[..plusIndex];
        }

        var dashIndex = trimmed.IndexOf('-');
        if (dashIndex >= 0)
        {
            trimmed = trimmed[..dashIndex];
        }

        return trimmed;
    }

    internal static int CompareVersions(string left, string right)
    {
        var leftParts = NormalizeVersion(left).Split('.', StringSplitOptions.RemoveEmptyEntries);
        var rightParts = NormalizeVersion(right).Split('.', StringSplitOptions.RemoveEmptyEntries);
        var maxLength = Math.Max(leftParts.Length, rightParts.Length);

        for (var index = 0; index < maxLength; index++)
        {
            var leftValue = VersionPart(leftParts, index);
            var rightValue = VersionPart(rightParts, index);
            if (leftValue > rightValue)
            {
                return 1;
            }

            if (leftValue < rightValue)
            {
                return -1;
            }
        }

        return 0;
    }

    private static string ResolveCurrentVersion()
    {
        var assembly = typeof(AppUpdateChecker).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var normalizedInformationalVersion = NormalizeVersion(informationalVersion);
        if (!string.IsNullOrWhiteSpace(normalizedInformationalVersion))
        {
            return normalizedInformationalVersion;
        }

        var version = assembly.GetName().Version?.ToString(3);
        return string.IsNullOrWhiteSpace(version) ? "0.0.0" : NormalizeVersion(version);
    }

    private static int VersionPart(string[] parts, int index)
    {
        if (index >= parts.Length)
        {
            return 0;
        }

        return int.TryParse(parts[index], out var value) ? value : 0;
    }

    private sealed class GitHubRelease
    {
        public string TagName { get; set; } = "";
    }
}
