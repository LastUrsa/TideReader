using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class ManualDetectorTests
{
    [Fact]
    public void Detect_ReturnsNull_ForBlankInput()
    {
        var detector = new ManualDetector();

        var result = detector.Detect("   ");

        Assert.Null(result);
    }

    [Fact]
    public void Detect_Parses_Artist_Title_And_Album()
    {
        var detector = new ManualDetector();

        var result = detector.Detect("Artist Name - Song Title | Album Name");

        Assert.NotNull(result);
        Assert.Equal("Artist Name", result!.Artist);
        Assert.Equal("Song Title", result.Title);
        Assert.Equal("Album Name", result.Album);
        Assert.Equal("manual", result.Method);
    }

    [Fact]
    public void Detect_Uses_Title_When_No_Artist_Separator_Present()
    {
        var detector = new ManualDetector();

        var result = detector.Detect("Standalone Title");

        Assert.NotNull(result);
        Assert.Equal("", result!.Artist);
        Assert.Equal("Standalone Title", result.Title);
    }
}
