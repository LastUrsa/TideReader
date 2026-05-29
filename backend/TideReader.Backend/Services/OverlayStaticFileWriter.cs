namespace TideReader.Backend.Services;

internal static class OverlayStaticFileWriter
{
    public static void Write(string outputFolder, int overlayPort)
    {
        var normalizedOutputFolder = OutputPathPolicy.NormalizeFolderPath(outputFolder);
        Directory.CreateDirectory(normalizedOutputFolder);

        var overlayPath = Path.Combine(normalizedOutputFolder, "overlay.html");
        var html = OverlayResponseBuilder.BuildStandaloneHtml(overlayPort);
        File.WriteAllText(overlayPath, html);
    }
}
