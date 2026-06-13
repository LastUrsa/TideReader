using System.Diagnostics.CodeAnalysis;

namespace TideReader.Backend.Services;

[ExcludeFromCodeCoverage]
public static class SipRuntimeModes
{
    public const string Standalone = "standalone";
    public const string Service = "service";
}

[ExcludeFromCodeCoverage]
public static class SipProtocol
{
    public const int Version = 1;
    public const string ProfilesCapability = "profiles";
    public const string BrowserSupportCapability = "browser-support";
}

[ExcludeFromCodeCoverage]
public sealed class SipHostOptions
{
    public string RuntimeMode { get; init; } = SipRuntimeModes.Standalone;
}

[ExcludeFromCodeCoverage]
public sealed class SipAppResponse
{
    public string AppId { get; set; } = "tidereader";
    public string AppName { get; set; } = "TideReader";
    public string Name { get; set; } = "TideReader";
    public string Version { get; set; } = "";
    public string Mode { get; set; } = SipRuntimeModes.Standalone;
    public int ProtocolVersion { get; set; } = SipProtocol.Version;
    public List<string> Capabilities { get; set; } =
    [
        SipProtocol.ProfilesCapability,
        SipProtocol.BrowserSupportCapability
    ];
}

[ExcludeFromCodeCoverage]
public sealed class SipHealthResponse
{
    public string Status { get; set; } = "ready";
    public string Message { get; set; } = "";
}

[ExcludeFromCodeCoverage]
public sealed class SipCapabilitiesResponse
{
    public int ProtocolVersion { get; set; } = SipProtocol.Version;
    public List<string> Capabilities { get; set; } =
    [
        SipProtocol.ProfilesCapability,
        SipProtocol.BrowserSupportCapability
    ];
    public bool SupportsProfiles { get; set; }
    public bool SupportsStatusReporting { get; set; }
}

[ExcludeFromCodeCoverage]
public sealed class SipStatusResponse
{
    public string State { get; set; } = "idle";
    public string Message { get; set; } = "";
    public bool Healthy { get; set; }
    public string ActiveProfile { get; set; } = "";
    public string ActiveProfileId { get; set; } = "";
    public string ActiveProfileName { get; set; } = "";
    public bool BrowserSupportEnabled { get; set; }
    public string Source { get; set; } = "none";
    public string OverlayUrl { get; set; } = "";
    public bool OverlayEnabled { get; set; }
    public int OverlayPort { get; set; }
    public string Layout { get; set; } = "";
    public bool AlbumArtVisible { get; set; }
    public int ImageSizePx { get; set; }
    public bool StatusPillVisible { get; set; }
    public string BackgroundMode { get; set; } = "";
    public string TextAlign { get; set; } = "";
    public int ProfileCount { get; set; }
    public SipNowPlayingSummary NowPlaying { get; set; } = new();
}

[ExcludeFromCodeCoverage]
public sealed class SipNowPlayingSummary
{
    public string Status { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public long DurationMs { get; set; }
    public bool HasArtwork { get; set; }
    public string ArtworkPath { get; set; } = "";
    public string Source { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Browser { get; set; } = "";
    public string Site { get; set; } = "";
    public double Confidence { get; set; }
    public string MetadataSource { get; set; } = "";
}

[ExcludeFromCodeCoverage]
public sealed class SipProfilesResponse
{
    public List<string> Profiles { get; set; } = [];
}

[ExcludeFromCodeCoverage]
public sealed class SipCurrentProfileResponse
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

[ExcludeFromCodeCoverage]
public sealed class SipActivateProfileRequest
{
    public string Profile { get; set; } = "";
}

[ExcludeFromCodeCoverage]
public sealed class SipProfileActivationResponse
{
    public bool Success { get; set; }
    public string Profile { get; set; } = "";
    public string ProfileId { get; set; } = "";
}

[ExcludeFromCodeCoverage]
public sealed class SipBrowserSupportResponse
{
    public bool Enabled { get; set; }
}

[ExcludeFromCodeCoverage]
public sealed class SipBrowserSupportRequest
{
    public bool? Enabled { get; set; }
}

[ExcludeFromCodeCoverage]
public sealed class SipBrowserSupportUpdateResponse
{
    public bool Success { get; set; }
}

[ExcludeFromCodeCoverage]
public sealed class SipErrorResponse
{
    public bool Success { get; set; }
    public string Error { get; set; } = "";
}

[ExcludeFromCodeCoverage]
public sealed class SipException(string error, int statusCode) : Exception(error)
{
    public int StatusCode { get; } = statusCode;
}
