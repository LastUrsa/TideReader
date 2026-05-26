using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class WindowTitleDetectorTests
{
    [Fact]
    public void ParseWindowTitle_UsesTitleThenArtistOrder()
    {
        var (artist, title, album) = WindowTitleDetector.ParseWindowTitle("Song Title - Artist Name - TIDAL");

        Assert.Equal("Artist Name", artist);
        Assert.Equal("Song Title", title);
        Assert.Equal("", album);
    }

    [Fact]
    public void ParseWindowTitle_HandlesHyphenatedTitles()
    {
        var (artist, title, album) = WindowTitleDetector.ParseWindowTitle("""Song of Returning ("Evening Lights" from "Sample Suite") - Dana Hart, Elias Stone""");

        Assert.Equal("Dana Hart, Elias Stone", artist);
        Assert.Equal("""Song of Returning ("Evening Lights" from "Sample Suite")""", title);
        Assert.Equal("", album);
    }

    [Fact]
    public void ParseWindowTitle_RemovesLeadingTidalPrefix_AndTrailingSuffix()
    {
        var (artist, title, album) = WindowTitleDetector.ParseWindowTitle("TIDAL - Song Title - Artist Name - TIDAL");

        Assert.Equal("Artist Name", artist);
        Assert.Equal("Song Title", title);
        Assert.Equal("", album);
    }

    [Fact]
    public void ParseWindowTitle_UsesWholeValueAsTitle_WhenNoSeparatorExists()
    {
        var (artist, title, album) = WindowTitleDetector.ParseWindowTitle("Standalone Song");

        Assert.Equal("", artist);
        Assert.Equal("Standalone Song", title);
        Assert.Equal("", album);
    }

    [Fact]
    public void ParseWindowTitle_UsesLastSegmentAsArtist_WhenMultipleSeparatorsExist()
    {
        var (artist, title, album) = WindowTitleDetector.ParseWindowTitle("Suite Part I - Live Edit - The Sample Ensemble");

        Assert.Equal("The Sample Ensemble", artist);
        Assert.Equal("Suite Part I - Live Edit", title);
        Assert.Equal("", album);
    }
}
