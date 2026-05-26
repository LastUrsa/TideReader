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
