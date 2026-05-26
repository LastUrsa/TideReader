using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using TideReader.Backend.Models;
using TideReader.Backend.Services;

namespace TideReader.Backend.Tests;

public sealed class OverlayServerIntegrationTests
{
    [Fact]
    public async Task ConfigureAsync_ServesOverlayPayloads_AndCanBeDisabled()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var logger = new AppLogger(tempDir);
            var store = new PlaybackSnapshotStore();
            store.Update(new DetectionResult
            {
                Status = "playing",
                Title = "Track",
                Artist = "Artist",
                Album = "Album",
                ArtworkPath = "cover.jpg",
                ArtworkBytes = [1, 2, 3],
                Source = "TIDAL",
                Confidence = 0.92
            });

            using var server = new OverlayServer(store, logger);
            var port = GetAvailablePort();
            await server.ConfigureAsync(true, port, CancellationToken.None);

            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };

            var overlayResponse = await client.GetAsync("overlay");
            var nowPlaying = await client.GetFromJsonAsync<NowPlayingFile>("nowplaying.json");
            var artworkResponse = await client.GetAsync("cover.jpg");
            var missingResponse = await client.GetAsync("missing");

            Assert.Equal($"http://127.0.0.1:{port}/overlay", server.Url);
            Assert.Equal(HttpStatusCode.OK, overlayResponse.StatusCode);
            Assert.NotNull(nowPlaying);
            Assert.Equal("Track", nowPlaying!.Title);
            Assert.Equal("Artist", nowPlaying.Artist);
            Assert.Equal(HttpStatusCode.OK, artworkResponse.StatusCode);
            Assert.Equal([1, 2, 3], await artworkResponse.Content.ReadAsByteArrayAsync());
            Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);

            await server.ConfigureAsync(false, port, CancellationToken.None);

            Assert.Equal("", server.Url);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
