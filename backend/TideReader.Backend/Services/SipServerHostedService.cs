using System.Diagnostics.CodeAnalysis;

namespace TideReader.Backend.Services;

[ExcludeFromCodeCoverage]
public sealed class SipServerHostedService(BridgeService bridgeService, IAppUpdateChecker appUpdateChecker, SipHostOptions options, AppLogger logger) : IHostedService
{
    private const int FirstPort = 47030;
    private const int LastPort = 47039;

    private WebApplication? _app;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.Info($"sip bind scan starting: ports {FirstPort}-{LastPort}");
        for (var port = FirstPort; port <= LastPort; port++)
        {
            logger.Info($"sip app build starting: {port}");
            var app = BuildSipApp(port);
            try
            {
                logger.Info($"sip bind attempt: {port}");
                await app.StartAsync(cancellationToken);
                _app = app;
                var address = app.Urls.FirstOrDefault() ?? $"http://127.0.0.1:{port}";
                logger.Info($"sip ready: {address}");
                return;
            }
            catch (IOException ex)
            {
                logger.Info($"sip port unavailable: {port} {ex.Message}");
                await app.DisposeAsync();
            }
            catch (Exception ex)
            {
                logger.Info($"sip bind failed: {port} {ex}");
                await app.DisposeAsync();
                throw;
            }
        }

        logger.Info("sip bind failed: no ports available");
        throw new InvalidOperationException("No TideReader SIP port is available.");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_app is null)
        {
            return;
        }

        try
        {
            await _app.StopAsync(cancellationToken);
        }
        finally
        {
            await _app.DisposeAsync();
            _app = null;
        }
    }

    private WebApplication BuildSipApp(int port)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(SipServerHostedService).Assembly.FullName,
            ContentRootPath = ResolveContentRoot(AppContext.BaseDirectory)
        });
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = SipHttpApi.MaxRequestBodyBytes;
        });
        builder.Services.AddSingleton(bridgeService);
        builder.Services.AddSingleton(appUpdateChecker);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<SipService>();

        var app = builder.Build();
        SipHttpApi.Configure(app);
        return app;
    }

    private static string ResolveContentRoot(string baseDirectory)
    {
        var contentRoot = Path.GetFullPath(baseDirectory);
        if (!contentRoot.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return contentRoot;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var fallback = string.IsNullOrWhiteSpace(appData)
            ? Path.GetTempPath()
            : appData;
        contentRoot = Path.Combine(fallback, "TideReader", "content-root");
        Directory.CreateDirectory(contentRoot);
        return contentRoot;
    }
}
