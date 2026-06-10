using TideReader.Backend.Models;
using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class BridgeServiceBehaviorTests
{
    [Fact]
    public async Task GetState_UsesFallbackAppUpdateChecker_WhenNoCheckerIsProvided()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var logger = new AppLogger(tempDir);
            var settings = new Settings
            {
                OutputFolder = Path.Combine(tempDir, "output"),
                OverlayEnabled = false,
                OverlayPort = 17655,
                PollIntervalMs = 1000,
                EnableWindowTitleFallback = false,
                EnableDebugManualInput = false,
                MetadataProviderMode = nameof(MetadataProviderMode.Off),
                ThemeMode = nameof(ThemeMode.Dark),
                OverlaySettings = new OverlaySettings()
            };

            var service = new BridgeService(
                new HarnessSettingsStore(settings),
                logger,
                new RecordingOutputWriter(),
                new SequencePlaybackDetector(),
                new ConfigurableWindowTitleDetector(),
                new ConfigurableManualDetector(),
                new ConfigurableMetadataEnricher(),
                new ConfigurableOverlayCoordinator(),
                new OverlaySettingsSnapshotStore(),
                new PlaybackSnapshotStore());

            await service.InitializeAsync(CancellationToken.None);
            var state = service.GetState();

            Assert.Equal("0.5.0", state.AppVersion);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task FallbackAppUpdateChecker_ReturnsLatestVersionPayload()
    {
        var checkerType = typeof(BridgeService).GetNestedType("FallbackAppUpdateChecker", System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(checkerType);

        var checker = Activator.CreateInstance(checkerType!) as IAppUpdateChecker;
        Assert.NotNull(checker);

        var update = await checker!.CheckForUpdatesAsync(CancellationToken.None);

        Assert.Equal("0.5.0", checker.CurrentVersion);
        Assert.Equal("0.5.0", update.CurrentVersion);
        Assert.Equal("0.5.0", update.LatestVersion);
        Assert.Equal("https://github.com/LastUrsa/TideReader/releases", checker.ReleaseUrl);
        Assert.Equal(checker.ReleaseUrl, update.ReleaseUrl);
        Assert.Equal("You're running the latest version.", update.Message);
    }

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
    public async Task RunDetectionAsync_IgnoresIdleGenericTidalWindowTitle_WhenNoPlaybackIsActive()
    {
        using var harness = new BridgeServiceHarness();
        harness.Settings.EnableWindowTitleFallback = true;
        harness.WindowTitleDetector.Result = new DetectionResult
        {
            Status = "playing",
            Artist = "",
            Title = "TIDAL",
            Method = "window_title",
            Confidence = 0.61,
            DetectedText = "TIDAL"
        };

        await harness.Service.InitializeAsync(CancellationToken.None);
        var state = await harness.Service.RunDetectionAsync(CancellationToken.None);

        Assert.Equal("not_running", state.NowPlaying.Status);
        Assert.Equal("none", state.NowPlaying.Method);
        Assert.True(string.IsNullOrWhiteSpace(state.NowPlaying.Title));
        Assert.True(string.IsNullOrWhiteSpace(state.NowPlaying.Artist));
    }

    [Fact]
    public async Task RunDetectionAsync_DoesNotUseGenericTidalWindowTitle_WhenPlaybackIsIdle()
    {
        using var harness = new BridgeServiceHarness();
        harness.Settings.EnableWindowTitleFallback = true;
        harness.WindowTitleDetector.Result = null;

        await harness.Service.InitializeAsync(CancellationToken.None);
        var baselineState = await harness.Service.RunDetectionAsync(CancellationToken.None);

        harness.WindowTitleDetector.Result = new DetectionResult
        {
            Status = "playing",
            Artist = "",
            Title = "TIDAL",
            Method = "window_title",
            Confidence = 0.61,
            DetectedText = "TIDAL"
        };

        var state = await harness.Service.RunDetectionAsync(CancellationToken.None);

        Assert.Equal("not_running", baselineState.NowPlaying.Status);
        Assert.Equal("not_running", state.NowPlaying.Status);
        Assert.True(string.IsNullOrWhiteSpace(state.NowPlaying.Title));
        Assert.True(string.IsNullOrWhiteSpace(state.NowPlaying.Artist));
    }

    [Fact]
    public async Task RunDetectionAsync_DoesNotUsePausedTidalMediaSessionMetadata_WhenGenericWindowTitleIsOnlySignal()
    {
        using var harness = new BridgeServiceHarness();
        harness.Settings.EnableWindowTitleFallback = true;
        harness.WindowTitleDetector.Result = null;

        await harness.Service.InitializeAsync(CancellationToken.None);
        var baselineState = await harness.Service.RunDetectionAsync(CancellationToken.None);

        harness.PlaybackDetector.Outcomes.Enqueue(new PlaybackDetectionOutcome(
            null,
            new BrowserDebugState
            {
                Sessions =
                [
                    new BrowserSessionDebugInfo
                    {
                        Provider = "tidal",
                        PlaybackState = "paused",
                        SourceAppId = "com.squirrel.TIDAL.TIDAL",
                        RawTitle = "30/90 (from \"tick, tick... BOOM!\")",
                        RawArtist = "Ben Visini",
                        ParsedTitle = "30/90 (from \"tick, tick... BOOM!\")",
                        ParsedArtist = "Ben Visini",
                        Confidence = 0.93,
                        SessionId = "tidal-session",
                        LastUpdatedUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
                    }
                ]
            }));
        harness.WindowTitleDetector.Result = new DetectionResult
        {
            Status = "playing",
            Artist = "",
            Title = "TIDAL",
            Method = "window_title",
            Confidence = 0.61,
            DetectedText = "TIDAL"
        };

        var state = await harness.Service.RunDetectionAsync(CancellationToken.None);

        Assert.Equal("not_running", baselineState.NowPlaying.Status);
        Assert.Equal("not_running", state.NowPlaying.Status);
        Assert.True(string.IsNullOrWhiteSpace(state.NowPlaying.Title));
        Assert.True(string.IsNullOrWhiteSpace(state.NowPlaying.Artist));
    }

    [Fact]
    public async Task RunDetectionAsync_UsesRecentGenericTidalWindowTitle_AfterBrowserStops_WhenTidalIsPreferred()
    {
        using var harness = new BridgeServiceHarness();
        harness.Settings.EnableWindowTitleFallback = true;
        harness.Settings.BrowserSettings.PreferTidalOverBrowser = true;
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Browser Artist",
            Title = "Browser Track",
            Source = "YouTube",
            Provider = "browser",
            Browser = "firefox",
            Site = "youtube",
            Method = "media_session",
            Confidence = 0.75
        });
        harness.WindowTitleDetector.Result = null;

        await harness.Service.InitializeAsync(CancellationToken.None);
        var browserState = await harness.Service.RunDetectionAsync(CancellationToken.None);

        harness.WindowTitleDetector.Result = new DetectionResult
        {
            Status = "playing",
            Artist = "",
            Title = "TIDAL",
            Method = "window_title",
            Confidence = 0.61,
            DetectedText = "TIDAL"
        };

        var state = await harness.Service.RunDetectionAsync(CancellationToken.None);

        Assert.Equal("browser", browserState.NowPlaying.Provider);
        Assert.Equal("playing", state.NowPlaying.Status);
        Assert.Equal("tidal", state.NowPlaying.Provider);
        Assert.Equal("TIDAL", state.NowPlaying.Title);
        Assert.Equal("selected: recent generic TIDAL title after browser stop", state.NowPlaying.SelectionReason);
    }

    [Fact]
    public async Task RunDetectionAsync_DoesNotPreferIdleWindowTitleTidal_OverBrowser_WhenNoRecentTitleChange()
    {
        using var harness = new BridgeServiceHarness();
        harness.Settings.EnableWindowTitleFallback = true;
        harness.Settings.BrowserSettings.PreferTidalOverBrowser = true;
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Browser Artist",
            Title = "Browser Track",
            Source = "YouTube",
            Provider = "browser",
            Browser = "firefox",
            Site = "youtube",
            Method = "media_session",
            Confidence = 0.75
        });
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Browser Artist",
            Title = "Browser Track",
            Source = "YouTube",
            Provider = "browser",
            Browser = "firefox",
            Site = "youtube",
            Method = "media_session",
            Confidence = 0.75
        });
        harness.WindowTitleDetector.Result = new DetectionResult
        {
            Status = "playing",
            Artist = "Tidal Artist",
            Title = "Tidal Track",
            Method = "window_title",
            Confidence = 0.61
        };

        await harness.Service.InitializeAsync(CancellationToken.None);
        var state = await harness.Service.RunDetectionAsync(CancellationToken.None);

        Assert.Equal("browser", state.NowPlaying.Provider);
        Assert.Equal("Browser Artist", state.NowPlaying.Artist);
        Assert.Equal("Browser Track", state.NowPlaying.Title);
        Assert.Equal("media_session", state.NowPlaying.Method);
    }

    [Fact]
    public async Task RunDetectionAsync_PrefersWindowTitleTidal_OverBrowser_AfterRecentTitleChange_WhenTidalPreferenceIsEnabled()
    {
        using var harness = new BridgeServiceHarness();
        harness.Settings.EnableWindowTitleFallback = true;
        harness.Settings.BrowserSettings.PreferTidalOverBrowser = true;
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Browser Artist",
            Title = "Browser Track",
            Source = "YouTube",
            Provider = "browser",
            Browser = "firefox",
            Site = "youtube",
            Method = "media_session",
            Confidence = 0.75
        });
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Browser Artist",
            Title = "Browser Track",
            Source = "YouTube",
            Provider = "browser",
            Browser = "firefox",
            Site = "youtube",
            Method = "media_session",
            Confidence = 0.75
        });
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Browser Artist",
            Title = "Browser Track",
            Source = "YouTube",
            Provider = "browser",
            Browser = "firefox",
            Site = "youtube",
            Method = "media_session",
            Confidence = 0.75
        });
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Browser Artist",
            Title = "Browser Track",
            Source = "YouTube",
            Provider = "browser",
            Browser = "firefox",
            Site = "youtube",
            Method = "media_session",
            Confidence = 0.75
        });
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Browser Artist",
            Title = "Browser Track",
            Source = "YouTube",
            Provider = "browser",
            Browser = "firefox",
            Site = "youtube",
            Method = "media_session",
            Confidence = 0.75
        });
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Browser Artist",
            Title = "Browser Track",
            Source = "YouTube",
            Provider = "browser",
            Browser = "firefox",
            Site = "youtube",
            Method = "media_session",
            Confidence = 0.75
        });
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Browser Artist",
            Title = "Browser Track",
            Source = "YouTube",
            Provider = "browser",
            Browser = "firefox",
            Site = "youtube",
            Method = "media_session",
            Confidence = 0.75
        });
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Browser Artist",
            Title = "Browser Track",
            Source = "YouTube",
            Provider = "browser",
            Browser = "firefox",
            Site = "youtube",
            Method = "media_session",
            Confidence = 0.75
        });
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Browser Artist",
            Title = "Browser Track",
            Source = "YouTube",
            Provider = "browser",
            Browser = "firefox",
            Site = "youtube",
            Method = "media_session",
            Confidence = 0.75
        });
        harness.WindowTitleDetector.Result = new DetectionResult
        {
            Status = "playing",
            Artist = "Idle Artist",
            Title = "Idle Track",
            Method = "window_title",
            Confidence = 0.61
        };

        await harness.Service.InitializeAsync(CancellationToken.None);
        var baseline = await harness.Service.RunDetectionAsync(CancellationToken.None);

        harness.WindowTitleDetector.Result = new DetectionResult
        {
            Status = "playing",
            Artist = "Tidal Artist",
            Title = "Tidal Track",
            Method = "window_title",
            Confidence = 0.61
        };

        var state = await harness.Service.RunDetectionAsync(CancellationToken.None);
        var stickyState = await harness.Service.RunDetectionAsync(CancellationToken.None);

        Assert.Equal("browser", baseline.NowPlaying.Provider);
        Assert.Equal("tidal", state.NowPlaying.Provider);
        Assert.Equal("Tidal Artist", state.NowPlaying.Artist);
        Assert.Equal("Tidal Track", state.NowPlaying.Title);
        Assert.Equal("window_title", state.NowPlaying.Method);
        Assert.Equal("selected: window title preferred over browser", state.NowPlaying.SelectionReason);
        Assert.Equal("tidal", stickyState.NowPlaying.Provider);
        Assert.Equal("Tidal Artist", stickyState.NowPlaying.Artist);
        Assert.Equal("Tidal Track", stickyState.NowPlaying.Title);
    }

    [Fact]
    public async Task RunDetectionAsync_HoldsWindowTitleTidalFallback_ThroughBriefGenericAndNullDropout()
    {
        using var harness = new BridgeServiceHarness();
        harness.Settings.EnableWindowTitleFallback = true;
        harness.Settings.BrowserSettings.PreferTidalOverBrowser = true;
        harness.Settings.BrowserSettings.SourceSwitchCooldownMs = 250;
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Browser Artist",
            Title = "Browser Track",
            Source = "YouTube",
            Provider = "browser",
            Browser = "firefox",
            Site = "youtube",
            Method = "media_session",
            Confidence = 0.75
        });
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Browser Artist",
            Title = "Browser Track",
            Source = "YouTube",
            Provider = "browser",
            Browser = "firefox",
            Site = "youtube",
            Method = "media_session",
            Confidence = 0.75
        });
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Browser Artist",
            Title = "Browser Track",
            Source = "YouTube",
            Provider = "browser",
            Browser = "firefox",
            Site = "youtube",
            Method = "media_session",
            Confidence = 0.75
        });
        harness.WindowTitleDetector.Result = new DetectionResult
        {
            Status = "playing",
            Artist = "Idle Artist",
            Title = "Idle Track",
            Method = "window_title",
            Confidence = 0.61
        };

        await harness.Service.InitializeAsync(CancellationToken.None);
        await harness.Service.RunDetectionAsync(CancellationToken.None);

        harness.WindowTitleDetector.Result = new DetectionResult
        {
            Status = "playing",
            Artist = "Tidal Artist",
            Title = "Tidal Track",
            Method = "window_title",
            Confidence = 0.61
        };

        var switchedState = await harness.Service.RunDetectionAsync(CancellationToken.None);
        harness.WindowTitleDetector.Result = new DetectionResult
        {
            Status = "playing",
            Artist = "",
            Title = "TIDAL",
            Method = "window_title",
            Confidence = 0.61,
            DetectedText = "TIDAL"
        };
        await Task.Delay(350);
        var bridgedState = await harness.Service.RunDetectionAsync(CancellationToken.None);
        await Task.Delay(350);
        harness.WindowTitleDetector.Result = null;
        var droppedState = await harness.Service.RunDetectionAsync(CancellationToken.None);

        Assert.Equal("tidal", switchedState.NowPlaying.Provider);
        Assert.Equal("tidal", bridgedState.NowPlaying.Provider);
        Assert.Equal("tidal", droppedState.NowPlaying.Provider);
        Assert.Equal("Tidal Track", droppedState.NowPlaying.Title);
    }

    [Fact]
    public async Task RunDetectionAsync_KeepsWindowTitleTidalFallback_WhileSameTrackRemainsPresent()
    {
        using var harness = new BridgeServiceHarness();
        harness.Settings.EnableWindowTitleFallback = true;
        harness.Settings.BrowserSettings.PreferTidalOverBrowser = true;
        harness.Settings.BrowserSettings.SourceSwitchCooldownMs = 250;
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Browser Artist",
            Title = "Browser Track",
            Source = "YouTube",
            Provider = "browser",
            Browser = "firefox",
            Site = "youtube",
            Method = "media_session",
            Confidence = 0.75
        });
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Browser Artist",
            Title = "Browser Track",
            Source = "YouTube",
            Provider = "browser",
            Browser = "firefox",
            Site = "youtube",
            Method = "media_session",
            Confidence = 0.75
        });
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Browser Artist",
            Title = "Browser Track",
            Source = "YouTube",
            Provider = "browser",
            Browser = "firefox",
            Site = "youtube",
            Method = "media_session",
            Confidence = 0.75
        });
        harness.WindowTitleDetector.Result = new DetectionResult
        {
            Status = "playing",
            Artist = "Idle Artist",
            Title = "Idle Track",
            Method = "window_title",
            Confidence = 0.61
        };

        await harness.Service.InitializeAsync(CancellationToken.None);
        await harness.Service.RunDetectionAsync(CancellationToken.None);

        harness.WindowTitleDetector.Result = new DetectionResult
        {
            Status = "playing",
            Artist = "Tidal Artist",
            Title = "Tidal Track",
            Method = "window_title",
            Confidence = 0.61
        };

        var switchedState = await harness.Service.RunDetectionAsync(CancellationToken.None);
        await Task.Delay(350);
        var heldState = await harness.Service.RunDetectionAsync(CancellationToken.None);

        Assert.Equal("tidal", switchedState.NowPlaying.Provider);
        Assert.Equal("tidal", heldState.NowPlaying.Provider);
        Assert.Equal("Tidal Track", heldState.NowPlaying.Title);
        Assert.Equal("Tidal Artist", heldState.NowPlaying.Artist);
        Assert.Equal("selected: window title preferred over browser", heldState.NowPlaying.SelectionReason);
    }

    [Fact]
    public async Task RunDetectionAsync_HoldsWindowTitleTidalFallback_Briefly_WhenTitleSignalDropsOut()
    {
        using var harness = new BridgeServiceHarness();
        harness.Settings.EnableWindowTitleFallback = true;
        harness.Settings.BrowserSettings.PreferTidalOverBrowser = true;
        harness.Settings.BrowserSettings.SourceSwitchCooldownMs = 250;
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Browser Artist",
            Title = "Browser Track",
            Source = "YouTube",
            Provider = "browser",
            Browser = "firefox",
            Site = "youtube",
            Method = "media_session",
            Confidence = 0.75
        });
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Browser Artist",
            Title = "Browser Track",
            Source = "YouTube",
            Provider = "browser",
            Browser = "firefox",
            Site = "youtube",
            Method = "media_session",
            Confidence = 0.75
        });
        harness.WindowTitleDetector.Result = new DetectionResult
        {
            Status = "playing",
            Artist = "Idle Artist",
            Title = "Idle Track",
            Method = "window_title",
            Confidence = 0.61
        };

        await harness.Service.InitializeAsync(CancellationToken.None);
        await harness.Service.RunDetectionAsync(CancellationToken.None);

        harness.WindowTitleDetector.Result = new DetectionResult
        {
            Status = "playing",
            Artist = "Tidal Artist",
            Title = "Tidal Track",
            Method = "window_title",
            Confidence = 0.61
        };

        var switchedState = await harness.Service.RunDetectionAsync(CancellationToken.None);
        harness.WindowTitleDetector.Result = null;
        var heldState = await harness.Service.RunDetectionAsync(CancellationToken.None);

        Assert.Equal("tidal", switchedState.NowPlaying.Provider);
        Assert.Equal("tidal", heldState.NowPlaying.Provider);
        Assert.Equal("selected: holding window title fallback after detection loss", heldState.NowPlaying.SelectionReason);
    }

    [Fact]
    public async Task RunDetectionAsync_HoldsRecentWindowTitleTidalTrack_WhenTitleTemporarilyFallsBackToGeneric()
    {
        using var harness = new BridgeServiceHarness();
        harness.Settings.EnableWindowTitleFallback = true;
        harness.WindowTitleDetector.Result = null;

        await harness.Service.InitializeAsync(CancellationToken.None);
        await harness.Service.RunDetectionAsync(CancellationToken.None);

        harness.WindowTitleDetector.Result = new DetectionResult
        {
            Status = "playing",
            Artist = "Tidal Artist",
            Title = "Tidal Track",
            Method = "window_title",
            Confidence = 0.72,
            DetectedText = "Tidal Track - Tidal Artist"
        };

        var playingState = await harness.Service.RunDetectionAsync(CancellationToken.None);

        harness.WindowTitleDetector.Result = new DetectionResult
        {
            Status = "playing",
            Artist = "",
            Title = "TIDAL",
            Method = "window_title",
            Confidence = 0.66,
            DetectedText = "TIDAL"
        };

        var heldState = await harness.Service.RunDetectionAsync(CancellationToken.None);

        Assert.Equal("tidal", playingState.NowPlaying.Provider);
        Assert.Equal("Tidal Track", playingState.NowPlaying.Title);
        Assert.Equal("tidal", heldState.NowPlaying.Provider);
        Assert.Equal("Tidal Track", heldState.NowPlaying.Title);
        Assert.Equal("Tidal Artist", heldState.NowPlaying.Artist);
        Assert.Equal("selected: holding recent TIDAL window title track", heldState.NowPlaying.SelectionReason);
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
        Assert.Equal("default", state.Settings.ActiveOverlayProfileId);
        Assert.Single(state.Settings.OverlayProfiles);
        Assert.Equal("Default", state.Settings.OverlayProfiles[0].Name);
        Assert.Equal(state.Settings.OverlaySettings.ImageSizePx, state.Settings.OverlayProfiles[0].OverlaySettings.ImageSizePx);
        Assert.Equal("overlay failed", state.LastError);
        Assert.Equal(state.Settings.OutputFolder, harness.Settings.OutputFolder);
    }

    [Fact]
    public async Task SaveSettingsAsync_NormalizesOverlayProfiles()
    {
        using var harness = new BridgeServiceHarness();

        await harness.Service.InitializeAsync(CancellationToken.None);
        var state = await harness.Service.SaveSettingsAsync(new Settings
        {
            OutputFolder = harness.Settings.OutputFolder,
            OverlaySettings = new OverlaySettings
            {
                ImageSizePx = 80,
                ImagePosition = "Right",
                TextAlign = "Center",
                OverlayContainerStyle = new OverlayContainerStyle
                {
                    BackgroundMode = "gradient",
                    Gradient = new GradientSettings
                    {
                        ColorCount = 3,
                        Preset = "Stream Neon",
                        Color1Hex = "#111111",
                        Color2Hex = "#222222",
                        Color3Hex = "#333333",
                        AngleDeg = 120
                    }
                }
            },
            ActiveOverlayProfileId = "missing",
            OverlayProfiles =
            [
                new OverlayProfile
                {
                    Id = "  ",
                    Name = "  ",
                    OverlaySettings = new OverlaySettings
                    {
                        ImageSizePx = -1,
                        ImagePosition = "sideways",
                        TextAlign = "diagonal",
                        OverlayContainerStyle = new OverlayContainerStyle
                        {
                            BackgroundMode = "static",
                            BackgroundColorHex = "bad",
                            Gradient = new GradientSettings
                            {
                                ColorCount = 4,
                                Preset = "Unknown",
                                Color1Hex = "bad",
                                Color2Hex = "",
                                Color3Hex = "nope",
                                AngleDeg = 999
                            },
                            Opacity = 3,
                            CornerRadiusPx = -1,
                            PaddingPx = -1,
                            GapPx = -1,
                            BorderColorHex = "bad",
                            BorderWidthPx = -1
                        }
                    }
                },
                new OverlayProfile
                {
                    Id = "wide",
                    Name = " Wide Layout ",
                    OverlaySettings = new OverlaySettings
                    {
                        ImageSizePx = 120,
                        ImagePosition = "Right",
                        TextAlign = "Right"
                    }
                }
            ]
        }, CancellationToken.None);

        Assert.Equal(state.Settings.OverlayProfiles[0].Id, state.Settings.ActiveOverlayProfileId);
        Assert.Equal(2, state.Settings.OverlayProfiles.Count);
        Assert.False(string.IsNullOrWhiteSpace(state.Settings.OverlayProfiles[0].Id));
        Assert.Equal("Overlay Profile 1", state.Settings.OverlayProfiles[0].Name);
        Assert.Equal(68, state.Settings.OverlayProfiles[0].OverlaySettings.ImageSizePx);
        Assert.Equal("Left", state.Settings.OverlayProfiles[0].OverlaySettings.ImagePosition);
        Assert.Equal("Left", state.Settings.OverlayProfiles[0].OverlaySettings.TextAlign);
        Assert.Equal("solid", state.Settings.OverlayProfiles[0].OverlaySettings.OverlayContainerStyle.BackgroundMode);
        Assert.Equal("#32334F", state.Settings.OverlayProfiles[0].OverlaySettings.OverlayContainerStyle.BackgroundColorHex);
        Assert.Equal(0.86, state.Settings.OverlayProfiles[0].OverlaySettings.OverlayContainerStyle.Opacity);
        Assert.Equal(18, state.Settings.OverlayProfiles[0].OverlaySettings.OverlayContainerStyle.CornerRadiusPx);
        Assert.Equal("wide", state.Settings.OverlayProfiles[1].Id);
        Assert.Equal("Wide Layout", state.Settings.OverlayProfiles[1].Name);
        Assert.Equal(120, state.Settings.OverlayProfiles[1].OverlaySettings.ImageSizePx);
        Assert.Equal("Right", state.Settings.OverlayProfiles[1].OverlaySettings.ImagePosition);
        Assert.Equal("Right", state.Settings.OverlayProfiles[1].OverlaySettings.TextAlign);
        Assert.Equal(state.Settings.ActiveOverlayProfileId, harness.Settings.ActiveOverlayProfileId);
        Assert.Equal(2, harness.Settings.OverlayProfiles.Count);
    }

    [Fact]
    public async Task GetState_ClonesOverlayProfiles()
    {
        using var harness = new BridgeServiceHarness();
        harness.Settings.OverlayProfiles =
        [
            new OverlayProfile
            {
                Id = "showcase",
                Name = "Showcase",
                OverlaySettings = new OverlaySettings
                {
                    ImageSizePx = 112,
                    OverlayContainerStyle = new OverlayContainerStyle
                    {
                        Gradient = new GradientSettings()
                    }
                }
            }
        ];
        harness.Settings.ActiveOverlayProfileId = "showcase";

        await harness.Service.InitializeAsync(CancellationToken.None);
        var state = harness.Service.GetState();

        state.Settings.OverlayProfiles[0].Name = "Changed";
        state.Settings.OverlayProfiles[0].OverlaySettings.ImageSizePx = 32;

        var nextState = harness.Service.GetState();
        Assert.Equal("Showcase", nextState.Settings.OverlayProfiles[0].Name);
        Assert.Equal(112, nextState.Settings.OverlayProfiles[0].OverlaySettings.ImageSizePx);
    }

    [Fact]
    public async Task SaveSettingsAsync_NormalizesBrowserSettings()
    {
        using var harness = new BridgeServiceHarness();

        await harness.Service.InitializeAsync(CancellationToken.None);
        var state = await harness.Service.SaveSettingsAsync(new Settings
        {
            OutputFolder = harness.Settings.OutputFolder,
            BrowserSettings = new BrowserSettings
            {
                Enabled = true,
                ActiveSourceMode = "bogus",
                SupportedBrowsers = new BrowserSupportSettings
                {
                    ChromeEnabled = false,
                    EdgeEnabled = false,
                    FirefoxEnabled = false,
                    BraveEnabled = false,
                    OperaEnabled = true
                },
                SourcePriority = ["browser", "", "youtubeMusic"],
                SourceSwitchCooldownMs = -1,
                AllowGenericPlayback = false,
                PreferTidalOverBrowser = false,
                MetadataCleanupEnabled = false,
                BrowserArtworkEnabled = false,
                YouTubeVideoImageFallbackEnabled = false,
                DebugLoggingEnabled = true,
                DeepDiagnosticLoggingEnabled = true,
                IgnorePausedSessions = false,
                IgnoreStaleSessions = false,
                StaleSessionAfterSeconds = -9
            },
            OverlaySettings = new OverlaySettings()
        }, CancellationToken.None);

        Assert.Equal("auto", state.Settings.BrowserSettings.ActiveSourceMode);
        Assert.Equal(5000, state.Settings.BrowserSettings.SourceSwitchCooldownMs);
        Assert.Equal(30, state.Settings.BrowserSettings.StaleSessionAfterSeconds);
        Assert.Equal(["browser", "youtubeMusic"], state.Settings.BrowserSettings.SourcePriority);
        Assert.False(state.Settings.BrowserSettings.AllowGenericPlayback);
        Assert.False(state.Settings.BrowserSettings.PreferTidalOverBrowser);
        Assert.False(state.Settings.BrowserSettings.MetadataCleanupEnabled);
        Assert.False(state.Settings.BrowserSettings.BrowserArtworkEnabled);
        Assert.False(state.Settings.BrowserSettings.YouTubeVideoImageFallbackEnabled);
        Assert.True(state.Settings.BrowserSettings.DebugLoggingEnabled);
        Assert.True(state.Settings.BrowserSettings.DeepDiagnosticLoggingEnabled);
        Assert.False(state.Settings.BrowserSettings.IgnorePausedSessions);
        Assert.False(state.Settings.BrowserSettings.IgnoreStaleSessions);
        Assert.True(state.Settings.BrowserSettings.SupportedBrowsers.OperaEnabled);
    }

    [Fact]
    public async Task SaveSettingsAsync_PreservesValidOverlaySettings()
    {
        using var harness = new BridgeServiceHarness();

        await harness.Service.InitializeAsync(CancellationToken.None);
        var state = await harness.Service.SaveSettingsAsync(new Settings
        {
            OutputFolder = harness.Settings.OutputFolder,
            OverlaySettings = new OverlaySettings
            {
                SongTextStyle = new OverlayTextStyle
                {
                    FontFamily = "Fira Sans",
                    ColorHex = "#a1b2c3",
                    FontSizePx = 28,
                    MaxCharacters = 64,
                    Bold = true
                },
                ArtistTextStyle = new OverlayTextStyle
                {
                    FontFamily = "IBM Plex Sans",
                    ColorHex = "#0f0f0f",
                    FontSizePx = 16,
                    MaxCharacters = 42
                },
                AlbumTextStyle = new OverlayTextStyle
                {
                    FontFamily = "IBM Plex Sans",
                    ColorHex = "#ffffff",
                    FontSizePx = 14,
                    MaxCharacters = 30
                },
                ImageSizePx = 72,
                BackgroundColorHex = "#112233",
                OverlayContainerStyle = new OverlayContainerStyle
                {
                    BackgroundMode = "gradient",
                    BackgroundColorHex = "",
                    Gradient = new GradientSettings
                    {
                        ColorCount = 2,
                        Preset = "Soft Radial",
                        Color1Hex = "#010203",
                        Color2Hex = "#040506",
                        Color3Hex = "#070809",
                        AngleDeg = 180
                    },
                    Opacity = 0.42,
                    CornerRadiusPx = 12,
                    PaddingPx = 18,
                    GapPx = 9,
                    BorderEnabled = true,
                    BorderColorHex = "#abcdef",
                    BorderWidthPx = 2
                },
                StatusPillStyle = new StatusPillStyle
                {
                    BackgroundColorHex = "#123456",
                    TextColorHex = "#654321",
                    Opacity = 0.75,
                    FontFamily = "JetBrains Mono",
                    FontSizePx = 13,
                    CornerRadiusPx = 18,
                    PaddingHorizontalPx = 8,
                    PaddingVerticalPx = 3
                },
                ImagePosition = "Right",
                TextAlign = "Center",
                ShowAppName = false,
                ShowPlaybackState = false
            },
            BrowserSettings = harness.Settings.BrowserSettings
        }, CancellationToken.None);

        Assert.Equal("Fira Sans", state.Settings.OverlaySettings.SongTextStyle.FontFamily);
        Assert.Equal("#A1B2C3", state.Settings.OverlaySettings.SongTextStyle.ColorHex);
        Assert.Equal(64, state.Settings.OverlaySettings.SongTextStyle.MaxCharacters);
        Assert.Equal("#112233", state.Settings.OverlaySettings.OverlayContainerStyle.BackgroundColorHex);
        Assert.Equal("gradient", state.Settings.OverlaySettings.OverlayContainerStyle.BackgroundMode);
        Assert.Equal(2, state.Settings.OverlaySettings.OverlayContainerStyle.Gradient.ColorCount);
        Assert.Equal("Soft Radial", state.Settings.OverlaySettings.OverlayContainerStyle.Gradient.Preset);
        Assert.Equal(180, state.Settings.OverlaySettings.OverlayContainerStyle.Gradient.AngleDeg);
        Assert.Equal(0.42, state.Settings.OverlaySettings.OverlayContainerStyle.Opacity);
        Assert.Equal("#ABCDEF", state.Settings.OverlaySettings.OverlayContainerStyle.BorderColorHex);
        Assert.Equal("JetBrains Mono", state.Settings.OverlaySettings.StatusPillStyle.FontFamily);
        Assert.Equal("Right", state.Settings.OverlaySettings.ImagePosition);
        Assert.Equal("Center", state.Settings.OverlaySettings.TextAlign);
        Assert.False(state.Settings.OverlaySettings.ShowAppName);
        Assert.False(state.Settings.OverlaySettings.ShowPlaybackState);
    }

    [Fact]
    public async Task SaveSettingsAsync_RestoresDefaultBrowserPriority_WhenPriorityListIsMissing()
    {
        using var harness = new BridgeServiceHarness();

        await harness.Service.InitializeAsync(CancellationToken.None);
        var state = await harness.Service.SaveSettingsAsync(new Settings
        {
            OutputFolder = harness.Settings.OutputFolder,
            BrowserSettings = new BrowserSettings
            {
                Enabled = true,
                ActiveSourceMode = "tidal",
                SupportedBrowsers = new BrowserSupportSettings(),
                SourcePriority = null!,
                SourceSwitchCooldownMs = 1500,
                AllowGenericPlayback = true,
                PreferTidalOverBrowser = true,
                MetadataCleanupEnabled = true,
                BrowserArtworkEnabled = true,
                YouTubeVideoImageFallbackEnabled = true,
                IgnorePausedSessions = true,
                IgnoreStaleSessions = true,
                StaleSessionAfterSeconds = 15
            },
            OverlaySettings = new OverlaySettings()
        }, CancellationToken.None);

        Assert.Equal("tidal", state.Settings.BrowserSettings.ActiveSourceMode);
        Assert.Equal(
            ["tidal", "youtubeMusic", "bandcamp", "soundcloud", "youtube", "genericBrowser"],
            state.Settings.BrowserSettings.SourcePriority);
        Assert.Equal(1500, state.Settings.BrowserSettings.SourceSwitchCooldownMs);
        Assert.Equal(15, state.Settings.BrowserSettings.StaleSessionAfterSeconds);
    }

    [Fact]
    public async Task RunDetectionAsync_RedactsSensitiveDebugIdentifiers_WhenDeepDiagnosticsAreDisabled()
    {
        using var harness = new BridgeServiceHarness();
        harness.Settings.BrowserSettings.DebugLoggingEnabled = true;
        harness.PlaybackDetector.Outcomes.Enqueue(new PlaybackDetectionOutcome(
            new DetectionResult
            {
                Status = "playing",
                Artist = "Artist",
                Title = "Track",
                Provider = "browser",
                Method = "media_session",
                Confidence = 0.8
            },
            new BrowserDebugState
            {
                Sessions =
                [
                    new BrowserSessionDebugInfo
                    {
                        SessionId = "browser-session-id",
                        Provider = "browser",
                        Browser = "firefox",
                        Site = "youtube",
                        PlaybackState = "playing",
                        SourceAppId = "firefox-app-id",
                        ParsedArtist = "Artist",
                        ParsedTitle = "Track",
                        DecisionReason = "selected"
                    }
                ],
                RawSessions =
                [
                    new RawMediaSessionDebugInfo
                    {
                        SessionId = "raw-session-id",
                        SourceAppId = "raw-source-app-id",
                        Browser = "firefox",
                        IsPlaying = true,
                        LastUpdatedUtc = DateTimeOffset.UtcNow,
                        Title = "Track",
                        Artist = "Artist",
                        Album = "Album"
                    }
                ],
                AudioEndpoints =
                [
                    new RawAudioEndpointDebugInfo
                    {
                        EndpointId = "endpoint-123",
                        FriendlyName = "VoiceMeeter Input",
                        DeviceState = "active",
                        IsDefaultMultimedia = true
                    }
                ],
                AudioSessions =
                [
                    new RawAudioSessionDebugInfo
                    {
                        SessionId = "audio-session-id",
                        EndpointId = "endpoint-123",
                        ProcessId = 42,
                        ProcessName = "firefox",
                        DisplayName = "Firefox Media Session",
                        State = "Active",
                        PeakLevel = 0.32f,
                        CapturedAtUtc = DateTimeOffset.UtcNow
                    }
                ]
            }));

        await harness.Service.InitializeAsync(CancellationToken.None);
        await harness.Service.RunDetectionAsync(CancellationToken.None);
        var log = ReadSharedText(harness.LogPath);

        Assert.Contains("sessionId=hash:", log);
        Assert.Contains("sourceAppId=\"hash:", log);
        Assert.Contains("endpointId=\"hash:", log);
        Assert.Contains("friendlyName=\"redacted(hash:", log);
        Assert.Contains("displayName=\"redacted(hash:", log);
        Assert.DoesNotContain("browser-session-id", log);
        Assert.DoesNotContain("raw-source-app-id", log);
        Assert.DoesNotContain("VoiceMeeter Input", log);
        Assert.DoesNotContain("Firefox Media Session", log);
    }

    [Fact]
    public async Task RunDetectionAsync_DoesNotWriteBrowserDebugLogLines_WhenDebugLoggingIsDisabled()
    {
        using var harness = new BridgeServiceHarness();
        harness.PlaybackDetector.Outcomes.Enqueue(new PlaybackDetectionOutcome(
            null,
            new BrowserDebugState
            {
                RawSessions =
                [
                    new RawMediaSessionDebugInfo
                    {
                        SessionId = "raw-session-id",
                        SourceAppId = "raw-source-app-id",
                        Browser = "firefox",
                        IsPlaying = true,
                        LastUpdatedUtc = DateTimeOffset.UtcNow,
                        Title = "Track",
                        Artist = "Artist",
                        Album = "Album"
                    }
                ],
                AudioEndpoints =
                [
                    new RawAudioEndpointDebugInfo
                    {
                        EndpointId = "endpoint-123",
                        FriendlyName = "VoiceMeeter Input",
                        DeviceState = "active",
                        IsDefaultMultimedia = true
                    }
                ],
                AudioSessions =
                [
                    new RawAudioSessionDebugInfo
                    {
                        SessionId = "audio-session-id",
                        EndpointId = "endpoint-123",
                        ProcessId = 42,
                        ProcessName = "firefox",
                        DisplayName = "Firefox Media Session",
                        State = "Active",
                        PeakLevel = 0.32f,
                        CapturedAtUtc = DateTimeOffset.UtcNow
                    }
                ]
            }));

        await harness.Service.InitializeAsync(CancellationToken.None);
        await harness.Service.RunDetectionAsync(CancellationToken.None);
        var log = ReadSharedText(harness.LogPath);

        Assert.DoesNotContain("raw-media-session", log);
        Assert.DoesNotContain("raw-audio-endpoint", log);
        Assert.DoesNotContain("raw-audio-session", log);
        Assert.DoesNotContain("browser-debug", log);
    }

    [Fact]
    public async Task RunDetectionAsync_LeavesBlankSensitiveFieldsBlank_WhenDeepDiagnosticsAreDisabled()
    {
        using var harness = new BridgeServiceHarness();
        harness.Settings.BrowserSettings.DebugLoggingEnabled = true;
        harness.PlaybackDetector.Outcomes.Enqueue(new PlaybackDetectionOutcome(
            null,
            new BrowserDebugState
            {
                Sessions =
                [
                    new BrowserSessionDebugInfo
                    {
                        SessionId = "",
                        Provider = "browser",
                        Browser = "firefox",
                        Site = "youtube",
                        PlaybackState = "playing",
                        SourceAppId = "",
                        ParsedArtist = "Artist",
                        ParsedTitle = "Track",
                        DecisionReason = "selected"
                    }
                ],
                AudioEndpoints =
                [
                    new RawAudioEndpointDebugInfo
                    {
                        EndpointId = "",
                        FriendlyName = "",
                        DeviceState = "active",
                        IsDefaultMultimedia = true
                    }
                ],
                AudioSessions =
                [
                    new RawAudioSessionDebugInfo
                    {
                        SessionId = "",
                        EndpointId = "",
                        ProcessId = 42,
                        ProcessName = "firefox",
                        DisplayName = "",
                        State = "Active",
                        PeakLevel = 0.32f,
                        CapturedAtUtc = DateTimeOffset.UtcNow
                    }
                ]
            }));

        await harness.Service.InitializeAsync(CancellationToken.None);
        await harness.Service.RunDetectionAsync(CancellationToken.None);
        var log = ReadSharedText(harness.LogPath);

        Assert.Contains("sessionId= provider=browser", log);
        Assert.Contains("sourceAppId=\"\" parsedArtist=\"Artist\"", log);
        Assert.Contains("endpointId=\"\" friendlyName=\"\"", log);
        Assert.Contains("displayName=\"\" state=Active", log);
        Assert.DoesNotContain("hash:", log);
        Assert.DoesNotContain("redacted(hash:", log);
    }

    [Fact]
    public async Task RunDetectionAsync_LogsFullSensitiveDebugIdentifiers_WhenDeepDiagnosticsAreEnabled()
    {
        using var harness = new BridgeServiceHarness();
        harness.Settings.BrowserSettings.DebugLoggingEnabled = true;
        harness.Settings.BrowserSettings.DeepDiagnosticLoggingEnabled = true;
        harness.PlaybackDetector.Outcomes.Enqueue(new PlaybackDetectionOutcome(
            null,
            new BrowserDebugState
            {
                AudioEndpoints =
                [
                    new RawAudioEndpointDebugInfo
                    {
                        EndpointId = "endpoint-123",
                        FriendlyName = "VoiceMeeter Input",
                        DeviceState = "active",
                        IsDefaultMultimedia = true
                    }
                ],
                AudioSessions =
                [
                    new RawAudioSessionDebugInfo
                    {
                        SessionId = "audio-session-id",
                        EndpointId = "endpoint-123",
                        ProcessId = 42,
                        ProcessName = "firefox",
                        DisplayName = "Firefox Media Session",
                        State = "Active",
                        PeakLevel = 0.32f,
                        CapturedAtUtc = DateTimeOffset.UtcNow
                    }
                ]
            }));

        await harness.Service.InitializeAsync(CancellationToken.None);
        await harness.Service.RunDetectionAsync(CancellationToken.None);
        var log = ReadSharedText(harness.LogPath);

        Assert.Contains("sessionId=audio-session-id", log);
        Assert.Contains("endpointId=\"endpoint-123\"", log);
        Assert.Contains("friendlyName=\"VoiceMeeter Input\"", log);
        Assert.Contains("displayName=\"Firefox Media Session\"", log);
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
    public async Task RunDetectionAsync_HoldsPreviousPlayingTrack_DuringShortSessionLoss()
    {
        using var harness = new BridgeServiceHarness();
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Browser Artist",
            Title = "Browser Track",
            Source = "YouTube",
            Provider = "browser",
            Browser = "firefox",
            Site = "youtube",
            Method = "media_session",
            Confidence = 0.75
        });
        harness.PlaybackDetector.Results.Enqueue(null);

        await harness.Service.InitializeAsync(CancellationToken.None);
        await harness.Service.RunDetectionAsync(CancellationToken.None);
        var state = await harness.Service.RunDetectionAsync(CancellationToken.None);

        Assert.Equal("playing", state.NowPlaying.Status);
        Assert.Equal("Browser Track", state.NowPlaying.Title);
        Assert.Equal("selected: cooldown active after session loss", state.NowPlaying.SelectionReason);
    }

    [Fact]
    public async Task RunDetectionAsync_DoesNotRenewHeldTrackForever_AfterSessionLoss()
    {
        using var harness = new BridgeServiceHarness();
        harness.Settings.BrowserSettings.SourceSwitchCooldownMs = 5;
        harness.PlaybackDetector.Results.Enqueue(new DetectionResult
        {
            Status = "playing",
            Artist = "Browser Artist",
            Title = "Browser Track",
            Source = "YouTube",
            Provider = "browser",
            Browser = "firefox",
            Site = "youtube",
            Method = "media_session",
            Confidence = 0.75
        });
        harness.PlaybackDetector.Results.Enqueue(null);
        harness.PlaybackDetector.Results.Enqueue(null);

        await harness.Service.InitializeAsync(CancellationToken.None);
        await harness.Service.RunDetectionAsync(CancellationToken.None);

        var heldState = await harness.Service.RunDetectionAsync(CancellationToken.None);
        await Task.Delay(25);
        var expiredState = await harness.Service.RunDetectionAsync(CancellationToken.None);

        Assert.Equal("playing", heldState.NowPlaying.Status);
        Assert.Equal("Browser Track", heldState.NowPlaying.Title);
        Assert.Equal("not_running", expiredState.NowPlaying.Status);
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

    private static string ReadSharedText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
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
        public string LogPath => Path.Combine(_tempDir, "bridge.log");

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
            settings.OverlayProfiles = updated.OverlayProfiles;
            settings.ActiveOverlayProfileId = updated.ActiveOverlayProfileId;
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
        public Queue<PlaybackDetectionOutcome> Outcomes { get; } = [];
        public Exception? Exception { get; set; }

        public Task<PlaybackDetectionOutcome> DetectAsync(DetectionResult previous, Settings settings, CancellationToken cancellationToken)
        {
            if (Exception is not null)
            {
                throw Exception;
            }

            if (Outcomes.Count > 0)
            {
                var outcome = Outcomes.Dequeue();
                return Task.FromResult(new PlaybackDetectionOutcome(
                    outcome.Result is null ? null : BridgeStatePolicy.CloneDetection(outcome.Result),
                    CloneBrowserDebugState(outcome.BrowserDebug)));
            }

            if (Results.Count == 0)
            {
                return Task.FromResult(new PlaybackDetectionOutcome(null, new BrowserDebugState()));
            }

            var result = Results.Dequeue();
            return Task.FromResult(new PlaybackDetectionOutcome(result is null ? null : BridgeStatePolicy.CloneDetection(result), new BrowserDebugState()));
        }

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
            }).ToList(),
            RawSessions = state.RawSessions.ToList(),
            AudioEndpoints = state.AudioEndpoints.ToList(),
            AudioSessions = state.AudioSessions.ToList(),
            WindowTitles = state.WindowTitles.ToList()
        };
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
        public string CurrentVersion => "0.5.0";
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
