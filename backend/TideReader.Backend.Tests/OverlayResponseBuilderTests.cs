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
        Assert.Contains("overlayContainerStyle", html);
        Assert.Contains("backgroundMode", html);
        Assert.Contains("gradient", html);
        Assert.Contains("colorCount", html);
        Assert.Contains("statusPillStyle", html);
        Assert.Contains("withAlpha", html);
        Assert.Contains("backgroundFromSettings", html);
        Assert.DoesNotContain("reloadOverlayPage", html);
        Assert.Contains("id=\"cover-shell\" style=\"display:none\"", html);
        Assert.Contains(">Offline<", html);
        Assert.Contains("Waiting for TideReader", html);
        Assert.Contains("fetch('/nowplaying.json'", html);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    public void Build_ReturnsHtml_ForOverlayAliases(string path)
    {
        var response = OverlayResponseBuilder.Build(path, new PlaybackSnapshotStore(), new OverlaySettingsSnapshotStore());

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", response.ContentType);
        Assert.NotEmpty(response.Body);
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
            OverlayContainerStyle = new OverlayContainerStyle
            {
                BackgroundMode = "gradient",
                BackgroundColorHex = "#112233",
                Gradient = new GradientSettings
                {
                    ColorCount = 2,
                    Preset = "Subtle Glass",
                    Color1Hex = "#010101",
                    Color2Hex = "#222222",
                    Color3Hex = "#333333",
                    AngleDeg = 210
                },
                Opacity = 0.8,
                CornerRadiusPx = 22,
                PaddingPx = 18,
                GapPx = 10,
                BorderEnabled = false,
                BorderColorHex = "#556677",
                BorderWidthPx = 0
            },
            StatusPillStyle = new StatusPillStyle
            {
                BackgroundColorHex = "#223344",
                TextColorHex = "#F8EEDD",
                Opacity = 0.9,
                FontFamily = "Arial",
                FontSizePx = 12,
                Bold = true,
                CornerRadiusPx = 14,
                PaddingHorizontalPx = 11,
                PaddingVerticalPx = 5
            },
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
        Assert.Equal("gradient", payload.OverlayContainerStyle.BackgroundMode);
        Assert.Equal("#112233", payload.OverlayContainerStyle.BackgroundColorHex);
        Assert.Equal("Subtle Glass", payload.OverlayContainerStyle.Gradient.Preset);
        Assert.Equal(2, payload.OverlayContainerStyle.Gradient.ColorCount);
        Assert.Equal("#010101", payload.OverlayContainerStyle.Gradient.Color1Hex);
        Assert.Equal("#222222", payload.OverlayContainerStyle.Gradient.Color2Hex);
        Assert.Equal("#333333", payload.OverlayContainerStyle.Gradient.Color3Hex);
        Assert.Equal(210, payload.OverlayContainerStyle.Gradient.AngleDeg);
        Assert.Equal(0.8, payload.OverlayContainerStyle.Opacity);
        Assert.Equal("#223344", payload.StatusPillStyle.BackgroundColorHex);
        Assert.Equal("#F8EEDD", payload.StatusPillStyle.TextColorHex);
        Assert.Equal(96, payload.ImageSizePx);
        Assert.Equal("Right", payload.ImagePosition);
        Assert.Equal("Center", payload.TextAlign);
        Assert.Equal(18, payload.SongTextStyle.MaxCharacters);
        Assert.Equal(12, payload.ArtistTextStyle.MaxCharacters);
        Assert.Equal(8, payload.AlbumTextStyle.MaxCharacters);
        Assert.False(payload.ShowAppName);
        Assert.False(payload.ShowPlaybackState);
    }

    [Fact]
    public void BuildStandaloneHtml_UsesAbsoluteOverlayApiUrls()
    {
        var html = OverlayResponseBuilder.BuildStandaloneHtml(17655);

        Assert.Contains("fetch('http://127.0.0.1:17655/nowplaying.json'", html);
        Assert.Contains("fetch('http://127.0.0.1:17655/overlay-settings.json'", html);
        Assert.Contains("cover.src = 'http://127.0.0.1:17655/cover.jpg?ts=' + Date.now();", html);
    }

    [Fact]
    public void BuildStandaloneHtml_FallsBackToDefaultPort_WhenPortIsNotPositive()
    {
        var html = OverlayResponseBuilder.BuildStandaloneHtml(0);

        Assert.Contains("http://127.0.0.1:17655/nowplaying.json", html);
    }
}
