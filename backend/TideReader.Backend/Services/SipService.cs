using TideReader.Backend.Models;

namespace TideReader.Backend.Services;

public sealed class SipService(BridgeService bridgeService, IAppUpdateChecker appUpdateChecker, SipHostOptions options)
{
    public SipAppResponse App() => new()
    {
        Version = appUpdateChecker.CurrentVersion,
        Mode = NormalizeRuntimeMode(options.RuntimeMode)
    };

    public SipHealthResponse Health()
    {
        var state = bridgeService.GetState();
        if (!string.IsNullOrWhiteSpace(state.LastError))
        {
            return new SipHealthResponse
            {
                Status = "degraded",
                Message = state.LastError
            };
        }

        return new SipHealthResponse
        {
            Status = "ready",
            Message = "TideReader operational"
        };
    }

    public SipCapabilitiesResponse Capabilities() => new()
    {
        SupportsProfiles = true,
        SupportsStatusReporting = true
    };

    public SipStatusResponse Status()
    {
        var health = Health();
        var state = bridgeService.GetState();
        var activeProfile = bridgeService.GetActiveOverlayProfile();
        return new SipStatusResponse
        {
            State = health.Status == "ready" ? StatusState(state.NowPlaying) : "warning",
            Message = string.IsNullOrWhiteSpace(state.StatusMessage) ? health.Message : state.StatusMessage,
            Healthy = health.Status is "ready" or "degraded",
            ActiveProfile = activeProfile.Name,
            ActiveProfileId = activeProfile.Id
        };
    }

    public SipProfilesResponse Profiles() => new()
    {
        Profiles = bridgeService.GetOverlayProfiles()
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Name))
            .Select(profile => profile.Name)
            .ToList()
    };

    public SipCurrentProfileResponse CurrentProfile()
    {
        var activeProfile = bridgeService.GetActiveOverlayProfile();
        return new SipCurrentProfileResponse
        {
            Id = activeProfile.Id,
            Name = activeProfile.Name
        };
    }

    public async Task<SipProfileActivationResponse> ActivateProfileAsync(string profile, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            throw new SipException("InvalidRequest", StatusCodes.Status400BadRequest);
        }

        var activeProfile = await bridgeService.ActivateOverlayProfileAsync(profile, cancellationToken);
        if (activeProfile is null)
        {
            throw new SipException("Profile not found", StatusCodes.Status404NotFound);
        }

        return new SipProfileActivationResponse
        {
            Success = true,
            Profile = activeProfile.Name,
            ProfileId = activeProfile.Id
        };
    }

    private static string NormalizeRuntimeMode(string mode) =>
        string.Equals(mode, SipRuntimeModes.Service, StringComparison.OrdinalIgnoreCase)
            ? SipRuntimeModes.Service
            : SipRuntimeModes.Standalone;

    private static string StatusState(DetectionResult result) =>
        result.Status switch
        {
            "playing" => "active",
            "paused" => "paused",
            "not_running" => "idle",
            _ => "idle"
        };
}
