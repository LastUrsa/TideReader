using System.Diagnostics;
using TideReader.Backend.Models;

namespace TideReader.Backend.Services;

public sealed class WindowTitleDetector : IWindowTitleDetector
{
    public DetectionResult? Detect()
    {
        var process = Process.GetProcessesByName("TIDAL")
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.MainWindowTitle));

        if (process is null || string.IsNullOrWhiteSpace(process.MainWindowTitle))
        {
            return null;
        }

        var (artist, title, album) = ParseWindowTitle(process.MainWindowTitle);
        return new DetectionResult
        {
            Status = "playing",
            Title = title,
            Artist = artist,
            Album = album,
            Source = "TIDAL",
            Method = "window_title",
            Confidence = Score(process.MainWindowTitle, artist, title, album),
            DetectedText = process.MainWindowTitle,
            MatcherReason = "window_title_fallback"
        };
    }

    public static (string Artist, string Title, string Album) ParseWindowTitle(string input)
    {
        var title = input.Trim();
        if (title.EndsWith(" - TIDAL", StringComparison.OrdinalIgnoreCase))
        {
            title = title[..^8];
        }

        if (title.StartsWith("TIDAL - ", StringComparison.OrdinalIgnoreCase))
        {
            title = title[8..];
        }

        title = title.Trim(' ', '-');
        var parts = title.Split(" - ", StringSplitOptions.None);
        return parts.Length switch
        {
            0 => ("", "", ""),
            1 => ("", parts[0].Trim(), ""),
            2 => (parts[1].Trim(), parts[0].Trim(), ""),
            _ => (parts[^1].Trim(), string.Join(" - ", parts[..^1]).Trim(), "")
        };
    }

    private static double Score(string raw, string artist, string title, string album)
    {
        var score = 0.36;
        if (raw.Contains("TIDAL", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.1;
        }
        if (!string.IsNullOrWhiteSpace(title))
        {
            score += 0.2;
        }
        if (!string.IsNullOrWhiteSpace(artist))
        {
            score += 0.16;
        }
        if (!string.IsNullOrWhiteSpace(album))
        {
            score += 0.04;
        }
        return Math.Min(score, 0.99);
    }
}
