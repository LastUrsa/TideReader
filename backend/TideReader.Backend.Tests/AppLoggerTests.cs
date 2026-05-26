using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class AppLoggerTests
{
    [Fact]
    public void Info_Tracks_Recent_Lines()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            using var logger = new AppLogger(tempDir, maxBytes: 1024 * 1024, maxArchives: 2);

            logger.Info("first");
            logger.Info("second");

            var lines = logger.GetRecentLines();
            Assert.Equal(2, lines.Length);
            Assert.Contains("first", lines[0]);
            Assert.Contains("second", lines[1]);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Info_Rotates_Log_When_Size_Limit_Reached()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string current;
            using (var logger = new AppLogger(tempDir, maxBytes: 128, maxArchives: 2))
            {
                logger.Info(new string('a', 256));
                logger.Info("after-rotate");

                Assert.True(File.Exists(System.IO.Path.Combine(tempDir, "bridge.log")));
                Assert.True(File.Exists(System.IO.Path.Combine(tempDir, "bridge.log.1")));
            }

            current = File.ReadAllText(System.IO.Path.Combine(tempDir, "bridge.log"));
            Assert.Contains("after-rotate", current);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
