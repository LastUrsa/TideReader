using TideReader.Backend.Models;
using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class MediaSessionDetectorTests
{
    [Fact]
    public async Task DetectAsync_ReturnsNull_WhenNoSessionIsAvailable()
    {
        var detector = new MediaSessionDetector(new FakeSnapshotProvider(null));

        var result = await detector.DetectAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task DetectAsync_MapsSnapshotToDetectionResult()
    {
        var detector = new MediaSessionDetector(new FakeSnapshotProvider(new MediaSessionSnapshot(
            SourceAppId: "TIDAL.exe",
            IsPaused: false,
            Title: "Track",
            Artist: "Artist",
            Album: "Album",
            DurationMs: 123000,
            ArtworkBytes: [1, 2, 3])));

        var result = await detector.DetectAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("playing", result!.Status);
        Assert.Equal("Track", result.Title);
        Assert.Equal("Artist", result.Artist);
        Assert.Equal("Album", result.Album);
        Assert.Equal(123000, result.DurationMs);
        Assert.Equal("cover.jpg", result.ArtworkPath);
        Assert.Equal([1, 2, 3], result.ArtworkBytes);
        Assert.Equal("Artist - Track", result.DetectedText);
        Assert.Equal("TIDAL.exe", result.SourceAppId);
        Assert.Equal("windows_media_session", result.MatcherReason);
        Assert.True(result.Confidence > 0.9);
    }

    [Fact]
    public async Task DetectAsync_UsesPausedStatus_AndOmitsArtworkPath_WhenArtworkMissing()
    {
        var detector = new MediaSessionDetector(new FakeSnapshotProvider(new MediaSessionSnapshot(
            SourceAppId: "Aspiro.TIDAL",
            IsPaused: true,
            Title: "Track",
            Artist: "",
            Album: "",
            DurationMs: 0,
            ArtworkBytes: [])));

        var result = await detector.DetectAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("paused", result!.Status);
        Assert.Equal("", result.ArtworkPath);
        Assert.Empty(result.ArtworkBytes);
        Assert.True(result.Confidence >= 0.83);
        Assert.True(result.Confidence < 0.99);
    }

    private sealed class FakeSnapshotProvider(MediaSessionSnapshot? snapshot) : IMediaSessionSnapshotProvider
    {
        public Task<MediaSessionSnapshot?> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }
}
