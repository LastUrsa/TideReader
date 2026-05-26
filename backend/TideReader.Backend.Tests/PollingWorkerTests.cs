using TideReader.Backend.Models;
using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class PollingWorkerTests
{
    [Fact]
    public async Task StartAsync_InitializesBridge_AndRunsDetectionLoop()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var logger = new AppLogger(tempDir);
            var settingsStore = new CountingSettingsStore();
            var outputWriter = new RecordingOutputWriter();
            var playbackDetector = new CountingPlaybackDetector();

            var bridge = new BridgeService(
                settingsStore,
                logger,
                outputWriter,
                playbackDetector,
                new NullWindowTitleDetector(),
                new NullManualDetector(),
                new PassiveMetadataEnricher(),
                new PassiveOverlayCoordinator(),
                new PlaybackSnapshotStore());

            using var worker = new PollingWorker(bridge);

            await worker.StartAsync(CancellationToken.None);
            await playbackDetector.FirstDetection.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await worker.StopAsync(CancellationToken.None);

            Assert.Equal(1, settingsStore.LoadCalls);
            Assert.True(playbackDetector.DetectCalls >= 1);
            Assert.NotEmpty(outputWriter.Writes);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private sealed class CountingSettingsStore : ISettingsStore
    {
        public int LoadCalls { get; private set; }

        public Task<Settings> LoadAsync(CancellationToken cancellationToken)
        {
            LoadCalls++;
            return Task.FromResult(new Settings
            {
                OutputFolder = @"C:\Temp\TideReaderWorkerTests",
                OverlayEnabled = false,
                OverlayPort = 17655,
                PollIntervalMs = 1000,
                EnableWindowTitleFallback = false,
                EnableDebugManualInput = false,
                MetadataProviderMode = nameof(MetadataProviderMode.Off)
            });
        }

        public Task SaveAsync(Settings settings, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingOutputWriter : IOutputWriter
    {
        public List<DetectionResult> Writes { get; } = [];

        public Task WriteAsync(string outputFolder, DetectionResult state, CancellationToken cancellationToken)
        {
            Writes.Add(BridgeStatePolicy.CloneDetection(state));
            return Task.CompletedTask;
        }
    }

    private sealed class CountingPlaybackDetector : IPlaybackDetector
    {
        public int DetectCalls { get; private set; }
        public TaskCompletionSource FirstDetection { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<DetectionResult?> DetectAsync(CancellationToken cancellationToken)
        {
            DetectCalls++;
            FirstDetection.TrySetResult();
            return Task.FromResult<DetectionResult?>(new DetectionResult
            {
                Status = "playing",
                Artist = "Worker Artist",
                Title = "Worker Title",
                Album = "Worker Album",
                Method = "media_session",
                Confidence = 0.91
            });
        }
    }

    private sealed class NullWindowTitleDetector : IWindowTitleDetector
    {
        public DetectionResult? Detect() => null;
    }

    private sealed class NullManualDetector : IManualDetector
    {
        public DetectionResult? Detect(string input) => null;
    }

    private sealed class PassiveMetadataEnricher : IMetadataEnricher
    {
        public DetectionResult ApplyCached(DetectionResult input) => input;
        public bool NeedsEnrichment(DetectionResult input, MetadataProviderMode mode) => false;
        public Task<DetectionResult> EnrichAsync(DetectionResult input, MetadataProviderMode mode, CancellationToken cancellationToken) => Task.FromResult(input);
        public Task<DetectionResult> EnrichArtworkAsync(DetectionResult input, MetadataProviderMode mode, CancellationToken cancellationToken) => Task.FromResult(input);
    }

    private sealed class PassiveOverlayCoordinator : IOverlayCoordinator
    {
        public string Url => "";
        public Task ConfigureAsync(bool enabled, int port, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
