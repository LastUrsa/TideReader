using System.Net;
using System.Text;
using TideReader.Backend.Models;
using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class MetadataEnricherCacheTests
{
    [Fact]
    public async Task EnrichArtworkAsync_PersistsFetchedMetadataAndArtworkInCache()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var logger = new AppLogger(tempDir);
            var cachePath = Path.Combine(tempDir, "cache.json");
            var handler = new DelegateHandler(request =>
            {
                if (request.RequestUri!.Host.Contains("itunes.apple.com", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"results":[{"artistName":"Artist","collectionName":"Album","trackName":"Track","trackTimeMillis":123000,"artworkUrl100":"https://example.test/art-100x100bb.jpg"}]}""", Encoding.UTF8, "application/json")
                    });
                }

                if (request.RequestUri!.Host.Contains("musicbrainz.org", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"recordings":[]}""", Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([7, 8, 9])
                });
            });

            var input = new DetectionResult
            {
                Artist = "Artist",
                Title = "Track"
            };

            var initialClient = new HttpClient(handler);
            var enricher = new MetadataEnricher(initialClient, logger, cachePath: cachePath);
            var metadataOnly = await enricher.EnrichAsync(input, MetadataProviderMode.MusicBrainzWithFallbacks, CancellationToken.None);
            var withArtwork = await enricher.EnrichArtworkAsync(metadataOnly, MetadataProviderMode.MusicBrainzWithFallbacks, CancellationToken.None);

            Assert.Equal("Album", metadataOnly.Album);
            Assert.Equal("itunes_search", metadataOnly.MetadataSource);
            Assert.Equal(123000, metadataOnly.DurationMs);
            Assert.Equal("cover.jpg", withArtwork.ArtworkPath);
            Assert.Equal([7, 8, 9], withArtwork.ArtworkBytes);
            Assert.True(File.Exists(cachePath));

            var cachedClient = new HttpClient(new DelegateHandler(_ => throw new InvalidOperationException("cache should satisfy this request")));
            var cachedEnricher = new MetadataEnricher(cachedClient, logger, cachePath: cachePath);
            var cachedResult = cachedEnricher.ApplyCached(new DetectionResult
            {
                Artist = "Artist",
                Title = "Track"
            });

            Assert.Equal("Album", cachedResult.Album);
            Assert.Equal("itunes_search", cachedResult.MetadataSource);
            Assert.Equal([7, 8, 9], cachedResult.ArtworkBytes);

            cachedResult.ArtworkBytes[0] = 42;

            var secondCachedResult = cachedEnricher.ApplyCached(new DetectionResult
            {
                Artist = "Artist",
                Title = "Track"
            });

            Assert.Equal([7, 8, 9], secondCachedResult.ArtworkBytes);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task EnrichArtworkAsync_ReturnsInput_WhenArtworkDownloadFails()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var logger = new AppLogger(tempDir);
            var handler = new DelegateHandler(request =>
            {
                if (request.RequestUri!.Host.Contains("itunes.apple.com", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"results":[{"artistName":"Artist","collectionName":"Album","trackName":"Track","trackTimeMillis":123000,"artworkUrl100":"https://example.test/art-100x100bb.jpg"}]}""", Encoding.UTF8, "application/json")
                    });
                }

                if (request.RequestUri!.Host.Contains("musicbrainz.org", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"recordings":[]}""", Encoding.UTF8, "application/json")
                    });
                }

                throw new HttpRequestException("artwork failed");
            });

            var client = new HttpClient(handler);
            var enricher = new MetadataEnricher(client, logger, cachePath: Path.Combine(tempDir, "cache.json"));
            var metadataOnly = await enricher.EnrichAsync(new DetectionResult
            {
                Artist = "Artist",
                Title = "Track"
            }, MetadataProviderMode.MusicBrainzWithFallbacks, CancellationToken.None);

            var result = await enricher.EnrichArtworkAsync(metadataOnly, MetadataProviderMode.MusicBrainzWithFallbacks, CancellationToken.None);

            Assert.Equal("Album", result.Album);
            Assert.Empty(result.ArtworkBytes);
            Assert.Equal("", result.ArtworkPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ApplyCached_ReturnsInput_WhenTitleOrArtistIsMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var logger = new AppLogger(tempDir);
            var enricher = new MetadataEnricher(new HttpClient(new DelegateHandler(_ => throw new InvalidOperationException("network should not be used"))), logger, cachePath: Path.Combine(tempDir, "cache.json"));
            var missingTitle = new DetectionResult { Artist = "Artist", Title = "" };
            var missingArtist = new DetectionResult { Artist = "", Title = "Track" };

            Assert.Same(missingTitle, enricher.ApplyCached(missingTitle));
            Assert.Same(missingArtist, enricher.ApplyCached(missingArtist));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void NeedsEnrichment_ReturnsFalse_WhenModeIsOff_OrIdentityIsMissing_OrMetadataIsComplete()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var logger = new AppLogger(tempDir);
            var enricher = new MetadataEnricher(new HttpClient(new DelegateHandler(_ => throw new InvalidOperationException("network should not be used"))), logger, cachePath: Path.Combine(tempDir, "cache.json"));

            Assert.False(enricher.NeedsEnrichment(new DetectionResult { Artist = "Artist", Title = "Track" }, MetadataProviderMode.Off));
            Assert.False(enricher.NeedsEnrichment(new DetectionResult { Artist = "", Title = "Track" }, MetadataProviderMode.MusicBrainzOnly));
            Assert.False(enricher.NeedsEnrichment(new DetectionResult
            {
                Artist = "Artist",
                Title = "Track",
                Album = "Album",
                DurationMs = 123000,
                ArtworkBytes = [1]
            }, MetadataProviderMode.MusicBrainzOnly));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task EnrichAsync_ReturnsInput_WhenEnrichmentIsNotNeeded()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var logger = new AppLogger(tempDir);
            var input = new DetectionResult
            {
                Artist = "Artist",
                Title = "Track",
                Album = "Album",
                DurationMs = 123000,
                ArtworkBytes = [1]
            };
            var enricher = new MetadataEnricher(new HttpClient(new DelegateHandler(_ => throw new InvalidOperationException("network should not be used"))), logger, cachePath: Path.Combine(tempDir, "cache.json"));

            var result = await enricher.EnrichAsync(input, MetadataProviderMode.MusicBrainzOnly, CancellationToken.None);

            Assert.Same(input, result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task EnrichAsync_UsesCachedMetadataWithoutNetwork_WhenCacheAlreadySatisfiesTheRequest()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var logger = new AppLogger(tempDir);
            var cachePath = Path.Combine(tempDir, "cache.json");
            await File.WriteAllTextAsync(cachePath, """
            {
              "artist|track": {
                "album": "Album",
                "durationMs": 123000,
                "metadataSource": "itunes_search",
                "artworkPath": "cover.jpg",
                "artworkBytes": "AQID"
              }
            }
            """);

            var enricher = new MetadataEnricher(new HttpClient(new DelegateHandler(_ => throw new InvalidOperationException("network should not be used"))), logger, cachePath: cachePath);
            var result = await enricher.EnrichAsync(new DetectionResult
            {
                Artist = "Artist",
                Title = "Track"
            }, MetadataProviderMode.MusicBrainzWithFallbacks, CancellationToken.None);

            Assert.Equal("Album", result.Album);
            Assert.Equal(123000, result.DurationMs);
            Assert.Equal("itunes_search", result.MetadataSource);
            Assert.Equal("cover.jpg", result.ArtworkPath);
            Assert.Equal([1, 2, 3], result.ArtworkBytes);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task EnrichArtworkAsync_ReturnsInput_WhenLookupHasNoArtworkUrl()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var logger = new AppLogger(tempDir);
            var handler = new DelegateHandler(request =>
            {
                if (request.RequestUri!.Host.Contains("musicbrainz.org", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"recordings":[{"score":95,"title":"Track","length":123000,"artistCredit":[{"name":"Artist"}],"releases":[]}]}""", Encoding.UTF8, "application/json")
                    });
                }

                throw new InvalidOperationException("no artwork request should be made");
            });

            var enricher = new MetadataEnricher(new HttpClient(handler), logger, cachePath: Path.Combine(tempDir, "cache.json"));
            var result = await enricher.EnrichArtworkAsync(new DetectionResult
            {
                Artist = "Artist",
                Title = "Track"
            }, MetadataProviderMode.MusicBrainzOnly, CancellationToken.None);

            Assert.Equal("", result.ArtworkPath);
            Assert.Empty(result.ArtworkBytes);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task EnrichArtworkAsync_ReturnsInput_WhenLookupFindsNoCandidates()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var logger = new AppLogger(tempDir);
            var handler = new DelegateHandler(request =>
            {
                if (request.RequestUri!.Host.Contains("musicbrainz.org", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"recordings":[]}""", Encoding.UTF8, "application/json")
                    });
                }

                throw new InvalidOperationException("no artwork request should be made");
            });

            var input = new DetectionResult { Artist = "Artist", Title = "Track" };
            var enricher = new MetadataEnricher(new HttpClient(handler), logger, cachePath: Path.Combine(tempDir, "cache.json"));
            var result = await enricher.EnrichArtworkAsync(input, MetadataProviderMode.MusicBrainzOnly, CancellationToken.None);

            Assert.Same(input, result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Constructor_IgnoresInvalidCacheContent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var logger = new AppLogger(tempDir);
            var cachePath = Path.Combine(tempDir, "cache.json");
            File.WriteAllText(cachePath, "{ invalid json");

            var enricher = new MetadataEnricher(new HttpClient(new DelegateHandler(_ => throw new InvalidOperationException("network should not be used"))), logger, cachePath: cachePath);
            var result = enricher.ApplyCached(new DetectionResult
            {
                Artist = "Artist",
                Title = "Track"
            });

            Assert.Equal("", result.Album);
            Assert.Empty(result.ArtworkBytes);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}
