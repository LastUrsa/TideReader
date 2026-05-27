using System.Diagnostics;

namespace TideReader.Backend.Services;

public sealed class ExternalUrlLauncher : IExternalUrlLauncher
{
    public void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("URL must be an absolute HTTP or HTTPS URL.", nameof(url));
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true
        });
    }
}
