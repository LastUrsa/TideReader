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
    Task<PlaybackDetectionOutcome> DetectAsync(DetectionResult previous, Settings settings, CancellationToken cancellationToken);
}

public sealed record MediaSessionSnapshot(
    string SessionId,
    string SourceAppId,
    string Browser,
    string Site,
    bool IsPlaying,
    bool IsPaused,
    string Title,
    string Artist,
    string Album,
    long DurationMs,
    DateTimeOffset LastUpdatedUtc,
    byte[] ArtworkBytes);

public sealed record AudioSessionSnapshot(
    string SessionId,
    string EndpointId,
    int ProcessId,
    string ProcessName,
    string DisplayName,
    string IconPath,
    string SessionIdentifier,
    string SessionInstanceIdentifier,
    string State,
    bool IsSystemSoundsSession,
    bool IsMuted,
    float PeakLevel,
    DateTimeOffset CapturedAtUtc);

public sealed record AudioEndpointSnapshot(
    string EndpointId,
    string FriendlyName,
    string DeviceState,
    bool IsDefaultMultimedia);

public sealed record AudioSessionSnapshotResult(
    IReadOnlyList<AudioEndpointSnapshot> Endpoints,
    IReadOnlyList<AudioSessionSnapshot> Sessions);

public sealed record PlaybackDetectionOutcome(DetectionResult? Result, BrowserDebugState BrowserDebug);

public interface IMediaSessionSnapshotProvider
{
    Task<IReadOnlyList<MediaSessionSnapshot>> GetCurrentAsync(CancellationToken cancellationToken);
}

public interface IAudioSessionSnapshotProvider
{
    Task<AudioSessionSnapshotResult> GetCurrentAsync(CancellationToken cancellationToken);
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

public interface IOverlaySettingsSnapshotStore
{
    void Update(OverlaySettings settings);
    OverlaySettings Get();
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

public interface ISystemFontCatalog
{
    IReadOnlyList<string> GetFontFamilies();
}

public interface IStartupRegistration
{
    void Sync(bool enabled);
}

public interface IAppUpdateChecker
{
    string CurrentVersion { get; }
    string ReleaseUrl { get; }
    Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken);
}

public interface IExternalUrlLauncher
{
    void OpenUrl(string url);
}
