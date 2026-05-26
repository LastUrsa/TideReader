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
    public string ImagePosition { get; set; } = "Left";
    public string TextAlign { get; set; } = "Left";
    public bool ShowAppName { get; set; } = true;
    public bool ShowPlaybackState { get; set; } = true;
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
}

public sealed class AppState
{
    public Settings Settings { get; set; } = new();
    public DetectionResult NowPlaying { get; set; } = new();
    public long ArtworkRevision { get; set; }
    public string OutputFolder { get; set; } = "";
    public string OverlayUrl { get; set; } = "";
    public string LogPath { get; set; } = "";
    public string LastError { get; set; } = "";
    public string ManualInput { get; set; } = "";
    public bool StartupReady { get; set; } = true;
    public string StatusMessage { get; set; } = "Loading...";
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
