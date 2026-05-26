using System.Net;
namespace TideReader.Backend.Services;

public sealed class OverlayServer : IOverlayCoordinator, IDisposable
{
    private readonly IPlaybackSnapshotStore _snapshotStore;
    private readonly IOverlaySettingsSnapshotStore _overlaySettingsSnapshotStore;
    private readonly AppLogger _logger;
    private readonly Lock _lock = new();

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private int _port;
    private bool _enabled;

    public OverlayServer(IPlaybackSnapshotStore snapshotStore, IOverlaySettingsSnapshotStore overlaySettingsSnapshotStore, AppLogger logger)
    {
        _snapshotStore = snapshotStore;
        _overlaySettingsSnapshotStore = overlaySettingsSnapshotStore;
        _logger = logger;
    }

    public string Url
    {
        get
        {
            lock (_lock)
            {
                return _enabled && _port > 0 ? $"http://127.0.0.1:{_port}/overlay" : "";
            }
        }
    }

    public async Task ConfigureAsync(bool enabled, int port, CancellationToken cancellationToken)
    {
        HttpListener? listenerToClose = null;
        Task? loopTask = null;
        CancellationTokenSource? ctsToCancel = null;

        lock (_lock)
        {
            if (_enabled == enabled && _port == port && _listener is not null)
            {
                return;
            }

            listenerToClose = _listener;
            loopTask = _loopTask;
            ctsToCancel = _cts;

            _listener = null;
            _loopTask = null;
            _cts = null;
            _enabled = false;
            _port = 0;
        }

        if (ctsToCancel is not null)
        {
            ctsToCancel.Cancel();
        }

        if (listenerToClose is not null)
        {
            listenerToClose.Close();
        }

        if (loopTask is not null)
        {
            try
            {
                await loopTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (HttpListenerException)
            {
            }
        }

        if (!enabled)
        {
            _logger.Info("overlay disabled");
            return;
        }

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var loop = Task.Run(() => HandleLoopAsync(listener, cts.Token), CancellationToken.None);

        lock (_lock)
        {
            _listener = listener;
            _cts = cts;
            _loopTask = loop;
            _enabled = true;
            _port = port;
        }

        _logger.Info($"overlay enabled on port {port}");
    }

    private async Task HandleLoopAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            if (context is null)
            {
                continue;
            }

            await HandleRequestAsync(context, cancellationToken);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        var response = OverlayResponseBuilder.Build(path, _snapshotStore, _overlaySettingsSnapshotStore);
        context.Response.StatusCode = response.StatusCode;
        context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Expires"] = "0";
        if (response.Body.Length == 0)
        {
            context.Response.Close();
            return;
        }

        context.Response.ContentType = response.ContentType;
        context.Response.ContentLength64 = response.Body.Length;
        await context.Response.OutputStream.WriteAsync(response.Body, cancellationToken);
        context.Response.Close();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _listener?.Close();
            _cts?.Dispose();
            _listener = null;
            _cts = null;
            _loopTask = null;
            _enabled = false;
            _port = 0;
        }
    }
}
