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
        for (var port = FirstPort; port <= LastPort; port++)
        {
            var app = BuildSipApp(port);
            try
            {
                await app.StartAsync(cancellationToken);
                _app = app;
                var address = app.Urls.FirstOrDefault() ?? $"http://127.0.0.1:{port}";
                logger.Info($"sip started: {address}");
                return;
            }
            catch (IOException ex)
            {
                logger.Info($"sip port unavailable: {port} {ex.Message}");
                await app.DisposeAsync();
            }
        }

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
            ApplicationName = typeof(SipServerHostedService).Assembly.FullName
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
}
