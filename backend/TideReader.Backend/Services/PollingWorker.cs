namespace TideReader.Backend.Services;

public sealed class PollingWorker(BridgeService bridgeService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        await bridgeService.InitializeAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await bridgeService.RunDetectionAsync(stoppingToken);
            await Task.Delay(bridgeService.PollIntervalMs(), stoppingToken);
        }
    }
}
