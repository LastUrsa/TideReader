using System.Text.Json;
using TideReader.Backend.Models;

namespace TideReader.Backend.Services;

public sealed class OutputWriter : IOutputWriter
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, string> _lastText = new(StringComparer.OrdinalIgnoreCase);
    private string _lastJson = "";
    private byte[] _lastArtwork = [];

    public Task WriteAsync(string outputFolder, DetectionResult state, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            outputFolder = OutputPathPolicy.NormalizeFolderPath(outputFolder);
            Directory.CreateDirectory(outputFolder);

            var payload = new NowPlayingFile
            {
                Status = state.Status,
                Title = state.Title,
                Artist = state.Artist,
                Album = state.Album,
                DurationMs = state.DurationMs,
                ArtworkPath = state.ArtworkPath,
                Source = state.Source,
                Confidence = state.Confidence,
                Provider = state.Provider,
                Browser = state.Browser,
                Site = state.Site
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            WriteIfChanged(System.IO.Path.Combine(outputFolder, "nowplaying.json"), ref _lastJson, json);

            WriteIfChanged(System.IO.Path.Combine(outputFolder, "title.txt"), "title.txt", state.Title);
            WriteIfChanged(System.IO.Path.Combine(outputFolder, "artist.txt"), "artist.txt", state.Artist);
            WriteIfChanged(System.IO.Path.Combine(outputFolder, "album.txt"), "album.txt", state.Album);
            WriteIfChanged(System.IO.Path.Combine(outputFolder, "status.txt"), "status.txt", state.Status);
            WriteIfChanged(System.IO.Path.Combine(outputFolder, "track.txt"), "track.txt", BuildTrack(state));

            var coverPath = System.IO.Path.Combine(outputFolder, "cover.jpg");
            if (state.ArtworkBytes.Length > 0)
            {
                if (!_lastArtwork.AsSpan().SequenceEqual(state.ArtworkBytes))
                {
                    File.WriteAllBytes(coverPath, state.ArtworkBytes);
                    _lastArtwork = state.ArtworkBytes.ToArray();
                }
            }
            else if (_lastArtwork.Length > 0)
            {
                if (File.Exists(coverPath))
                {
                    File.Delete(coverPath);
                }

                _lastArtwork = [];
            }
        }

        return Task.CompletedTask;
    }

    private void WriteIfChanged(string path, string key, string value)
    {
        if (_lastText.TryGetValue(key, out var last) && last == value)
        {
            return;
        }

        File.WriteAllText(path, value ?? "");
        _lastText[key] = value ?? "";
    }

    private static void WriteIfChanged(string path, ref string current, string next)
    {
        if (current == next)
        {
            return;
        }

        File.WriteAllText(path, next);
        current = next;
    }

    private static string BuildTrack(DetectionResult state)
    {
        if (string.IsNullOrWhiteSpace(state.Artist))
        {
            return state.Title;
        }

        if (string.IsNullOrWhiteSpace(state.Title))
        {
            return state.Artist;
        }

        return $"{state.Artist} - {state.Title}";
    }
}
