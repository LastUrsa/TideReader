using TideReader.Backend.Models;

namespace TideReader.Backend.Services;

public interface IPlaybackProvider
{
    string Name { get; }
    IReadOnlyList<PlaybackCandidate> GetCandidates(IReadOnlyList<MediaSessionSnapshot> sessions, Settings settings);
}

public sealed record PlaybackCandidate(
    DetectionResult Result,
    BrowserSessionDebugInfo Debug,
    int Priority,
    bool IsPlaying,
    bool IsPaused,
    bool IsStale,
    DateTimeOffset LastUpdatedUtc);

public sealed class MediaSessionDetector(
    IMediaSessionSnapshotProvider snapshotProvider,
    IEnumerable<IPlaybackProvider> providers) : IPlaybackDetector
{
    private readonly IReadOnlyList<IPlaybackProvider> _providers = providers.ToArray();

    public async Task<PlaybackDetectionOutcome> DetectAsync(DetectionResult previous, Settings settings, CancellationToken cancellationToken)
    {
        var sessions = await snapshotProvider.GetCurrentAsync(cancellationToken);
        var candidates = _providers
            .SelectMany(provider => provider.GetCandidates(sessions, settings))
            .ToList();

        var result = SelectCandidate(candidates, previous, settings, out var debugState);
        return new PlaybackDetectionOutcome(result, debugState);
    }

    private static DetectionResult? SelectCandidate(
        List<PlaybackCandidate> candidates,
        DetectionResult previous,
        Settings settings,
        out BrowserDebugState debugState)
    {
        var browserSettings = settings.BrowserSettings ?? new BrowserSettings();
        var eligible = candidates
            .Where(candidate => IsAllowedByMode(candidate, browserSettings.ActiveSourceMode))
            .Where(candidate => !(browserSettings.IgnorePausedSessions && candidate.IsPaused))
            .Where(candidate => !(browserSettings.IgnoreStaleSessions && candidate.IsStale))
            .ToList();

        var playing = eligible.Where(candidate => candidate.IsPlaying).ToList();
        var pool = playing.Count > 0 ? playing : eligible;
        var ordered = pool
            .OrderBy(candidate => candidate.Priority)
            .ThenByDescending(candidate => candidate.LastUpdatedUtc)
            .ThenByDescending(candidate => candidate.Result.Confidence)
            .ThenByDescending(candidate => MatchesPrevious(candidate, previous))
            .ToList();

        var selected = ordered.FirstOrDefault();
        if (selected is not null && CooldownApplies(previous, selected.Result, browserSettings, candidates))
        {
            var sticky = candidates.FirstOrDefault(candidate => SameSource(candidate.Result, previous) && candidate.IsPlaying);
            if (sticky is not null)
            {
                selected = new PlaybackCandidate(
                    WithSelectionReason(sticky.Result, "cooldown active"),
                    CloneDebugInfo(sticky.Debug, true, "selected: cooldown active"),
                    sticky.Priority,
                    sticky.IsPlaying,
                    sticky.IsPaused,
                    sticky.IsStale,
                    sticky.LastUpdatedUtc);
            }
        }

        debugState = new BrowserDebugState
        {
            Sessions = candidates
                .Select(candidate =>
                {
                    var isSelected = selected is not null && candidate.Debug.SessionId == selected.Debug.SessionId;
                    var reason = isSelected
                        ? selected?.Result.SelectionReason ?? "selected: highest priority active source"
                        : BuildIgnoredReason(candidate, browserSettings, selected);
                    return CloneDebugInfo(candidate.Debug, isSelected, reason);
                })
                .OrderByDescending(session => session.IsSelected)
                .ThenBy(session => session.Provider)
                .ThenBy(session => session.Browser)
                .ThenBy(session => session.Site)
                .ToList()
        };

        return selected is null ? null : WithSelectionReason(selected.Result, selected.Result.SelectionReason);
    }

    private static DetectionResult WithSelectionReason(DetectionResult result, string reason)
    {
        var copy = BridgeStatePolicy.CloneDetection(result);
        copy.SelectionReason = string.IsNullOrWhiteSpace(reason) ? "selected: highest priority active source" : $"selected: {reason}";
        return copy;
    }

    private static BrowserSessionDebugInfo CloneDebugInfo(BrowserSessionDebugInfo debug, bool isSelected, string reason) => new()
    {
        Provider = debug.Provider,
        Browser = debug.Browser,
        Site = debug.Site,
        PlaybackState = debug.PlaybackState,
        SourceAppId = debug.SourceAppId,
        RawTitle = debug.RawTitle,
        RawArtist = debug.RawArtist,
        RawAlbum = debug.RawAlbum,
        ParsedTitle = debug.ParsedTitle,
        ParsedArtist = debug.ParsedArtist,
        ParsedAlbum = debug.ParsedAlbum,
        Confidence = debug.Confidence,
        HasArtwork = debug.HasArtwork,
        IsSelected = isSelected,
        DecisionReason = reason,
        SessionId = debug.SessionId,
        LastUpdatedUtc = debug.LastUpdatedUtc
    };

    private static string BuildIgnoredReason(PlaybackCandidate candidate, BrowserSettings settings, PlaybackCandidate? selected)
    {
        if (settings.IgnorePausedSessions && candidate.IsPaused)
        {
            return "ignored: paused";
        }

        if (settings.IgnoreStaleSessions && candidate.IsStale)
        {
            return "ignored: stale session";
        }

        if (selected is not null && !SameSource(candidate.Result, selected.Result))
        {
            return $"ignored: lower priority than {selected.Result.Source}";
        }

        return "ignored: not selected";
    }

    private static bool IsAllowedByMode(PlaybackCandidate candidate, string activeSourceMode)
    {
        return activeSourceMode.ToLowerInvariant() switch
        {
            "tidal" => candidate.Result.Provider == "tidal",
            "browser" => candidate.Result.Provider == "browser",
            _ => true
        };
    }

    private static bool CooldownApplies(DetectionResult previous, DetectionResult selected, BrowserSettings settings, List<PlaybackCandidate> candidates)
    {
        if (ShouldPreferTidalImmediately(previous, selected, settings))
        {
            return false;
        }

        var cooldownMs = settings.SourceSwitchCooldownMs;
        if (cooldownMs <= 0 || previous.Status != "playing" || SameSource(previous, selected))
        {
            return false;
        }

        var previousCandidate = candidates.FirstOrDefault(candidate => SameSource(candidate.Result, previous));
        if (previousCandidate is null || !previousCandidate.IsPlaying)
        {
            return false;
        }

        return (DateTimeOffset.UtcNow - previousCandidate.LastUpdatedUtc).TotalMilliseconds < cooldownMs;
    }

    private static bool ShouldPreferTidalImmediately(DetectionResult previous, DetectionResult selected, BrowserSettings settings) =>
        settings.PreferTidalOverBrowser &&
        string.Equals(previous.Provider, "browser", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(selected.Provider, "tidal", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesPrevious(PlaybackCandidate candidate, DetectionResult previous) => SameSource(candidate.Result, previous);

    private static bool SameSource(DetectionResult left, DetectionResult right) =>
        string.Equals(left.Provider, right.Provider, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Browser, right.Browser, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Site, right.Site, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Title, right.Title, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Artist, right.Artist, StringComparison.OrdinalIgnoreCase);
}

public sealed class TidalPlaybackProvider : IPlaybackProvider
{
    public string Name => "tidal";

    public IReadOnlyList<PlaybackCandidate> GetCandidates(IReadOnlyList<MediaSessionSnapshot> sessions, Settings settings)
    {
        return sessions
            .Where(static snapshot =>
                snapshot.SourceAppId.Contains("TIDAL", StringComparison.OrdinalIgnoreCase) ||
                snapshot.SourceAppId.Contains("Aspiro", StringComparison.OrdinalIgnoreCase))
            .Select(snapshot => CreateCandidate(snapshot, settings))
            .ToList();
    }

    private static PlaybackCandidate CreateCandidate(MediaSessionSnapshot snapshot, Settings settings)
    {
        var browserSettings = settings.BrowserSettings ?? new BrowserSettings();
        var result = new DetectionResult
        {
            Status = snapshot.IsPaused ? "paused" : "playing",
            Title = snapshot.Title,
            Artist = snapshot.Artist,
            Album = snapshot.Album,
            DurationMs = snapshot.DurationMs,
            ArtworkBytes = snapshot.ArtworkBytes.ToArray(),
            ArtworkPath = snapshot.ArtworkBytes.Length > 0 ? "cover.jpg" : "",
            Source = "TIDAL",
            Method = "media_session",
            Confidence = Score(snapshot.Title, snapshot.Artist, snapshot.Album, snapshot.SourceAppId, snapshot.IsPaused),
            DetectedText = $"{snapshot.Artist} - {snapshot.Title}".Trim(' ', '-'),
            SourceAppId = snapshot.SourceAppId,
            MatcherReason = "windows_media_session",
            Provider = "tidal",
            RawTitle = snapshot.Title,
            RawArtist = snapshot.Artist,
            RawAlbum = snapshot.Album,
            SelectionReason = browserSettings.PreferTidalOverBrowser ? "highest priority active source" : "active source"
        };

        return new PlaybackCandidate(
            result,
            new BrowserSessionDebugInfo
            {
                Provider = "tidal",
                PlaybackState = result.Status,
                SourceAppId = snapshot.SourceAppId,
                RawTitle = snapshot.Title,
                RawArtist = snapshot.Artist,
                RawAlbum = snapshot.Album,
                ParsedTitle = result.Title,
                ParsedArtist = result.Artist,
                ParsedAlbum = result.Album,
                Confidence = result.Confidence,
                HasArtwork = result.ArtworkBytes.Length > 0,
                SessionId = snapshot.SessionId,
                LastUpdatedUtc = snapshot.LastUpdatedUtc
            },
            Priority: ResolvePriority("tidal", browserSettings),
            IsPlaying: !snapshot.IsPaused,
            IsPaused: snapshot.IsPaused,
            IsStale: IsStale(snapshot, browserSettings),
            LastUpdatedUtc: snapshot.LastUpdatedUtc);
    }

    private static double Score(string? title, string? artist, string? album, string? sourceAppId, bool isPaused)
    {
        var score = 0.72;
        if (!string.IsNullOrWhiteSpace(title)) score += 0.12;
        if (!string.IsNullOrWhiteSpace(artist)) score += 0.08;
        if (!string.IsNullOrWhiteSpace(album)) score += 0.04;
        if ((sourceAppId ?? "").Contains("TIDAL", StringComparison.OrdinalIgnoreCase) || (sourceAppId ?? "").Contains("Aspiro", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.02;
        }
        if (isPaused)
        {
            score -= 0.01;
        }
        return Math.Min(score, 0.99);
    }

    private static bool IsStale(MediaSessionSnapshot snapshot, BrowserSettings settings) =>
        (DateTimeOffset.UtcNow - snapshot.LastUpdatedUtc).TotalSeconds > settings.StaleSessionAfterSeconds;

    private static int ResolvePriority(string site, BrowserSettings settings)
    {
        if (settings.PreferTidalOverBrowser)
        {
            return int.MinValue;
        }

        var priorityKey = site == "generic" ? "genericBrowser" : site;
        var priorities = settings.SourcePriority ?? [];
        for (var index = 0; index < priorities.Count; index++)
        {
            if (string.Equals(priorities[index], priorityKey, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return int.MaxValue;
    }
}

public sealed class BrowserMediaProvider : IPlaybackProvider
{
    public string Name => "browser";

    public IReadOnlyList<PlaybackCandidate> GetCandidates(IReadOnlyList<MediaSessionSnapshot> sessions, Settings settings)
    {
        var browserSettings = settings.BrowserSettings ?? new BrowserSettings();
        if (!browserSettings.Enabled)
        {
            return [];
        }

        return sessions
            .Where(snapshot => IsSupportedBrowser(snapshot.Browser, browserSettings.SupportedBrowsers))
            .Select(snapshot => CreateCandidate(snapshot, browserSettings))
            .Where(candidate => candidate.Result.Site != "generic" || browserSettings.AllowGenericPlayback)
            .ToList();
    }

    private static PlaybackCandidate CreateCandidate(MediaSessionSnapshot snapshot, BrowserSettings settings)
    {
        var normalized = BrowserMetadataParser.Normalize(snapshot, settings.MetadataCleanupEnabled);
        var artworkBytes = ResolveBrowserArtwork(snapshot, normalized, settings);
        var sourceLabel = GetSourceLabel(normalized.Site);
        var result = new DetectionResult
        {
            Status = snapshot.IsPaused ? "paused" : "playing",
            Title = normalized.Title,
            Artist = normalized.Artist,
            Album = normalized.Album,
            DurationMs = snapshot.DurationMs,
            ArtworkBytes = artworkBytes,
            ArtworkPath = artworkBytes.Length > 0 ? "cover.jpg" : "",
            Source = sourceLabel,
            Method = "media_session",
            Confidence = normalized.Confidence,
            DetectedText = $"{normalized.Artist} - {normalized.Title}".Trim(' ', '-'),
            SourceAppId = snapshot.SourceAppId,
            MatcherReason = "windows_media_session",
            Provider = "browser",
            Browser = snapshot.Browser,
            Site = normalized.Site,
            RawTitle = snapshot.Title,
            RawArtist = snapshot.Artist,
            RawAlbum = snapshot.Album,
            SelectionReason = "highest priority active source"
        };

        return new PlaybackCandidate(
            result,
            new BrowserSessionDebugInfo
            {
                Provider = "browser",
                Browser = snapshot.Browser,
                Site = normalized.Site,
                PlaybackState = result.Status,
                SourceAppId = snapshot.SourceAppId,
                RawTitle = snapshot.Title,
                RawArtist = snapshot.Artist,
                RawAlbum = snapshot.Album,
                ParsedTitle = result.Title,
                ParsedArtist = result.Artist,
                ParsedAlbum = result.Album,
                Confidence = result.Confidence,
                HasArtwork = result.ArtworkBytes.Length > 0,
                SessionId = snapshot.SessionId,
                LastUpdatedUtc = snapshot.LastUpdatedUtc
            },
            Priority: ResolvePriority(normalized.Site, settings),
            IsPlaying: !snapshot.IsPaused,
            IsPaused: snapshot.IsPaused,
            IsStale: IsStale(snapshot, settings),
            LastUpdatedUtc: snapshot.LastUpdatedUtc);
    }

    private static bool IsSupportedBrowser(string browser, BrowserSupportSettings supportedBrowsers)
    {
        return browser.ToLowerInvariant() switch
        {
            "chrome" => supportedBrowsers.ChromeEnabled,
            "edge" => supportedBrowsers.EdgeEnabled,
            "firefox" => supportedBrowsers.FirefoxEnabled,
            "brave" => supportedBrowsers.BraveEnabled,
            "opera" => supportedBrowsers.OperaEnabled,
            _ => false
        };
    }

    private static int ResolvePriority(string site, BrowserSettings settings)
    {
        var priorities = settings.SourcePriority ?? [];
        for (var index = 0; index < priorities.Count; index++)
        {
            if (string.Equals(priorities[index], site, StringComparison.OrdinalIgnoreCase))
            {
                return site == "tidal" && !settings.PreferTidalOverBrowser ? index + 50 : index;
            }
        }

        return int.MaxValue;
    }

    private static bool IsStale(MediaSessionSnapshot snapshot, BrowserSettings settings) =>
        (DateTimeOffset.UtcNow - snapshot.LastUpdatedUtc).TotalSeconds > settings.StaleSessionAfterSeconds;

    private static string GetSourceLabel(string site) => site switch
    {
        "youtubeMusic" => "YouTube Music",
        "bandcamp" => "Bandcamp",
        "soundcloud" => "SoundCloud",
        "youtube" => "YouTube",
        _ => "Browser"
    };

    private static byte[] ResolveBrowserArtwork(MediaSessionSnapshot snapshot, BrowserMetadataNormalization normalized, BrowserSettings settings)
    {
        if (!settings.BrowserArtworkEnabled || snapshot.ArtworkBytes.Length == 0)
        {
            return [];
        }

        if (normalized.Site == "generic" && string.IsNullOrWhiteSpace(normalized.Album) && !settings.YouTubeVideoImageFallbackEnabled)
        {
            return [];
        }

        return snapshot.ArtworkBytes.ToArray();
    }
}

internal sealed record BrowserMetadataNormalization(string Title, string Artist, string Album, string Site, double Confidence);

internal static class BrowserMetadataParser
{
    public static BrowserMetadataNormalization Normalize(MediaSessionSnapshot snapshot, bool cleanupEnabled)
    {
        var rawTitle = snapshot.Title.Trim();
        var rawArtist = snapshot.Artist.Trim();
        var rawAlbum = snapshot.Album.Trim();
        var site = DetectSite(snapshot, rawTitle, rawArtist, rawAlbum);
        if (!cleanupEnabled)
        {
            return new BrowserMetadataNormalization(rawTitle, rawArtist, rawAlbum, site, rawArtist.Length > 0 ? 0.95 : 0.5);
        }

        if (site == "bandcamp" && rawTitle.Contains(", by ", StringComparison.OrdinalIgnoreCase))
        {
            var split = rawTitle.Split(", by ", 2, StringSplitOptions.TrimEntries);
            if (split.Length == 2)
            {
                return new BrowserMetadataNormalization(split[0], split[1], rawAlbum, site, 0.82);
            }
        }

        if (site == "bandcamp" && string.IsNullOrWhiteSpace(rawArtist) && rawTitle.Contains(" | ", StringComparison.Ordinal))
        {
            var split = rawTitle.Split(" | ", 2, StringSplitOptions.TrimEntries);
            if (split.Length == 2)
            {
                return new BrowserMetadataNormalization(TrimBandcampTrackPrefix(split[0]), split[1], rawAlbum, site, 0.78);
            }
        }

        if ((site is "youtube" or "soundcloud" or "genericBrowser") && string.IsNullOrWhiteSpace(rawArtist))
        {
            var match = TrySplitOnDash(rawTitle);
            if (match is not null)
            {
                return new BrowserMetadataNormalization(match.Value.title, match.Value.artist, rawAlbum, site, site == "youtube" ? 0.75 : 0.72);
            }
        }

        if (site == "youtubeMusic" && !string.IsNullOrWhiteSpace(rawArtist))
        {
            return new BrowserMetadataNormalization(rawTitle, rawArtist, rawAlbum, site, 0.95);
        }

        return new BrowserMetadataNormalization(rawTitle, rawArtist, rawAlbum, site, string.IsNullOrWhiteSpace(rawArtist) ? 0.5 : 0.95);
    }

    private static (string artist, string title)? TrySplitOnDash(string input)
    {
        var trimmed = StripVideoSuffix(input);
        var separatorIndex = trimmed.IndexOf(" - ", StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex >= trimmed.Length - 3)
        {
            return null;
        }

        var artist = trimmed[..separatorIndex].Trim();
        var title = trimmed[(separatorIndex + 3)..].Trim();
        if (artist.Length == 0 || title.Length == 0)
        {
            return null;
        }

        return (artist, title);
    }

    private static string StripVideoSuffix(string value)
    {
        var cleaned = value.Replace("(Official Video)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("(Official Audio)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("[Official Video]", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
        return cleaned;
    }

    private static string TrimBandcampTrackPrefix(string value)
    {
        return value.Trim().TrimStart('▶', '\uFE0E', '\uFE0F', ' ');
    }

    private static string DetectSite(MediaSessionSnapshot snapshot, string rawTitle, string rawArtist, string rawAlbum)
    {
        var haystack = string.Join(" ", snapshot.SourceAppId, rawTitle, rawArtist, rawAlbum);
        if (haystack.Contains("music.youtube", StringComparison.OrdinalIgnoreCase) ||
            haystack.Contains("youtube music", StringComparison.OrdinalIgnoreCase))
        {
            return "youtubeMusic";
        }

        if (haystack.Contains("bandcamp", StringComparison.OrdinalIgnoreCase))
        {
            return "bandcamp";
        }

        if (rawTitle.Contains(" | ", StringComparison.Ordinal) && rawTitle.TrimStart().StartsWith("▶", StringComparison.Ordinal))
        {
            return "bandcamp";
        }

        if (haystack.Contains("soundcloud", StringComparison.OrdinalIgnoreCase))
        {
            return "soundcloud";
        }

        if (haystack.Contains("youtube", StringComparison.OrdinalIgnoreCase))
        {
            return "youtube";
        }

        return "generic";
    }
}
