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
                ThemeMode = "Light"
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
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
