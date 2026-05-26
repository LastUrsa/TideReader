using TideReader.Backend.Models;
using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class OutputWriterTests
{
    [Fact]
    public async Task WriteAsync_CreatesExpectedFiles()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var writer = new OutputWriter();
            var state = new DetectionResult
            {
                Status = "playing",
                Title = "Song Title",
                Artist = "Artist Name",
                Album = "Album Name",
                DurationMs = 210000,
                ArtworkPath = "cover.jpg",
                ArtworkBytes = [1, 2, 3],
                Source = "TIDAL",
                Confidence = 0.95
            };

            await writer.WriteAsync(tempDir, state, CancellationToken.None);

            Assert.True(File.Exists(System.IO.Path.Combine(tempDir, "nowplaying.json")));
            Assert.True(File.Exists(System.IO.Path.Combine(tempDir, "title.txt")));
            Assert.True(File.Exists(System.IO.Path.Combine(tempDir, "artist.txt")));
            Assert.True(File.Exists(System.IO.Path.Combine(tempDir, "album.txt")));
            Assert.True(File.Exists(System.IO.Path.Combine(tempDir, "track.txt")));
            Assert.True(File.Exists(System.IO.Path.Combine(tempDir, "status.txt")));
            Assert.True(File.Exists(System.IO.Path.Combine(tempDir, "cover.jpg")));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task WriteAsync_RemovesCover_WhenArtworkClears()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var writer = new OutputWriter();
            await writer.WriteAsync(tempDir, new DetectionResult
            {
                Status = "playing",
                Title = "Song Title",
                Artist = "Artist Name",
                ArtworkPath = "cover.jpg",
                ArtworkBytes = [1, 2, 3],
                Source = "TIDAL",
                Confidence = 0.95
            }, CancellationToken.None);

            await writer.WriteAsync(tempDir, new DetectionResult
            {
                Status = "paused",
                Title = "Song Title",
                Artist = "Artist Name",
                ArtworkPath = "",
                ArtworkBytes = [],
                Source = "TIDAL",
                Confidence = 0.95
            }, CancellationToken.None);

            Assert.False(File.Exists(System.IO.Path.Combine(tempDir, "cover.jpg")));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
