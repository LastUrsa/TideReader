using System.Diagnostics;

namespace TideReader.Backend.Services;

public sealed class ExplorerFolderLauncher : IFolderLauncher
{
    public void OpenFolder(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
    }
}
