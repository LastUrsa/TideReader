using TideReader.Backend.Models;

namespace TideReader.Backend.Services;

public sealed class ManualDetector : IManualDetector
{
    public DetectionResult? Detect(string input)
    {
        input = input.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var sections = input.Split('|', 2, StringSplitOptions.TrimEntries);
        var main = sections[0];
        var album = sections.Length > 1 ? sections[1] : "";
        var split = main.Split(" - ", 2, StringSplitOptions.TrimEntries);

        return new DetectionResult
        {
            Status = "playing",
            Artist = split.Length > 1 ? split[0] : "",
            Title = split.Length > 1 ? split[1] : main,
            Album = album,
            Source = "TIDAL",
            Method = "manual",
            Confidence = 0.65,
            DetectedText = input,
            MatcherReason = "manual_debug_input"
        };
    }
}
