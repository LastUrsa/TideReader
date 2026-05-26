using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class OutputPathPolicyTests
{
    [Fact]
    public void NormalizeFolderPath_ReturnsNormalizedAbsolutePath()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "TideReader", "..", "TideReader", "obs-output");

        var normalized = OutputPathPolicy.NormalizeFolderPath(tempPath);

        Assert.True(Path.IsPathFullyQualified(normalized));
        Assert.DoesNotContain("..", normalized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\")]
    [InlineData(@"\\?\C:\Temp\Obs")]
    [InlineData(@"relative\path")]
    public void NormalizeFolderPath_RejectsUnsafePaths(string path)
    {
        Assert.Throws<ArgumentException>(() => OutputPathPolicy.NormalizeFolderPath(path));
    }

    [Fact]
    public void NormalizeFolderPath_RejectsExistingFiles()
    {
        var tempFile = Path.GetTempFileName();

        try
        {
            Assert.Throws<ArgumentException>(() => OutputPathPolicy.NormalizeFolderPath(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
