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
    private readonly IAppUpdateChecker _appUpdateChecker;
    private readonly Lock _lock = new();
    private readonly HashSet<string> _pendingEnrichmentKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _detectionTimesUtc = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex HexColorPattern = new("^#(?:[0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$", RegexOptions.Compiled);

    private Settings _settings = new();
    private DetectionResult _state = new();
    private DetectionResult _confirmed = new();
    private string _manualInput = "";
    private string _lastError = "";
    private string _statusMessage = "Waiting for TIDAL";
    private MetadataProviderMode _metadataProviderMode = MetadataProviderMode.MusicBrainzWithFallbacks;
    private long _artworkRevision = 1;
    private BrowserDebugState _browserDebug = new();
    private DateTimeOffset? _lastPlayingDetectedUtc;

    public event Action<Settings>? SettingsChanged;

    public BridgeService(ISettingsStore settingsStore, AppLogger logger, IOutputWriter outputWriter, IPlaybackDetector mediaSessionDetector, IWindowTitleDetector windowTitleDetector, IManualDetector manualDetector, IMetadataEnricher metadataEnricher, IOverlayCoordinator overlayServer, IOverlaySettingsSnapshotStore overlaySettingsSnapshotStore, IPlaybackSnapshotStore snapshotStore, IAppUpdateChecker? appUpdateChecker = null)
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
        _appUpdateChecker = appUpdateChecker ?? new FallbackAppUpdateChecker();
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
                AppVersion = _appUpdateChecker.CurrentVersion,
                ArtworkRevision = _artworkRevision,
                OutputFolder = _settings.OutputFolder,
                OverlayUrl = _overlayServer.Url,
                LogPath = _logger.Path,
                LastError = _lastError,
                ManualInput = _manualInput,
                StartupReady = true,
                StatusMessage = _statusMessage,
                BrowserDebug = CloneBrowserDebugState(_browserDebug)
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
                if (result.Status == "playing")
                {
                    _lastPlayingDetectedUtc = DateTimeOffset.UtcNow;
                }
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
                _state = new DetectionResult { Status = "not_running", Source = "TIDAL", Method = "none", Confidence = 0, Provider = "tidal" };
                _snapshotStore.Update(_state);
                _statusMessage = "TIDAL not running";
                _browserDebug = new BrowserDebugState();
                _lastPlayingDetectedUtc = null;
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
                Confidence = _state.Confidence,
                Provider = _state.Provider,
                Browser = _state.Browser,
                Site = _state.Site
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
        Settings settings;
        string manualInput;
        DetectionResult previous;
        lock (_lock)
        {
            settings = CloneSettings(_settings);
            manualInput = _manualInput;
            previous = BridgeStatePolicy.CloneDetection(_state);
        }

        var playback = await _mediaSessionDetector.DetectAsync(previous, settings, cancellationToken);
        lock (_lock)
        {
            _browserDebug = CloneBrowserDebugState(playback.BrowserDebug);
        }

        if (playback.Result is not null)
        {
            return playback.Result;
        }

        var heldPrevious = TryHoldPreviousPlayback(previous, settings);
        if (heldPrevious is not null)
        {
            return heldPrevious;
        }

        if (settings.EnableWindowTitleFallback)
        {
            var result = _windowTitleDetector.Detect();
            if (result is not null)
            {
                return result;
            }
        }

        if (settings.EnableDebugManualInput)
        {
            var result = _manualDetector.Detect(manualInput);
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
            Confidence = 0,
            Provider = settings.BrowserSettings.ActiveSourceMode.Equals("browser", StringComparison.OrdinalIgnoreCase) ? "browser" : "tidal"
        };
    }

    private DetectionResult? TryHoldPreviousPlayback(DetectionResult previous, Settings settings)
    {
        if (previous.Status != "playing")
        {
            return null;
        }

        var cooldownMs = settings.BrowserSettings.SourceSwitchCooldownMs;
        if (cooldownMs <= 0)
        {
            return null;
        }

        DateTimeOffset? lastPlayingDetectedUtc;
        lock (_lock)
        {
            lastPlayingDetectedUtc = _lastPlayingDetectedUtc;
        }

        if (lastPlayingDetectedUtc is null || (DateTimeOffset.UtcNow - lastPlayingDetectedUtc.Value).TotalMilliseconds > cooldownMs)
        {
            return null;
        }

        var held = BridgeStatePolicy.CloneDetection(previous);
        held.SelectionReason = "selected: cooldown active after session loss";
        return held;
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
            "paused" => $"{result.Source} paused",
            _ => result.Provider == "browser" ? "Browser not running" : "TIDAL not running"
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
        OverlaySettings = CloneOverlaySettings(settings.OverlaySettings),
        BrowserSettings = CloneBrowserSettings(settings.BrowserSettings)
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
        NormalizeBrowserSettings(settings);
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
        OverlayContainerStyle = CloneOverlayContainerStyle(settings.OverlayContainerStyle),
        StatusPillStyle = CloneStatusPillStyle(settings.StatusPillStyle),
        ImagePosition = settings.ImagePosition,
        TextAlign = settings.TextAlign,
        ShowAppName = settings.ShowAppName,
        ShowPlaybackState = settings.ShowPlaybackState,
        ShowPlaybackProvider = settings.ShowPlaybackProvider
    };

    private static BrowserSettings CloneBrowserSettings(BrowserSettings settings) => new()
    {
        Enabled = settings.Enabled,
        ActiveSourceMode = settings.ActiveSourceMode,
        SupportedBrowsers = new BrowserSupportSettings
        {
            ChromeEnabled = settings.SupportedBrowsers.ChromeEnabled,
            EdgeEnabled = settings.SupportedBrowsers.EdgeEnabled,
            FirefoxEnabled = settings.SupportedBrowsers.FirefoxEnabled,
            BraveEnabled = settings.SupportedBrowsers.BraveEnabled,
            OperaEnabled = settings.SupportedBrowsers.OperaEnabled
        },
        SourcePriority = settings.SourcePriority.ToList(),
        SourceSwitchCooldownMs = settings.SourceSwitchCooldownMs,
        AllowGenericPlayback = settings.AllowGenericPlayback,
        PreferTidalOverBrowser = settings.PreferTidalOverBrowser,
        MetadataCleanupEnabled = settings.MetadataCleanupEnabled,
        BrowserArtworkEnabled = settings.BrowserArtworkEnabled,
        YouTubeVideoImageFallbackEnabled = settings.YouTubeVideoImageFallbackEnabled,
        DebugLoggingEnabled = settings.DebugLoggingEnabled,
        IgnorePausedSessions = settings.IgnorePausedSessions,
        IgnoreStaleSessions = settings.IgnoreStaleSessions,
        StaleSessionAfterSeconds = settings.StaleSessionAfterSeconds,
        ShowRawBrowserMetadata = settings.ShowRawBrowserMetadata
    };

    private static BrowserDebugState CloneBrowserDebugState(BrowserDebugState state) => new()
    {
        Sessions = state.Sessions.Select(session => new BrowserSessionDebugInfo
        {
            Provider = session.Provider,
            Browser = session.Browser,
            Site = session.Site,
            PlaybackState = session.PlaybackState,
            SourceAppId = session.SourceAppId,
            RawTitle = session.RawTitle,
            RawArtist = session.RawArtist,
            RawAlbum = session.RawAlbum,
            ParsedTitle = session.ParsedTitle,
            ParsedArtist = session.ParsedArtist,
            ParsedAlbum = session.ParsedAlbum,
            Confidence = session.Confidence,
            HasArtwork = session.HasArtwork,
            IsSelected = session.IsSelected,
            DecisionReason = session.DecisionReason,
            SessionId = session.SessionId,
            LastUpdatedUtc = session.LastUpdatedUtc
        }).ToList()
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

    private static OverlayContainerStyle CloneOverlayContainerStyle(OverlayContainerStyle style) => new()
    {
        BackgroundMode = style.BackgroundMode,
        BackgroundColorHex = style.BackgroundColorHex,
        Gradient = CloneGradientSettings(style.Gradient),
        Opacity = style.Opacity,
        CornerRadiusPx = style.CornerRadiusPx,
        PaddingPx = style.PaddingPx,
        GapPx = style.GapPx,
        BorderEnabled = style.BorderEnabled,
        BorderColorHex = style.BorderColorHex,
        BorderWidthPx = style.BorderWidthPx
    };

    private static GradientSettings CloneGradientSettings(GradientSettings settings) => new()
    {
        ColorCount = settings.ColorCount,
        Preset = settings.Preset,
        Color1Hex = settings.Color1Hex,
        Color2Hex = settings.Color2Hex,
        Color3Hex = settings.Color3Hex,
        AngleDeg = settings.AngleDeg
    };

    private static StatusPillStyle CloneStatusPillStyle(StatusPillStyle style) => new()
    {
        BackgroundColorHex = style.BackgroundColorHex,
        TextColorHex = style.TextColorHex,
        Opacity = style.Opacity,
        FontFamily = style.FontFamily,
        FontSizePx = style.FontSizePx,
        Bold = style.Bold,
        Italic = style.Italic,
        Underline = style.Underline,
        CornerRadiusPx = style.CornerRadiusPx,
        PaddingHorizontalPx = style.PaddingHorizontalPx,
        PaddingVerticalPx = style.PaddingVerticalPx
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

        settings.OverlaySettings.BackgroundColorHex = NormalizeHexColor(settings.OverlaySettings.BackgroundColorHex, defaults.BackgroundColorHex);
        NormalizeOverlayContainerStyle(settings.OverlaySettings, defaults);
        NormalizeStatusPillStyle(settings.OverlaySettings.StatusPillStyle ??= new StatusPillStyle(), defaults.StatusPillStyle);
        settings.OverlaySettings.ImagePosition = NormalizeOverlayChoice(
            settings.OverlaySettings.ImagePosition,
            defaults.ImagePosition,
            ["Left", "Right"]);
        settings.OverlaySettings.TextAlign = NormalizeOverlayChoice(
            settings.OverlaySettings.TextAlign,
            defaults.TextAlign,
            ["Left", "Center", "Right"]);
    }

    private static void NormalizeBrowserSettings(Settings settings)
    {
        settings.BrowserSettings ??= new BrowserSettings();
        settings.BrowserSettings.SupportedBrowsers ??= new BrowserSupportSettings();
        settings.BrowserSettings.ActiveSourceMode = settings.BrowserSettings.ActiveSourceMode?.Trim().ToLowerInvariant() switch
        {
            "tidal" => "tidal",
            "browser" => "browser",
            _ => "auto"
        };

        if (settings.BrowserSettings.SourceSwitchCooldownMs < 0)
        {
            settings.BrowserSettings.SourceSwitchCooldownMs = 5000;
        }

        if (settings.BrowserSettings.StaleSessionAfterSeconds <= 0)
        {
            settings.BrowserSettings.StaleSessionAfterSeconds = 30;
        }

        settings.BrowserSettings.SourcePriority = settings.BrowserSettings.SourcePriority?
            .Where(priority => !string.IsNullOrWhiteSpace(priority))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? [];

        if (settings.BrowserSettings.SourcePriority.Count == 0)
        {
            settings.BrowserSettings.SourcePriority =
            [
                "tidal",
                "youtubeMusic",
                "bandcamp",
                "soundcloud",
                "youtube",
                "genericBrowser"
            ];
        }
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

    private static void NormalizeOverlayContainerStyle(OverlaySettings settings, OverlaySettings defaults)
    {
        var style = settings.OverlayContainerStyle ??= new OverlayContainerStyle();
        var defaultStyle = defaults.OverlayContainerStyle;

        if (string.IsNullOrWhiteSpace(style.BackgroundColorHex) && !string.IsNullOrWhiteSpace(settings.BackgroundColorHex))
        {
            style.BackgroundColorHex = settings.BackgroundColorHex;
        }

        style.BackgroundMode = NormalizeOverlayChoice(
            style.BackgroundMode,
            defaultStyle.BackgroundMode,
            ["solid", "gradient"]);
        style.BackgroundColorHex = NormalizeHexColor(style.BackgroundColorHex, defaultStyle.BackgroundColorHex);
        NormalizeGradientSettings(style.Gradient ??= new GradientSettings(), defaultStyle.Gradient);
        style.Opacity = NormalizeOpacity(style.Opacity, defaultStyle.Opacity);
        style.CornerRadiusPx = NormalizeZeroOrPositiveInt(style.CornerRadiusPx, defaultStyle.CornerRadiusPx);
        style.PaddingPx = NormalizeZeroOrPositiveInt(style.PaddingPx, defaultStyle.PaddingPx);
        style.GapPx = NormalizeZeroOrPositiveInt(style.GapPx, defaultStyle.GapPx);
        style.BorderColorHex = NormalizeHexColor(style.BorderColorHex, defaultStyle.BorderColorHex);
        style.BorderWidthPx = NormalizeZeroOrPositiveInt(style.BorderWidthPx, defaultStyle.BorderWidthPx);

        settings.BackgroundColorHex = style.BackgroundColorHex;
    }

    private static void NormalizeGradientSettings(GradientSettings settings, GradientSettings defaults)
    {
        settings.ColorCount = settings.ColorCount is 2 or 3
            ? settings.ColorCount
            : defaults.ColorCount;
        settings.Preset = NormalizeOverlayChoice(
            settings.Preset,
            defaults.Preset,
            [
                "Linear Left to Right",
                "Linear Top to Bottom",
                "Diagonal",
                "Reverse Diagonal",
                "Soft Radial",
                "Spotlight",
                "Stream Neon",
                "Subtle Glass"
            ]);
        settings.Color1Hex = NormalizeHexColor(settings.Color1Hex, defaults.Color1Hex);
        settings.Color2Hex = NormalizeHexColor(settings.Color2Hex, defaults.Color2Hex);
        settings.Color3Hex = NormalizeHexColor(settings.Color3Hex, defaults.Color3Hex);
        settings.AngleDeg = settings.AngleDeg >= 0 && settings.AngleDeg <= 360
            ? settings.AngleDeg
            : defaults.AngleDeg;
    }

    private static void NormalizeStatusPillStyle(StatusPillStyle style, StatusPillStyle defaults)
    {
        if (string.IsNullOrWhiteSpace(style.FontFamily))
        {
            style.FontFamily = defaults.FontFamily;
        }

        style.BackgroundColorHex = NormalizeHexColor(style.BackgroundColorHex, defaults.BackgroundColorHex);
        style.TextColorHex = NormalizeHexColor(style.TextColorHex, defaults.TextColorHex);
        style.Opacity = NormalizeOpacity(style.Opacity, defaults.Opacity);
        style.FontSizePx = NormalizePositiveInt(style.FontSizePx, defaults.FontSizePx);
        style.CornerRadiusPx = NormalizeZeroOrPositiveInt(style.CornerRadiusPx, defaults.CornerRadiusPx);
        style.PaddingHorizontalPx = NormalizeZeroOrPositiveInt(style.PaddingHorizontalPx, defaults.PaddingHorizontalPx);
        style.PaddingVerticalPx = NormalizeZeroOrPositiveInt(style.PaddingVerticalPx, defaults.PaddingVerticalPx);
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

    private static int NormalizePositiveInt(int value, int fallback) =>
        value > 0 ? value : fallback;

    private static int NormalizeZeroOrPositiveInt(int value, int fallback) =>
        value >= 0 ? value : fallback;

    private static double NormalizeOpacity(double value, double fallback) =>
        double.IsFinite(value) && value >= 0 && value <= 1
            ? value
            : fallback;

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

    private sealed class FallbackAppUpdateChecker : IAppUpdateChecker
    {
        public string CurrentVersion => "0.3.0";
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
