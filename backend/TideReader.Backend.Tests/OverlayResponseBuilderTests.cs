using System.Text.Json;
using TideReader.Backend.Models;
using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class OverlayResponseBuilderTests
{
    [Fact]
    public void Build_ReturnsHtml_ForOverlay()
    {
        var response = OverlayResponseBuilder.Build("/overlay", new PlaybackSnapshotStore());

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", response.ContentType);
        Assert.NotEmpty(response.Body);
    }

    [Fact]
    public void Build_ReturnsJson_ForNowPlaying()
    {
        var store = new PlaybackSnapshotStore();
        store.Update(new DetectionResult { Status = "playing", Title = "Track", Artist = "Artist" });

        var response = OverlayResponseBuilder.Build("/nowplaying.json", store);
        var payload = JsonSerializer.Deserialize<NowPlayingFile>(response.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("application/json", response.ContentType);
        Assert.Equal("Track", payload!.Title);
    }

    [Fact]
    public void Build_ReturnsNotFound_ForMissingCover()
    {
        var response = OverlayResponseBuilder.Build("/cover.jpg", new PlaybackSnapshotStore());

        Assert.Equal(404, response.StatusCode);
        Assert.Empty(response.Body);
    }
}
