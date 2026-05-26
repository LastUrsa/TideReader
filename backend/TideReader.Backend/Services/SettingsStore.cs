using System.Text.Json;
using TideReader.Backend.Models;

namespace TideReader.Backend.Services;

public sealed class SettingsStore : ISettingsStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public SettingsStore(string? rootDirectory = null)
    {
        var dir = rootDirectory ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TideReader");
        Directory.CreateDirectory(dir);
        _path = System.IO.Path.Combine(dir, "settings.json");
    }

    public string Path => _path;

    public async Task<Settings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new Settings();
        }

        await using var stream = File.OpenRead(_path);
        var settings = await JsonSerializer.DeserializeAsync<Settings>(stream, _jsonOptions, cancellationToken);
        return settings ?? new Settings();
    }

    public async Task SaveAsync(Settings settings, CancellationToken cancellationToken)
    {
        var directory = System.IO.Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(settings, _jsonOptions), cancellationToken);
    }
}
