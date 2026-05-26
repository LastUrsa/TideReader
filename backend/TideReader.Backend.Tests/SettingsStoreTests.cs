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
            Assert.Equal(68, settings.OverlaySettings.ImageSizePx);
            Assert.Equal("Left", settings.OverlaySettings.ImagePosition);
            Assert.Equal("Left", settings.OverlaySettings.TextAlign);
            Assert.True(settings.OverlaySettings.ShowAppName);
            Assert.True(settings.OverlaySettings.ShowPlaybackState);
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
