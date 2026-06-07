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
    public const string Version = "1.1";
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
    public string Name { get; set; } = "TideReader";
    public string Version { get; set; } = "";
    public string Mode { get; set; } = SipRuntimeModes.Standalone;
    public string ProtocolVersion { get; set; } = SipProtocol.Version;
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
