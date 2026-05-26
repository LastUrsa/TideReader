using System.Net;
using System.Net.Http;
using System.Text;
using TideReader.Backend.Models;
using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class MetadataEnricherNetworkTests
{
    [Fact]
    public async Task EnrichAsync_ReturnsInput_WhenLookupTimesOut()
    {
        var logger = CreateLogger(out var tempDir);
        try
        {
            using (logger)
            {
                var handler = new DelegateHandler(async _ =>
                {
                    await Task.Delay(200);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"results": []}""", Encoding.UTF8, "application/json")
                    };
                });
                var client = new HttpClient(handler);
                var enricher = new MetadataEnricher(client, logger, cachePath: System.IO.Path.Combine(tempDir, "cache.json"), metadataLookupTimeout: TimeSpan.FromMilliseconds(20));

                var result = await enricher.EnrichAsync(new DetectionResult
                {
                    Artist = "Artist",
                    Title = "Track"
                }, MetadataProviderMode.MusicBrainzWithFallbacks, CancellationToken.None);

                Assert.Equal("", result.Album);
                Assert.Empty(result.ArtworkBytes);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task EnrichArtworkAsync_ReturnsInput_WhenArtworkTimesOut()
    {
        var logger = CreateLogger(out var tempDir);
        try
        {
            using (logger)
            {
                var handler = new DelegateHandler(async request =>
                {
                    if (request.RequestUri!.Host.Contains("itunes.apple.com", StringComparison.OrdinalIgnoreCase))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("""{"results":[{"artistName":"Artist","collectionName":"Album","trackName":"Track","trackTimeMillis":123000,"artworkUrl100":"https://example.test/art-100x100bb.jpg"}]}""", Encoding.UTF8, "application/json")
                        };
                    }

                    if (request.RequestUri!.Host.Contains("musicbrainz.org", StringComparison.OrdinalIgnoreCase))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("""{"recordings":[]}""", Encoding.UTF8, "application/json")
                        };
                    }

                    await Task.Delay(200);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent([1, 2, 3])
                    };
                });
                var client = new HttpClient(handler);
                var enricher = new MetadataEnricher(
                    client,
                    logger,
                    cachePath: System.IO.Path.Combine(tempDir, "cache.json"),
                    artworkFetchTimeout: TimeSpan.FromMilliseconds(20));

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
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static AppLogger CreateLogger(out string tempDir)
    {
        tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        return new AppLogger(tempDir);
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}
