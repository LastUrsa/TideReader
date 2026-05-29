using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class OverlayStaticFileWriterTests
{
    [Fact]
    public void Write_CreatesStandaloneOverlayHtmlInOutputFolder()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TideReader.Tests", Guid.NewGuid().ToString("N"));

        try
        {
            OverlayStaticFileWriter.Write(tempDir, 17655);

            var overlayPath = Path.Combine(tempDir, "overlay.html");
            Assert.True(File.Exists(overlayPath));

            var html = File.ReadAllText(overlayPath);
            Assert.Contains("http://127.0.0.1:17655/nowplaying.json", html);
            Assert.Contains("http://127.0.0.1:17655/overlay-settings.json", html);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
