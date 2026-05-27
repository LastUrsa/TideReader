using TideReader.Backend.Models;
using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class BridgeStatePolicyTests
{
    [Fact]
    public void ApplyConfirmedCache_FillsMissing_Metadata_ForSameTrack()
    {
        var current = new DetectionResult
        {
            Artist = "Artist",
            Title = "Track",
            Method = "media_session"
        };
        var confirmed = new DetectionResult
        {
            Artist = "Artist",
            Title = "Track",
            Album = "Album",
            DurationMs = 123000,
            MetadataSource = "itunes_search",
            ArtworkPath = "cover.jpg",
            ArtworkBytes = [1, 2, 3]
        };

        var result = BridgeStatePolicy.ApplyConfirmedCache(current, confirmed);

        Assert.Equal("Album", result.Album);
        Assert.Equal(123000, result.DurationMs);
        Assert.Equal("itunes_search", result.MetadataSource);
        Assert.Equal("cover.jpg", result.ArtworkPath);
        Assert.Equal([1, 2, 3], result.ArtworkBytes);
    }

    [Fact]
    public void ClearSuspectCarryoverArtwork_ClearsMatchingArtwork_OnTrackChange()
    {
        var previous = new DetectionResult
        {
            Artist = "Artist A",
            Title = "Track A",
            ArtworkPath = "cover.jpg",
            ArtworkBytes = [9, 9, 9]
        };
        var current = new DetectionResult
        {
            Artist = "Artist B",
            Title = "Track B",
            Method = "media_session",
            ArtworkPath = "cover.jpg",
            ArtworkBytes = [9, 9, 9]
        };

        var result = BridgeStatePolicy.ClearSuspectCarryoverArtwork(previous, current);

        Assert.Equal("", result.ArtworkPath);
        Assert.Empty(result.ArtworkBytes);
    }

    [Fact]
    public void SuppressArtworkUntilAlbumResolved_ClearsMediaSessionArtwork_WhenAlbumMissing()
    {
        var current = new DetectionResult
        {
            Artist = "Artist",
            Title = "Track",
            Method = "media_session",
            Provider = "tidal",
            ArtworkPath = "cover.jpg",
            ArtworkBytes = [1, 2, 3]
        };

        var result = BridgeStatePolicy.SuppressArtworkUntilAlbumResolved(current, MetadataProviderMode.MusicBrainzWithFallbacks);

        Assert.Equal("", result.ArtworkPath);
        Assert.Empty(result.ArtworkBytes);
    }

    [Fact]
    public void SuppressArtworkUntilAlbumResolved_PreservesBrowserArtwork_WhenAlbumMissing()
    {
        var current = new DetectionResult
        {
            Artist = "Artist",
            Title = "Track",
            Method = "media_session",
            Provider = "browser",
            ArtworkPath = "cover.jpg",
            ArtworkBytes = [1, 2, 3]
        };

        var result = BridgeStatePolicy.SuppressArtworkUntilAlbumResolved(current, MetadataProviderMode.MusicBrainzWithFallbacks);

        Assert.Equal("cover.jpg", result.ArtworkPath);
        Assert.Equal([1, 2, 3], result.ArtworkBytes);
    }

    [Fact]
    public void MergeEnrichment_ReplacesArtwork_WhenMetadataSourceProvided()
    {
        var baseline = new DetectionResult
        {
            Artist = "Artist",
            Title = "Track",
            ArtworkPath = "cover.jpg",
            ArtworkBytes = [1, 1, 1]
        };
        var enriched = new DetectionResult
        {
            Album = "Album",
            MetadataSource = "musicbrainz",
            ArtworkPath = "cover.jpg",
            ArtworkBytes = [2, 2, 2]
        };

        BridgeStatePolicy.MergeEnrichment(baseline, enriched);

        Assert.Equal("Album", baseline.Album);
        Assert.Equal("musicbrainz", baseline.MetadataSource);
        Assert.Equal([2, 2, 2], baseline.ArtworkBytes);
    }
}
