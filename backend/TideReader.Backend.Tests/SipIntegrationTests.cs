using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TideReader.Backend.Models;
using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class SipIntegrationTests
{
    [Fact]
    public async Task SipContract_ReturnsLivePanelDiscoveryPayloads()
    {
        using var context = await StartSipServiceAsync(SipRuntimeModes.Service);

        var appPayload = ToJson(context.Service.App());
        var healthPayload = ToJson(context.Service.Health());
        var capabilitiesPayload = ToJson(context.Service.Capabilities());
        var statusPayload = ToJson(context.Service.Status());

        Assert.Equal("tidereader", appPayload.GetProperty("appId").GetString());
        Assert.Equal("TideReader", appPayload.GetProperty("name").GetString());
        Assert.Equal("0.5.0", appPayload.GetProperty("version").GetString());
        Assert.Equal("service", appPayload.GetProperty("mode").GetString());
        Assert.Equal("1.2", appPayload.GetProperty("protocolVersion").GetString());
        Assert.Equal("ready", healthPayload.GetProperty("status").GetString());
        Assert.Equal("TideReader operational", healthPayload.GetProperty("message").GetString());
        Assert.True(capabilitiesPayload.GetProperty("supportsProfiles").GetBoolean());
        Assert.True(capabilitiesPayload.GetProperty("supportsStatusReporting").GetBoolean());
        Assert.Equal("Default", statusPayload.GetProperty("activeProfile").GetString());
        Assert.Equal("default", statusPayload.GetProperty("activeProfileId").GetString());
        Assert.Equal("http://127.0.0.1:17655/overlay", statusPayload.GetProperty("overlayUrl").GetString());
        Assert.True(statusPayload.GetProperty("overlayEnabled").GetBoolean());
        Assert.Equal(17655, statusPayload.GetProperty("overlayPort").GetInt32());
        Assert.Equal("Right", statusPayload.GetProperty("layout").GetString());
        Assert.True(statusPayload.GetProperty("albumArtVisible").GetBoolean());
        Assert.Equal(96, statusPayload.GetProperty("imageSizePx").GetInt32());
        Assert.False(statusPayload.GetProperty("statusPillVisible").GetBoolean());
        Assert.Equal("gradient", statusPayload.GetProperty("backgroundMode").GetString());
        Assert.Equal("Center", statusPayload.GetProperty("textAlign").GetString());
        Assert.Equal(2, statusPayload.GetProperty("profileCount").GetInt32());

        var nowPlaying = statusPayload.GetProperty("nowPlaying");
        Assert.Equal("Signal Bloom", nowPlaying.GetProperty("title").GetString());
        Assert.Equal("Starsong", nowPlaying.GetProperty("artist").GetString());
        Assert.Equal("Local Skies", nowPlaying.GetProperty("album").GetString());
        Assert.Equal("not_running", nowPlaying.GetProperty("status").GetString());
        Assert.True(nowPlaying.GetProperty("hasArtwork").GetBoolean());
        Assert.Equal("tidal", nowPlaying.GetProperty("provider").GetString());
    }

    [Fact]
    public async Task Profiles_CanBeListedReadAndActivated()
    {
        using var context = await StartSipServiceAsync();

        var profiles = context.Service.Profiles();
        Assert.Equal(["Default", "Listening Party"], profiles.Profiles);

        var activationPayload = await context.Service.ActivateProfileAsync("listening party", CancellationToken.None);
        var current = context.Service.CurrentProfile();

        Assert.True(activationPayload.Success);
        Assert.Equal("Listening Party", activationPayload.Profile);
        Assert.Equal("listening-party", activationPayload.ProfileId);
        Assert.Equal("listening-party", current.Id);
        Assert.Equal("Listening Party", current.Name);
        Assert.Equal(42, context.Bridge.GetState().Settings.OverlaySettings.SongTextStyle.FontSizePx);
    }

    [Fact]
    public async Task ActivateProfile_RejectsInvalidAndMissingProfiles()
    {
        using var context = await StartSipServiceAsync();

        var invalid = await Assert.ThrowsAsync<SipException>(() => context.Service.ActivateProfileAsync("", CancellationToken.None));
        var missing = await Assert.ThrowsAsync<SipException>(() => context.Service.ActivateProfileAsync("Missing", CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, invalid.StatusCode);
        Assert.Equal("InvalidRequest", invalid.Message);
        Assert.Equal(StatusCodes.Status404NotFound, missing.StatusCode);
        Assert.Equal("Profile not found", missing.Message);
    }

    [Fact]
    public async Task App_FallsBackToStandalone_ForUnknownRuntimeMode()
    {
        using var context = await StartSipServiceAsync("unexpected");

        var app = context.Service.App();

        Assert.Equal(SipRuntimeModes.Standalone, app.Mode);
    }

    [Fact]
    public async Task Health_ReturnsDegraded_WhenBridgeReportsLastError()
    {
        using var context = await StartSipServiceAsync(lastError: "Metadata provider unavailable");

        var health = context.Service.Health();
        var status = context.Service.Status();

        Assert.Equal("degraded", health.Status);
        Assert.Equal("Metadata provider unavailable", health.Message);
        Assert.Equal("warning", status.State);
        Assert.True(status.Healthy);
    }

    [Theory]
    [InlineData("playing", "active")]
    [InlineData("paused", "paused")]
    [InlineData("not_running", "idle")]
    [InlineData("buffering", "idle")]
    public async Task Status_MapsPlaybackState(string playbackStatus, string sipState)
    {
        using var context = await StartSipServiceAsync(playbackStatus: playbackStatus);

        var status = context.Service.Status();

        Assert.Equal(sipState, status.State);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("localhost", true)]
    [InlineData("[::1]", true)]
    [InlineData("example.com", false)]
    [InlineData("192.168.1.5", false)]
    public void SipHostValidation_AllowsOnlyLocalHosts(string host, bool expected)
    {
        Assert.Equal(expected, SipHttpApi.IsLocalHost(host));
    }

    [Theory]
    [InlineData("application/json", true)]
    [InlineData("application/json; charset=utf-8", true)]
    [InlineData("text/plain", false)]
    [InlineData("", false)]
    public void SipProfileActivation_RequiresJsonContentType(string? contentType, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = contentType;

        Assert.Equal(expected, SipHttpApi.IsJsonRequest(context.Request));
    }

    [Fact]
    public void SipProfileActivation_RejectsUnknownRequestFields()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SipActivateProfileRequest>(
                """{"profile":"Default","extra":true}""",
                SipHttpApi.JsonOptions));
    }

    [Fact]
    public void SipProfileActivation_RejectsOversizedKnownContentLength()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentLength = SipHttpApi.MaxRequestBodyBytes + 1;

        Assert.True(SipHttpApi.IsOversizedRequest(context.Request));
    }

    [Fact]
    public async Task SipHttpApi_WritesJsonErrorResponses()
    {
        var context = new DefaultHttpContext();
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var responseBody = new MemoryStream();
        context.RequestServices = services;
        context.Response.Body = responseBody;

        await SipHttpApi.WriteErrorAsync(context, StatusCodes.Status403Forbidden, "Forbidden");

        responseBody.Position = 0;
        using var document = await JsonDocument.ParseAsync(responseBody);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Forbidden", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task SipProfileActivationHttp_RejectsUnsupportedMediaType()
    {
        using var context = await StartSipServiceAsync();
        var httpContext = CreateJsonRequest("""{"profile":"Default"}""");
        httpContext.Request.ContentType = "text/plain";

        var result = await SipHttpApi.ActivateProfileAsync(httpContext, context.Service, CancellationToken.None);
        var response = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, response.StatusCode);
        Assert.Equal("InvalidRequest", response.Body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task SipProfileActivationHttp_RejectsOversizedPayload()
    {
        using var context = await StartSipServiceAsync();
        var httpContext = CreateJsonRequest("""{"profile":"Default"}""");
        httpContext.Request.ContentLength = SipHttpApi.MaxRequestBodyBytes + 1;

        var result = await SipHttpApi.ActivateProfileAsync(httpContext, context.Service, CancellationToken.None);
        var response = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, response.StatusCode);
        Assert.Equal("InvalidRequest", response.Body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task SipProfileActivationHttp_RejectsMalformedJson()
    {
        using var context = await StartSipServiceAsync();
        var httpContext = CreateJsonRequest("""{"profile":""");

        var result = await SipHttpApi.ActivateProfileAsync(httpContext, context.Service, CancellationToken.None);
        var response = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal("InvalidRequest", response.Body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task SipProfileActivationHttp_ReturnsNotFoundForMissingProfile()
    {
        using var context = await StartSipServiceAsync();
        var httpContext = CreateJsonRequest("""{"profile":"Missing"}""");

        var result = await SipHttpApi.ActivateProfileAsync(httpContext, context.Service, CancellationToken.None);
        var response = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal("Profile not found", response.Body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task SipProfileActivationHttp_RejectsMissingProfileField()
    {
        using var context = await StartSipServiceAsync();
        var httpContext = CreateJsonRequest("""{}""");

        var result = await SipHttpApi.ActivateProfileAsync(httpContext, context.Service, CancellationToken.None);
        var response = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal("InvalidRequest", response.Body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task SipProfileActivationHttp_ActivatesProfile()
    {
        using var context = await StartSipServiceAsync();
        var httpContext = CreateJsonRequest("""{"profile":"Listening Party"}""");

        var result = await SipHttpApi.ActivateProfileAsync(httpContext, context.Service, CancellationToken.None);
        var response = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.True(response.Body.GetProperty("success").GetBoolean());
        Assert.Equal("listening-party", response.Body.GetProperty("profileId").GetString());
        Assert.Equal("listening-party", context.Service.CurrentProfile().Id);
    }

    private static async Task<TestSipContext> StartSipServiceAsync(
        string runtimeMode = SipRuntimeModes.Standalone,
        string playbackStatus = "not_running",
        string lastError = "")
    {
        var settingsStore = new SipFakeSettingsStore();
        var logger = new AppLogger(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "logs"));
        var bridge = new BridgeService(
            settingsStore,
            logger,
            new SipFakeOutputWriter(),
            new SipFakePlaybackDetector(playbackStatus, lastError),
            new SipFakeWindowTitleDetector(),
            new SipFakeManualDetector(),
            new SipFakeMetadataEnricher(),
            new SipFakeOverlayCoordinator(),
            new OverlaySettingsSnapshotStore(),
            new PlaybackSnapshotStore(),
            new SipFakeAppUpdateChecker());
        await bridge.InitializeAsync(CancellationToken.None);
        await bridge.RunDetectionAsync(CancellationToken.None);
        var service = new SipService(bridge, new SipFakeAppUpdateChecker(), new SipHostOptions { RuntimeMode = runtimeMode });
        return new TestSipContext(service, bridge, logger);
    }

    private static JsonElement ToJson<T>(T value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static DefaultHttpContext CreateJsonRequest(string body)
    {
        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = bytes.Length;
        context.Request.Body = new MemoryStream(bytes);
        return context;
    }

    private static async Task<(int StatusCode, JsonElement Body)> ExecuteResultAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        await using var responseBody = new MemoryStream();
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        context.RequestServices = services;
        context.Response.Body = responseBody;

        await result.ExecuteAsync(context);

        responseBody.Position = 0;
        using var document = await JsonDocument.ParseAsync(responseBody);
        return (context.Response.StatusCode, document.RootElement.Clone());
    }

    private sealed class TestSipContext(SipService service, BridgeService bridge, AppLogger logger) : IDisposable
    {
        public SipService Service { get; } = service;
        public BridgeService Bridge { get; } = bridge;

        public void Dispose()
        {
            logger.Dispose();
        }
    }

    private sealed class SipFakeSettingsStore : ISettingsStore
    {
        private Settings _settings = CreateSettings();

        public Task<Settings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(_settings);

        public Task SaveAsync(Settings settings, CancellationToken cancellationToken)
        {
            _settings = settings;
            return Task.CompletedTask;
        }

        private static Settings CreateSettings()
        {
            var defaultSettings = new OverlaySettings();
            defaultSettings.ImageSizePx = 96;
            defaultSettings.ImagePosition = "Right";
            defaultSettings.TextAlign = "Center";
            defaultSettings.ShowPlaybackState = false;
            defaultSettings.OverlayContainerStyle.BackgroundMode = "gradient";
            var listeningSettings = new OverlaySettings();
            listeningSettings.SongTextStyle.FontSizePx = 42;
            return new Settings
            {
                OutputFolder = @"C:\Temp\TideReaderTests",
                OverlayEnabled = true,
                OverlayPort = 17655,
                EnableWindowTitleFallback = false,
                EnableDebugManualInput = false,
                MetadataProviderMode = nameof(MetadataProviderMode.Off),
                ActiveOverlayProfileId = "default",
                OverlaySettings = defaultSettings,
                OverlayProfiles =
                [
                    new OverlayProfile { Id = "default", Name = "Default", OverlaySettings = defaultSettings },
                    new OverlayProfile { Id = "listening-party", Name = "Listening Party", OverlaySettings = listeningSettings }
                ]
            };
        }
    }

    private sealed class SipFakeOutputWriter : IOutputWriter
    {
        public Task WriteAsync(string outputFolder, DetectionResult state, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SipFakePlaybackDetector(string playbackStatus, string errorMessage) : IPlaybackDetector
    {
        public Task<PlaybackDetectionOutcome> DetectAsync(DetectionResult previous, Settings settings, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                throw new InvalidOperationException(errorMessage);
            }

            return Task.FromResult(new PlaybackDetectionOutcome(new DetectionResult
            {
                Status = playbackStatus,
                Title = "Signal Bloom",
                Artist = "Starsong",
                Album = "Local Skies",
                DurationMs = 214000,
                ArtworkPath = "cover.jpg",
                Source = "TIDAL",
                Method = "media_session",
                Confidence = 0.9,
                Provider = "tidal",
                MetadataSource = "MusicBrainz",
                ArtworkBytes = [1, 2, 3]
            }, new BrowserDebugState()));
        }
    }

    private sealed class SipFakeWindowTitleDetector : IWindowTitleDetector
    {
        public DetectionResult? Detect() => null;
    }

    private sealed class SipFakeManualDetector : IManualDetector
    {
        public DetectionResult? Detect(string input) => null;
    }

    private sealed class SipFakeMetadataEnricher : IMetadataEnricher
    {
        public DetectionResult ApplyCached(DetectionResult input) => input;
        public bool NeedsEnrichment(DetectionResult input, MetadataProviderMode mode) => false;
        public Task<DetectionResult> EnrichAsync(DetectionResult input, MetadataProviderMode mode, CancellationToken cancellationToken) => Task.FromResult(input);
        public Task<DetectionResult> EnrichArtworkAsync(DetectionResult input, MetadataProviderMode mode, CancellationToken cancellationToken) => Task.FromResult(input);
    }

    private sealed class SipFakeOverlayCoordinator : IOverlayCoordinator
    {
        public string Url => "http://127.0.0.1:17655/overlay";
        public Task ConfigureAsync(bool enabled, int port, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SipFakeAppUpdateChecker : IAppUpdateChecker
    {
        public string CurrentVersion => "0.5.0";
        public string ReleaseUrl => "https://github.com/LastUrsa/TideReader/releases";
        public Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken) => Task.FromResult(new UpdateInfo());
    }
}
