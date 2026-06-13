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
        var overlaySettings = activeProfile.OverlaySettings ?? new OverlaySettings();
        var containerStyle = overlaySettings.OverlayContainerStyle ?? new OverlayContainerStyle();
        return new SipStatusResponse
        {
            State = health.Status == "ready" ? StatusState(state.NowPlaying) : "warning",
            Message = string.IsNullOrWhiteSpace(state.StatusMessage) ? health.Message : state.StatusMessage,
            Healthy = health.Status is "ready" or "degraded",
            ActiveProfile = activeProfile.Name,
            ActiveProfileId = activeProfile.Id,
            ActiveProfileName = activeProfile.Name,
            BrowserSupportEnabled = state.Settings.BrowserSettings.Enabled,
            Source = PlaybackSource(state.NowPlaying),
            OverlayUrl = state.OverlayUrl,
            OverlayEnabled = state.Settings.OverlayEnabled,
            OverlayPort = state.Settings.OverlayPort,
            Layout = overlaySettings.ImagePosition,
            AlbumArtVisible = overlaySettings.ImageSizePx > 0,
            ImageSizePx = overlaySettings.ImageSizePx,
            StatusPillVisible = overlaySettings.ShowPlaybackState,
            BackgroundMode = containerStyle.BackgroundMode,
            TextAlign = overlaySettings.TextAlign,
            ProfileCount = bridgeService.GetOverlayProfiles().Count,
            NowPlaying = ToNowPlayingSummary(state.NowPlaying)
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

    public SipBrowserSupportResponse BrowserSupport()
    {
        var state = bridgeService.GetState();
        return new SipBrowserSupportResponse
        {
            Enabled = state.Settings.BrowserSettings.Enabled
        };
    }

    public async Task<SipBrowserSupportUpdateResponse> SetBrowserSupportAsync(bool? enabled, CancellationToken cancellationToken)
    {
        if (enabled is null)
        {
            throw new SipException("InvalidRequest", StatusCodes.Status400BadRequest);
        }

        await bridgeService.SetBrowserSupportEnabledAsync(enabled.Value, cancellationToken);
        return new SipBrowserSupportUpdateResponse
        {
            Success = true
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

    private static string PlaybackSource(DetectionResult result)
    {
        if (result.Status is not ("playing" or "paused"))
        {
            return "none";
        }

        return string.Equals(result.Provider, "browser", StringComparison.OrdinalIgnoreCase)
            ? "browser"
            : "desktop";
    }

    private static SipNowPlayingSummary ToNowPlayingSummary(DetectionResult result) => new()
    {
        Status = result.Status,
        Title = result.Title,
        Artist = result.Artist,
        Album = result.Album,
        DurationMs = result.DurationMs,
        HasArtwork = result.ArtworkBytes.Length > 0 || !string.IsNullOrWhiteSpace(result.ArtworkPath),
        ArtworkPath = result.ArtworkPath,
        Source = result.Source,
        Provider = result.Provider,
        Browser = result.Browser,
        Site = result.Site,
        Confidence = result.Confidence,
        MetadataSource = result.MetadataSource
    };
}
