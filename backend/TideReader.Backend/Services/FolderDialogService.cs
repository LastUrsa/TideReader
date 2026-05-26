namespace TideReader.Backend.Services;

public sealed class FolderDialogService(IFolderPicker folderPicker, IFolderLauncher folderLauncher) : IFolderDialogService
{
    public Task<string?> ChooseOutputFolderAsync() => folderPicker.ChooseOutputFolderAsync();

    public Task OpenFolderAsync(string path)
    {
        var normalizedPath = OutputPathPolicy.NormalizeFolderPath(path);
        Directory.CreateDirectory(normalizedPath);
        folderLauncher.OpenFolder(normalizedPath);
        return Task.CompletedTask;
    }
}
