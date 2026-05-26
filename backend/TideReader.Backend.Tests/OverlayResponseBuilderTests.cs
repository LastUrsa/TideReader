using System.Text.Json;
using TideReader.Backend.Models;
using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class OverlayResponseBuilderTests
{
    [Fact]
    public void Build_ReturnsHtml_ForOverlay()
    {
        var response = OverlayResponseBuilder.Build("/overlay", new PlaybackSnapshotStore(), new OverlaySettingsSnapshotStore());
        var html = System.Text.Encoding.UTF8.GetString(response.Body);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", response.ContentType);
        Assert.NotEmpty(response.Body);
        Assert.Contains("border-radius: 0;", html);
        Assert.Contains("truncateText", html);
        Assert.Contains("activeSettings.songTextStyle?.maxCharacters", html);
    }

    [Fact]
    public void Build_ReturnsJson_ForNowPlaying()
    {
        var store = new PlaybackSnapshotStore();
        store.Update(new DetectionResult { Status = "playing", Title = "Track", Artist = "Artist" });

        var response = OverlayResponseBuilder.Build("/nowplaying.json", store, new OverlaySettingsSnapshotStore());
        var payload = JsonSerializer.Deserialize<NowPlayingFile>(response.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("application/json", response.ContentType);
        Assert.Equal("Track", payload!.Title);
    }

    [Fact]
    public void Build_ReturnsNotFound_ForMissingCover()
    {
        var response = OverlayResponseBuilder.Build("/cover.jpg", new PlaybackSnapshotStore(), new OverlaySettingsSnapshotStore());

        Assert.Equal(404, response.StatusCode);
        Assert.Empty(response.Body);
    }

    [Fact]
    public void Build_ReturnsJson_ForOverlaySettings()
    {
        var settingsStore = new OverlaySettingsSnapshotStore();
        settingsStore.Update(new OverlaySettings
        {
            SongTextStyle = new OverlayTextStyle { MaxCharacters = 18 },
            ArtistTextStyle = new OverlayTextStyle { MaxCharacters = 12 },
            AlbumTextStyle = new OverlayTextStyle { MaxCharacters = 8 },
            BackgroundColorHex = "#112233",
            ImageSizePx = 96,
            ImagePosition = "Right",
            TextAlign = "Center",
            ShowAppName = false,
            ShowPlaybackState = false
        });

        var response = OverlayResponseBuilder.Build("/overlay-settings.json", new PlaybackSnapshotStore(), settingsStore);
        var payload = JsonSerializer.Deserialize<OverlaySettings>(response.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("application/json", response.ContentType);
        Assert.Equal("#112233", payload!.BackgroundColorHex);
        Assert.Equal(96, payload.ImageSizePx);
        Assert.Equal("Right", payload.ImagePosition);
        Assert.Equal("Center", payload.TextAlign);
        Assert.Equal(18, payload.SongTextStyle.MaxCharacters);
        Assert.Equal(12, payload.ArtistTextStyle.MaxCharacters);
        Assert.Equal(8, payload.AlbumTextStyle.MaxCharacters);
        Assert.False(payload.ShowAppName);
        Assert.False(payload.ShowPlaybackState);
    }
}
