using Windows.Media.Control;
using Windows.Storage.Streams;

namespace TideReader.Backend.Services;

public sealed class WindowsMediaSessionSnapshotProvider : IMediaSessionSnapshotProvider
{
    public async Task<MediaSessionSnapshot?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cancellationToken);
        var session = manager.GetSessions()
            .FirstOrDefault(s => (s.SourceAppUserModelId ?? "").Contains("TIDAL", StringComparison.OrdinalIgnoreCase)
                              || (s.SourceAppUserModelId ?? "").Contains("Aspiro", StringComparison.OrdinalIgnoreCase));

        if (session is null)
        {
            return null;
        }

        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();
        var media = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken);
        var artworkBytes = await ReadThumbnailAsync(media.Thumbnail, cancellationToken);

        return new MediaSessionSnapshot(
            SourceAppId: session.SourceAppUserModelId ?? "",
            IsPaused: playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused,
            Title: media.Title ?? "",
            Artist: media.Artist ?? "",
            Album: media.AlbumTitle ?? "",
            DurationMs: (long)timeline.EndTime.TotalMilliseconds,
            ArtworkBytes: artworkBytes);
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
}
