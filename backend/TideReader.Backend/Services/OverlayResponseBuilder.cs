using System.Net;
using System.Text;
using System.Text.Json;
using TideReader.Backend.Models;

namespace TideReader.Backend.Services;

internal sealed record OverlayResponse(int StatusCode, string ContentType, byte[] Body);

internal static class OverlayResponseBuilder
{
    public static OverlayResponse Build(string path, IPlaybackSnapshotStore snapshotStore)
    {
        if (path.Equals("/overlay", StringComparison.OrdinalIgnoreCase))
        {
            return Text((int)HttpStatusCode.OK, "text/html; charset=utf-8", OverlayHtml);
        }

        if (path.Equals("/nowplaying.json", StringComparison.OrdinalIgnoreCase))
        {
            var payload = snapshotStore.GetNowPlaying();
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return Text((int)HttpStatusCode.OK, "application/json", json);
        }

        if (path.Equals("/cover.jpg", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = snapshotStore.GetArtwork();
            return bytes.Length == 0
                ? new OverlayResponse((int)HttpStatusCode.NotFound, "text/plain", [])
                : new OverlayResponse((int)HttpStatusCode.OK, "image/jpeg", bytes);
        }

        return new OverlayResponse((int)HttpStatusCode.NotFound, "text/plain", []);
    }

    private static OverlayResponse Text(int statusCode, string contentType, string body) =>
        new(statusCode, contentType, Encoding.UTF8.GetBytes(body));

    public const string OverlayHtml = """
<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>TIDAL Overlay</title>
    <style>
      :root {
        color-scheme: dark;
        --surface-primary: rgba(50, 51, 79, 0.86);
        --surface-input: rgba(35, 37, 56, 0.94);
        --surface-active: #445875;
        --surface-active-pill: #3f5067;
        --border: rgba(146, 148, 152, 0.22);
        --text: #ebebeb;
        --muted: #929498;
        --accent: #50657f;
        --accent-hover: #627a96;
        --success: #89c89d;
        --warning: #d6b36a;
        --danger: #720201;
        --danger-bg: #4a1014;
        --shadow: rgba(0, 0, 0, 0.32);
        --art-gradient-start: rgba(98, 122, 150, 0.42);
        --art-gradient-end: rgba(35, 37, 56, 0.92);
        --art-sheen-top: rgba(235, 235, 235, 0.08);
        --art-sheen-bottom: rgba(235, 235, 235, 0.02);
      }
      body {
        margin: 0;
        font-family: "Segoe UI", "IBM Plex Sans", sans-serif;
        background: transparent;
        color: var(--text);
      }
      .frame {
        display: flex;
        gap: 14px;
        align-items: center;
        width: fit-content;
        min-width: 360px;
        padding: 12px 14px;
        border: 1px solid var(--border);
        border-radius: 18px;
        background:
          radial-gradient(circle at top left, rgba(98, 122, 150, 0.18), transparent 34%),
          radial-gradient(circle at top right, rgba(80, 101, 127, 0.14), transparent 28%),
          linear-gradient(180deg, rgba(38, 39, 61, 0.96), var(--surface-primary) 48%, rgba(50, 51, 79, 0.92));
        box-shadow: 0 18px 42px var(--shadow);
        backdrop-filter: blur(18px);
      }
      img {
        width: 68px;
        height: 68px;
        object-fit: cover;
        border-radius: 14px;
        background:
          linear-gradient(135deg, var(--art-gradient-start), var(--art-gradient-end)),
          linear-gradient(180deg, var(--art-sheen-top), var(--art-sheen-bottom));
        border: 1px solid rgba(146, 148, 152, 0.18);
      }
      .copy {
        min-width: 0;
        flex: 1;
      }
      .topline {
        display: flex;
        align-items: center;
        gap: 10px;
        margin-bottom: 4px;
      }
      .brand {
        color: var(--text);
        font-size: 12px;
        font-weight: 600;
        letter-spacing: .02em;
      }
      .status-pill {
        padding: 4px 9px;
        border-radius: 999px;
        font-size: 11px;
        line-height: 1;
        text-transform: capitalize;
        white-space: nowrap;
        background: #45475d;
        border: 1px solid rgba(146, 148, 152, 0.18);
        color: #787b80;
      }
      .status-pill.playing {
        background: var(--surface-primary);
        color: var(--success);
        border-color: var(--border);
      }
      .status-pill.paused {
        background: var(--surface-primary);
        color: #d6b36a;
        border-color: var(--border);
      }
      .status-pill.not_running {
        background: var(--danger-bg);
        color: var(--danger);
      }
      .title {
        font-size: 24px;
        font-weight: 700;
        line-height: 1.08;
      }
      .meta {
        margin-top: 4px;
        font-size: 15px;
        color: var(--muted);
      }
    </style>
  </head>
  <body>
    <div class="frame">
      <img id="cover" alt="">
      <div class="copy">
        <div class="topline">
          <div class="brand">TideReader</div>
          <div class="status-pill not_running" id="status">not running</div>
        </div>
        <div class="title" id="title">Waiting for TIDAL</div>
        <div class="meta" id="meta">Artist - Album</div>
      </div>
    </div>
    <script>
      async function refresh() {
        const response = await fetch('/nowplaying.json', { cache: 'no-store' });
        const data = await response.json();
        const status = String(data.status || 'not_running');
        const statusEl = document.getElementById('status');
        statusEl.textContent = status.replaceAll('_', ' ');
        statusEl.className = 'status-pill ' + status;
        document.getElementById('title').textContent = data.title || 'Waiting for TIDAL';
        document.getElementById('meta').textContent = [data.artist, data.album].filter(Boolean).join(' - ') || 'Artist - Album';
        const cover = document.getElementById('cover');
        if (data.artworkPath) {
          cover.src = '/cover.jpg?ts=' + Date.now();
        } else {
          cover.removeAttribute('src');
        }
      }
      refresh();
      setInterval(refresh, 1000);
    </script>
  </body>
</html>
""";
}
