using TideReader.Backend.Models;
using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class BridgeServiceOrchestrationTests
{
    [Fact]
    public async Task RunDetectionAsync_SuppressesMediaSessionArtwork_WhenAlbumMissing()
    {
        using var harness = new BridgeServiceHarness(
            detectorResult: new DetectionResult
            {
                Status = "playing",
                Artist = "Artist",
                Title = "Track",
                Method = "media_session",
                ArtworkPath = "cover.jpg",
                ArtworkBytes = [1, 2, 3],
                Confidence = 0.9
            },
            metadataEnricher: new FakeMetadataEnricher
            {
                NeedsEnrichmentHandler = static (_, _) => false
            });

        var state = await harness.Service.RunDetectionAsync(CancellationToken.None);

        Assert.Equal("", state.NowPlaying.ArtworkPath);
        Assert.Empty(state.NowPlaying.ArtworkBytes);
        Assert.Equal("Track", state.NowPlaying.Title);
    }

    [Fact]
    public async Task RunDetectionAsync_AppliesMetadataBeforeArtwork_InBackgroundFlow()
    {
        var metadataGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var artworkGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var harness = new BridgeServiceHarness(
            detectorResult: new DetectionResult
            {
                Status = "playing",
                Artist = "Artist",
                Title = "Track",
                Method = "media_session",
                Confidence = 0.9
            },
            metadataEnricher: new FakeMetadataEnricher
            {
                NeedsEnrichmentHandler = static (_, _) => true,
                EnrichAsyncHandler = async (input, _, _) =>
                {
                    await metadataGate.Task;
                    return new DetectionResult
                    {
                        Artist = input.Artist,
                        Title = input.Title,
                        Album = "Album",
                        DurationMs = 123000,
                        MetadataSource = "itunes_search",
                        Confidence = 0.95
                    };
                },
                EnrichArtworkAsyncHandler = async (input, _, _) =>
                {
                    await artworkGate.Task;
                    return new DetectionResult
                    {
                        Artist = input.Artist,
                        Title = input.Title,
                        Album = input.Album,
                        DurationMs = input.DurationMs,
                        MetadataSource = input.MetadataSource,
                        ArtworkPath = "cover.jpg",
                        ArtworkBytes = [9, 8, 7],
                        Confidence = 0.95
                    };
                }
            });

        _ = harness.Service.RunDetectionAsync(CancellationToken.None);

        metadataGate.SetResult();
        await WaitUntilAsync(() => harness.Service.GetState().NowPlaying.Album == "Album");

        var metadataState = harness.Service.GetState();
        Assert.Equal("Album", metadataState.NowPlaying.Album);
        Assert.Equal("", metadataState.NowPlaying.ArtworkPath);

        artworkGate.SetResult();
        await WaitUntilAsync(() => harness.Service.GetState().NowPlaying.ArtworkPath == "cover.jpg");

        var artworkState = harness.Service.GetState();
        Assert.Equal("cover.jpg", artworkState.NowPlaying.ArtworkPath);
        Assert.Equal([9, 8, 7], artworkState.NowPlaying.ArtworkBytes);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, int timeoutMs = 1500)
    {
        var started = Environment.TickCount64;
        while (!predicate())
        {
            if (Environment.TickCount64 - started > timeoutMs)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(20);
        }
    }

    private sealed class BridgeServiceHarness : IDisposable
    {
        private readonly string _tempDir;
        private readonly AppLogger _logger;

        public BridgeServiceHarness(DetectionResult detectorResult, FakeMetadataEnricher metadataEnricher)
        {
            _tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _logger = new AppLogger(_tempDir);

            Service = new BridgeService(
                new FakeSettingsStore(),
                _logger,
                new FakeOutputWriter(),
                new FakePlaybackDetector(detectorResult),
                new FakeWindowTitleDetector(),
                new FakeManualDetector(),
                metadataEnricher,
                new FakeOverlayCoordinator(),
                new OverlaySettingsSnapshotStore(),
                new PlaybackSnapshotStore());
        }

        public BridgeService Service { get; }

        public void Dispose()
        {
            _logger.Dispose();
            Directory.Delete(_tempDir, true);
        }
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        private readonly Settings _settings = new()
        {
            OutputFolder = @"C:\Temp\TideReaderTest",
            EnableDebugManualInput = false,
            EnableWindowTitleFallback = false
        };

        public Task<Settings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(_settings);

        public Task SaveAsync(Settings settings, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeOutputWriter : IOutputWriter
    {
        public Task WriteAsync(string outputFolder, DetectionResult state, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakePlaybackDetector(DetectionResult result) : IPlaybackDetector
    {
        public Task<PlaybackDetectionOutcome> DetectAsync(DetectionResult previous, Settings settings, CancellationToken cancellationToken) =>
            Task.FromResult(new PlaybackDetectionOutcome(BridgeStatePolicy.CloneDetection(result), new BrowserDebugState()));
    }

    private sealed class FakeWindowTitleDetector : IWindowTitleDetector
    {
        public DetectionResult? Detect() => null;
    }

    private sealed class FakeManualDetector : IManualDetector
    {
        public DetectionResult? Detect(string input) => null;
    }

    private sealed class FakeOverlayCoordinator : IOverlayCoordinator
    {
        public string Url => "";

        public Task ConfigureAsync(bool enabled, int port, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeMetadataEnricher : IMetadataEnricher
    {
        public Func<DetectionResult, MetadataProviderMode, bool>? NeedsEnrichmentHandler { get; init; }
        public Func<DetectionResult, MetadataProviderMode, CancellationToken, Task<DetectionResult>>? EnrichAsyncHandler { get; init; }
        public Func<DetectionResult, MetadataProviderMode, CancellationToken, Task<DetectionResult>>? EnrichArtworkAsyncHandler { get; init; }

        public DetectionResult ApplyCached(DetectionResult input) => input;

        public bool NeedsEnrichment(DetectionResult input, MetadataProviderMode mode) =>
            NeedsEnrichmentHandler?.Invoke(input, mode) ?? false;

        public Task<DetectionResult> EnrichAsync(DetectionResult input, MetadataProviderMode mode, CancellationToken cancellationToken) =>
            EnrichAsyncHandler?.Invoke(input, mode, cancellationToken) ?? Task.FromResult(input);

        public Task<DetectionResult> EnrichArtworkAsync(DetectionResult input, MetadataProviderMode mode, CancellationToken cancellationToken) =>
            EnrichArtworkAsyncHandler?.Invoke(input, mode, cancellationToken) ?? Task.FromResult(input);
    }
}
