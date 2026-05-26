using System.Text;
using System.Text.RegularExpressions;

namespace TideReader.Backend.Services;

public static partial class MetadataMatchScorer
{
    public static double ScoreTitle(string candidate, string input)
    {
        var normalizedCandidate = Normalize(candidate);
        var normalizedInput = Normalize(input);
        var canonicalCandidate = Canonicalize(candidate);
        var canonicalInput = Canonicalize(input);

        if (string.IsNullOrWhiteSpace(canonicalCandidate) || string.IsNullOrWhiteSpace(canonicalInput))
        {
            return 0;
        }

        if (normalizedCandidate == normalizedInput)
        {
            return 0.42;
        }

        if (canonicalCandidate == canonicalInput)
        {
            return 0.38;
        }

        if (normalizedCandidate.Contains(normalizedInput, StringComparison.Ordinal) || normalizedInput.Contains(normalizedCandidate, StringComparison.Ordinal))
        {
            return 0.28;
        }

        if (canonicalCandidate.Contains(canonicalInput, StringComparison.Ordinal) || canonicalInput.Contains(canonicalCandidate, StringComparison.Ordinal))
        {
            return 0.22;
        }

        var overlap = TokenOverlap(canonicalCandidate, canonicalInput);
        return overlap >= 0.6 ? 0.12 + (overlap * 0.1) : 0;
    }

    public static double ScoreArtist(string candidate, string input)
    {
        var normalizedCandidate = Normalize(candidate);
        var normalizedInput = Normalize(input);
        if (string.IsNullOrWhiteSpace(normalizedCandidate) || string.IsNullOrWhiteSpace(normalizedInput))
        {
            return 0;
        }

        if (normalizedCandidate == normalizedInput)
        {
            return 0.26;
        }

        if (normalizedCandidate.Contains(normalizedInput, StringComparison.Ordinal) || normalizedInput.Contains(normalizedCandidate, StringComparison.Ordinal))
        {
            return 0.18;
        }

        var overlap = TokenOverlap(normalizedCandidate, normalizedInput);
        return overlap >= 0.5 ? 0.08 + (overlap * 0.08) : 0;
    }

    public static double ScoreDuration(long candidateDurationMs, long inputDurationMs)
    {
        if (candidateDurationMs <= 0 || inputDurationMs <= 0)
        {
            return 0;
        }

        var delta = Math.Abs(candidateDurationMs - inputDurationMs);
        if (delta <= 1500)
        {
            return 0.08;
        }

        if (delta <= 4000)
        {
            return 0.05;
        }

        if (delta <= 10000)
        {
            return 0.02;
        }

        return 0;
    }

    public static string Canonicalize(string value)
    {
        var withoutGroups = GroupPattern().Replace(value, " ");
        var builder = new StringBuilder(withoutGroups.Length);
        foreach (var ch in withoutGroups)
        {
            builder.Append(char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) ? char.ToLowerInvariant(ch) : ' ');
        }

        return Normalize(builder.ToString());
    }

    public static string Normalize(string value) =>
        string.Join(' ', value.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static double TokenOverlap(string left, string right)
    {
        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (leftTokens.Length == 0 || rightTokens.Length == 0)
        {
            return 0;
        }

        var leftSet = new HashSet<string>(leftTokens, StringComparer.Ordinal);
        var rightSet = new HashSet<string>(rightTokens, StringComparer.Ordinal);
        leftSet.IntersectWith(rightSet);
        return (double)leftSet.Count / Math.Max(leftTokens.Length, rightTokens.Length);
    }

    [GeneratedRegex(@"\([^)]*\)|\[[^\]]*\]|\{[^}]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex GroupPattern();
}
