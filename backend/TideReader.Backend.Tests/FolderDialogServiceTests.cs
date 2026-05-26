using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class FolderDialogServiceTests
{
    [Fact]
    public async Task ChooseOutputFolderAsync_DelegatesToPicker()
    {
        var service = new FolderDialogService(new FakeFolderPicker(@"C:\Temp\Chosen"), new RecordingFolderLauncher());

        var result = await service.ChooseOutputFolderAsync();

        Assert.Equal(@"C:\Temp\Chosen", result);
    }

    [Fact]
    public async Task OpenFolderAsync_CreatesDirectory_AndLaunchesExplorer()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            var launcher = new RecordingFolderLauncher();
            var service = new FolderDialogService(new FakeFolderPicker(null), launcher);

            await service.OpenFolderAsync(tempDir);

            Assert.True(Directory.Exists(tempDir));
            Assert.Equal(tempDir, launcher.OpenedPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\")]
    public async Task OpenFolderAsync_RejectsBlankPaths(string path)
    {
        var service = new FolderDialogService(new FakeFolderPicker(null), new RecordingFolderLauncher());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.OpenFolderAsync(path));

        Assert.Equal("path", ex.ParamName);
    }

    private sealed class FakeFolderPicker(string? selectedPath) : IFolderPicker
    {
        public Task<string?> ChooseOutputFolderAsync() => Task.FromResult(selectedPath);
    }

    private sealed class RecordingFolderLauncher : IFolderLauncher
    {
        public string OpenedPath { get; private set; } = "";

        public void OpenFolder(string path) => OpenedPath = path;
    }
}
