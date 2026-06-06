using System.Windows;
using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.Text.Json;
using System.IO;

namespace TideReader.Desktop;

public partial class MainWindow : Window
{
    private const double CompactWidth = 580;
    private const double CompactHeight = 250;
    private const double CompactMinWidth = 470;
    private const double CompactMinHeight = 220;
    private const double SettingsWidth = 1024;
    private const double SettingsHeight = 736;
    private const double SettingsMinWidth = 900;
    private const double SettingsMinHeight = 680;

    private readonly HashSet<string> _allowedOrigins = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions WebMessageJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private bool _browserConfigured;

    public MainWindow()
    {
        InitializeComponent();
    }

    public async Task NavigateAsync(BrowserTarget target)
    {
        var environment = await CreateBrowserEnvironmentAsync();
        await Browser.EnsureCoreWebView2Async(environment);
        ConfigureBrowser();

        _allowedOrigins.Clear();
        foreach (var origin in target.AllowedOrigins)
        {
            if (!string.IsNullOrWhiteSpace(origin))
            {
                _allowedOrigins.Add(origin);
            }
        }

        if (!string.IsNullOrWhiteSpace(target.Url))
        {
            Browser.Source = new Uri(target.Url, UriKind.Absolute);
            return;
        }

        Browser.NavigateToString(target.Html ?? "<html><body></body></html>");
    }

    private static Task<CoreWebView2Environment> CreateBrowserEnvironmentAsync()
    {
        var userDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TideReader",
            "WebView2");

        Directory.CreateDirectory(userDataDir);
        return CoreWebView2Environment.CreateAsync(userDataFolder: userDataDir);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (!string.IsNullOrWhiteSpace(e.Uri))
        {
            if (IsAllowedWebViewUrl(e.Uri))
            {
                Browser.CoreWebView2.Navigate(e.Uri);
            }
            else
            {
                OpenExternal(e.Uri);
            }
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (IsAllowedWebViewUrl(e.Uri))
        {
            return;
        }

        e.Cancel = true;
        if (!string.IsNullOrWhiteSpace(e.Uri))
        {
            OpenExternal(e.Uri);
        }
    }

    private void ConfigureBrowser()
    {
        if (_browserConfigured)
        {
            return;
        }

        Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
        Browser.CoreWebView2.Settings.IsZoomControlEnabled = false;
        Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
        Browser.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
        Browser.CoreWebView2.NavigationStarting += OnNavigationStarting;
        Browser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        _browserConfigured = true;
    }

    private bool IsAllowedWebViewUrl(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri) || string.Equals(uri, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        return _allowedOrigins.Contains(parsed.GetLeftPart(UriPartial.Authority));
    }

    private static void OpenExternal(string uri)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = uri,
            UseShellExecute = true
        });
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = ReadLayoutMessage(e);
            if (message is null || !string.Equals(message.Type, "layout", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(message.Mode))
            {
                return;
            }

            ApplyLayoutMode(message.Mode);
        }
        catch (JsonException)
        {
        }
    }

    private static LayoutMessage? ReadLayoutMessage(CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            return JsonSerializer.Deserialize<LayoutMessage>(e.WebMessageAsJson, WebMessageJsonOptions);
        }
        catch (JsonException)
        {
        }

        var payload = e.TryGetWebMessageAsString();
        return string.IsNullOrWhiteSpace(payload)
            ? null
            : JsonSerializer.Deserialize<LayoutMessage>(payload, WebMessageJsonOptions);
    }

    private void ApplyLayoutMode(string mode)
    {
        if (string.Equals(mode, "settings", StringComparison.OrdinalIgnoreCase))
        {
            MinWidth = SettingsMinWidth;
            MinHeight = SettingsMinHeight;
            Width = SettingsWidth;
            Height = SettingsHeight;
            return;
        }

        MinWidth = CompactMinWidth;
        MinHeight = CompactMinHeight;
        Width = CompactWidth;
        Height = CompactHeight;
    }

    private sealed class LayoutMessage
    {
        public string Type { get; set; } = "";
        public string Mode { get; set; } = "";
    }
}
