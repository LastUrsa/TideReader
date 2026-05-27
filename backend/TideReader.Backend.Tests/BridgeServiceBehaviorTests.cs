using TideReader.Backend.Models;
using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class BridgeServiceBehaviorTests
{
    [Fact]
    public async Task RunDetectionAsync_UsesWindowTitleFallback_WhenEnabled()
    {
        using var harness = new BridgeServiceHarness();
        harness.Settings.EnableWindowTitleFallback = true;
        harness.WindowTitleDetector.Result = new DetectionResult
        {
            Status = "playing",
            Artist = "Fallback Artist",
            Title = "Fallback Title",
            Method = "window_title",
            Confidence = 0.61
        };

        await harness.Service.InitializeAsync(CancellationToken.None);
        var state = await harness.Service.RunDetectionAsync(CancellationToken.None);

        Assert.Equal("Fallback Artist", state.NowPlaying.Artist);
        Assert.Equal("Fallback Title", state.NowPlaying.Title);
        Assert.Equal("window_title", state.NowPlaying.Method);
    }

    [Fact]
    public async Task RunDetectionAsync_UsesManualInputFallback_WhenEnabled()
    {
        using var harness = new BridgeServiceHarness();
        harness.Settings.EnableDebugManualInput = true;
        harness.ManualDetector.Result = new DetectionResult
        {
            Status = "playing",
            Artist = "Manual Artist",
            Title = "Manual Title",
            Method = "manual_input",
            Confidence = 0.52
        };

        await harness.Service.InitializeAsync(CancellationToken.None);
        harness.Service.SetManualInput(" Manual Artist - Manual Title ");
        var state = await harness.Service.RunDetectionAsync(CancellationToken.None);

        Assert.Equal("Manual Artist", state.NowPlaying.Artist);
        Assert.Equal("Manual Title", state.NowPlaying.Title);
        Assert.Equal("manual_input", state.NowPlaying.Method);
        Assert.Equal("Manual Artist - Manual Title", state.ManualInput);
    }

    [Fact]
    public async Task SaveSettingsAsync_NormalizesSettings_AndCapturesOverlayErrors()
    {
        using var harness = new BridgeServiceHarness();
        Settings? changedSettings = null;
        harness.Service.SettingsChanged += settings => changedSettings = settings;

        await harness.Service.InitializeAsync(CancellationToken.None);
        harness.OverlayCoordinator.ThrowOnConfigure = new InvalidOperationException("overlay failed");
        var state = await harness.Service.SaveSettingsAsync(new Settings
        {
            OutputFolder = "",
            OverlayEnabled = true,
            OverlayPort = 0,
            PollIntervalMs = 50,
            MetadataProviderMode = "bogus",
            ThemeMode = "bogus",
            OverlaySettings = new OverlaySettings
            {
                SongTextStyle = new OverlayTextStyle
                {
                    FontFamily = "",
                    ColorHex = "bad-color",
                    FontSizePx = 0,
                    MaxCharacters = -1
                },
                ArtistTextStyle = new OverlayTextStyle
                {
                    FontFamily = "",
                    ColorHex = "#12345",
                    FontSizePx = -1,
                    MaxCharacters = -2
                },
                AlbumTextStyle = new OverlayTextStyle
                {
                    FontFamily = "",
                    ColorHex = "123456",
                    FontSizePx = -2,
                    MaxCharacters = -3
                },
                ImageSizePx = 0,
                BackgroundColorHex = "bad-color",
                ImagePosition = "sideways",
                TextAlign = "diagonal"
            }
        }, CancellationToken.None);

        Assert.NotNull(changedSettings);
        Assert.Equal(Defaults.OutputFolder, state.Settings.OutputFolder);
        Assert.Equal(17655, state.Settings.OverlayPort);
        Assert.Equal(1000, state.Settings.PollIntervalMs);
        Assert.Equal(nameof(MetadataProviderMode.MusicBrainzWithFallbacks), state.Settings.MetadataProviderMode);
        Assert.Equal(nameof(ThemeMode.Dark), state.Settings.ThemeMode);
        Assert.Equal("Segoe UI", state.Settings.OverlaySettings.SongTextStyle.FontFamily);
        Assert.Equal("#EBEBEB", state.Settings.OverlaySettings.SongTextStyle.ColorHex);
        Assert.Equal(24, state.Settings.OverlaySettings.SongTextStyle.FontSizePx);
        Assert.Equal(0, state.Settings.OverlaySettings.SongTextStyle.MaxCharacters);
        Assert.Equal(0, state.Settings.OverlaySettings.ArtistTextStyle.MaxCharacters);
        Assert.Equal(0, state.Settings.OverlaySettings.AlbumTextStyle.MaxCharacters);
        Assert.Equal("#32334F", state.Settings.OverlaySettings.BackgroundColorHex);
        Assert.Equal(68, state.Settings.OverlaySettings.ImageSizePx);
        Assert.Equal("Left", state.Settings.OverlaySettings.ImagePosition);
        Assert.Equal("Left", state.Settings.OverlaySettings.TextAlign);
        Assert.True(state.Settings.OverlaySettings.ShowAppName);
        Assert.True(state.Settings.OverlaySettings.ShowPlaybackState);
        Assert.Equal("overlay failed", state.LastError);
        Assert.Equal(state.Settings.OutputFolder, harness.Settings.OutputFolder);
    }

    [Fact]
    public async Task RunDetectionAsync_ResetsState_WhenDetectionThrows()
    {
        using var harness = new BridgeServiceHarness();
        harness.PlaybackDetector.Exception = new InvalidOperationException("detector blew up");

        await harness.Service.InitializeAsync(CancellationToken.None);
        var state = await harness.Service.RunDetectionAsync(CancellationToken.None);

        Assert.Equal("not_running", state.NowPlaying.Status);
        Assert.Equal("none", state.NowPlaying.Method);
        Assert.Equal("detector blew up", state.LastError);
        Assert.Equal("TIDAL not running", state.StatusMessage);
    }

    [Fact]
    public async Task RunDetectionAsync_DoesNotApplyStaleBackgroundEnrichment()
    {
        using var harness = new BridgeServiceHarness();
        var enrichStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseEnrichment = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Artist A",
            Title = "Track A",
            Method = "media_session",
            Confidence = 0.91
        });
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Artist B",
            Title = "Track B",
            Album = "Album B",
            Method = "media_session",
            Confidence = 0.93
        });

        harness.MetadataEnricher.NeedsEnrichmentHandler = static (input, _) => string.IsNullOrWhiteSpace(input.Album);
        harness.MetadataEnricher.EnrichAsyncHandler = async (input, _, _) =>
        {
            enrichStarted.TrySetResult();
            await releaseEnrichment.Task;
            return new DetectionResult
            {
                Artist = input.Artist,
                Title = input.Title,
                Album = "Album A",
                MetadataSource = "itunes_search",
                Confidence = 0.97
            };
        };

        await harness.Service.InitializeAsync(CancellationToken.None);
        _ = harness.Service.RunDetectionAsync(CancellationToken.None);
        await enrichStarted.Task;

        var secondState = await harness.Service.RunDetectionAsync(CancellationToken.None);
        releaseEnrichment.SetResult();
        await Task.Delay(100);

        var finalState = harness.Service.GetState();
        Assert.Equal("Track B", secondState.NowPlaying.Title);
        Assert.Equal("Track B", finalState.NowPlaying.Title);
        Assert.Equal("Album B", finalState.NowPlaying.Album);
        Assert.DoesNotContain(harness.OutputWriter.Writes, write => write.state.Title == "Track A" && write.state.Album == "Album A");
    }

    [Fact]
    public async Task RunDetectionAsync_ReturnsNotRunning_WhenNoDetectionSourcesProduceAResult()
    {
        using var harness = new BridgeServiceHarness();

        await harness.Service.InitializeAsync(CancellationToken.None);
        var state = await harness.Service.RunDetectionAsync(CancellationToken.None);
        var nowPlayingFile = harness.Service.GetNowPlayingFile();

        Assert.Equal("not_running", state.NowPlaying.Status);
        Assert.Equal("none", state.NowPlaying.Method);
        Assert.Equal("TIDAL not running", state.StatusMessage);
        Assert.Equal(1000, harness.Service.PollIntervalMs());
        Assert.Empty(harness.Service.GetArtwork());
        Assert.Equal("not_running", nowPlayingFile.Status);
        Assert.Equal("TIDAL", nowPlayingFile.Source);
    }

    [Fact]
    public async Task RunDetectionAsync_ReusesConfirmedCache_ForSameTrackWithMissingMetadata()
    {
        using var harness = new BridgeServiceHarness();
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Artist",
            Title = "Track",
            Album = "Album",
            DurationMs = 123000,
            ArtworkPath = "cover.jpg",
            ArtworkBytes = [4, 5, 6],
            Method = "media_session",
            Confidence = 0.93,
            MetadataSource = "itunes_search"
        });
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Artist",
            Title = "Track",
            Method = "media_session",
            Confidence = 0.4
        });
        harness.MetadataEnricher.NeedsEnrichmentHandler = static (_, _) => false;

        await harness.Service.InitializeAsync(CancellationToken.None);
        await harness.Service.RunDetectionAsync(CancellationToken.None);
        var state = await harness.Service.RunDetectionAsync(CancellationToken.None);

        Assert.Equal("Album", state.NowPlaying.Album);
        Assert.Equal(123000, state.NowPlaying.DurationMs);
        Assert.Equal("cover.jpg", state.NowPlaying.ArtworkPath);
        Assert.Equal([4, 5, 6], state.NowPlaying.ArtworkBytes);
        Assert.Equal("itunes_search", state.NowPlaying.MetadataSource);
    }

    private sealed class BridgeServiceHarness : IDisposable
    {
        private readonly string _tempDir;
        private readonly AppLogger _logger;

        public BridgeServiceHarness()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _logger = new AppLogger(_tempDir);

            Settings = new Settings
            {
                OutputFolder = Path.Combine(_tempDir, "output"),
                OverlayEnabled = false,
                OverlayPort = 17655,
                PollIntervalMs = 1000,
                EnableWindowTitleFallback = false,
                EnableDebugManualInput = false,
                MetadataProviderMode = nameof(MetadataProviderMode.Off),
                ThemeMode = nameof(ThemeMode.Dark),
                OverlaySettings = new OverlaySettings()
            };

            PlaybackDetector = new SequencePlaybackDetector();
            WindowTitleDetector = new ConfigurableWindowTitleDetector();
            ManualDetector = new ConfigurableManualDetector();
            MetadataEnricher = new ConfigurableMetadataEnricher();
            OverlayCoordinator = new ConfigurableOverlayCoordinator();
            OutputWriter = new RecordingOutputWriter();

            Service = new BridgeService(
                new HarnessSettingsStore(Settings),
                _logger,
                OutputWriter,
                PlaybackDetector,
                WindowTitleDetector,
                ManualDetector,
                MetadataEnricher,
                OverlayCoordinator,
                new OverlaySettingsSnapshotStore(),
                new PlaybackSnapshotStore(),
                new StubAppUpdateChecker());
        }

        public Settings Settings { get; }
        public SequencePlaybackDetector PlaybackDetector { get; }
        public ConfigurableWindowTitleDetector WindowTitleDetector { get; }
        public ConfigurableManualDetector ManualDetector { get; }
        public ConfigurableMetadataEnricher MetadataEnricher { get; }
        public ConfigurableOverlayCoordinator OverlayCoordinator { get; }
        public RecordingOutputWriter OutputWriter { get; }
        public BridgeService Service { get; }

        public void Dispose()
        {
            _logger.Dispose();
            Directory.Delete(_tempDir, true);
        }
    }

    private sealed class HarnessSettingsStore(Settings settings) : ISettingsStore
    {
        public Task<Settings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(settings);
        public Task SaveAsync(Settings updated, CancellationToken cancellationToken)
        {
            settings.OutputFolder = updated.OutputFolder;
            settings.OverlayEnabled = updated.OverlayEnabled;
            settings.OverlayPort = updated.OverlayPort;
            settings.PollIntervalMs = updated.PollIntervalMs;
            settings.EnableWindowTitleFallback = updated.EnableWindowTitleFallback;
            settings.EnableDebugManualInput = updated.EnableDebugManualInput;
            settings.StartMinimized = updated.StartMinimized;
            settings.LaunchAtStartup = updated.LaunchAtStartup;
            settings.MetadataProviderMode = updated.MetadataProviderMode;
            settings.ThemeMode = updated.ThemeMode;
            settings.OverlaySettings = updated.OverlaySettings;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingOutputWriter : IOutputWriter
    {
        public List<(string outputFolder, DetectionResult state)> Writes { get; } = [];

        public Task WriteAsync(string outputFolder, DetectionResult state, CancellationToken cancellationToken)
        {
            Writes.Add((outputFolder, BridgeStatePolicy.CloneDetection(state)));
            return Task.CompletedTask;
        }
    }

    private sealed class SequencePlaybackDetector : IPlaybackDetector
    {
        public Queue<DetectionResult?> Results { get; } = [];
        public Exception? Exception { get; set; }

        public Task<DetectionResult?> DetectAsync(CancellationToken cancellationToken)
        {
            if (Exception is not null)
            {
                throw Exception;
            }

            if (Results.Count == 0)
            {
                return Task.FromResult<DetectionResult?>(null);
            }

            var result = Results.Dequeue();
            return Task.FromResult(result is null ? null : BridgeStatePolicy.CloneDetection(result));
        }
    }

    private sealed class ConfigurableWindowTitleDetector : IWindowTitleDetector
    {
        public DetectionResult? Result { get; set; }
        public DetectionResult? Detect() => Result is null ? null : BridgeStatePolicy.CloneDetection(Result);
    }

    private sealed class ConfigurableManualDetector : IManualDetector
    {
        public DetectionResult? Result { get; set; }
        public DetectionResult? Detect(string input) => Result is null ? null : BridgeStatePolicy.CloneDetection(Result);
    }

    private sealed class ConfigurableOverlayCoordinator : IOverlayCoordinator
    {
        public Exception? ThrowOnConfigure { get; set; }
        public string Url => "";

        public Task ConfigureAsync(bool enabled, int port, CancellationToken cancellationToken)
        {
            if (ThrowOnConfigure is not null)
            {
                throw ThrowOnConfigure;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ConfigurableMetadataEnricher : IMetadataEnricher
    {
        public Func<DetectionResult, MetadataProviderMode, bool>? NeedsEnrichmentHandler { get; set; }
        public Func<DetectionResult, MetadataProviderMode, CancellationToken, Task<DetectionResult>>? EnrichAsyncHandler { get; set; }
        public Func<DetectionResult, MetadataProviderMode, CancellationToken, Task<DetectionResult>>? EnrichArtworkAsyncHandler { get; set; }

        public DetectionResult ApplyCached(DetectionResult input) => input;

        public bool NeedsEnrichment(DetectionResult input, MetadataProviderMode mode) =>
            NeedsEnrichmentHandler?.Invoke(input, mode) ?? false;

        public Task<DetectionResult> EnrichAsync(DetectionResult input, MetadataProviderMode mode, CancellationToken cancellationToken) =>
            EnrichAsyncHandler?.Invoke(BridgeStatePolicy.CloneDetection(input), mode, cancellationToken) ?? Task.FromResult(input);

        public Task<DetectionResult> EnrichArtworkAsync(DetectionResult input, MetadataProviderMode mode, CancellationToken cancellationToken) =>
            EnrichArtworkAsyncHandler?.Invoke(BridgeStatePolicy.CloneDetection(input), mode, cancellationToken) ?? Task.FromResult(input);
    }

    private sealed class StubAppUpdateChecker : IAppUpdateChecker
    {
        public string CurrentVersion => "0.2.0";
        public string ReleaseUrl => "https://github.com/LastUrsa/TideReader/releases";
        public Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken) => Task.FromResult(new UpdateInfo
        {
            CurrentVersion = CurrentVersion,
            LatestVersion = CurrentVersion,
            ReleaseUrl = ReleaseUrl,
            Message = "You're running the latest version."
        });
    }
}
