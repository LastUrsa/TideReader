using TideReader.Backend.Models;

namespace TideReader.Backend.Services;

internal static class BridgeStatePolicy
{
    public static DetectionResult ApplyConfirmedCache(DetectionResult current, DetectionResult confirmed)
    {
        var key = TrackKey(current);
        var confirmedKey = TrackKey(confirmed);
        if (string.IsNullOrWhiteSpace(key) || !string.Equals(key, confirmedKey, StringComparison.Ordinal))
        {
            return current;
        }

        if (string.IsNullOrWhiteSpace(current.Album)) current.Album = confirmed.Album;
        if (current.DurationMs == 0) current.DurationMs = confirmed.DurationMs;
        if (string.IsNullOrWhiteSpace(current.MetadataSource)) current.MetadataSource = confirmed.MetadataSource;
        if (current.ArtworkBytes.Length == 0)
        {
            current.ArtworkBytes = confirmed.ArtworkBytes.ToArray();
            current.ArtworkPath = confirmed.ArtworkPath;
        }

        return current;
    }

    public static DetectionResult ClearSuspectCarryoverArtwork(DetectionResult previous, DetectionResult current)
    {
        if (!string.Equals(current.Method, "media_session", StringComparison.OrdinalIgnoreCase) ||
            current.ArtworkBytes.Length == 0 ||
            !string.Equals(current.Provider, "tidal", StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        var previousKey = TrackKey(previous);
        var currentKey = TrackKey(current);
        var shouldClear =
            !string.IsNullOrWhiteSpace(previousKey) &&
            !string.IsNullOrWhiteSpace(currentKey) &&
            !string.Equals(previousKey, currentKey, StringComparison.Ordinal) &&
            previous.ArtworkBytes.Length > 0 &&
            previous.ArtworkBytes.AsSpan().SequenceEqual(current.ArtworkBytes) &&
            string.IsNullOrWhiteSpace(current.Album);

        if (!shouldClear)
        {
            return current;
        }

        current.ArtworkBytes = [];
        current.ArtworkPath = "";
        return current;
    }

    public static DetectionResult SuppressArtworkUntilAlbumResolved(DetectionResult current, MetadataProviderMode mode)
    {
        if (!string.Equals(current.Method, "media_session", StringComparison.OrdinalIgnoreCase) ||
            current.ArtworkBytes.Length == 0 ||
            !string.Equals(current.Provider, "tidal", StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        if (!string.IsNullOrWhiteSpace(current.Album) || mode == MetadataProviderMode.Off)
        {
            return current;
        }

        current.ArtworkBytes = [];
        current.ArtworkPath = "";
        return current;
    }

    public static bool Equivalent(DetectionResult left, DetectionResult right) =>
        left.Status == right.Status &&
        left.Title == right.Title &&
        left.Artist == right.Artist &&
        left.Album == right.Album &&
        left.DurationMs == right.DurationMs &&
        left.ArtworkPath == right.ArtworkPath &&
        left.Source == right.Source &&
        left.Method == right.Method &&
        Math.Abs(left.Confidence - right.Confidence) < 0.0001 &&
        left.DetectedText == right.DetectedText &&
        left.SourceAppId == right.SourceAppId &&
        left.MatcherReason == right.MatcherReason &&
        left.MetadataSource == right.MetadataSource &&
        left.Provider == right.Provider &&
        left.Browser == right.Browser &&
        left.Site == right.Site &&
        left.RawTitle == right.RawTitle &&
        left.RawArtist == right.RawArtist &&
        left.RawAlbum == right.RawAlbum &&
        left.SelectionReason == right.SelectionReason &&
        left.ArtworkBytes.AsSpan().SequenceEqual(right.ArtworkBytes);

    public static bool ArtworkChanged(DetectionResult left, DetectionResult right) =>
        left.ArtworkPath != right.ArtworkPath ||
        !left.ArtworkBytes.AsSpan().SequenceEqual(right.ArtworkBytes);

    public static bool AddsEnrichment(DetectionResult baseline, DetectionResult enriched) =>
        (string.IsNullOrWhiteSpace(baseline.Album) && !string.IsNullOrWhiteSpace(enriched.Album)) ||
        (baseline.DurationMs == 0 && enriched.DurationMs > 0) ||
        (string.IsNullOrWhiteSpace(baseline.MetadataSource) && !string.IsNullOrWhiteSpace(enriched.MetadataSource)) ||
        (baseline.ArtworkBytes.Length == 0 && enriched.ArtworkBytes.Length > 0);

    public static void MergeEnrichment(DetectionResult target, DetectionResult extra)
    {
        if (string.IsNullOrWhiteSpace(target.Album)) target.Album = extra.Album;
        if (target.DurationMs == 0) target.DurationMs = extra.DurationMs;
        if (string.IsNullOrWhiteSpace(target.MetadataSource)) target.MetadataSource = extra.MetadataSource;
        if (extra.Confidence > target.Confidence) target.Confidence = extra.Confidence;
        if (extra.ArtworkBytes.Length > 0 && ShouldReplaceArtwork(target, extra))
        {
            target.ArtworkBytes = extra.ArtworkBytes.ToArray();
            target.ArtworkPath = extra.ArtworkPath;
        }
    }

    public static bool ShouldReplaceArtwork(DetectionResult current, DetectionResult extra)
    {
        if (extra.ArtworkBytes.Length == 0)
        {
            return false;
        }

        if (current.ArtworkBytes.Length == 0)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(extra.MetadataSource) &&
            !current.ArtworkBytes.AsSpan().SequenceEqual(extra.ArtworkBytes);
    }

    public static DetectionResult MergeForComparison(DetectionResult baseline, DetectionResult extra)
    {
        var merged = CloneDetection(baseline);
        MergeEnrichment(merged, extra);
        return merged;
    }

    public static string TrackKey(DetectionResult result) => Normalize($"{result.Artist}|{result.Title}");

    public static DetectionResult CloneDetection(DetectionResult input) => new()
    {
        Status = input.Status,
        Title = input.Title,
        Artist = input.Artist,
        Album = input.Album,
        DurationMs = input.DurationMs,
        ArtworkPath = input.ArtworkPath,
        Source = input.Source,
        Method = input.Method,
        Confidence = input.Confidence,
        TidalUrl = input.TidalUrl,
        DetectedText = input.DetectedText,
        SourceAppId = input.SourceAppId,
        MatcherReason = input.MatcherReason,
        MetadataSource = input.MetadataSource,
        ArtworkBytes = input.ArtworkBytes.ToArray(),
        Provider = input.Provider,
        Browser = input.Browser,
        Site = input.Site,
        RawTitle = input.RawTitle,
        RawArtist = input.RawArtist,
        RawAlbum = input.RawAlbum,
        SelectionReason = input.SelectionReason
    };

    private static string Normalize(string input) => string.Join(' ', input.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
