using System.Net;
using System.Text;
using System.Text.Json;
using TideReader.Backend.Models;

namespace TideReader.Backend.Services;

internal sealed record OverlayResponse(int StatusCode, string ContentType, byte[] Body);

internal static class OverlayResponseBuilder
{
    public static OverlayResponse Build(string path, IPlaybackSnapshotStore snapshotStore, IOverlaySettingsSnapshotStore overlaySettingsSnapshotStore)
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

        if (path.Equals("/overlay-settings.json", StringComparison.OrdinalIgnoreCase))
        {
            var payload = overlaySettingsSnapshotStore.Get();
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
        display: block;
        width: 68px;
        height: 68px;
        object-fit: cover;
        border-radius: 0;
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
      .artist,
      .album {
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
        <div class="artist" id="artist">Artist unavailable</div>
        <div class="album" id="album">Album unavailable</div>
      </div>
    </div>
    <script>
      const defaultSettings = {
        songTextStyle: {
          fontFamily: 'Segoe UI',
          colorHex: '#EBEBEB',
          fontSizePx: 24,
          maxCharacters: 0,
          bold: true,
          italic: false,
          underline: false
        },
        artistTextStyle: {
          fontFamily: 'Segoe UI',
          colorHex: '#929498',
          fontSizePx: 15,
          maxCharacters: 0,
          bold: false,
          italic: false,
          underline: false
        },
        albumTextStyle: {
          fontFamily: 'Segoe UI',
          colorHex: '#929498',
          fontSizePx: 15,
          maxCharacters: 0,
          bold: false,
          italic: false,
          underline: false
        },
        imageSizePx: 68,
        backgroundColorHex: '#32334F',
        imagePosition: 'Left',
        textAlign: 'Left',
        showAppName: true,
        showPlaybackState: true
      };

      function applyTextStyle(element, style) {
        element.style.fontFamily = style.fontFamily;
        element.style.color = style.colorHex;
        element.style.fontSize = style.fontSizePx + 'px';
        element.style.fontWeight = style.bold ? '700' : '400';
        element.style.fontStyle = style.italic ? 'italic' : 'normal';
        element.style.textDecoration = style.underline ? 'underline' : 'none';
      }

      function truncateText(value, maxCharacters, fallback) {
        const next = String(value || fallback);
        const limit = Number(maxCharacters || 0);
        if (!Number.isFinite(limit) || limit <= 0 || next.length <= limit) {
          return next;
        }

        return next.slice(0, limit) + '...';
      }

      function applyOverlaySettings(settings) {
        const next = settings || defaultSettings;
        const frame = document.querySelector('.frame');
        const copy = document.querySelector('.copy');
        const topline = document.querySelector('.topline');
        const brand = document.querySelector('.brand');
        const statusEl = document.getElementById('status');
        const cover = document.getElementById('cover');
        frame.style.background = next.backgroundColorHex;
        statusEl.style.background = next.backgroundColorHex;
        cover.style.width = next.imageSizePx + 'px';
        cover.style.height = next.imageSizePx + 'px';
        cover.style.order = next.imagePosition === 'Right' ? '1' : '0';
        copy.style.order = next.imagePosition === 'Right' ? '0' : '1';
        copy.style.textAlign = String(next.textAlign || 'Left').toLowerCase();
        topline.style.justifyContent = next.textAlign === 'Center'
          ? 'center'
          : next.textAlign === 'Right'
            ? 'flex-end'
            : 'flex-start';
        brand.style.display = next.showAppName === false ? 'none' : '';
        statusEl.style.display = next.showPlaybackState === false ? 'none' : '';
        topline.style.display = next.showAppName === false && next.showPlaybackState === false ? 'none' : 'flex';
        applyTextStyle(document.getElementById('title'), next.songTextStyle);
        applyTextStyle(document.getElementById('artist'), next.artistTextStyle);
        applyTextStyle(document.getElementById('album'), next.albumTextStyle);
      }

      async function refresh() {
        const [nowPlayingResponse, settingsResponse] = await Promise.all([
          fetch('/nowplaying.json', { cache: 'no-store' }),
          fetch('/overlay-settings.json', { cache: 'no-store' })
        ]);

        const data = await nowPlayingResponse.json();
        const settings = settingsResponse.ok
          ? await settingsResponse.json()
          : defaultSettings;
        const activeSettings = settings || defaultSettings;

        applyOverlaySettings(activeSettings);
        const status = String(data.status || 'not_running');
        const statusEl = document.getElementById('status');
        statusEl.textContent = status.replaceAll('_', ' ');
        statusEl.className = 'status-pill ' + status;
        document.getElementById('title').textContent = truncateText(data.title, activeSettings.songTextStyle?.maxCharacters, 'Waiting for TIDAL');
        document.getElementById('artist').textContent = truncateText(data.artist, activeSettings.artistTextStyle?.maxCharacters, 'Artist unavailable');
        document.getElementById('album').textContent = truncateText(data.album, activeSettings.albumTextStyle?.maxCharacters, 'Album unavailable');
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
