using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TideReader.Backend;
using TideReader.Backend.Models;
using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class BackendHostIntegrationTests
{
    [Fact]
    public async Task Health_ReturnsOk()
    {
        await using var app = await StartTestAppAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MutatingEndpoints_RejectRequests_WithoutLocalApiToken_WhenConfigured()
    {
        await using var app = await StartTestAppAsync(localApiToken: "secret-token");
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/run-detection", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiState_RejectsRequests_WithoutLocalApiToken_WhenConfigured()
    {
        await using var app = await StartTestAppAsync(localApiToken: "secret-token");
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/state");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiEndpoints_AcceptRequests_WithLocalApiToken_WhenConfigured()
    {
        await using var app = await StartTestAppAsync(localApiToken: "secret-token");
        var bridge = app.Services.GetRequiredService<BridgeService>();
        await bridge.InitializeAsync(CancellationToken.None);
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-TideReader-Token", "secret-token");

        var stateResponse = await client.GetAsync("/api/state");
        var runResponse = await client.PostAsJsonAsync("/api/run-detection", new { });

        Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, runResponse.StatusCode);
    }

    [Fact]
    public async Task ApiArtwork_AcceptsQueryToken_WhenConfigured()
    {
        var detector = new HostFakePlaybackDetector(new DetectionResult
        {
            Status = "playing",
            Artist = "Artist",
            Title = "Track",
            Album = "Album",
            Method = "media_session",
            ArtworkPath = "cover.jpg",
            ArtworkBytes = [1, 2, 3],
            Confidence = 0.9
        });

        await using var app = await StartTestAppAsync(services =>
        {
            services.RemoveAll<IPlaybackDetector>();
            services.AddSingleton<IPlaybackDetector>(detector);
        }, localApiToken: "secret-token");

        var bridge = app.Services.GetRequiredService<BridgeService>();
        await bridge.InitializeAsync(CancellationToken.None);
        await bridge.RunDetectionAsync(CancellationToken.None);
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/artwork?tr_token=secret-token");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SaveSettings_ReturnsBadRequest_ForUnsafeOutputFolder()
    {
        await using var app = await StartTestAppAsync(localApiToken: "secret-token");
        var bridge = app.Services.GetRequiredService<BridgeService>();
        await bridge.InitializeAsync(CancellationToken.None);
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-TideReader-Token", "secret-token");

        var response = await client.PostAsJsonAsync("/api/settings", new Settings
        {
            OutputFolder = @"C:\",
            OverlayEnabled = true,
            OverlayPort = 17655,
            PollIntervalMs = 1000,
            EnableWindowTitleFallback = true,
            MetadataProviderMode = nameof(MetadataProviderMode.Off)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task State_ReturnsInitializedState()
    {
        await using var app = await StartTestAppAsync();
        var bridge = app.Services.GetRequiredService<BridgeService>();
        await bridge.InitializeAsync(CancellationToken.None);
        var client = app.GetTestClient();

        var state = await client.GetFromJsonAsync<AppState>("/api/state");

        Assert.NotNull(state);
        Assert.True(state!.StartupReady);
        Assert.Equal(@"C:\Temp\TideReaderTests", state.OutputFolder);
        Assert.Equal("0.5.0", state.AppVersion);
    }

    [Fact]
    public async Task CheckForUpdates_ReturnsPayloadFromService()
    {
        await using var app = await StartTestAppAsync();
        var client = app.GetTestClient();

        var payload = await client.GetFromJsonAsync<UpdateInfo>("/api/check-for-updates");

        Assert.NotNull(payload);
        Assert.Equal("0.5.0", payload!.CurrentVersion);
        Assert.Equal("0.1.1", payload.LatestVersion);
        Assert.True(payload.UpdateAvailable);
    }

    [Fact]
    public async Task OpenReleasesPage_UsesUrlLauncher()
    {
        var launcher = new HostFakeExternalUrlLauncher();

        await using var app = await StartTestAppAsync(services =>
        {
            services.RemoveAll<IExternalUrlLauncher>();
            services.AddSingleton<IExternalUrlLauncher>(launcher);
        });

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync("/api/open-releases-page", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://github.com/LastUrsa/TideReader/releases", launcher.OpenedUrl);
    }

    [Fact]
    public async Task RunDetection_ThenArtwork_ReturnsJpeg()
    {
        var detector = new HostFakePlaybackDetector(new DetectionResult
        {
            Status = "playing",
            Artist = "Artist",
            Title = "Track",
            Album = "Album",
            Method = "media_session",
            ArtworkPath = "cover.jpg",
            ArtworkBytes = [1, 2, 3],
            Confidence = 0.9
        });

        await using var app = await StartTestAppAsync(services =>
        {
            services.RemoveAll<IPlaybackDetector>();
            services.AddSingleton<IPlaybackDetector>(detector);
        });

        var bridge = app.Services.GetRequiredService<BridgeService>();
        await bridge.InitializeAsync(CancellationToken.None);
        var client = app.GetTestClient();

        var runResponse = await client.PostAsJsonAsync("/api/run-detection", new { });
        var artResponse = await client.GetAsync("/api/artwork");

        Assert.Equal(HttpStatusCode.OK, runResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, artResponse.StatusCode);
        Assert.Equal("image/jpeg", artResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal([1, 2, 3], await artResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task InitializedBridge_HasNoArtwork_WhenNoArtworkIsAvailable()
    {
        using var logger = new AppLogger(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "logs"));
        var bridge = new BridgeService(
            new HostFakeSettingsStore(),
            logger,
            new HostFakeOutputWriter(),
            new HostFakePlaybackDetector(null),
            new HostFakeWindowTitleDetector(),
            new HostFakeManualDetector(),
            new HostFakeMetadataEnricher(),
            new HostFakeOverlayCoordinator(),
            new OverlaySettingsSnapshotStore(),
            new PlaybackSnapshotStore(),
            new HostFakeAppUpdateChecker());
        await bridge.InitializeAsync(CancellationToken.None);

        Assert.Empty(bridge.GetArtwork());
    }

    [Fact]
    public async Task ManualInputEndpoint_UpdatesState()
    {
        await using var app = await StartTestAppAsync();
        var bridge = app.Services.GetRequiredService<BridgeService>();
        await bridge.InitializeAsync(CancellationToken.None);
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/manual-input", new ManualInputRequest
        {
            Input = " Artist - Title "
        });
        var state = await response.Content.ReadFromJsonAsync<AppState>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(state);
        Assert.Equal("Artist - Title", state!.ManualInput);
    }

    [Fact]
    public async Task ChooseOutputFolder_ReturnsFolderFromService()
    {
        var folders = new HostFakeFolderDialogService();

        await using var app = await StartTestAppAsync(services =>
        {
            services.RemoveAll<IFolderDialogService>();
            services.AddSingleton<IFolderDialogService>(folders);
        });

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync("/api/choose-output-folder", new { });
        var payload = await response.Content.ReadFromJsonAsync<ChooseFolderResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(@"C:\Temp\ChosenFolder", payload!.Folder);
    }

    [Fact]
    public async Task Root_ReturnsBackendMessage_WhenNoBundledFrontendExists()
    {
        await using var app = await StartTestAppAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("TideReader backend is running.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_Response_IncludesCorsHeaders_ForAllowedOrigin()
    {
        await using var app = await StartCorsTestAppAsync();
        var client = app.GetTestClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add("Origin", "http://localhost:5173");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("http://localhost:5173", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task FolderOpenPaths_ComeFromInitializedBridgeState()
    {
        var folders = new HostFakeFolderDialogService();
        var logDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "logs");
        using var logger = new AppLogger(logDir);
        var bridge = new BridgeService(
            new HostFakeSettingsStore(),
            logger,
            new HostFakeOutputWriter(),
            new HostFakePlaybackDetector(null),
            new HostFakeWindowTitleDetector(),
            new HostFakeManualDetector(),
            new HostFakeMetadataEnricher(),
            new HostFakeOverlayCoordinator(),
            new OverlaySettingsSnapshotStore(),
            new PlaybackSnapshotStore(),
            new HostFakeAppUpdateChecker());
        await bridge.InitializeAsync(CancellationToken.None);
        var state = bridge.GetState();

        await folders.OpenFolderAsync(state.OutputFolder);
        await folders.OpenFolderAsync(Path.GetDirectoryName(state.LogPath)!);

        Assert.Contains(@"C:\Temp\TideReaderTests", folders.OpenedPaths);
        Assert.Contains(folders.OpenedPaths, path => path.EndsWith(@"\logs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SystemFonts_ReturnsConfiguredFontFamilies()
    {
        await using var app = await StartTestAppAsync(services =>
        {
            services.RemoveAll<ISystemFontCatalog>();
            services.AddSingleton<ISystemFontCatalog>(new HostFakeSystemFontCatalog());
        });

        var client = app.GetTestClient();
        var payload = await client.GetFromJsonAsync<SystemFontsResponse>("/api/system-fonts");

        Assert.NotNull(payload);
        Assert.Equal(["Segoe UI", "Arial", "Tahoma"], payload!.Fonts);
    }

    [Fact]
    public async Task Root_ServesBundledFrontend_WhenIndexExists()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(webRoot);
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<html><body>frontend ok</body></html>");

        try
        {
            await using var app = await StartTestAppAsync(webRootPath: webRoot);
            var client = app.GetTestClient();

            var response = await client.GetAsync("/");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("frontend ok", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            Directory.Delete(webRoot, true);
        }
    }

    [Fact]
    public async Task PollingWorker_StartAsync_ReturnsPromptly_WhenFirstDetectionBlocksSynchronously()
    {
        using var logger = new AppLogger(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "logs"));
        var outputFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "output");
        var bridge = new BridgeService(
            new HostFakeSettingsStore(outputFolder),
            logger,
            new HostFakeOutputWriter(),
            new BlockingPlaybackDetector(),
            new HostFakeWindowTitleDetector(),
            new HostFakeManualDetector(),
            new HostFakeMetadataEnricher(),
            new HostFakeOverlayCoordinator(),
            new OverlaySettingsSnapshotStore(),
            new PlaybackSnapshotStore(),
            new HostFakeAppUpdateChecker());
        var worker = new PollingWorker(bridge);

        await worker.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));

        try
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await worker.StopAsync(shutdown.Token);
        }
        finally
        {
            if (Directory.Exists(outputFolder))
            {
                Directory.Delete(outputFolder, true);
            }
        }
    }

    private static async Task<TestBackendApp> StartTestAppAsync(Action<IServiceCollection>? configure = null, string? webRootPath = null, string? localApiToken = null)
    {
        var app = BackendHost.Build(options: new BackendHostOptions
        {
            UseTestServer = true,
            LocalApiToken = localApiToken,
            WebRootPath = webRootPath,
            ConfigureTestServices = services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<ISettingsStore>();
                services.RemoveAll<IOutputWriter>();
                services.RemoveAll<IPlaybackDetector>();
                services.RemoveAll<IWindowTitleDetector>();
                services.RemoveAll<IManualDetector>();
                services.RemoveAll<IMetadataEnricher>();
                services.RemoveAll<IOverlayCoordinator>();
                services.RemoveAll<IAppUpdateChecker>();
                services.RemoveAll<IExternalUrlLauncher>();
                services.RemoveAll<ISystemFontCatalog>();
                services.RemoveAll<IPlaybackSnapshotStore>();
                services.RemoveAll<SettingsStore>();
                services.RemoveAll<OutputWriter>();
                services.RemoveAll<MediaSessionDetector>();
                services.RemoveAll<WindowTitleDetector>();
                services.RemoveAll<ManualDetector>();
                services.RemoveAll<OverlayServer>();
                services.RemoveAll<PlaybackSnapshotStore>();

                services.AddSingleton<ISettingsStore>(new HostFakeSettingsStore());
                services.AddSingleton<IOutputWriter>(new HostFakeOutputWriter());
                services.AddSingleton<IPlaybackDetector>(new HostFakePlaybackDetector(null));
                services.AddSingleton<IWindowTitleDetector>(new HostFakeWindowTitleDetector());
                services.AddSingleton<IManualDetector>(new HostFakeManualDetector());
                services.AddSingleton<IMetadataEnricher>(new HostFakeMetadataEnricher());
                services.AddSingleton<IOverlayCoordinator>(new HostFakeOverlayCoordinator());
                services.AddSingleton<IAppUpdateChecker>(new HostFakeAppUpdateChecker());
                services.AddSingleton<IExternalUrlLauncher>(new HostFakeExternalUrlLauncher());
                services.AddSingleton<ISystemFontCatalog>(new HostFakeSystemFontCatalog());
                services.AddSingleton<IPlaybackSnapshotStore>(new PlaybackSnapshotStore());
                services.AddSingleton<FolderDialogService>();
                services.AddSingleton<IFolderDialogService>(new HostFakeFolderDialogService());
                configure?.Invoke(services);
            }
        });

        await app.StartAsync();
        return new TestBackendApp(app);
    }

    private static async Task<TestBackendApp> StartCorsTestAppAsync()
    {
        var app = BackendHost.Build(options: new BackendHostOptions
        {
            UseTestServer = true,
            AllowedOrigins = ["http://localhost:5173"],
            ConfigureTestServices = services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<ISettingsStore>();
                services.RemoveAll<IOutputWriter>();
                services.RemoveAll<IPlaybackDetector>();
                services.RemoveAll<IWindowTitleDetector>();
                services.RemoveAll<IManualDetector>();
                services.RemoveAll<IMetadataEnricher>();
                services.RemoveAll<IOverlayCoordinator>();
                services.RemoveAll<IAppUpdateChecker>();
                services.RemoveAll<IExternalUrlLauncher>();
                services.RemoveAll<ISystemFontCatalog>();
                services.RemoveAll<IPlaybackSnapshotStore>();
                services.RemoveAll<SettingsStore>();
                services.RemoveAll<OutputWriter>();
                services.RemoveAll<MediaSessionDetector>();
                services.RemoveAll<WindowTitleDetector>();
                services.RemoveAll<ManualDetector>();
                services.RemoveAll<OverlayServer>();
                services.RemoveAll<PlaybackSnapshotStore>();

                services.AddSingleton<ISettingsStore>(new HostFakeSettingsStore());
                services.AddSingleton<IOutputWriter>(new HostFakeOutputWriter());
                services.AddSingleton<IPlaybackDetector>(new HostFakePlaybackDetector(null));
                services.AddSingleton<IWindowTitleDetector>(new HostFakeWindowTitleDetector());
                services.AddSingleton<IManualDetector>(new HostFakeManualDetector());
                services.AddSingleton<IMetadataEnricher>(new HostFakeMetadataEnricher());
                services.AddSingleton<IOverlayCoordinator>(new HostFakeOverlayCoordinator());
                services.AddSingleton<IAppUpdateChecker>(new HostFakeAppUpdateChecker());
                services.AddSingleton<IExternalUrlLauncher>(new HostFakeExternalUrlLauncher());
                services.AddSingleton<ISystemFontCatalog>(new HostFakeSystemFontCatalog());
                services.AddSingleton<IPlaybackSnapshotStore>(new PlaybackSnapshotStore());
                services.AddSingleton<FolderDialogService>();
                services.AddSingleton<IFolderDialogService>(new HostFakeFolderDialogService());
            }
        });

        await app.StartAsync();
        return new TestBackendApp(app);
    }

    private sealed class TestBackendApp(WebApplication app) : IAsyncDisposable
    {
        public IServiceProvider Services => app.Services;

        public HttpClient GetTestClient() => app.GetTestClient();

        public async ValueTask DisposeAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await app.StopAsync(timeout.Token);
            await app.DisposeAsync();
        }
    }

    private sealed class BlockingPlaybackDetector : IPlaybackDetector
    {
        public Task<PlaybackDetectionOutcome> DetectAsync(DetectionResult previous, Settings settings, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Thread.Sleep(25);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new PlaybackDetectionOutcome(null, new BrowserDebugState()));
        }
    }

    private sealed class HostFakeSettingsStore(string? outputFolder = null) : ISettingsStore
    {
        public Task<Settings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new Settings
        {
            OutputFolder = outputFolder ?? @"C:\Temp\TideReaderTests",
            OverlayEnabled = false,
            EnableWindowTitleFallback = false,
            EnableDebugManualInput = false,
            MetadataProviderMode = nameof(MetadataProviderMode.Off)
        });

        public Task SaveAsync(Settings settings, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class HostFakeOutputWriter : IOutputWriter
    {
        public Task WriteAsync(string outputFolder, DetectionResult state, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class HostFakePlaybackDetector(DetectionResult? result) : IPlaybackDetector
    {
        public Task<PlaybackDetectionOutcome> DetectAsync(DetectionResult previous, Settings settings, CancellationToken cancellationToken) =>
            Task.FromResult(new PlaybackDetectionOutcome(result is null ? null : BridgeStatePolicy.CloneDetection(result), new BrowserDebugState()));
    }

    private sealed class HostFakeWindowTitleDetector : IWindowTitleDetector
    {
        public DetectionResult? Detect() => null;
    }

    private sealed class HostFakeManualDetector : IManualDetector
    {
        public DetectionResult? Detect(string input) => null;
    }

    private sealed class HostFakeMetadataEnricher : IMetadataEnricher
    {
        public DetectionResult ApplyCached(DetectionResult input) => input;
        public bool NeedsEnrichment(DetectionResult input, MetadataProviderMode mode) => false;
        public Task<DetectionResult> EnrichAsync(DetectionResult input, MetadataProviderMode mode, CancellationToken cancellationToken) => Task.FromResult(input);
        public Task<DetectionResult> EnrichArtworkAsync(DetectionResult input, MetadataProviderMode mode, CancellationToken cancellationToken) => Task.FromResult(input);
    }

    private sealed class HostFakeOverlayCoordinator : IOverlayCoordinator
    {
        public string Url => "";
        public Task ConfigureAsync(bool enabled, int port, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class HostFakeFolderDialogService : IFolderDialogService
    {
        public List<string> OpenedPaths { get; } = [];
        public Task<string?> ChooseOutputFolderAsync() => Task.FromResult<string?>(@"C:\Temp\ChosenFolder");
        public Task OpenFolderAsync(string path)
        {
            OpenedPaths.Add(path);
            return Task.CompletedTask;
        }
    }

    private sealed class HostFakeSystemFontCatalog : ISystemFontCatalog
    {
        public IReadOnlyList<string> GetFontFamilies() => ["Segoe UI", "Arial", "Tahoma"];
    }

    private sealed class HostFakeAppUpdateChecker : IAppUpdateChecker
    {
        public string CurrentVersion => "0.5.0";
        public string ReleaseUrl => "https://github.com/LastUrsa/TideReader/releases";

        public Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken) => Task.FromResult(new UpdateInfo
        {
            CurrentVersion = CurrentVersion,
            LatestVersion = "0.1.1",
            UpdateAvailable = true,
            ReleaseUrl = ReleaseUrl,
            Message = "Version 0.1.1 is available."
        });
    }

    private sealed class HostFakeExternalUrlLauncher : IExternalUrlLauncher
    {
        public string OpenedUrl { get; private set; } = "";

        public void OpenUrl(string url)
        {
            OpenedUrl = url;
        }
    }

    private sealed class ChooseFolderResponse
    {
        public string Folder { get; set; } = "";
    }

    private sealed class SystemFontsResponse
    {
        public string[] Fonts { get; set; } = [];
    }
}
