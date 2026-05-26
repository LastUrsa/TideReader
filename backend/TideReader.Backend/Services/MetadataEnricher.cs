using System.Net.Http.Json;
using System.Text.Json;
using TideReader.Backend.Models;

namespace TideReader.Backend.Services;

public sealed class MetadataEnricher : IMetadataEnricher
{
    private readonly TimeSpan _artworkFetchTimeout;
    private readonly TimeSpan _metadataLookupTimeout;
    private readonly HttpClient _httpClient;
    private readonly AppLogger _logger;
    private readonly string _cachePath;
    private readonly Dictionary<string, DetectionResult> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<DetectionResult>> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MetadataCandidateOption> _lookupCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public MetadataEnricher(HttpClient httpClient, AppLogger logger, string? cachePath = null, TimeSpan? metadataLookupTimeout = null, TimeSpan? artworkFetchTimeout = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cachePath = cachePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TideReader", "metadata-cache.json");
        _metadataLookupTimeout = metadataLookupTimeout ?? TimeSpan.FromSeconds(2.5);
        _artworkFetchTimeout = artworkFetchTimeout ?? TimeSpan.FromSeconds(2.5);
        LoadCache();
    }

    public DetectionResult ApplyCached(DetectionResult input)
    {
        if (string.IsNullOrWhiteSpace(input.Title) || string.IsNullOrWhiteSpace(input.Artist))
        {
            return input;
        }

        var key = CacheKey(input.Artist, input.Title);
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                _logger.Info($"metadata cache hit: artist=\"{input.Artist}\" title=\"{input.Title}\" source={cached.MetadataSource}");
                return Merge(input, CloneDetection(cached));
            }
        }

        return input;
    }

    public bool NeedsEnrichment(DetectionResult input, MetadataProviderMode mode)
    {
        if (mode == MetadataProviderMode.Off)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(input.Title) || string.IsNullOrWhiteSpace(input.Artist))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(input.Album) || input.DurationMs == 0 || input.ArtworkBytes.Length == 0;
    }

    public async Task<DetectionResult> EnrichAsync(DetectionResult input, MetadataProviderMode mode, CancellationToken cancellationToken)
    {
        if (!NeedsEnrichment(input, mode))
        {
            return input;
        }

        var cached = ApplyCached(CloneDetection(input));
        if (!NeedsEnrichment(cached, mode))
        {
            return cached;
        }

        var key = CacheKey(input.Artist, input.Title);
        Task<DetectionResult> task;
        lock (_lock)
        {
            if (!_inflight.TryGetValue(key, out task!))
            {
                task = FetchAndCacheMetadataAsync(CloneDetection(input), mode, key, cancellationToken);
                _inflight[key] = task;
            }
        }

        try
        {
            var enriched = await task;
            return Merge(input, CloneDetection(enriched));
        }
        finally
        {
            lock (_lock)
            {
                if (_inflight.TryGetValue(key, out var current) && current == task)
                {
                    _inflight.Remove(key);
                }
            }
        }
    }

    public async Task<DetectionResult> EnrichArtworkAsync(DetectionResult input, MetadataProviderMode mode, CancellationToken cancellationToken)
    {
        if (input.ArtworkBytes.Length > 0 || string.IsNullOrWhiteSpace(input.Title) || string.IsNullOrWhiteSpace(input.Artist))
        {
            return input;
        }

        var key = CacheKey(input.Artist, input.Title);
        MetadataCandidateOption lookup;
        var hasLookup = false;
        lock (_lock)
        {
            hasLookup = _lookupCache.TryGetValue(key, out lookup);
        }

        if (!hasLookup)
        {
            var resolved = await ResolveBestCandidateAsync(CloneDetection(input), mode, cancellationToken);
            if (resolved is null)
            {
                return input;
            }

            lookup = resolved.Value;
        }

        if (string.IsNullOrWhiteSpace(lookup.ArtworkUrl))
        {
            return input;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_artworkFetchTimeout);
        try
        {
            var artwork = await _httpClient.GetByteArrayAsync(lookup.ArtworkUrl, timeoutCts.Token);
            if (artwork.Length == 0)
            {
                return input;
            }

            var enriched = new DetectionResult
            {
                Album = lookup.Album,
                DurationMs = lookup.DurationMs,
                Confidence = lookup.Confidence,
                MetadataSource = lookup.MetadataSource,
                ArtworkBytes = artwork,
                ArtworkPath = "cover.jpg"
            };

            lock (_lock)
            {
                _cache[key] = CloneDetection(Merge(CloneDetection(input), CloneDetection(enriched)));
            }

            await SaveCacheAsync(CancellationToken.None);
            _logger.Info($"artwork fetch applied: artist=\"{input.Artist}\" title=\"{input.Title}\" source={lookup.MetadataSource} elapsedBudgetMs={(int)_artworkFetchTimeout.TotalMilliseconds}");
            return Merge(input, enriched);
        }
        catch (OperationCanceledException)
        {
            _logger.Info($"artwork fetch timed out: artist=\"{input.Artist}\" title=\"{input.Title}\" source={lookup.MetadataSource}");
            return input;
        }
        catch
        {
            return input;
        }
    }

    private async Task<DetectionResult> FetchAndCacheMetadataAsync(DetectionResult input, MetadataProviderMode mode, string key, CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        var best = await ResolveBestCandidateAsync(input, mode, cancellationToken);

        if (best is null)
        {
            _logger.Info($"metadata lookup no-match: artist=\"{input.Artist}\" title=\"{input.Title}\" mode={mode}");
            return input;
        }

        var selected = best.Value;

        var enriched = new DetectionResult
        {
            Album = selected.Album,
            DurationMs = selected.DurationMs,
            Confidence = selected.Confidence,
            MetadataSource = selected.MetadataSource
        };

        lock (_lock)
        {
            _lookupCache[key] = selected;
            _cache[key] = CloneDetection(enriched);
        }

        await SaveCacheAsync(cancellationToken);

        var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;
        _logger.Info($"metadata lookup match: artist=\"{input.Artist}\" title=\"{input.Title}\" source={selected.MetadataSource} album=\"{enriched.Album}\" artwork=deferred score={selected.Confidence:F2} reason={selected.MatchSummary} elapsedMs={elapsedMs:F0}");
        return enriched;
    }

    private async Task<MetadataCandidateOption?> ResolveBestCandidateAsync(DetectionResult input, MetadataProviderMode mode, CancellationToken cancellationToken)
    {
        Task<List<MetadataCandidateOption>> musicBrainzTask = SearchMusicBrainzAsync(input, cancellationToken);
        Task<List<MetadataCandidateOption>>? iTunesTask = mode == MetadataProviderMode.MusicBrainzWithFallbacks
            ? SearchItunesAsync(input, cancellationToken)
            : null;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_metadataLookupTimeout);

        var lookupTasks = iTunesTask is null
            ? new Task<List<MetadataCandidateOption>>[] { musicBrainzTask }
            : [musicBrainzTask, iTunesTask];

        try
        {
            await Task.WhenAll(lookupTasks).WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.Info($"metadata lookup timed out: artist=\"{input.Artist}\" title=\"{input.Title}\" budgetMs={(int)_metadataLookupTimeout.TotalMilliseconds}");
        }

        var candidates = new List<MetadataCandidateOption>();
        if (musicBrainzTask.IsCompletedSuccessfully)
        {
            candidates.AddRange(musicBrainzTask.Result);
        }
        if (iTunesTask is not null && iTunesTask.IsCompletedSuccessfully)
        {
            candidates.AddRange(iTunesTask.Result);
        }

        if (candidates.Count == 0)
        {
            _logger.Info($"metadata lookup empty: artist=\"{input.Artist}\" title=\"{input.Title}\" mode={mode}");
            return null;
        }

        _logger.Info($"metadata lookup candidates: artist=\"{input.Artist}\" title=\"{input.Title}\" top={MetadataCandidateSelector.DescribeTop(candidates)}");
        return MetadataCandidateSelector.SelectBest(candidates);
    }

    private async Task<List<MetadataCandidateOption>> SearchMusicBrainzAsync(DetectionResult input, CancellationToken cancellationToken)
    {
        try
        {
            var query = Uri.EscapeDataString($"recording:\"{input.Title}\" AND artist:\"{input.Artist}\"");
            var response = await _httpClient.GetFromJsonAsync<MusicBrainzResponse>($"https://musicbrainz.org/ws/2/recording/?fmt=json&limit=5&query={query}", cancellationToken);
            if (response?.Recordings is null)
            {
                return [];
            }

            var candidates = new List<MetadataCandidateOption>();
            foreach (var candidate in response.Recordings)
            {
                var artistName = string.Join(", ", candidate.ArtistCredit.Select(a => a.Name));
                var titleScore = MetadataMatchScorer.ScoreTitle(candidate.Title, input.Title);
                var artistScore = MetadataMatchScorer.ScoreArtist(artistName, input.Artist);
                var durationScore = MetadataMatchScorer.ScoreDuration(candidate.Length, input.DurationMs);
                var providerScore = candidate.Score / 100.0 * 0.18;
                var releaseScore = candidate.Releases.Count > 0 ? 0.04 : 0;
                var releaseId = candidate.Releases.FirstOrDefault()?.Id;
                var artworkScore = !string.IsNullOrWhiteSpace(releaseId) ? 0.03 : 0;
                var score = providerScore + titleScore + artistScore + durationScore + releaseScore + artworkScore;

                candidates.Add(new MetadataCandidateOption(
                    MetadataSource: "musicbrainz",
                    Album: candidate.Releases.FirstOrDefault()?.Title ?? "",
                    DurationMs: candidate.Length,
                    Confidence: Math.Max(input.Confidence, Math.Min(score, 0.99)),
                    ArtworkUrl: !string.IsNullOrWhiteSpace(releaseId) ? $"https://coverartarchive.org/release/{releaseId}/front-250" : "",
                    HasArtwork: !string.IsNullOrWhiteSpace(releaseId),
                    MatchSummary: $"title={titleScore:F2},artist={artistScore:F2},duration={durationScore:F2},provider={providerScore:F2}"));
            }

            return candidates;
        }
        catch
        {
            return [];
        }
    }

    private async Task<List<MetadataCandidateOption>> SearchItunesAsync(DetectionResult input, CancellationToken cancellationToken)
    {
        try
        {
            var query = Uri.EscapeDataString($"{input.Artist} {input.Title}");
            var response = await _httpClient.GetFromJsonAsync<ItunesResponse>($"https://itunes.apple.com/search?entity=song&limit=10&term={query}", cancellationToken);
            if (response?.Results is null)
            {
                return [];
            }

            var candidates = new List<MetadataCandidateOption>();
            foreach (var result in response.Results)
            {
                var titleScore = MetadataMatchScorer.ScoreTitle(result.TrackName, input.Title);
                var artistScore = MetadataMatchScorer.ScoreArtist(result.ArtistName, input.Artist);
                var durationScore = MetadataMatchScorer.ScoreDuration(result.TrackTimeMillis, input.DurationMs);
                var albumScore = !string.IsNullOrWhiteSpace(result.CollectionName) ? 0.05 : 0;
                var artworkUrl = !string.IsNullOrWhiteSpace(result.ArtworkUrl100)
                    ? result.ArtworkUrl100.Replace("100x100bb", "600x600bb", StringComparison.OrdinalIgnoreCase)
                    : "";
                var artworkScore = !string.IsNullOrWhiteSpace(artworkUrl) ? 0.03 : 0;
                var score = 0.22 + titleScore + artistScore + durationScore + albumScore + artworkScore;

                candidates.Add(new MetadataCandidateOption(
                    MetadataSource: "itunes_search",
                    Album: result.CollectionName ?? "",
                    DurationMs: result.TrackTimeMillis > 0 ? result.TrackTimeMillis : input.DurationMs,
                    Confidence: Math.Max(input.Confidence, Math.Min(score, 0.98)),
                    ArtworkUrl: artworkUrl,
                    HasArtwork: !string.IsNullOrWhiteSpace(artworkUrl),
                    MatchSummary: $"title={titleScore:F2},artist={artistScore:F2},duration={durationScore:F2},album={albumScore:F2}"));
            }

            return candidates;
        }
        catch
        {
            return [];
        }
    }

    private static DetectionResult Merge(DetectionResult input, DetectionResult extra)
    {
        if (string.IsNullOrWhiteSpace(input.Album)) input.Album = extra.Album;
        if (input.DurationMs == 0) input.DurationMs = extra.DurationMs;
        if (string.IsNullOrWhiteSpace(input.MetadataSource)) input.MetadataSource = extra.MetadataSource;
        if (ShouldReplaceArtwork(input, extra))
        {
            input.ArtworkBytes = extra.ArtworkBytes.ToArray();
            input.ArtworkPath = extra.ArtworkPath;
        }
        if (extra.Confidence > input.Confidence) input.Confidence = extra.Confidence;
        return input;
    }

    private static string CacheKey(string artist, string title) => $"{MetadataMatchScorer.Normalize(artist)}|{MetadataMatchScorer.Normalize(title)}";

    private static bool ShouldReplaceArtwork(DetectionResult current, DetectionResult extra)
    {
        if (extra.ArtworkBytes.Length == 0)
        {
            return false;
        }

        if (current.ArtworkBytes.Length == 0)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(extra.MetadataSource) &&
            !current.ArtworkBytes.AsSpan().SequenceEqual(extra.ArtworkBytes);
    }

    private DetectionResult CloneDetection(DetectionResult input) => new()
    {
        Status = input.Status,
        Title = input.Title,
        Artist = input.Artist,
        Album = input.Album,
        DurationMs = input.DurationMs,
        Source = input.Source,
        Method = input.Method,
        Confidence = input.Confidence,
        TidalUrl = input.TidalUrl,
        DetectedText = input.DetectedText,
        SourceAppId = input.SourceAppId,
        MatcherReason = input.MatcherReason,
        MetadataSource = input.MetadataSource,
        ArtworkPath = input.ArtworkPath,
        ArtworkBytes = input.ArtworkBytes.ToArray()
    };

    private void LoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath))
            {
                return;
            }

            var json = File.ReadAllText(_cachePath);
            var items = JsonSerializer.Deserialize<Dictionary<string, DetectionResult>>(json, _jsonOptions);
            if (items is null)
            {
                return;
            }

            lock (_lock)
            {
                _cache.Clear();
                foreach (var item in items)
                {
                    _cache[item.Key] = CloneDetection(item.Value);
                }
            }
        }
        catch
        {
        }
    }

    private async Task SaveCacheAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, DetectionResult> snapshot;
        lock (_lock)
        {
            snapshot = _cache.ToDictionary(pair => pair.Key, pair => CloneDetection(pair.Value), StringComparer.OrdinalIgnoreCase);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
        await File.WriteAllTextAsync(_cachePath, json, cancellationToken);
    }

    private sealed class MusicBrainzResponse
    {
        public List<MusicBrainzRecording> Recordings { get; set; } = [];
    }

    private sealed class MusicBrainzRecording
    {
        public int Score { get; set; }
        public string Title { get; set; } = "";
        public long Length { get; set; }
        public List<MusicBrainzArtistCredit> ArtistCredit { get; set; } = [];
        public List<MusicBrainzRelease> Releases { get; set; } = [];
    }

    private sealed class MusicBrainzArtistCredit
    {
        public string Name { get; set; } = "";
    }

    private sealed class MusicBrainzRelease
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
    }

    private sealed class ItunesResponse
    {
        public List<ItunesTrack> Results { get; set; } = [];
    }

    private sealed class ItunesTrack
    {
        public string ArtistName { get; set; } = "";
        public string CollectionName { get; set; } = "";
        public string TrackName { get; set; } = "";
        public long TrackTimeMillis { get; set; }
        public string ArtworkUrl100 { get; set; } = "";
    }

}
