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
        --text: #ebebeb;
        --muted: #929498;
        --success: #89c89d;
        --warning: #d6b36a;
        --danger: #720201;
        --shadow: rgba(0, 0, 0, 0.32);
        --art-gradient-start: rgba(98, 122, 150, 0.42);
        --art-gradient-end: rgba(35, 37, 56, 0.92);
        --art-sheen-top: rgba(235, 235, 235, 0.08);
        --art-sheen-bottom: rgba(235, 235, 235, 0.02);
      }
      * { box-sizing: border-box; }
      body {
        margin: 0;
        background: transparent;
        color: var(--text);
        font-family: "Segoe UI", sans-serif;
      }
      .frame {
        --overlay-gap: 14px;
        --overlay-padding: 14px;
        --overlay-radius: 18px;
        --overlay-border-width: 1px;
        --overlay-border-color: rgba(146, 148, 152, 1);
        --overlay-bg: rgba(50, 51, 79, 0.86);
        --image-size: 68px;
        display: inline-flex;
        align-items: center;
        gap: var(--overlay-gap);
        min-width: 360px;
        padding: var(--overlay-padding);
        position: relative;
        isolation: isolate;
      }
      .frame::before {
        content: "";
        position: absolute;
        inset: 0;
        border-radius: var(--overlay-radius);
        background: var(--overlay-bg);
        border: var(--overlay-border-width) solid var(--overlay-border-color);
        box-shadow: 0 18px 42px var(--shadow);
        backdrop-filter: blur(18px);
        z-index: -1;
      }
      .cover-shell {
        display: grid;
        place-items: center;
        width: var(--image-size);
        min-width: var(--image-size);
        height: var(--image-size);
        overflow: hidden;
        border-radius: 0;
        background:
          linear-gradient(135deg, var(--art-gradient-start), var(--art-gradient-end)),
          linear-gradient(180deg, var(--art-sheen-top), var(--art-sheen-bottom));
      }
      .cover-shell.has-artwork {
        background: transparent;
      }
      img {
        display: block;
        width: 100%;
        height: 100%;
        object-fit: cover;
        border-radius: 0;
      }
      .cover-placeholder {
        font-size: 13px;
        font-weight: 700;
        letter-spacing: .12em;
      }
      .copy {
        min-width: 0;
        flex: 1;
        text-align: left;
      }
      .frame[data-text-align='center'] .copy {
        text-align: center;
      }
      .frame[data-text-align='right'] .copy {
        text-align: right;
      }
      .frame[data-image-position='right'] .cover-shell {
        order: 2;
      }
      .frame[data-image-position='right'] .copy {
        order: 1;
      }
      .topline {
        display: flex;
        align-items: center;
        gap: 10px;
        margin-bottom: 4px;
      }
      .frame[data-text-align='center'] .topline {
        justify-content: center;
      }
      .frame[data-text-align='right'] .topline {
        justify-content: flex-end;
      }
      .brand {
        color: var(--text);
        font-size: 12px;
        font-weight: 600;
        letter-spacing: .02em;
      }
      .status-pill {
        --pill-bg: rgba(69, 71, 93, 1);
        --pill-text: #787b80;
        position: relative;
        display: inline-block;
        padding: 4px 9px;
        border-radius: 999px;
        font-size: 11px;
        line-height: 1;
        text-transform: capitalize;
        white-space: nowrap;
        color: var(--pill-text);
        isolation: isolate;
      }
      .status-pill::before {
        content: "";
        position: absolute;
        inset: 0;
        border-radius: inherit;
        background: var(--pill-bg);
        z-index: -1;
      }
      .status-pill.playing {
        color: var(--success);
      }
      .status-pill.paused {
        color: var(--warning);
      }
      .status-pill.not_running {
        color: var(--danger);
      }
      .title {
        margin: 0;
        line-height: 1.08;
      }
      .artist,
      .album {
        margin-top: 4px;
        line-height: 1.2;
      }
    </style>
  </head>
  <body>
    <div class="frame" data-image-position="left" data-text-align="left">
      <div class="cover-shell" id="cover-shell">
        <img id="cover" alt="">
        <span class="cover-placeholder" id="cover-placeholder">ART</span>
      </div>
      <div class="copy">
        <div class="topline" id="topline">
          <div class="brand" id="brand">TideReader</div>
          <div class="status-pill not_running" id="status">Not running</div>
        </div>
        <div class="title" id="title">Waiting for playback</div>
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
        overlayContainerStyle: {
          backgroundMode: 'solid',
          backgroundColorHex: '#32334F',
          gradient: {
            colorCount: 3,
            preset: 'Diagonal',
            color1Hex: '#1F1F2E',
            color2Hex: '#6B46C1',
            color3Hex: '#111827',
            angleDeg: 135
          },
          opacity: 0.86,
          cornerRadiusPx: 18,
          paddingPx: 14,
          gapPx: 14,
          borderEnabled: true,
          borderColorHex: '#929498',
          borderWidthPx: 1
        },
        statusPillStyle: {
          backgroundColorHex: '#45475D',
          textColorHex: '#787B80',
          opacity: 1,
          fontFamily: 'Segoe UI',
          fontSizePx: 11,
          bold: false,
          italic: false,
          underline: false,
          cornerRadiusPx: 999,
          paddingHorizontalPx: 9,
          paddingVerticalPx: 4
        },
        imagePosition: 'Left',
        textAlign: 'Left',
        showAppName: true,
        showPlaybackState: true
      };

      function normalizeHex(value, fallback) {
        const next = String(value || '').trim();
        return /^#([0-9a-f]{3}|[0-9a-f]{6})$/i.test(next) ? next : fallback;
      }

      function withAlpha(hexColor, opacity) {
        const normalized = normalizeHex(hexColor, '#000000');
        const expanded = normalized.length === 4
          ? '#' + normalized[1] + normalized[1] + normalized[2] + normalized[2] + normalized[3] + normalized[3]
          : normalized;
        const red = parseInt(expanded.slice(1, 3), 16);
        const green = parseInt(expanded.slice(3, 5), 16);
        const blue = parseInt(expanded.slice(5, 7), 16);
        return 'rgba(' + red + ', ' + green + ', ' + blue + ', ' + opacity + ')';
      }

      function backgroundFromSettings(style) {
        if (String(style.backgroundMode || 'solid').toLowerCase() !== 'gradient') {
          return withAlpha(style.backgroundColorHex, style.opacity);
        }

        const gradient = style.gradient || {};
        const color1 = withAlpha(gradient.color1Hex, style.opacity);
        const color2 = withAlpha(gradient.color2Hex, style.opacity);
        const color3 = withAlpha(gradient.color3Hex, style.opacity);
        const angle = Number.isFinite(Number(gradient.angleDeg)) ? Number(gradient.angleDeg) : 135;
        const colorCount = Number(gradient.colorCount) === 2 ? 2 : 3;
        const stops = colorCount === 2
          ? [color1, color2]
          : [color1, color2, color3];
        const joinedStops = stops.join(', ');

        switch (gradient.preset) {
          case 'Linear Left to Right':
            return 'linear-gradient(90deg, ' + joinedStops + ')';
          case 'Linear Top to Bottom':
            return 'linear-gradient(180deg, ' + joinedStops + ')';
          case 'Reverse Diagonal':
            return 'linear-gradient(45deg, ' + joinedStops + ')';
          case 'Soft Radial':
            return colorCount === 2
              ? 'radial-gradient(circle, ' + color1 + ' 0%, ' + color2 + ' 100%)'
              : 'radial-gradient(circle, ' + color1 + ' 0%, ' + color2 + ' 50%, ' + color3 + ' 100%)';
          case 'Spotlight':
            return colorCount === 2
              ? 'radial-gradient(circle at top left, ' + color1 + ' 0%, ' + color2 + ' 100%)'
              : 'radial-gradient(circle at top left, ' + color1 + ' 0%, ' + color2 + ' 45%, ' + color3 + ' 100%)';
          case 'Stream Neon':
            return colorCount === 2
              ? 'linear-gradient(120deg, ' + color1 + ' 0%, ' + color2 + ' 100%)'
              : 'linear-gradient(120deg, ' + color1 + ' 0%, ' + color2 + ' 50%, ' + color3 + ' 100%)';
          case 'Subtle Glass':
            return colorCount === 2
              ? 'linear-gradient(135deg, ' + color1 + ' 0%, ' + color2 + ' 100%)'
              : 'linear-gradient(135deg, ' + color1 + ' 0%, ' + color2 + ' 60%, ' + color3 + ' 100%)';
          case 'Diagonal':
          default:
            return 'linear-gradient(' + angle + 'deg, ' + joinedStops + ')';
        }
      }

      function formatStatus(status) {
        const next = String(status || 'not_running').replaceAll('_', ' ');
        return next.charAt(0).toUpperCase() + next.slice(1);
      }

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

      function getAlbumDisplayText(data, fallback) {
        const album = String(data.album || '').trim();
        if (album) {
          return album;
        }

        if (String(data.provider || '') === 'browser' && String(data.title || '').trim()) {
          if (isMetadataLimitedBrowserSession(data)) {
            return 'Metadata limited';
          }

          switch (String(data.site || '')) {
            case 'youtubeMusic':
              return 'Music playback';
            case 'youtube':
              return 'Video playback';
            case 'soundcloud':
              return 'Stream playback';
            default:
              return 'Browser playback';
          }
        }

        return fallback;
      }

      function getArtistDisplayText(data, fallback) {
        const artist = String(data.artist || '').trim();
        if (artist) {
          return artist;
        }

        if (String(data.provider || '') === 'browser' && String(data.title || '').trim()) {
          const source = String(data.source || '').trim();
          if (source) {
            return source;
          }

          switch (String(data.site || '')) {
            case 'youtubeMusic':
              return 'YouTube Music';
            case 'youtube':
              return 'YouTube';
            case 'bandcamp':
              return 'Bandcamp';
            case 'soundcloud':
              return 'SoundCloud';
            default:
              return 'Browser';
          }
        }

        return fallback;
      }

      function isMetadataLimitedBrowserSession(data) {
        return String(data.provider || '') === 'browser'
          && String(data.site || '') === 'bandcamp'
          && String(data.title || '').trim().length > 0
          && String(data.album || '').trim().length === 0;
      }

      function mergeSettings(settings) {
        const next = settings || {};
        return {
          ...defaultSettings,
          ...next,
          songTextStyle: { ...defaultSettings.songTextStyle, ...(next.songTextStyle || {}) },
          artistTextStyle: { ...defaultSettings.artistTextStyle, ...(next.artistTextStyle || {}) },
          albumTextStyle: { ...defaultSettings.albumTextStyle, ...(next.albumTextStyle || {}) },
          overlayContainerStyle: {
            ...defaultSettings.overlayContainerStyle,
            ...(next.overlayContainerStyle || {}),
            backgroundColorHex: (next.overlayContainerStyle || {}).backgroundColorHex || next.backgroundColorHex || defaultSettings.overlayContainerStyle.backgroundColorHex,
            gradient: {
              ...defaultSettings.overlayContainerStyle.gradient,
              ...((next.overlayContainerStyle || {}).gradient || {})
            }
          },
          statusPillStyle: { ...defaultSettings.statusPillStyle, ...(next.statusPillStyle || {}) }
        };
      }

      function applyOverlaySettings(settings) {
        const next = mergeSettings(settings);
        const frame = document.querySelector('.frame');
        const copy = document.querySelector('.copy');
        const topline = document.getElementById('topline');
        const brand = document.getElementById('brand');
        const statusEl = document.getElementById('status');
        const coverShell = document.getElementById('cover-shell');
        const containerStyle = next.overlayContainerStyle;
        const pillStyle = next.statusPillStyle;

        frame.dataset.imagePosition = String(next.imagePosition || 'Left').toLowerCase();
        frame.dataset.textAlign = String(next.textAlign || 'Left').toLowerCase();
        frame.style.setProperty('--overlay-gap', containerStyle.gapPx + 'px');
        frame.style.setProperty('--overlay-padding', containerStyle.paddingPx + 'px');
        frame.style.setProperty('--overlay-radius', containerStyle.cornerRadiusPx + 'px');
        frame.style.setProperty('--overlay-border-width', (containerStyle.borderEnabled ? containerStyle.borderWidthPx : 0) + 'px');
        frame.style.setProperty('--overlay-border-color', normalizeHex(containerStyle.borderColorHex, '#929498'));
        frame.style.setProperty('--overlay-bg', backgroundFromSettings(containerStyle));
        frame.style.setProperty('--image-size', next.imageSizePx + 'px');

        statusEl.style.setProperty('--pill-bg', withAlpha(pillStyle.backgroundColorHex, pillStyle.opacity));
        statusEl.style.setProperty('--pill-text', normalizeHex(pillStyle.textColorHex, '#787B80'));
        statusEl.style.fontFamily = pillStyle.fontFamily;
        statusEl.style.fontSize = pillStyle.fontSizePx + 'px';
        statusEl.style.fontWeight = pillStyle.bold ? '700' : '400';
        statusEl.style.fontStyle = pillStyle.italic ? 'italic' : 'normal';
        statusEl.style.textDecoration = pillStyle.underline ? 'underline' : 'none';
        statusEl.style.borderRadius = pillStyle.cornerRadiusPx + 'px';
        statusEl.style.padding = pillStyle.paddingVerticalPx + 'px ' + pillStyle.paddingHorizontalPx + 'px';

        brand.style.display = next.showAppName === false ? 'none' : '';
        statusEl.style.display = next.showPlaybackState === false ? 'none' : '';
        topline.style.display = next.showAppName === false && next.showPlaybackState === false ? 'none' : 'flex';

        applyTextStyle(document.getElementById('title'), next.songTextStyle);
        applyTextStyle(document.getElementById('artist'), next.artistTextStyle);
        applyTextStyle(document.getElementById('album'), next.albumTextStyle);
        copy.style.textAlign = String(next.textAlign || 'Left').toLowerCase();
        coverShell.style.width = next.imageSizePx + 'px';
        coverShell.style.minWidth = next.imageSizePx + 'px';
        coverShell.style.height = next.imageSizePx + 'px';

        return next;
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
        const activeSettings = applyOverlaySettings(settings);
        const status = String(data.status || 'not_running');
        const statusEl = document.getElementById('status');
        statusEl.textContent = formatStatus(status);
        statusEl.className = 'status-pill ' + status;
        document.getElementById('title').textContent = truncateText(data.title, activeSettings.songTextStyle.maxCharacters, 'Waiting for playback');
        document.getElementById('artist').textContent = truncateText(getArtistDisplayText(data, 'Artist unavailable'), activeSettings.artistTextStyle.maxCharacters, 'Artist unavailable');
        document.getElementById('album').textContent = truncateText(getAlbumDisplayText(data, 'Album unavailable'), activeSettings.albumTextStyle.maxCharacters, 'Album unavailable');

        const cover = document.getElementById('cover');
        const coverShell = document.getElementById('cover-shell');
        const placeholder = document.getElementById('cover-placeholder');
        if (data.artworkPath) {
          cover.src = '/cover.jpg?ts=' + Date.now();
          cover.style.display = '';
          coverShell.classList.add('has-artwork');
          coverShell.style.display = '';
          placeholder.style.display = 'none';
        } else if (isMetadataLimitedBrowserSession(data)) {
          cover.removeAttribute('src');
          cover.style.display = 'none';
          coverShell.classList.remove('has-artwork');
          coverShell.style.display = 'none';
          placeholder.style.display = 'none';
        } else {
          cover.removeAttribute('src');
          cover.style.display = '';
          coverShell.classList.remove('has-artwork');
          coverShell.style.display = '';
          placeholder.style.display = '';
        }
      }
      refresh();
      setInterval(refresh, 1000);
    </script>
  </body>
</html>
""";
}
