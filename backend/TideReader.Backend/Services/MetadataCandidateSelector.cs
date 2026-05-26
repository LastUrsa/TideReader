namespace TideReader.Backend.Services;

internal readonly record struct MetadataCandidateOption(
    string MetadataSource,
    string Album,
    long DurationMs,
    double Confidence,
    string ArtworkUrl,
    bool HasArtwork,
    string MatchSummary);

internal static class MetadataCandidateSelector
{
    public static MetadataCandidateOption? SelectBest(IReadOnlyList<MetadataCandidateOption> candidates)
    {
        MetadataCandidateOption? best = null;
        double bestScore = 0;
        foreach (var candidate in candidates)
        {
            if (best is not null && candidate.Confidence < bestScore)
            {
                continue;
            }

            if (best is null || IsBetterCandidate(candidate, best.Value))
            {
                best = candidate;
                bestScore = candidate.Confidence;
            }
        }

        return best;
    }

    public static bool IsBetterCandidate(MetadataCandidateOption left, MetadataCandidateOption right)
    {
        if (left.Confidence > right.Confidence + 0.005)
        {
            return true;
        }

        if (right.Confidence > left.Confidence + 0.005)
        {
            return false;
        }

        if (left.HasArtwork != right.HasArtwork)
        {
            return left.HasArtwork;
        }

        if (left.DurationMs > 0 && right.DurationMs == 0)
        {
            return true;
        }

        return string.Compare(left.MetadataSource, right.MetadataSource, StringComparison.Ordinal) < 0;
    }

    public static string DescribeTop(IReadOnlyList<MetadataCandidateOption> candidates) =>
        string.Join("; ", candidates
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenByDescending(candidate => candidate.HasArtwork)
            .Take(3)
            .Select(candidate => $"{candidate.MetadataSource}:{candidate.Confidence:F2}:art={(candidate.HasArtwork ? "y" : "n")}"));
}
