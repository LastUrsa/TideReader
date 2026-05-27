using TideReader.Backend.Models;

namespace TideReader.Backend.Services;

public sealed class PlaybackSnapshotStore : IPlaybackSnapshotStore
{
    private readonly Lock _lock = new();
    private NowPlayingFile _nowPlaying = new();
    private byte[] _artwork = [];

    public void Update(DetectionResult state)
    {
        lock (_lock)
        {
            _nowPlaying = new NowPlayingFile
            {
                Status = state.Status,
                Title = state.Title,
                Artist = state.Artist,
                Album = state.Album,
                DurationMs = state.DurationMs,
                ArtworkPath = state.ArtworkPath,
                Source = state.Source,
                Confidence = state.Confidence,
                Provider = state.Provider,
                Browser = state.Browser,
                Site = state.Site
            };
            _artwork = state.ArtworkBytes.ToArray();
        }
    }

    public NowPlayingFile GetNowPlaying()
    {
        lock (_lock)
        {
            return new NowPlayingFile
            {
                Status = _nowPlaying.Status,
                Title = _nowPlaying.Title,
                Artist = _nowPlaying.Artist,
                Album = _nowPlaying.Album,
                DurationMs = _nowPlaying.DurationMs,
                ArtworkPath = _nowPlaying.ArtworkPath,
                Source = _nowPlaying.Source,
                Confidence = _nowPlaying.Confidence,
                Provider = _nowPlaying.Provider,
                Browser = _nowPlaying.Browser,
                Site = _nowPlaying.Site
            };
        }
    }

    public byte[] GetArtwork()
    {
        lock (_lock)
        {
            return _artwork.ToArray();
        }
    }
}
