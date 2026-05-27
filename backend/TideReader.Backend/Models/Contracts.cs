namespace TideReader.Backend.Models;

public enum PlaybackStatus
{
    Playing,
    Paused,
    NotRunning
}

public enum DetectionMethod
{
    MediaSession,
    WindowTitle,
    Manual,
    None
}

public enum MetadataProviderMode
{
    Off,
    MusicBrainzOnly,
    MusicBrainzWithFallbacks
}

public enum ThemeMode
{
    Dark,
    Light
}

public sealed class OverlayTextStyle
{
    public string FontFamily { get; set; } = "Segoe UI";
    public string ColorHex { get; set; } = "#EBEBEB";
    public int FontSizePx { get; set; } = 24;
    public int MaxCharacters { get; set; }
    public bool Bold { get; set; } = true;
    public bool Italic { get; set; }
    public bool Underline { get; set; }
}

public sealed class OverlayContainerStyle
{
    public string BackgroundMode { get; set; } = "solid";
    public string BackgroundColorHex { get; set; } = "#32334F";
    public GradientSettings Gradient { get; set; } = new();
    public double Opacity { get; set; } = 0.86;
    public int CornerRadiusPx { get; set; } = 18;
    public int PaddingPx { get; set; } = 14;
    public int GapPx { get; set; } = 14;
    public bool BorderEnabled { get; set; } = true;
    public string BorderColorHex { get; set; } = "#929498";
    public int BorderWidthPx { get; set; } = 1;
}

public sealed class GradientSettings
{
    public int ColorCount { get; set; } = 3;
    public string Preset { get; set; } = "Diagonal";
    public string Color1Hex { get; set; } = "#1F1F2E";
    public string Color2Hex { get; set; } = "#6B46C1";
    public string Color3Hex { get; set; } = "#111827";
    public int AngleDeg { get; set; } = 135;
}

public sealed class StatusPillStyle
{
    public string BackgroundColorHex { get; set; } = "#45475D";
    public string TextColorHex { get; set; } = "#787B80";
    public double Opacity { get; set; } = 1;
    public string FontFamily { get; set; } = "Segoe UI";
    public int FontSizePx { get; set; } = 11;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public int CornerRadiusPx { get; set; } = 999;
    public int PaddingHorizontalPx { get; set; } = 9;
    public int PaddingVerticalPx { get; set; } = 4;
}

public sealed class OverlaySettings
{
    public OverlayTextStyle SongTextStyle { get; set; } = new()
    {
        FontFamily = "Segoe UI",
        ColorHex = "#EBEBEB",
        FontSizePx = 24,
        Bold = true
    };

    public OverlayTextStyle ArtistTextStyle { get; set; } = new()
    {
        FontFamily = "Segoe UI",
        ColorHex = "#929498",
        FontSizePx = 15
    };

    public OverlayTextStyle AlbumTextStyle { get; set; } = new()
    {
        FontFamily = "Segoe UI",
        ColorHex = "#929498",
        FontSizePx = 15
    };

    public int ImageSizePx { get; set; } = 68;
    public string BackgroundColorHex { get; set; } = "#32334F";
    public OverlayContainerStyle OverlayContainerStyle { get; set; } = new();
    public StatusPillStyle StatusPillStyle { get; set; } = new();
    public string ImagePosition { get; set; } = "Left";
    public string TextAlign { get; set; } = "Left";
    public bool ShowAppName { get; set; } = true;
    public bool ShowPlaybackState { get; set; } = true;
    public bool ShowPlaybackProvider { get; set; }
}

public sealed class BrowserSupportSettings
{
    public bool ChromeEnabled { get; set; } = true;
    public bool EdgeEnabled { get; set; } = true;
    public bool FirefoxEnabled { get; set; } = true;
    public bool BraveEnabled { get; set; } = true;
    public bool OperaEnabled { get; set; }
}

public sealed class BrowserSettings
{
    public bool Enabled { get; set; } = true;
    public string ActiveSourceMode { get; set; } = "auto";
    public BrowserSupportSettings SupportedBrowsers { get; set; } = new();
    public List<string> SourcePriority { get; set; } =
    [
        "tidal",
        "youtubeMusic",
        "bandcamp",
        "soundcloud",
        "youtube",
        "genericBrowser"
    ];
    public int SourceSwitchCooldownMs { get; set; } = 5000;
    public bool AllowGenericPlayback { get; set; } = true;
    public bool PreferTidalOverBrowser { get; set; } = true;
    public bool MetadataCleanupEnabled { get; set; } = true;
    public bool BrowserArtworkEnabled { get; set; } = true;
    public bool YouTubeVideoImageFallbackEnabled { get; set; } = true;
    public bool DebugLoggingEnabled { get; set; }
    public bool IgnorePausedSessions { get; set; } = true;
    public bool IgnoreStaleSessions { get; set; } = true;
    public int StaleSessionAfterSeconds { get; set; } = 30;
    public bool ShowRawBrowserMetadata { get; set; }
}

public sealed class Settings
{
    public string OutputFolder { get; set; } = Defaults.OutputFolder;
    public bool OverlayEnabled { get; set; } = true;
    public int OverlayPort { get; set; } = 17655;
    public int PollIntervalMs { get; set; } = 1000;
    public bool EnableWindowTitleFallback { get; set; } = true;
    public bool EnableDebugManualInput { get; set; }
    public bool StartMinimized { get; set; }
    public bool LaunchAtStartup { get; set; }
    public string MetadataProviderMode { get; set; } = nameof(Models.MetadataProviderMode.MusicBrainzWithFallbacks);
    public string ThemeMode { get; set; } = nameof(Models.ThemeMode.Dark);
    public OverlaySettings OverlaySettings { get; set; } = new();
    public BrowserSettings BrowserSettings { get; set; } = new();
}

public sealed class BrowserSessionDebugInfo
{
    public string Provider { get; set; } = "";
    public string Browser { get; set; } = "";
    public string Site { get; set; } = "generic";
    public string PlaybackState { get; set; } = "not_running";
    public string SourceAppId { get; set; } = "";
    public string RawTitle { get; set; } = "";
    public string RawArtist { get; set; } = "";
    public string RawAlbum { get; set; } = "";
    public string ParsedTitle { get; set; } = "";
    public string ParsedArtist { get; set; } = "";
    public string ParsedAlbum { get; set; } = "";
    public double Confidence { get; set; }
    public bool HasArtwork { get; set; }
    public bool IsSelected { get; set; }
    public string DecisionReason { get; set; } = "";
    public string SessionId { get; set; } = "";
    public DateTimeOffset LastUpdatedUtc { get; set; }
}

public sealed class BrowserDebugState
{
    public List<BrowserSessionDebugInfo> Sessions { get; set; } = [];
}

public sealed class DetectionResult
{
    public string Status { get; set; } = "not_running";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public long DurationMs { get; set; }
    public string ArtworkPath { get; set; } = "";
    public string Source { get; set; } = "TIDAL";
    public string Method { get; set; } = "none";
    public double Confidence { get; set; }
    public string TidalUrl { get; set; } = "";
    public string DetectedText { get; set; } = "";
    public string SourceAppId { get; set; } = "";
    public string MatcherReason { get; set; } = "";
    public string MetadataSource { get; set; } = "";
    public byte[] ArtworkBytes { get; set; } = [];
    public string Provider { get; set; } = "tidal";
    public string Browser { get; set; } = "";
    public string Site { get; set; } = "";
    public string RawTitle { get; set; } = "";
    public string RawArtist { get; set; } = "";
    public string RawAlbum { get; set; } = "";
    public string SelectionReason { get; set; } = "";
}

public sealed class AppState
{
    public Settings Settings { get; set; } = new();
    public DetectionResult NowPlaying { get; set; } = new();
    public string AppVersion { get; set; } = "";
    public long ArtworkRevision { get; set; }
    public string OutputFolder { get; set; } = "";
    public string OverlayUrl { get; set; } = "";
    public string LogPath { get; set; } = "";
    public string LastError { get; set; } = "";
    public string ManualInput { get; set; } = "";
    public bool StartupReady { get; set; } = true;
    public string StatusMessage { get; set; } = "Loading...";
    public BrowserDebugState BrowserDebug { get; set; } = new();
}

public sealed class UpdateInfo
{
    public string CurrentVersion { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public bool UpdateAvailable { get; set; }
    public string ReleaseUrl { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class NowPlayingFile
{
    public string Status { get; set; } = "not_running";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public long DurationMs { get; set; }
    public string ArtworkPath { get; set; } = "";
    public string Source { get; set; } = "TIDAL";
    public double Confidence { get; set; }
    public string Provider { get; set; } = "tidal";
    public string Browser { get; set; } = "";
    public string Site { get; set; } = "";
}

public static class Defaults
{
    public static string OutputFolder
    {
        get
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documents, "TideReader", "obs-output");
        }
    }
}

public sealed class ManualInputRequest
{
    public string Input { get; set; } = "";
}
