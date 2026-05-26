using TideReader.Backend.Models;

namespace TideReader.Backend.Services;

public interface ISettingsStore
{
    Task<Settings> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(Settings settings, CancellationToken cancellationToken);
}

public interface IOutputWriter
{
    Task WriteAsync(string outputFolder, DetectionResult state, CancellationToken cancellationToken);
}

public interface IPlaybackDetector
{
    Task<DetectionResult?> DetectAsync(CancellationToken cancellationToken);
}

public sealed record MediaSessionSnapshot(
    string SourceAppId,
    bool IsPaused,
    string Title,
    string Artist,
    string Album,
    long DurationMs,
    byte[] ArtworkBytes);

public interface IMediaSessionSnapshotProvider
{
    Task<MediaSessionSnapshot?> GetCurrentAsync(CancellationToken cancellationToken);
}

public interface IWindowTitleDetector
{
    DetectionResult? Detect();
}

public interface IManualDetector
{
    DetectionResult? Detect(string input);
}

public interface IMetadataEnricher
{
    DetectionResult ApplyCached(DetectionResult input);
    bool NeedsEnrichment(DetectionResult input, MetadataProviderMode mode);
    Task<DetectionResult> EnrichAsync(DetectionResult input, MetadataProviderMode mode, CancellationToken cancellationToken);
    Task<DetectionResult> EnrichArtworkAsync(DetectionResult input, MetadataProviderMode mode, CancellationToken cancellationToken);
}

public interface IOverlayCoordinator
{
    string Url { get; }
    Task ConfigureAsync(bool enabled, int port, CancellationToken cancellationToken);
}

public interface IPlaybackSnapshotStore
{
    void Update(DetectionResult state);
    NowPlayingFile GetNowPlaying();
    byte[] GetArtwork();
}

public interface IFolderDialogService
{
    Task<string?> ChooseOutputFolderAsync();
    Task OpenFolderAsync(string path);
}

public interface IFolderPicker
{
    Task<string?> ChooseOutputFolderAsync();
}

public interface IFolderLauncher
{
    void OpenFolder(string path);
}

public interface IStartupRegistration
{
    void Sync(bool enabled);
}
