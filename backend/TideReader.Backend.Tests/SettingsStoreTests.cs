using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public async Task LoadAsync_ReturnsDefaults_WhenFileMissing()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new SettingsStore(tempDir);

            var settings = await store.LoadAsync(CancellationToken.None);

            Assert.NotNull(settings);
            Assert.Equal(17655, settings.OverlayPort);
            Assert.Equal(1000, settings.PollIntervalMs);
            Assert.Equal("Dark", settings.ThemeMode);
            Assert.Equal("#32334F", settings.OverlaySettings.BackgroundColorHex);
            Assert.Equal("solid", settings.OverlaySettings.OverlayContainerStyle.BackgroundMode);
            Assert.Equal("#32334F", settings.OverlaySettings.OverlayContainerStyle.BackgroundColorHex);
            Assert.Equal("Diagonal", settings.OverlaySettings.OverlayContainerStyle.Gradient.Preset);
            Assert.Equal(3, settings.OverlaySettings.OverlayContainerStyle.Gradient.ColorCount);
            Assert.Equal("#1F1F2E", settings.OverlaySettings.OverlayContainerStyle.Gradient.Color1Hex);
            Assert.Equal("#6B46C1", settings.OverlaySettings.OverlayContainerStyle.Gradient.Color2Hex);
            Assert.Equal("#111827", settings.OverlaySettings.OverlayContainerStyle.Gradient.Color3Hex);
            Assert.Equal(135, settings.OverlaySettings.OverlayContainerStyle.Gradient.AngleDeg);
            Assert.Equal(0.86, settings.OverlaySettings.OverlayContainerStyle.Opacity);
            Assert.Equal("#45475D", settings.OverlaySettings.StatusPillStyle.BackgroundColorHex);
            Assert.Equal("#787B80", settings.OverlaySettings.StatusPillStyle.TextColorHex);
            Assert.Equal(68, settings.OverlaySettings.ImageSizePx);
            Assert.Equal("Left", settings.OverlaySettings.ImagePosition);
            Assert.Equal("Left", settings.OverlaySettings.TextAlign);
            Assert.True(settings.OverlaySettings.ShowAppName);
            Assert.True(settings.OverlaySettings.ShowPlaybackState);
            Assert.True(settings.BrowserSettings.YouTubeVideoImageFallbackEnabled);
            Assert.Equal(0, settings.OverlaySettings.SongTextStyle.MaxCharacters);
            Assert.Equal(0, settings.OverlaySettings.ArtistTextStyle.MaxCharacters);
            Assert.Equal(0, settings.OverlaySettings.AlbumTextStyle.MaxCharacters);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task SaveAsync_RoundTrips_Settings()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new SettingsStore(tempDir);
            var input = new TideReader.Backend.Models.Settings
            {
                OutputFolder = @"C:\Temp\Obs",
                OverlayEnabled = false,
                OverlayPort = 19001,
                PollIntervalMs = 500,
                EnableWindowTitleFallback = false,
                EnableDebugManualInput = false,
                StartMinimized = true,
                LaunchAtStartup = true,
                MetadataProviderMode = "MusicBrainzOnly",
                ThemeMode = "Light",
                BrowserSettings = new TideReader.Backend.Models.BrowserSettings
                {
                    Enabled = true,
                    ActiveSourceMode = "browser",
                    YouTubeVideoImageFallbackEnabled = false
                },
                OverlaySettings = new TideReader.Backend.Models.OverlaySettings
                {
                    SongTextStyle = new TideReader.Backend.Models.OverlayTextStyle
                    {
                        FontFamily = "Arial",
                        ColorHex = "#112233",
                        FontSizePx = 30,
                        MaxCharacters = 18,
                        Bold = false,
                        Italic = true,
                        Underline = true
                    },
                    ArtistTextStyle = new TideReader.Backend.Models.OverlayTextStyle
                    {
                        FontFamily = "Tahoma",
                        ColorHex = "#445566",
                        FontSizePx = 18,
                        MaxCharacters = 12
                    },
                    AlbumTextStyle = new TideReader.Backend.Models.OverlayTextStyle
                    {
                        FontFamily = "Verdana",
                        ColorHex = "#778899",
                        FontSizePx = 16,
                        MaxCharacters = 8,
                        Bold = true
                    },
                    ImageSizePx = 92,
                    BackgroundColorHex = "#AABBCC",
                    OverlayContainerStyle = new TideReader.Backend.Models.OverlayContainerStyle
                    {
                        BackgroundMode = "gradient",
                        BackgroundColorHex = "#AABBCC",
                        Gradient = new TideReader.Backend.Models.GradientSettings
                        {
                            ColorCount = 2,
                            Preset = "Spotlight",
                            Color1Hex = "#010203",
                            Color2Hex = "#A1B2C3",
                            Color3Hex = "#F1E2D3",
                            AngleDeg = 270
                        },
                        Opacity = 0.75,
                        CornerRadiusPx = 24,
                        PaddingPx = 16,
                        GapPx = 12,
                        BorderEnabled = false,
                        BorderColorHex = "#111111",
                        BorderWidthPx = 0
                    },
                    StatusPillStyle = new TideReader.Backend.Models.StatusPillStyle
                    {
                        BackgroundColorHex = "#223344",
                        TextColorHex = "#F0E0D0",
                        Opacity = 0.9,
                        FontFamily = "Arial",
                        FontSizePx = 13,
                        Bold = true,
                        Italic = true,
                        Underline = true,
                        CornerRadiusPx = 20,
                        PaddingHorizontalPx = 12,
                        PaddingVerticalPx = 6
                    },
                    ImagePosition = "Right",
                    TextAlign = "Center",
                    ShowAppName = false,
                    ShowPlaybackState = false
                }
            };

            await store.SaveAsync(input, CancellationToken.None);
            var loaded = await store.LoadAsync(CancellationToken.None);

            Assert.Equal(input.OutputFolder, loaded.OutputFolder);
            Assert.Equal(input.OverlayEnabled, loaded.OverlayEnabled);
            Assert.Equal(input.OverlayPort, loaded.OverlayPort);
            Assert.Equal(input.PollIntervalMs, loaded.PollIntervalMs);
            Assert.Equal(input.EnableWindowTitleFallback, loaded.EnableWindowTitleFallback);
            Assert.Equal(input.EnableDebugManualInput, loaded.EnableDebugManualInput);
            Assert.Equal(input.StartMinimized, loaded.StartMinimized);
            Assert.Equal(input.LaunchAtStartup, loaded.LaunchAtStartup);
            Assert.Equal(input.MetadataProviderMode, loaded.MetadataProviderMode);
            Assert.Equal(input.ThemeMode, loaded.ThemeMode);
            Assert.Equal(input.BrowserSettings.YouTubeVideoImageFallbackEnabled, loaded.BrowserSettings.YouTubeVideoImageFallbackEnabled);
            Assert.Equal(input.OverlaySettings.SongTextStyle.FontFamily, loaded.OverlaySettings.SongTextStyle.FontFamily);
            Assert.Equal(input.OverlaySettings.SongTextStyle.ColorHex, loaded.OverlaySettings.SongTextStyle.ColorHex);
            Assert.Equal(input.OverlaySettings.SongTextStyle.FontSizePx, loaded.OverlaySettings.SongTextStyle.FontSizePx);
            Assert.Equal(input.OverlaySettings.SongTextStyle.MaxCharacters, loaded.OverlaySettings.SongTextStyle.MaxCharacters);
            Assert.Equal(input.OverlaySettings.SongTextStyle.Bold, loaded.OverlaySettings.SongTextStyle.Bold);
            Assert.Equal(input.OverlaySettings.SongTextStyle.Italic, loaded.OverlaySettings.SongTextStyle.Italic);
            Assert.Equal(input.OverlaySettings.SongTextStyle.Underline, loaded.OverlaySettings.SongTextStyle.Underline);
            Assert.Equal(input.OverlaySettings.ArtistTextStyle.FontFamily, loaded.OverlaySettings.ArtistTextStyle.FontFamily);
            Assert.Equal(input.OverlaySettings.ArtistTextStyle.MaxCharacters, loaded.OverlaySettings.ArtistTextStyle.MaxCharacters);
            Assert.Equal(input.OverlaySettings.AlbumTextStyle.ColorHex, loaded.OverlaySettings.AlbumTextStyle.ColorHex);
            Assert.Equal(input.OverlaySettings.AlbumTextStyle.MaxCharacters, loaded.OverlaySettings.AlbumTextStyle.MaxCharacters);
            Assert.Equal(input.OverlaySettings.ImageSizePx, loaded.OverlaySettings.ImageSizePx);
            Assert.Equal(input.OverlaySettings.BackgroundColorHex, loaded.OverlaySettings.BackgroundColorHex);
            Assert.Equal(input.OverlaySettings.OverlayContainerStyle.BackgroundMode, loaded.OverlaySettings.OverlayContainerStyle.BackgroundMode);
            Assert.Equal(input.OverlaySettings.OverlayContainerStyle.BackgroundColorHex, loaded.OverlaySettings.OverlayContainerStyle.BackgroundColorHex);
            Assert.Equal(input.OverlaySettings.OverlayContainerStyle.Gradient.Preset, loaded.OverlaySettings.OverlayContainerStyle.Gradient.Preset);
            Assert.Equal(input.OverlaySettings.OverlayContainerStyle.Gradient.ColorCount, loaded.OverlaySettings.OverlayContainerStyle.Gradient.ColorCount);
            Assert.Equal(input.OverlaySettings.OverlayContainerStyle.Gradient.Color1Hex, loaded.OverlaySettings.OverlayContainerStyle.Gradient.Color1Hex);
            Assert.Equal(input.OverlaySettings.OverlayContainerStyle.Gradient.Color2Hex, loaded.OverlaySettings.OverlayContainerStyle.Gradient.Color2Hex);
            Assert.Equal(input.OverlaySettings.OverlayContainerStyle.Gradient.Color3Hex, loaded.OverlaySettings.OverlayContainerStyle.Gradient.Color3Hex);
            Assert.Equal(input.OverlaySettings.OverlayContainerStyle.Gradient.AngleDeg, loaded.OverlaySettings.OverlayContainerStyle.Gradient.AngleDeg);
            Assert.Equal(input.OverlaySettings.OverlayContainerStyle.Opacity, loaded.OverlaySettings.OverlayContainerStyle.Opacity);
            Assert.Equal(input.OverlaySettings.OverlayContainerStyle.BorderEnabled, loaded.OverlaySettings.OverlayContainerStyle.BorderEnabled);
            Assert.Equal(input.OverlaySettings.StatusPillStyle.BackgroundColorHex, loaded.OverlaySettings.StatusPillStyle.BackgroundColorHex);
            Assert.Equal(input.OverlaySettings.StatusPillStyle.TextColorHex, loaded.OverlaySettings.StatusPillStyle.TextColorHex);
            Assert.Equal(input.OverlaySettings.StatusPillStyle.FontFamily, loaded.OverlaySettings.StatusPillStyle.FontFamily);
            Assert.Equal(input.OverlaySettings.StatusPillStyle.Bold, loaded.OverlaySettings.StatusPillStyle.Bold);
            Assert.Equal(input.OverlaySettings.ImagePosition, loaded.OverlaySettings.ImagePosition);
            Assert.Equal(input.OverlaySettings.TextAlign, loaded.OverlaySettings.TextAlign);
            Assert.Equal(input.OverlaySettings.ShowAppName, loaded.OverlaySettings.ShowAppName);
            Assert.Equal(input.OverlaySettings.ShowPlaybackState, loaded.OverlaySettings.ShowPlaybackState);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
