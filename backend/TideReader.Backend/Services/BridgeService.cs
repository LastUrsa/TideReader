using TideReader.Backend.Models;
using System.Text.RegularExpressions;

namespace TideReader.Backend.Services;

public sealed class BridgeService
{
    private readonly ISettingsStore _settingsStore;
    private readonly AppLogger _logger;
    private readonly IOutputWriter _outputWriter;
    private readonly IPlaybackDetector _mediaSessionDetector;
    private readonly IWindowTitleDetector _windowTitleDetector;
    private readonly IManualDetector _manualDetector;
    private readonly IMetadataEnricher _metadataEnricher;
    private readonly IOverlayCoordinator _overlayServer;
    private readonly IOverlaySettingsSnapshotStore _overlaySettingsSnapshotStore;
    private readonly IPlaybackSnapshotStore _snapshotStore;
    private readonly Lock _lock = new();
    private readonly HashSet<string> _pendingEnrichmentKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _detectionTimesUtc = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex HexColorPattern = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    private Settings _settings = new();
    private DetectionResult _state = new();
    private DetectionResult _confirmed = new();
    private string _manualInput = "";
    private string _lastError = "";
    private string _statusMessage = "Waiting for TIDAL";
    private MetadataProviderMode _metadataProviderMode = MetadataProviderMode.MusicBrainzWithFallbacks;
    private long _artworkRevision = 1;

    public event Action<Settings>? SettingsChanged;

    public BridgeService(ISettingsStore settingsStore, AppLogger logger, IOutputWriter outputWriter, IPlaybackDetector mediaSessionDetector, IWindowTitleDetector windowTitleDetector, IManualDetector manualDetector, IMetadataEnricher metadataEnricher, IOverlayCoordinator overlayServer, IOverlaySettingsSnapshotStore overlaySettingsSnapshotStore, IPlaybackSnapshotStore snapshotStore)
    {
        _settingsStore = settingsStore;
        _logger = logger;
        _outputWriter = outputWriter;
        _mediaSessionDetector = mediaSessionDetector;
        _windowTitleDetector = windowTitleDetector;
        _manualDetector = manualDetector;
        _metadataEnricher = metadataEnricher;
        _overlayServer = overlayServer;
        _overlaySettingsSnapshotStore = overlaySettingsSnapshotStore;
        _snapshotStore = snapshotStore;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _settings = await _settingsStore.LoadAsync(cancellationToken);
        NormalizeSettings(_settings, fallbackInvalidOutputFolder: true);
        _overlaySettingsSnapshotStore.Update(_settings.OverlaySettings);
        _metadataProviderMode = ParseMetadataProviderMode(_settings.MetadataProviderMode);
        await ConfigureOverlayAsync(cancellationToken);
        SettingsChanged?.Invoke(CloneSettings(_settings));
        _logger.Info("startup");
    }

    public AppState GetState()
    {
        lock (_lock)
        {
            return new AppState
            {
                Settings = CloneSettings(_settings),
                NowPlaying = BridgeStatePolicy.CloneDetection(_state),
                ArtworkRevision = _artworkRevision,
                OutputFolder = _settings.OutputFolder,
                OverlayUrl = _overlayServer.Url,
                LogPath = _logger.Path,
                LastError = _lastError,
                ManualInput = _manualInput,
                StartupReady = true,
                StatusMessage = _statusMessage
            };
        }
    }

    public async Task<AppState> SaveSettingsAsync(Settings settings, CancellationToken cancellationToken)
    {
        NormalizeSettings(settings, fallbackInvalidOutputFolder: false);
        await _settingsStore.SaveAsync(settings, cancellationToken);

        lock (_lock)
        {
            _settings = settings;
            _overlaySettingsSnapshotStore.Update(settings.OverlaySettings);
            _metadataProviderMode = ParseMetadataProviderMode(settings.MetadataProviderMode);
        }

        try
        {
            await ConfigureOverlayAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _lastError = ex.Message;
            }
            _logger.Info($"overlay error: {ex.Message}");
        }

        _logger.Info($"settings updated: output={settings.OutputFolder} overlay={settings.OverlayEnabled} port={settings.OverlayPort} interval={settings.PollIntervalMs}ms metadata={settings.MetadataProviderMode}");
        SettingsChanged?.Invoke(CloneSettings(settings));
        return GetState();
    }

    public AppState SetManualInput(string input)
    {
        lock (_lock)
        {
            _manualInput = input.Trim();
        }
        return GetState();
    }

    public async Task<AppState> RunDetectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await DetectAsync(cancellationToken);
            var previous = GetCurrentStateSnapshot();
            result = BridgeStatePolicy.ClearSuspectCarryoverArtwork(previous, result);
            if (result.ArtworkBytes.Length == 0 && previous.ArtworkBytes.Length > 0 && !previous.ArtworkBytes.AsSpan().SequenceEqual(result.ArtworkBytes))
            {
                _logger.Info($"suspect artwork carryover cleared: previous=\"{previous.Artist} - {previous.Title}\" current=\"{result.Artist} - {result.Title}\"");
            }
            var beforeSuppressionHadArtwork = result.ArtworkBytes.Length > 0;
            result = BridgeStatePolicy.SuppressArtworkUntilAlbumResolved(result, CurrentMetadataProviderMode());
            if (beforeSuppressionHadArtwork && result.ArtworkBytes.Length == 0)
            {
                _logger.Info($"artwork deferred until album resolves: artist=\"{result.Artist}\" title=\"{result.Title}\"");
            }
            var metadataMode = CurrentMetadataProviderMode();
            result = ApplyCache(result);
            result = _metadataEnricher.ApplyCached(result);
            await _outputWriter.WriteAsync(CurrentOutputFolder(), result, cancellationToken);

            lock (_lock)
            {
                var changed = !BridgeStatePolicy.Equivalent(_state, result);
                var artworkChanged = BridgeStatePolicy.ArtworkChanged(_state, result);
                _state = result;
                if (artworkChanged)
                {
                    _artworkRevision++;
                }
                _snapshotStore.Update(result);
                _statusMessage = Describe(result);
                _lastError = "";
                if (changed)
                {
                        var trackKey = BridgeStatePolicy.TrackKey(result);
                    if (!string.IsNullOrWhiteSpace(trackKey))
                    {
                        _detectionTimesUtc[trackKey] = DateTime.UtcNow;
                    }

                    _logger.Info($"track detection changed: status={result.Status} title=\"{result.Title}\" artist=\"{result.Artist}\" method={result.Method} confidence={result.Confidence:F2} metadata={(string.IsNullOrWhiteSpace(result.Album) || result.ArtworkBytes.Length == 0 ? "pending" : "ready")} artwork={(result.ArtworkBytes.Length > 0 ? "present" : "missing")}");
                }
            }

            StartBackgroundEnrichmentIfNeeded(result, metadataMode);
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _lastError = ex.Message;
                if (BridgeStatePolicy.ArtworkChanged(_state, new DetectionResult()))
                {
                    _artworkRevision++;
                }
                _state = new DetectionResult { Status = "not_running", Source = "TIDAL", Method = "none", Confidence = 0 };
                _snapshotStore.Update(_state);
                _statusMessage = "TIDAL not running";
            }
            _logger.Info($"poll error: {ex.Message}");
        }

        return GetState();
    }

    public byte[] GetArtwork()
    {
        lock (_lock)
        {
            return _state.ArtworkBytes.ToArray();
        }
    }

    public NowPlayingFile GetNowPlayingFile()
    {
        lock (_lock)
        {
            return new NowPlayingFile
            {
                Status = _state.Status,
                Title = _state.Title,
                Artist = _state.Artist,
                Album = _state.Album,
                DurationMs = _state.DurationMs,
                ArtworkPath = _state.ArtworkPath,
                Source = _state.Source,
                Confidence = _state.Confidence
            };
        }
    }

    public int PollIntervalMs()
    {
        lock (_lock)
        {
            return _settings.PollIntervalMs;
        }
    }

    private async Task<DetectionResult> DetectAsync(CancellationToken cancellationToken)
    {
        var result = await _mediaSessionDetector.DetectAsync(cancellationToken);
        if (result is not null)
        {
            return result;
        }

        Settings settings;
        string manualInput;
        lock (_lock)
        {
            settings = CloneSettings(_settings);
            manualInput = _manualInput;
        }

        if (settings.EnableWindowTitleFallback)
        {
            result = _windowTitleDetector.Detect();
            if (result is not null)
            {
                return result;
            }
        }

        if (settings.EnableDebugManualInput)
        {
            result = _manualDetector.Detect(manualInput);
            if (result is not null)
            {
                return result;
            }
        }

        return new DetectionResult
        {
            Status = "not_running",
            Source = "TIDAL",
            Method = "none",
            Confidence = 0
        };
    }

    private DetectionResult ApplyCache(DetectionResult current)
    {
        lock (_lock)
        {
            current = BridgeStatePolicy.ApplyConfirmedCache(current, _confirmed);
            var key = BridgeStatePolicy.TrackKey(current);

            if (!string.IsNullOrWhiteSpace(key) && current.Confidence >= 0.5)
            {
                _confirmed = BridgeStatePolicy.CloneDetection(current);
            }
        }

        return current;
    }

    private string CurrentOutputFolder()
    {
        lock (_lock)
        {
            return _settings.OutputFolder;
        }
    }

    private MetadataProviderMode CurrentMetadataProviderMode()
    {
        lock (_lock)
        {
            return _metadataProviderMode;
        }
    }

    private void StartBackgroundEnrichmentIfNeeded(DetectionResult result, MetadataProviderMode mode)
    {
        if (!_metadataEnricher.NeedsEnrichment(result, mode))
        {
            return;
        }

        var key = BridgeStatePolicy.TrackKey(result);
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        lock (_lock)
        {
            if (!_pendingEnrichmentKeys.Add(key))
            {
                return;
            }
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var metadataEnriched = await _metadataEnricher.EnrichAsync(BridgeStatePolicy.CloneDetection(result), mode, CancellationToken.None);
                var currentBaseline = result;
                if (BridgeStatePolicy.AddsEnrichment(result, metadataEnriched))
                {
                    await ApplyBackgroundEnrichmentAsync(key, metadataEnriched);
                    currentBaseline = BridgeStatePolicy.MergeForComparison(result, metadataEnriched);
                }

                if (currentBaseline.ArtworkBytes.Length == 0)
                {
                    var artworkEnriched = await _metadataEnricher.EnrichArtworkAsync(BridgeStatePolicy.CloneDetection(currentBaseline), mode, CancellationToken.None);
                    if (BridgeStatePolicy.AddsEnrichment(currentBaseline, artworkEnriched))
                    {
                        await ApplyBackgroundEnrichmentAsync(key, artworkEnriched);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Info($"background enrichment error: {ex.Message}");
            }
            finally
            {
                lock (_lock)
                {
                    _pendingEnrichmentKeys.Remove(key);
                }
            }
        });
    }

    private async Task ApplyBackgroundEnrichmentAsync(string key, DetectionResult enriched)
    {
        DetectionResult? snapshotToWrite = null;
        string outputFolder = "";
        double? elapsedMs = null;

        lock (_lock)
        {
            if (BridgeStatePolicy.TrackKey(_state) != key)
            {
                return;
            }

            var merged = BridgeStatePolicy.CloneDetection(_state);
            BridgeStatePolicy.MergeEnrichment(merged, enriched);
            if (BridgeStatePolicy.Equivalent(_state, merged))
            {
                return;
            }

            if (BridgeStatePolicy.ArtworkChanged(_state, merged))
            {
                _artworkRevision++;
            }
            _state = merged;
            _confirmed = BridgeStatePolicy.CloneDetection(merged);
            _snapshotStore.Update(merged);
            _statusMessage = Describe(merged);
            outputFolder = _settings.OutputFolder;
            snapshotToWrite = BridgeStatePolicy.CloneDetection(merged);

            if (_detectionTimesUtc.TryGetValue(key, out var detectedAtUtc))
            {
                elapsedMs = (DateTime.UtcNow - detectedAtUtc).TotalMilliseconds;
            }
        }

        await _outputWriter.WriteAsync(outputFolder, snapshotToWrite, CancellationToken.None);
        var elapsedPart = elapsedMs is null ? "" : $" elapsedSinceDetectMs={elapsedMs.Value:F0}";
        _logger.Info($"background enrichment applied: artist=\"{snapshotToWrite.Artist}\" title=\"{snapshotToWrite.Title}\" source={snapshotToWrite.MetadataSource} artwork={(snapshotToWrite.ArtworkBytes.Length > 0 ? "present" : "missing")}{elapsedPart}");
    }

    private static string Describe(DetectionResult result) =>
        result.Status switch
        {
            "playing" when !string.IsNullOrWhiteSpace(result.Artist) && !string.IsNullOrWhiteSpace(result.Title) => $"Playing {result.Artist} - {result.Title}",
            "playing" when !string.IsNullOrWhiteSpace(result.Title) => $"Playing {result.Title}",
            "paused" => "TIDAL paused",
            _ => "TIDAL not running"
        };

    private static Settings CloneSettings(Settings settings) => new()
    {
        OutputFolder = settings.OutputFolder,
        OverlayEnabled = settings.OverlayEnabled,
        OverlayPort = settings.OverlayPort,
        PollIntervalMs = settings.PollIntervalMs,
        EnableWindowTitleFallback = settings.EnableWindowTitleFallback,
        EnableDebugManualInput = settings.EnableDebugManualInput,
        StartMinimized = settings.StartMinimized,
        LaunchAtStartup = settings.LaunchAtStartup,
        MetadataProviderMode = settings.MetadataProviderMode,
        ThemeMode = settings.ThemeMode,
        OverlaySettings = CloneOverlaySettings(settings.OverlaySettings)
    };

    private DetectionResult GetCurrentStateSnapshot()
    {
        lock (_lock)
        {
            return BridgeStatePolicy.CloneDetection(_state);
        }
    }

    private void NormalizeSettings(Settings settings, bool fallbackInvalidOutputFolder)
    {
        if (string.IsNullOrWhiteSpace(settings.OutputFolder))
        {
            settings.OutputFolder = Defaults.OutputFolder;
        }

        try
        {
            settings.OutputFolder = OutputPathPolicy.NormalizeFolderPath(settings.OutputFolder);
        }
        catch (ArgumentException) when (fallbackInvalidOutputFolder)
        {
            settings.OutputFolder = OutputPathPolicy.NormalizeFolderPath(Defaults.OutputFolder);
            _logger.Info("invalid output folder in persisted settings; reverted to default output folder");
        }

        if (settings.OverlayPort <= 0)
        {
            settings.OverlayPort = 17655;
        }
        if (settings.PollIntervalMs < 250)
        {
            settings.PollIntervalMs = 1000;
        }
        if (!Enum.TryParse<MetadataProviderMode>(settings.MetadataProviderMode, ignoreCase: true, out _))
        {
            settings.MetadataProviderMode = nameof(MetadataProviderMode.MusicBrainzWithFallbacks);
        }
        if (!Enum.TryParse<ThemeMode>(settings.ThemeMode, ignoreCase: true, out _))
        {
            settings.ThemeMode = nameof(ThemeMode.Dark);
        }

        NormalizeOverlaySettings(settings);
    }

    private async Task ConfigureOverlayAsync(CancellationToken cancellationToken)
    {
        Settings snapshot;
        lock (_lock)
        {
            snapshot = CloneSettings(_settings);
        }

        await _overlayServer.ConfigureAsync(snapshot.OverlayEnabled, snapshot.OverlayPort, cancellationToken);
    }

    private static MetadataProviderMode ParseMetadataProviderMode(string value) =>
        Enum.TryParse<MetadataProviderMode>(value, ignoreCase: true, out var parsed)
            ? parsed
            : MetadataProviderMode.MusicBrainzWithFallbacks;

    private static OverlaySettings CloneOverlaySettings(OverlaySettings settings) => new()
    {
        SongTextStyle = CloneOverlayTextStyle(settings.SongTextStyle),
        ArtistTextStyle = CloneOverlayTextStyle(settings.ArtistTextStyle),
        AlbumTextStyle = CloneOverlayTextStyle(settings.AlbumTextStyle),
        ImageSizePx = settings.ImageSizePx,
        BackgroundColorHex = settings.BackgroundColorHex,
        ImagePosition = settings.ImagePosition,
        TextAlign = settings.TextAlign,
        ShowAppName = settings.ShowAppName,
        ShowPlaybackState = settings.ShowPlaybackState
    };

    private static OverlayTextStyle CloneOverlayTextStyle(OverlayTextStyle style) => new()
    {
        FontFamily = style.FontFamily,
        ColorHex = style.ColorHex,
        FontSizePx = style.FontSizePx,
        MaxCharacters = style.MaxCharacters,
        Bold = style.Bold,
        Italic = style.Italic,
        Underline = style.Underline
    };

    private static void NormalizeOverlaySettings(Settings settings)
    {
        settings.OverlaySettings ??= new OverlaySettings();
        var defaults = new OverlaySettings();

        NormalizeOverlayTextStyle(settings.OverlaySettings.SongTextStyle ??= new OverlayTextStyle(), defaults.SongTextStyle);
        NormalizeOverlayTextStyle(settings.OverlaySettings.ArtistTextStyle ??= new OverlayTextStyle(), defaults.ArtistTextStyle);
        NormalizeOverlayTextStyle(settings.OverlaySettings.AlbumTextStyle ??= new OverlayTextStyle(), defaults.AlbumTextStyle);

        if (settings.OverlaySettings.ImageSizePx <= 0)
        {
            settings.OverlaySettings.ImageSizePx = defaults.ImageSizePx;
        }

        settings.OverlaySettings.BackgroundColorHex = NormalizeHexColor(
            settings.OverlaySettings.BackgroundColorHex,
            defaults.BackgroundColorHex);
        settings.OverlaySettings.ImagePosition = NormalizeOverlayChoice(
            settings.OverlaySettings.ImagePosition,
            defaults.ImagePosition,
            ["Left", "Right"]);
        settings.OverlaySettings.TextAlign = NormalizeOverlayChoice(
            settings.OverlaySettings.TextAlign,
            defaults.TextAlign,
            ["Left", "Center", "Right"]);
    }

    private static void NormalizeOverlayTextStyle(OverlayTextStyle style, OverlayTextStyle defaults)
    {
        if (string.IsNullOrWhiteSpace(style.FontFamily))
        {
            style.FontFamily = defaults.FontFamily;
        }

        if (style.FontSizePx <= 0)
        {
            style.FontSizePx = defaults.FontSizePx;
        }

        if (style.MaxCharacters < 0)
        {
            style.MaxCharacters = defaults.MaxCharacters;
        }

        style.ColorHex = NormalizeHexColor(style.ColorHex, defaults.ColorHex);
    }

    private static string NormalizeHexColor(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        return HexColorPattern.IsMatch(trimmed)
            ? trimmed.ToUpperInvariant()
            : fallback;
    }

    private static string NormalizeOverlayChoice(string? value, string fallback, IReadOnlyList<string> allowedValues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        foreach (var allowedValue in allowedValues)
        {
            if (string.Equals(trimmed, allowedValue, StringComparison.OrdinalIgnoreCase))
            {
                return allowedValue;
            }
        }

        return fallback;
    }
}
