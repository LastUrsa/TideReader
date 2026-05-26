using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class MetadataCandidateSelectorTests
{
    [Fact]
    public void SelectBest_PrefersArtwork_WhenScoresAreEffectivelyTied()
    {
        var left = new MetadataCandidateOption("itunes_search", "Album A", 100, 0.95, "", false, "a");
        var right = new MetadataCandidateOption("musicbrainz", "Album B", 100, 0.951, "http://art", true, "b");

        var best = MetadataCandidateSelector.SelectBest([left, right]);

        Assert.Equal(right, best);
    }

    [Fact]
    public void SelectBest_PrefersHigherConfidence_WhenGapIsMeaningful()
    {
        var left = new MetadataCandidateOption("itunes_search", "Album A", 100, 0.97, "", false, "a");
        var right = new MetadataCandidateOption("musicbrainz", "Album B", 100, 0.95, "http://art", true, "b");

        var best = MetadataCandidateSelector.SelectBest([left, right]);

        Assert.Equal(left, best);
    }

    [Fact]
    public void DescribeTop_FormatsTopCandidates()
    {
        var text = MetadataCandidateSelector.DescribeTop([
            new MetadataCandidateOption("itunes_search", "Album A", 100, 0.91, "", false, "a"),
            new MetadataCandidateOption("musicbrainz", "Album B", 100, 0.95, "http://art", true, "b")
        ]);

        Assert.Contains("musicbrainz:0.95:art=y", text);
        Assert.Contains("itunes_search:0.91:art=n", text);
    }
}
