using TideReader.Backend.Models;
using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class MediaSessionDetectorTests
{
    [Fact]
    public async Task DetectAsync_ReturnsNull_WhenNoSessionIsAvailable()
    {
        var detector = CreateDetector([]);

        var result = await detector.DetectAsync(new DetectionResult(), new Settings(), CancellationToken.None);

        Assert.Null(result.Result);
    }

    [Fact]
    public async Task DetectAsync_MapsTidalSnapshotToDetectionResult()
    {
        var detector = CreateDetector([
            new MediaSessionSnapshot(
                SessionId: "tidal-1",
                SourceAppId: "TIDAL.exe",
                Browser: "",
                Site: "",
                IsPlaying: true,
                IsPaused: false,
                Title: "Track",
                Artist: "Artist",
                Album: "Album",
                DurationMs: 123000,
                LastUpdatedUtc: DateTimeOffset.UtcNow,
                ArtworkBytes: [1, 2, 3])
        ]);

        var result = await detector.DetectAsync(new DetectionResult(), new Settings(), CancellationToken.None);

        Assert.NotNull(result.Result);
        Assert.Equal("playing", result.Result!.Status);
        Assert.Equal("Track", result.Result.Title);
        Assert.Equal("Artist", result.Result.Artist);
        Assert.Equal("Album", result.Result.Album);
        Assert.Equal("tidal", result.Result.Provider);
        Assert.Equal("cover.jpg", result.Result.ArtworkPath);
        Assert.Equal([1, 2, 3], result.Result.ArtworkBytes);
        Assert.True(result.Result.Confidence > 0.9);
    }

    [Fact]
    public async Task DetectAsync_ParsesBrowserMetadata_AndPrefersPlayingSource()
    {
        var settings = new Settings();
        var detector = CreateDetector([
            new MediaSessionSnapshot(
                SessionId: "browser-1",
                SourceAppId: "chrome.exe youtube",
                Browser: "chrome",
                Site: "",
                IsPlaying: true,
                IsPaused: false,
                Title: "Artist - Song Title (Official Video)",
                Artist: "",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow,
                ArtworkBytes: []),
            new MediaSessionSnapshot(
                SessionId: "tidal-1",
                SourceAppId: "TIDAL.exe",
                Browser: "",
                Site: "",
                IsPlaying: false,
                IsPaused: true,
                Title: "Paused Track",
                Artist: "Paused Artist",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow.AddSeconds(-1),
                ArtworkBytes: [])
        ]);

        var result = await detector.DetectAsync(new DetectionResult(), settings, CancellationToken.None);

        Assert.NotNull(result.Result);
        Assert.Equal("browser", result.Result!.Provider);
        Assert.Equal("chrome", result.Result.Browser);
        Assert.Equal("youtube", result.Result.Site);
        Assert.Equal("Song Title", result.Result.Title);
        Assert.Equal("Artist", result.Result.Artist);
        Assert.NotEmpty(result.BrowserDebug.Sessions);
    }

    [Fact]
    public async Task DetectAsync_IncludesAudioSessionDebugSnapshots()
    {
        var detector = CreateDetector(
        [
            new MediaSessionSnapshot(
                SessionId: "browser-1",
                SourceAppId: "chrome.exe youtube",
                Browser: "chrome",
                Site: "",
                IsPlaying: true,
                IsPaused: false,
                Title: "Artist - Song Title",
                Artist: "",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow,
                ArtworkBytes: [])
        ],
        [
            new AudioSessionSnapshot(
                SessionId: "123|chrome|YouTube|instance",
                EndpointId: "endpoint-1",
                ProcessId: 123,
                ProcessName: "chrome",
                DisplayName: "YouTube",
                IconPath: "",
                SessionIdentifier: "session",
                SessionInstanceIdentifier: "instance",
                State: "active",
                IsSystemSoundsSession: false,
                IsMuted: false,
                PeakLevel: 0.42f,
                CapturedAtUtc: DateTimeOffset.UtcNow)
        ]);

        var result = await detector.DetectAsync(new DetectionResult(), new Settings(), CancellationToken.None);

        Assert.Contains(result.BrowserDebug.AudioSessions, session => session.ProcessName == "chrome" && session.PeakLevel == 0.42f);
    }

    [Fact]
    public async Task DetectAsync_TreatsFreshBrowserSessionWithMetadataAsPlaying_WhenWindowsStatusIsNotExplicitlyPlaying()
    {
        var detector = CreateDetector([
            new MediaSessionSnapshot(
                SessionId: "browser-1",
                SourceAppId: "chrome.exe youtube",
                Browser: "chrome",
                Site: "",
                IsPlaying: false,
                IsPaused: false,
                Title: "Artist - Song Title (Official Video)",
                Artist: "",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow,
                ArtworkBytes: []),
            new MediaSessionSnapshot(
                SessionId: "tidal-1",
                SourceAppId: "TIDAL.exe",
                Browser: "",
                Site: "",
                IsPlaying: false,
                IsPaused: true,
                Title: "Paused Track",
                Artist: "Paused Artist",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow.AddSeconds(-1),
                ArtworkBytes: [])
        ]);

        var result = await detector.DetectAsync(new DetectionResult(), new Settings(), CancellationToken.None);

        Assert.NotNull(result.Result);
        Assert.Equal("browser", result.Result!.Provider);
        Assert.Equal("playing", result.Result.Status);
        Assert.Equal("Song Title", result.Result.Title);
    }

    [Fact]
    public async Task DetectAsync_ParsesBandcampTitlePipeFormat()
    {
        var detector = CreateDetector([
            new MediaSessionSnapshot(
                SessionId: "browser-1",
                SourceAppId: "308046B0AF4A39CB",
                Browser: "firefox",
                Site: "",
                IsPlaying: true,
                IsPaused: false,
                Title: "▶︎ Title Theme (from \"Megaman Battle Network\") | Lowlander",
                Artist: "",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow,
                ArtworkBytes: [])
        ]);

        var result = await detector.DetectAsync(new DetectionResult(), new Settings(), CancellationToken.None);

        Assert.NotNull(result.Result);
        Assert.Equal("bandcamp", result.Result!.Site);
        Assert.Equal("Title Theme (from \"Megaman Battle Network\")", result.Result.Title);
        Assert.Equal("Lowlander", result.Result.Artist);
    }

    [Fact]
    public async Task DetectAsync_RespectsActiveSourceMode()
    {
        var settings = new Settings
        {
            BrowserSettings = new BrowserSettings
            {
                ActiveSourceMode = "tidal"
            }
        };
        var detector = CreateDetector([
            new MediaSessionSnapshot(
                SessionId: "browser-1",
                SourceAppId: "chrome.exe youtube",
                Browser: "chrome",
                Site: "",
                IsPlaying: true,
                IsPaused: false,
                Title: "Artist - Browser Track",
                Artist: "",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow,
                ArtworkBytes: []),
            new MediaSessionSnapshot(
                SessionId: "tidal-1",
                SourceAppId: "TIDAL.exe",
                Browser: "",
                Site: "",
                IsPlaying: true,
                IsPaused: false,
                Title: "Tidal Track",
                Artist: "Tidal Artist",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow,
                ArtworkBytes: [])
        ]);

        var result = await detector.DetectAsync(new DetectionResult(), settings, CancellationToken.None);

        Assert.NotNull(result.Result);
        Assert.Equal("tidal", result.Result!.Provider);
    }

    [Fact]
    public async Task DetectAsync_AppliesCooldownToKeepPreviousSource()
    {
        var settings = new Settings
        {
            BrowserSettings = new BrowserSettings
            {
                SourcePriority = ["tidal", "youtubeMusic", "bandcamp", "soundcloud", "youtube", "genericBrowser"],
                SourceSwitchCooldownMs = 5000
            }
        };
        var previous = new DetectionResult
        {
            Status = "playing",
            Provider = "browser",
            Browser = "firefox",
            Site = "youtube",
            Title = "Track A",
            Artist = "Artist A"
        };
        var detector = CreateDetector([
            new MediaSessionSnapshot(
                SessionId: "previous-session",
                SourceAppId: "firefox youtube",
                Browser: "firefox",
                Site: "",
                IsPlaying: true,
                IsPaused: false,
                Title: "Artist A - Track A",
                Artist: "",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow.AddSeconds(-1),
                ArtworkBytes: []),
            new MediaSessionSnapshot(
                SessionId: "selected-session",
                SourceAppId: "firefox bandcamp",
                Browser: "firefox",
                Site: "",
                IsPlaying: true,
                IsPaused: false,
                Title: "Track Name, by Artist B",
                Artist: "",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow,
                ArtworkBytes: [])
        ]);

        var result = await detector.DetectAsync(previous, settings, CancellationToken.None);

        Assert.NotNull(result.Result);
        Assert.Equal("youtube", result.Result!.Site);
        Assert.Equal("selected: selected: cooldown active", result.Result.SelectionReason);
    }

    [Fact]
    public async Task DetectAsync_PreferTidalOverBrowser_SelectsTidalEvenWhenPriorityListOmitsIt()
    {
        var settings = new Settings
        {
            BrowserSettings = new BrowserSettings
            {
                PreferTidalOverBrowser = true,
                SourcePriority = ["browser", "youtubeMusic", "youtube", "genericBrowser"]
            }
        };
        var detector = CreateDetector([
            new MediaSessionSnapshot(
                SessionId: "browser-1",
                SourceAppId: "chrome.exe youtube",
                Browser: "chrome",
                Site: "",
                IsPlaying: true,
                IsPaused: false,
                Title: "Artist - Browser Track",
                Artist: "",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow,
                ArtworkBytes: []),
            new MediaSessionSnapshot(
                SessionId: "tidal-1",
                SourceAppId: "TIDAL.exe",
                Browser: "",
                Site: "",
                IsPlaying: true,
                IsPaused: false,
                Title: "Tidal Track",
                Artist: "Tidal Artist",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow.AddSeconds(-1),
                ArtworkBytes: [])
        ]);

        var result = await detector.DetectAsync(new DetectionResult(), settings, CancellationToken.None);

        Assert.NotNull(result.Result);
        Assert.Equal("tidal", result.Result!.Provider);
        Assert.Equal("Tidal Track", result.Result.Title);
    }

    [Fact]
    public async Task DetectAsync_PreferTidalOverBrowser_BypassesCooldownWhenTidalIsPlaying()
    {
        var settings = new Settings
        {
            BrowserSettings = new BrowserSettings
            {
                PreferTidalOverBrowser = true,
                SourceSwitchCooldownMs = 5000
            }
        };
        var previous = new DetectionResult
        {
            Status = "playing",
            Provider = "browser",
            Browser = "chrome",
            Site = "youtube",
            Title = "Browser Track",
            Artist = "Browser Artist"
        };
        var detector = CreateDetector([
            new MediaSessionSnapshot(
                SessionId: "browser-session",
                SourceAppId: "chrome.exe youtube",
                Browser: "chrome",
                Site: "",
                IsPlaying: true,
                IsPaused: false,
                Title: "Browser Artist - Browser Track",
                Artist: "",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow.AddSeconds(-1),
                ArtworkBytes: []),
            new MediaSessionSnapshot(
                SessionId: "tidal-session",
                SourceAppId: "TIDAL.exe",
                Browser: "",
                Site: "",
                IsPlaying: true,
                IsPaused: false,
                Title: "Tidal Track",
                Artist: "Tidal Artist",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow,
                ArtworkBytes: [])
        ]);

        var result = await detector.DetectAsync(previous, settings, CancellationToken.None);

        Assert.NotNull(result.Result);
        Assert.Equal("tidal", result.Result!.Provider);
        Assert.Equal("Tidal Track", result.Result.Title);
    }

    [Fact]
    public async Task DetectAsync_PreferTidalOverBrowser_DoesNotSelectFreshTidalSession_WhenWindowsStatusIsNotExplicitlyPlaying()
    {
        var settings = new Settings
        {
            BrowserSettings = new BrowserSettings
            {
                PreferTidalOverBrowser = true
            }
        };
        var detector = CreateDetector([
            new MediaSessionSnapshot(
                SessionId: "browser-session",
                SourceAppId: "chrome.exe youtube",
                Browser: "chrome",
                Site: "",
                IsPlaying: true,
                IsPaused: false,
                Title: "Browser Artist - Browser Track",
                Artist: "",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow.AddSeconds(-1),
                ArtworkBytes: []),
            new MediaSessionSnapshot(
                SessionId: "tidal-fresh-session",
                SourceAppId: "TIDAL.exe",
                Browser: "",
                Site: "",
                IsPlaying: false,
                IsPaused: false,
                Title: "Tidal Track",
                Artist: "Tidal Artist",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow,
                ArtworkBytes: [])
        ]);

        var result = await detector.DetectAsync(new DetectionResult(), settings, CancellationToken.None);

        Assert.NotNull(result.Result);
        Assert.Equal("browser", result.Result!.Provider);
        Assert.Equal("Browser Track", result.Result.Title);
    }

    [Fact]
    public async Task DetectAsync_IgnoresInactiveBrowserSession_AndSelectsActiveTidal()
    {
        var settings = new Settings
        {
            BrowserSettings = new BrowserSettings
            {
                PreferTidalOverBrowser = true
            }
        };
        var detector = CreateDetector([
            new MediaSessionSnapshot(
                SessionId: "browser-inactive",
                SourceAppId: "chrome.exe youtube",
                Browser: "chrome",
                Site: "",
                IsPlaying: false,
                IsPaused: false,
                Title: "Artist - Old Browser Track",
                Artist: "",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow.AddMinutes(-2),
                ArtworkBytes: []),
            new MediaSessionSnapshot(
                SessionId: "tidal-active",
                SourceAppId: "TIDAL.exe",
                Browser: "",
                Site: "",
                IsPlaying: true,
                IsPaused: false,
                Title: "Tidal Track",
                Artist: "Tidal Artist",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow.AddSeconds(-1),
                ArtworkBytes: [])
        ]);

        var result = await detector.DetectAsync(new DetectionResult(), settings, CancellationToken.None);

        Assert.NotNull(result.Result);
        Assert.Equal("tidal", result.Result!.Provider);
        Assert.Contains(result.BrowserDebug.Sessions, session => session.SessionId == "browser-inactive" && session.DecisionReason == "ignored: stale session");
    }

    [Fact]
    public async Task DetectAsync_KeepsActiveBrowserSessionEligible_EvenWhenMetadataTimestampIsOld()
    {
        var settings = new Settings
        {
            BrowserSettings = new BrowserSettings
            {
                IgnoreStaleSessions = true,
                StaleSessionAfterSeconds = 5
            }
        };
        var detector = CreateDetector([
            new MediaSessionSnapshot(
                SessionId: "browser-playing-old-timestamp",
                SourceAppId: "chrome.exe youtube",
                Browser: "chrome",
                Site: "",
                IsPlaying: true,
                IsPaused: false,
                Title: "Artist - Browser Track",
                Artist: "",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow.AddMinutes(-2),
                ArtworkBytes: []),
            new MediaSessionSnapshot(
                SessionId: "tidal-inactive",
                SourceAppId: "TIDAL.exe",
                Browser: "",
                Site: "",
                IsPlaying: false,
                IsPaused: false,
                Title: "Stopped Tidal Track",
                Artist: "Stopped Artist",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow.AddMinutes(-2),
                ArtworkBytes: [])
        ]);

        var result = await detector.DetectAsync(new DetectionResult(), settings, CancellationToken.None);

        Assert.NotNull(result.Result);
        Assert.Equal("browser", result.Result!.Provider);
        Assert.Equal("Browser Track", result.Result.Title);
        Assert.DoesNotContain(result.BrowserDebug.Sessions, session => session.SessionId == "browser-playing-old-timestamp" && session.DecisionReason == "ignored: stale session");
    }

    [Fact]
    public async Task DetectAsync_ReportsIgnoredReasons_ForPausedStaleAndLowerPrioritySessions()
    {
        var settings = new Settings
        {
            BrowserSettings = new BrowserSettings
            {
                IgnorePausedSessions = true,
                IgnoreStaleSessions = true,
                StaleSessionAfterSeconds = 5
            }
        };
        var detector = CreateDetector([
            new MediaSessionSnapshot(
                SessionId: "paused-session",
                SourceAppId: "chrome.exe soundcloud",
                Browser: "chrome",
                Site: "",
                IsPlaying: false,
                IsPaused: true,
                Title: "Artist - Paused Track",
                Artist: "",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow,
                ArtworkBytes: []),
            new MediaSessionSnapshot(
                SessionId: "stale-session",
                SourceAppId: "chrome.exe generic",
                Browser: "chrome",
                Site: "",
                IsPlaying: false,
                IsPaused: false,
                Title: "Old Generic Track",
                Artist: "",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow.AddMinutes(-2),
                ArtworkBytes: []),
            new MediaSessionSnapshot(
                SessionId: "selected-session",
                SourceAppId: "TIDAL.exe",
                Browser: "",
                Site: "",
                IsPlaying: true,
                IsPaused: false,
                Title: "Chosen Track",
                Artist: "Chosen Artist",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow,
                ArtworkBytes: []),
            new MediaSessionSnapshot(
                SessionId: "ignored-lower-priority",
                SourceAppId: "chrome.exe youtube",
                Browser: "chrome",
                Site: "",
                IsPlaying: true,
                IsPaused: false,
                Title: "Artist - Browser Track",
                Artist: "",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow.AddSeconds(-1),
                ArtworkBytes: [])
        ]);

        var result = await detector.DetectAsync(new DetectionResult(), settings, CancellationToken.None);

        Assert.NotNull(result.Result);
        Assert.Contains(result.BrowserDebug.Sessions, session => session.SessionId == "paused-session" && session.DecisionReason == "ignored: paused");
        Assert.Contains(result.BrowserDebug.Sessions, session => session.SessionId == "stale-session" && session.DecisionReason == "ignored: stale session");
        Assert.Contains(result.BrowserDebug.Sessions, session => session.SessionId == "ignored-lower-priority" && session.DecisionReason == "ignored: lower priority than TIDAL");
    }

    [Fact]
    public async Task DetectAsync_PreservesRawBrowserMetadata_WhenCleanupDisabled()
    {
        var settings = new Settings
        {
            BrowserSettings = new BrowserSettings
            {
                MetadataCleanupEnabled = false
            }
        };
        var detector = CreateDetector([
            new MediaSessionSnapshot(
                SessionId: "browser-1",
                SourceAppId: "chrome.exe youtube",
                Browser: "chrome",
                Site: "",
                IsPlaying: true,
                IsPaused: false,
                Title: "Artist - Song Title (Official Video)",
                Artist: "",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow,
                ArtworkBytes: [])
        ]);

        var result = await detector.DetectAsync(new DetectionResult(), settings, CancellationToken.None);

        Assert.NotNull(result.Result);
        Assert.Equal("Artist - Song Title (Official Video)", result.Result!.Title);
        Assert.Equal(string.Empty, result.Result.Artist);
        Assert.Equal(0.5, result.Result.Confidence);
    }

    [Fact]
    public async Task DetectAsync_UsesArtworkAndSiteSpecificBrowserBranches()
    {
        var settings = new Settings
        {
            BrowserSettings = new BrowserSettings
            {
                BrowserArtworkEnabled = false,
                YouTubeVideoImageFallbackEnabled = false,
                SupportedBrowsers = new BrowserSupportSettings
                {
                    ChromeEnabled = false,
                    EdgeEnabled = false,
                    FirefoxEnabled = true,
                    BraveEnabled = false,
                    OperaEnabled = false
                }
            }
        };
        var detector = CreateDetector([
            new MediaSessionSnapshot(
                SessionId: "ignored-chrome",
                SourceAppId: "chrome.exe soundcloud",
                Browser: "chrome",
                Site: "",
                IsPlaying: true,
                IsPaused: false,
                Title: "Artist - Ignored Track",
                Artist: "",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow,
                ArtworkBytes: [1, 2, 3]),
            new MediaSessionSnapshot(
                SessionId: "selected-firefox",
                SourceAppId: "firefox soundcloud",
                Browser: "firefox",
                Site: "",
                IsPlaying: true,
                IsPaused: false,
                Title: "Artist - Stream Track",
                Artist: "",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow,
                ArtworkBytes: [1, 2, 3])
        ]);

        var result = await detector.DetectAsync(new DetectionResult(), settings, CancellationToken.None);

        Assert.NotNull(result.Result);
        Assert.Equal("soundcloud", result.Result!.Site);
        Assert.Equal("SoundCloud", result.Result.Source);
        Assert.Empty(result.Result.ArtworkBytes);
        Assert.DoesNotContain(result.BrowserDebug.Sessions, session => session.SessionId == "ignored-chrome");
    }

    [Fact]
    public async Task DetectAsync_DetectsYouTubeMusic_AndPreservesStructuredArtwork()
    {
        var settings = new Settings
        {
            BrowserSettings = new BrowserSettings
            {
                BrowserArtworkEnabled = true
            }
        };
        var detector = CreateDetector([
            new MediaSessionSnapshot(
                SessionId: "ytmusic-1",
                SourceAppId: "firefox music.youtube.com",
                Browser: "firefox",
                Site: "",
                IsPlaying: true,
                IsPaused: false,
                Title: "Structured Song",
                Artist: "Structured Artist",
                Album: "Structured Album",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow,
                ArtworkBytes: [7, 8, 9])
        ]);

        var result = await detector.DetectAsync(new DetectionResult(), settings, CancellationToken.None);

        Assert.NotNull(result.Result);
        Assert.Equal("youtubeMusic", result.Result!.Site);
        Assert.Equal("YouTube Music", result.Result.Source);
        Assert.Equal([7, 8, 9], result.Result.ArtworkBytes);
        Assert.Equal(0.95, result.Result.Confidence);
    }

    [Fact]
    public async Task DetectAsync_StripsGenericBrowserArtwork_WhenVideoFallbackDisabled()
    {
        var settings = new Settings
        {
            BrowserSettings = new BrowserSettings
            {
                BrowserArtworkEnabled = true,
                YouTubeVideoImageFallbackEnabled = false
            }
        };
        var detector = CreateDetector([
            new MediaSessionSnapshot(
                SessionId: "generic-1",
                SourceAppId: "firefox media session",
                Browser: "firefox",
                Site: "",
                IsPlaying: true,
                IsPaused: false,
                Title: "Generic Video Title",
                Artist: "",
                Album: "",
                DurationMs: 0,
                LastUpdatedUtc: DateTimeOffset.UtcNow,
                ArtworkBytes: [4, 5, 6])
        ]);

        var result = await detector.DetectAsync(new DetectionResult(), settings, CancellationToken.None);

        Assert.NotNull(result.Result);
        Assert.Equal("generic", result.Result!.Site);
        Assert.Equal("Browser", result.Result.Source);
        Assert.Empty(result.Result.ArtworkBytes);
    }

    private static MediaSessionDetector CreateDetector(
        IReadOnlyList<MediaSessionSnapshot> snapshots,
        IReadOnlyList<AudioSessionSnapshot>? audioSnapshots = null) =>
        new(
            new FakeSnapshotProvider(snapshots),
            new FakeAudioSnapshotProvider(audioSnapshots ?? []),
            [new TidalPlaybackProvider(), new BrowserMediaProvider()]);

    private sealed class FakeSnapshotProvider(IReadOnlyList<MediaSessionSnapshot> snapshots) : IMediaSessionSnapshotProvider
    {
        public Task<IReadOnlyList<MediaSessionSnapshot>> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult(snapshots);
    }

    private sealed class FakeAudioSnapshotProvider(IReadOnlyList<AudioSessionSnapshot> snapshots) : IAudioSessionSnapshotProvider
    {
        public Task<AudioSessionSnapshotResult> GetCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AudioSessionSnapshotResult([], snapshots));
    }
}
