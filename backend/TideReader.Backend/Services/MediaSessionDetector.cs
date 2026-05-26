using TideReader.Backend.Models;
namespace TideReader.Backend.Services;

public sealed class MediaSessionDetector(IMediaSessionSnapshotProvider snapshotProvider) : IPlaybackDetector
{
    public async Task<DetectionResult?> DetectAsync(CancellationToken cancellationToken)
    {
        var snapshot = await snapshotProvider.GetCurrentAsync(cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        return new DetectionResult
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
            DetectedText = $"{snapshot.Artist} - {snapshot.Title}".Trim(),
            SourceAppId = snapshot.SourceAppId,
            MatcherReason = "windows_media_session"
        };
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
}
