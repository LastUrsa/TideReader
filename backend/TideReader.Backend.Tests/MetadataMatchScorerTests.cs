using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class MetadataMatchScorerTests
{
    [Fact]
    public void Canonicalize_StripsBracketedQualifiers()
    {
        var canonical = MetadataMatchScorer.Canonicalize("""Sky Harbor (From "Demo Collection")""");

        Assert.Equal("sky harbor", canonical);
    }

    [Fact]
    public void ScoreTitle_PrefersCanonicalMatchOverLooseContainment()
    {
        var canonicalScore = MetadataMatchScorer.ScoreTitle("""Sky Harbor""", """Sky Harbor (From "Demo Collection")""");
        var looseScore = MetadataMatchScorer.ScoreTitle("Harbor Medley", """Sky Harbor (From "Demo Collection")""");

        Assert.True(canonicalScore > looseScore);
    }

    [Fact]
    public void ScoreArtist_PrefersExactArtistMatch()
    {
        var exactScore = MetadataMatchScorer.ScoreArtist("North Harbor Ensemble", "North Harbor Ensemble");
        var partialScore = MetadataMatchScorer.ScoreArtist("North Harbor", "North Harbor Ensemble");

        Assert.True(exactScore > partialScore);
    }

    [Theory]
    [InlineData(180000, 180000, 0.08)]
    [InlineData(180000, 182500, 0.05)]
    [InlineData(180000, 188000, 0.02)]
    [InlineData(180000, 195000, 0.0)]
    [InlineData(0, 195000, 0.0)]
    public void ScoreDuration_UsesExpectedThresholds(long candidateDurationMs, long inputDurationMs, double expected)
    {
        var score = MetadataMatchScorer.ScoreDuration(candidateDurationMs, inputDurationMs);

        Assert.Equal(expected, score);
    }

    [Fact]
    public void ScoreTitle_UsesTokenOverlap_WhenCanonicalFormsShareMostTokens()
    {
        var score = MetadataMatchScorer.ScoreTitle("The Long Return Home", "Long Return Home Live");

        Assert.True(score > 0);
        Assert.True(score < 0.28);
    }

    [Fact]
    public void ScoreArtist_UsesTokenOverlap_ForRelatedNames()
    {
        var score = MetadataMatchScorer.ScoreArtist("North Harbor String Quartet", "North Harbor Quartet");

        Assert.True(score > 0);
        Assert.True(score < 0.18);
    }

    [Fact]
    public void Normalize_CollapsesWhitespace_AndLowercases()
    {
        var normalized = MetadataMatchScorer.Normalize("  North   Harbor   Ensemble  ");

        Assert.Equal("north harbor ensemble", normalized);
    }
}
