namespace TideReader.Backend.Services;

public sealed record ServiceStartupReadiness(bool BackendReady, int? SipPort)
{
    public bool Ready => BackendReady && SipPort is not null;
}

public sealed class ServiceStartupVerifier(HttpClient httpClient, Action<string>? log = null)
{
    private const int FirstSipPort = 47030;
    private const int LastSipPort = 47039;

    public async Task<ServiceStartupReadiness> WaitForReadyAsync(
        Uri backendHealthUrl,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var backendReady = false;
        int? sipPort = null;

        while (!timeoutCts.IsCancellationRequested)
        {
            backendReady = backendReady || await IsHealthyAsync(backendHealthUrl, timeoutCts.Token);
            sipPort ??= await FindSipPortAsync(timeoutCts.Token);

            if (backendReady && sipPort is not null)
            {
                log?.Invoke($"service readiness confirmed: backend={backendHealthUrl} sipPort={sipPort}");
                return new ServiceStartupReadiness(true, sipPort);
            }

            try
            {
                await Task.Delay(pollInterval, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        log?.Invoke($"service readiness timed out: backendReady={backendReady} sipPort={(sipPort?.ToString() ?? "none")}");
        return new ServiceStartupReadiness(backendReady, sipPort);
    }

    private async Task<bool> IsHealthyAsync(Uri url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(url, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<int?> FindSipPortAsync(CancellationToken cancellationToken)
    {
        for (var port = FirstSipPort; port <= LastSipPort; port++)
        {
            var uri = new Uri($"http://127.0.0.1:{port}/api/v1/app");
            if (await IsHealthyAsync(uri, cancellationToken))
            {
                return port;
            }
        }

        return null;
    }
}
