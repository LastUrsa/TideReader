using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using TideReader.Backend;
using TideReader.Backend.Models;
using TideReader.Backend.Services;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using NotifyIcon = System.Windows.Forms.NotifyIcon;

namespace TideReader.Desktop;

public partial class App : Application
{
    private const string ApiUrl = "http://127.0.0.1:17656";
    private static readonly bool KeepWindowVisible = string.Equals(
        Environment.GetEnvironmentVariable("TIDEREADER_KEEP_WINDOW_VISIBLE"),
        "1",
        StringComparison.Ordinal);
    private static readonly string LocalApiToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly IStartupRegistration _startupRegistration = new StartupRegistrationService(new RegistryRunKeyFactory());

    private NotifyIcon? _notifyIcon;
    private WebApplication? _backendApp;
    private BridgeService? _bridgeService;
    private MainWindow? _window;
    private Mutex? _singleInstanceMutex;
    private Task? _showSignalTask;
    private LaunchConfig _launchConfig = LaunchConfig.Standalone;
    private bool _explicitExit;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _launchConfig = LaunchConfig.Parse(e.Args);
            if (!AcquireSingleInstance())
            {
                if (_launchConfig.ShouldActivateExisting)
                {
                    await SendShowSignalAsync();
                }

                Shutdown();
                return;
            }

            _showSignalTask = ListenForShowSignalsAsync(_shutdownCts.Token);

            var initialSettings = await LoadInitialSettingsAsync(_shutdownCts.Token);
            _backendApp = await Task.Run(() => BackendHost.Build([], CreateBackendOptions(_launchConfig)), _shutdownCts.Token);
            await _backendApp.StartAsync(_shutdownCts.Token);

            _bridgeService = _backendApp.Services.GetRequiredService<BridgeService>();
            _bridgeService.SettingsChanged += OnSettingsChanged;
            _startupRegistration.Sync(initialSettings.LaunchAtStartup);

            if (!_launchConfig.ServiceMode)
            {
                await EnsureMainWindowAsync(initialSettings.StartMinimized);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to start TideReader.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            await ExitApplicationAsync();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await ShutdownBackendAsync();
        _notifyIcon?.Dispose();
        _singleInstanceMutex?.Dispose();
        _shutdownCts.Dispose();
        base.OnExit(e);
    }

    private static BackendHostOptions CreateBackendOptions(LaunchConfig launchConfig)
    {
        var devServerUrl = ResolveDevServerUrl();
        string[] allowedOrigins = string.IsNullOrWhiteSpace(devServerUrl)
            ? []
            : [new Uri(devServerUrl, UriKind.Absolute).GetLeftPart(UriPartial.Authority)];

        return new BackendHostOptions
        {
            ApiUrl = ApiUrl,
            LocalApiToken = LocalApiToken,
            AllowedOrigins = allowedOrigins,
            WebRootPath = ResolveFrontendDistPath(),
            RuntimeMode = launchConfig.ServiceMode ? SipRuntimeModes.Service : SipRuntimeModes.Standalone
        };
    }

    private static async Task<Settings> LoadInitialSettingsAsync(CancellationToken cancellationToken)
    {
        var store = new SettingsStore();
        return await store.LoadAsync(cancellationToken);
    }

    private static BrowserTarget CreateBrowserTarget()
    {
        var devServerUrl = ResolveDevServerUrl();
        if (!string.IsNullOrWhiteSpace(devServerUrl))
        {
            return BrowserTarget.ForUrl(AppendLocalApiToken(devServerUrl), [new Uri(devServerUrl, UriKind.Absolute).GetLeftPart(UriPartial.Authority)]);
        }

        if (File.Exists(Path.Combine(ResolveFrontendDistPath(), "index.html")))
        {
            return BrowserTarget.ForUrl(AppendLocalApiToken($"{ApiUrl}/"), [ApiUrl]);
        }

        return BrowserTarget.ForHtml(
            """
            <!doctype html>
            <html lang="en">
              <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>TideReader</title>
                <style>
                  body {
                    margin: 0;
                    padding: 32px;
                    font-family: "Segoe UI", sans-serif;
                    background: #09121a;
                    color: #f7fbff;
                  }
                  .card {
                    max-width: 720px;
                    padding: 24px;
                    border-radius: 16px;
                    background: linear-gradient(180deg, rgba(18, 31, 43, .96), rgba(10, 18, 27, .96));
                    box-shadow: 0 24px 64px rgba(0, 0, 0, .32);
                  }
                  h1 { margin-top: 0; }
                  code {
                    display: block;
                    margin: 12px 0;
                    padding: 12px;
                    border-radius: 10px;
                    background: rgba(255, 255, 255, .06);
                    white-space: pre-wrap;
                  }
                </style>
              </head>
              <body>
                <div class="card">
                  <h1>Frontend build not found</h1>
                  <p>The desktop host started the backend, but no production frontend bundle was found.</p>
                  <p>Build the React app before launching the desktop host in production:</p>
                  <code>cd frontend
                    npm install
                    npm run build</code>
                  <p>For development, set <strong>TIDAL_DESKTOP_DEV_SERVER_URL</strong> to your Vite URL before launching the desktop app.</p>
                </div>
              </body>
            </html>
            """);
    }

    private static string ResolveFrontendDistPath() => Path.Combine(AppContext.BaseDirectory, "frontend-dist");

    private static string AppendLocalApiToken(string url)
    {
        var builder = new UriBuilder(url);
        var query = System.Web.HttpUtility.ParseQueryString(builder.Query);
        query["tr_token"] = LocalApiToken;
        builder.Query = query.ToString() ?? "";
        return builder.Uri.ToString();
    }

    private static string? ResolveDevServerUrl()
    {
        var value = Environment.GetEnvironmentVariable("TIDAL_DESKTOP_DEV_SERVER_URL");
        return string.IsNullOrWhiteSpace(value) ? null : value.TrimEnd('/');
    }

    private async Task EnsureMainWindowAsync(bool startMinimized = false)
    {
        var created = false;
        if (_window is null)
        {
            _window = new MainWindow();
            _window.StateChanged += OnWindowStateChanged;
            _window.Closing += OnWindowClosing;
            MainWindow = _window;
            InitializeTray();
            created = true;
        }

        _window.Show();
        if (!startMinimized)
        {
            _window.WindowState = WindowState.Normal;
            _window.Activate();
        }

        if (created)
        {
            await _window.NavigateAsync(CreateBrowserTarget());
        }

        if (startMinimized)
        {
            _window.Hide();
            return;
        }
    }

    private void InitializeTray()
    {
        if (_notifyIcon is not null)
        {
            return;
        }

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, async (_, _) => await EnsureMainWindowAsync());
        menu.Items.Add("Exit", null, async (_, _) => await ExitApplicationAsync());

        _notifyIcon = new NotifyIcon
        {
            Text = "TideReader",
            Icon = ResolveTrayIcon(),
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private static Icon ResolveTrayIcon()
    {
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            var extracted = Icon.ExtractAssociatedIcon(Environment.ProcessPath);
            if (extracted is not null)
            {
                return extracted;
            }
        }

        return SystemIcons.Application;
    }

    private void ShowMainWindow()
    {
        _ = EnsureMainWindowAsync();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (KeepWindowVisible)
        {
            return;
        }

        if (_window?.WindowState == WindowState.Minimized)
        {
            _window.Hide();
        }
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (KeepWindowVisible)
        {
            return;
        }

        if (_explicitExit)
        {
            return;
        }

        e.Cancel = true;
        _window?.Hide();
    }

    private void OnSettingsChanged(Settings settings)
    {
        Dispatcher.Invoke(() => _startupRegistration.Sync(settings.LaunchAtStartup));
    }

    private async Task ExitApplicationAsync()
    {
        _explicitExit = true;
        await ShutdownBackendAsync();
        _notifyIcon?.Dispose();
        Shutdown();
    }

    private async Task ShutdownBackendAsync()
    {
        _shutdownCts.Cancel();

        if (_bridgeService is not null)
        {
            _bridgeService.SettingsChanged -= OnSettingsChanged;
            _bridgeService = null;
        }

        if (_backendApp is null)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _backendApp.StopAsync(timeout.Token);
        }
        catch (Exception)
        {
        }
        finally
        {
            await _backendApp.DisposeAsync();
            _backendApp = null;
        }
    }

    private bool AcquireSingleInstance()
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, "Local\\TideReader", out var createdNew);
        return createdNew;
    }

    private async Task ListenForShowSignalsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream("TideReader.Show", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                var message = await reader.ReadToEndAsync(cancellationToken);
                if (string.Equals(message.Trim(), "show", StringComparison.OrdinalIgnoreCase))
                {
                    var showTask = await Dispatcher.InvokeAsync(() => EnsureMainWindowAsync());
                    await showTask;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
            }
        }
    }

    private static async Task SendShowSignalAsync()
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", "TideReader.Show", PipeDirection.Out, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(1000);
            await pipe.WriteAsync(Encoding.UTF8.GetBytes("show"));
        }
        catch (IOException)
        {
        }
        catch (TimeoutException)
        {
        }
    }
}

public sealed record BrowserTarget(string? Url, string? Html, IReadOnlyList<string> AllowedOrigins)
{
    public static BrowserTarget ForUrl(string url, IReadOnlyList<string> allowedOrigins) => new(url, null, allowedOrigins);
    public static BrowserTarget ForHtml(string html) => new(null, html, []);
}

public sealed record LaunchConfig(bool ServiceMode, bool ShouldActivateExisting)
{
    public static readonly LaunchConfig Standalone = new(ServiceMode: false, ShouldActivateExisting: true);

    public static LaunchConfig Parse(IEnumerable<string> args)
    {
        var service = false;
        var show = false;
        foreach (var arg in args)
        {
            if (string.Equals(arg, "--service", StringComparison.OrdinalIgnoreCase))
            {
                service = true;
            }
            else if (string.Equals(arg, "--show", StringComparison.OrdinalIgnoreCase))
            {
                show = true;
            }
        }

        return new LaunchConfig(
            ServiceMode: service && !show,
            ShouldActivateExisting: show || !service);
    }
}
