using TideReader.Backend.Models;
using TideReader.Backend.Services;
using Microsoft.AspNetCore.TestHost;
using System.Security.Cryptography;
using System.Text;

namespace TideReader.Backend;

public sealed class BackendHostOptions
{
    public string ApiUrl { get; init; } = "http://127.0.0.1:17656";
    public string? LocalApiToken { get; init; }
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];
    public string? WebRootPath { get; init; }
    public bool UseTestServer { get; init; }
    public bool EnableSipServer { get; init; } = true;
    public string RuntimeMode { get; init; } = SipRuntimeModes.Standalone;
    public Action<IServiceCollection>? ConfigureTestServices { get; init; }
}

public static class BackendHost
{
    private const string LocalApiTokenHeader = "X-TideReader-Token";
    private const string LocalApiTokenQueryKey = "tr_token";

    public static WebApplication Build(string[]? args = null, BackendHostOptions? options = null)
    {
        options ??= new BackendHostOptions();
        var webRootPath = ResolveWebRoot(options.WebRootPath);

        var builderOptions = new WebApplicationOptions
        {
            Args = args ?? [],
            ApplicationName = typeof(BackendHost).Assembly.FullName,
            WebRootPath = IsUncPath(webRootPath) ? null : webRootPath
        };

        var builder = WebApplication.CreateBuilder(builderOptions);
        if (options.UseTestServer)
        {
            builder.WebHost.UseTestServer();
        }
        else
        {
            builder.WebHost.UseUrls(options.ApiUrl);
        }

        ConfigureServices(builder.Services, options);

        var app = builder.Build();
        ConfigurePipeline(app, options);
        return app;
    }

    private static void ConfigureServices(IServiceCollection services, BackendHostOptions options)
    {
        var allowedOrigins = options.AllowedOrigins
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (allowedOrigins.Length > 0)
        {
            services.AddCors(cors =>
            {
                cors.AddDefaultPolicy(policy => policy
                    .WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod());
            });
        }

        services.AddSingleton<SettingsStore>();
        services.AddSingleton<ISettingsStore>(sp => sp.GetRequiredService<SettingsStore>());
        services.AddSingleton<AppLogger>();
        services.AddSingleton<OutputWriter>();
        services.AddSingleton<IOutputWriter>(sp => sp.GetRequiredService<OutputWriter>());
        services.AddSingleton<IMediaSessionSnapshotProvider, WindowsMediaSessionSnapshotProvider>();
        services.AddSingleton<IAudioSessionSnapshotProvider, WindowsAudioSessionSnapshotProvider>();
        services.AddSingleton<IPlaybackProvider, TidalPlaybackProvider>();
        services.AddSingleton<IPlaybackProvider, BrowserMediaProvider>();
        services.AddSingleton<MediaSessionDetector>();
        services.AddSingleton<IPlaybackDetector>(sp => sp.GetRequiredService<MediaSessionDetector>());
        services.AddSingleton<WindowTitleDetector>();
        services.AddSingleton<IWindowTitleDetector>(sp => sp.GetRequiredService<WindowTitleDetector>());
        services.AddSingleton<ManualDetector>();
        services.AddSingleton<IManualDetector>(sp => sp.GetRequiredService<ManualDetector>());
        services.AddSingleton<IFolderPicker, WindowsFolderPicker>();
        services.AddSingleton<IFolderLauncher, ExplorerFolderLauncher>();
        services.AddSingleton<FolderDialogService>();
        services.AddSingleton<IFolderDialogService>(sp => sp.GetRequiredService<FolderDialogService>());
        services.AddSingleton<ISystemFontCatalog, WindowsSystemFontCatalog>();
        services.AddSingleton<PlaybackSnapshotStore>();
        services.AddSingleton<IPlaybackSnapshotStore>(sp => sp.GetRequiredService<PlaybackSnapshotStore>());
        services.AddSingleton<OverlaySettingsSnapshotStore>();
        services.AddSingleton<IOverlaySettingsSnapshotStore>(sp => sp.GetRequiredService<OverlaySettingsSnapshotStore>());
        services.AddSingleton<OverlayServer>();
        services.AddSingleton<IOverlayCoordinator>(sp => sp.GetRequiredService<OverlayServer>());
        services.AddSingleton<IExternalUrlLauncher, ExternalUrlLauncher>();
        services.AddHttpClient<MetadataEnricher>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TideReader/0.1");
        });
        services.AddSingleton<IMetadataEnricher>(sp => sp.GetRequiredService<MetadataEnricher>());
        services.AddHttpClient<IAppUpdateChecker, AppUpdateChecker>();
        services.AddSingleton<BridgeService>();
        services.AddHostedService<PollingWorker>();
        services.AddSingleton(new SipHostOptions { RuntimeMode = options.RuntimeMode });
        services.AddSingleton<SipService>();
        if (options.EnableSipServer && !options.UseTestServer)
        {
            services.AddHostedService<SipServerHostedService>();
        }
        options.ConfigureTestServices?.Invoke(services);
    }

    private static void ConfigurePipeline(WebApplication app, BackendHostOptions options)
    {
        if (options.AllowedOrigins.Count > 0)
        {
            app.UseCors();
        }

        if (!string.IsNullOrWhiteSpace(options.LocalApiToken))
        {
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) &&
                    !context.Request.Path.Equals("/api/health", StringComparison.OrdinalIgnoreCase))
                {
                    var providedToken = context.Request.Headers[LocalApiTokenHeader].ToString();
                    if (string.IsNullOrWhiteSpace(providedToken))
                    {
                        providedToken = context.Request.Query[LocalApiTokenQueryKey].ToString();
                    }

                    if (!TokensMatch(providedToken, options.LocalApiToken))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
                        return;
                    }
                }

                await next(context);
            });
        }

        if (HasBundledFrontend(app.Environment.WebRootPath))
        {
            app.UseDefaultFiles();
            app.UseStaticFiles();
        }

        app.MapGet("/api/state", (BridgeService bridgeService) => Results.Ok(bridgeService.GetState()));
        app.MapPost("/api/settings", async (Settings settings, BridgeService bridgeService, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await bridgeService.SaveSettingsAsync(settings, cancellationToken));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/manual-input", (ManualInputRequest request, BridgeService bridgeService) =>
            Results.Ok(bridgeService.SetManualInput(request.Input)));
        app.MapPost("/api/run-detection", async (BridgeService bridgeService, CancellationToken cancellationToken) =>
            Results.Ok(await bridgeService.RunDetectionAsync(cancellationToken)));
        app.MapGet("/api/artwork", (HttpContext context, BridgeService bridgeService) =>
        {
            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.Expires = "0";
            var artwork = bridgeService.GetArtwork();
            return artwork.Length == 0
                ? Results.NotFound()
                : Results.File(artwork, "image/jpeg");
        });
        app.MapPost("/api/choose-output-folder", async (IFolderDialogService folders) =>
        {
            var folder = await folders.ChooseOutputFolderAsync();
            return Results.Ok(new { folder });
        });
        app.MapPost("/api/open-output-folder", async (BridgeService bridgeService, IFolderDialogService folders) =>
        {
            try
            {
                await folders.OpenFolderAsync(bridgeService.GetState().OutputFolder);
                return Results.Ok(new { ok = true });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/open-logs-folder", async (BridgeService bridgeService, IFolderDialogService folders) =>
        {
            var logDirectory = System.IO.Path.GetDirectoryName(bridgeService.GetState().LogPath)
                ?? throw new InvalidOperationException("Log folder is unavailable.");
            try
            {
                await folders.OpenFolderAsync(logDirectory);
                return Results.Ok(new { ok = true });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapGet("/api/system-fonts", (ISystemFontCatalog fonts) => Results.Ok(new { fonts = fonts.GetFontFamilies() }));
        app.MapGet("/api/check-for-updates", async (IAppUpdateChecker checker, CancellationToken cancellationToken) =>
            Results.Ok(await checker.CheckForUpdatesAsync(cancellationToken)));
        app.MapPost("/api/open-releases-page", (IAppUpdateChecker checker, IExternalUrlLauncher urlLauncher) =>
        {
            urlLauncher.OpenUrl(checker.ReleaseUrl);
            return Results.Ok(new { ok = true });
        });
        app.MapGet("/api/health", () => Results.Ok(new { ok = true }));

        if (HasBundledFrontend(app.Environment.WebRootPath))
        {
            app.MapFallbackToFile("index.html");
        }
        else
        {
            app.MapGet("/", () => Results.Text("TideReader backend is running."));
        }
    }

    private static string? ResolveWebRoot(string? webRootPath)
    {
        if (string.IsNullOrWhiteSpace(webRootPath))
        {
            return null;
        }

        return Path.GetFullPath(webRootPath);
    }

    private static bool HasBundledFrontend(string? webRootPath)
    {
        if (string.IsNullOrWhiteSpace(webRootPath))
        {
            return false;
        }

        return File.Exists(Path.Combine(webRootPath, "index.html"));
    }

    private static bool IsUncPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.StartsWith(@"\\", StringComparison.Ordinal);

    private static bool TokensMatch(string providedToken, string expectedToken)
    {
        if (string.IsNullOrWhiteSpace(providedToken) || providedToken.Length != expectedToken.Length)
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(providedToken);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
