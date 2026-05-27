using Windows.Media.Control;
using Windows.Storage.Streams;

namespace TideReader.Backend.Services;

public sealed class WindowsMediaSessionSnapshotProvider : IMediaSessionSnapshotProvider
{
    public async Task<IReadOnlyList<MediaSessionSnapshot>> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cancellationToken);
        var results = new List<MediaSessionSnapshot>();

        foreach (var session in manager.GetSessions())
        {
            try
            {
                var playback = session.GetPlaybackInfo();
                var timeline = session.GetTimelineProperties();
                var media = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken);
                var artworkBytes = await ReadThumbnailAsync(media.Thumbnail, cancellationToken);
                var sourceAppId = session.SourceAppUserModelId ?? "";

                results.Add(new MediaSessionSnapshot(
                    SessionId: BuildSessionId(sourceAppId, media.Title, media.Artist, timeline.LastUpdatedTime),
                    SourceAppId: sourceAppId,
                    Browser: DetectBrowser(sourceAppId),
                    Site: "",
                    IsPaused: playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused,
                    Title: media.Title ?? "",
                    Artist: media.Artist ?? "",
                    Album: media.AlbumTitle ?? "",
                    DurationMs: (long)timeline.EndTime.TotalMilliseconds,
                    LastUpdatedUtc: timeline.LastUpdatedTime,
                    ArtworkBytes: artworkBytes));
            }
            catch
            {
                // Ignore malformed or inaccessible sessions and keep detection running.
            }
        }

        return results;
    }

    private static async Task<byte[]> ReadThumbnailAsync(IRandomAccessStreamReference? thumbnail, CancellationToken cancellationToken)
    {
        if (thumbnail is null)
        {
            return [];
        }

        try
        {
            using var stream = await thumbnail.OpenReadAsync().AsTask(cancellationToken);
            using var input = stream.GetInputStreamAt(0);
            using var reader = new DataReader(input);
            await reader.LoadAsync((uint)stream.Size).AsTask(cancellationToken);
            var bytes = new byte[stream.Size];
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch
        {
            return [];
        }
    }

    private static string BuildSessionId(string sourceAppId, string? title, string? artist, DateTimeOffset lastUpdatedUtc) =>
        $"{sourceAppId}|{title}|{artist}|{lastUpdatedUtc.UtcTicks}";

    private static string DetectBrowser(string sourceAppId)
    {
        if (sourceAppId.Contains("msedge", StringComparison.OrdinalIgnoreCase))
        {
            return "edge";
        }

        if (sourceAppId.Contains("firefox", StringComparison.OrdinalIgnoreCase))
        {
            return "firefox";
        }

        if (sourceAppId.Contains("brave", StringComparison.OrdinalIgnoreCase))
        {
            return "brave";
        }

        if (sourceAppId.Contains("opera", StringComparison.OrdinalIgnoreCase))
        {
            return "opera";
        }

        if (sourceAppId.Contains("chrome", StringComparison.OrdinalIgnoreCase))
        {
            return "chrome";
        }

        if (LooksLikeFirefoxAppId(sourceAppId))
        {
            return "firefox";
        }

        return "";
    }

    private static bool LooksLikeFirefoxAppId(string sourceAppId)
    {
        if (sourceAppId.Length != 16)
        {
            return false;
        }

        return sourceAppId.All(static character =>
            (character >= '0' && character <= '9') ||
            (character >= 'A' && character <= 'F'));
    }
}
