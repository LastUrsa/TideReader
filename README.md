# TideReader

Windows desktop app for publishing now-playing data and an OBS-friendly overlay from Windows media sessions.

TideReader supports:
- TIDAL desktop playback
- Browser media playback through Windows media sessions
- Chrome, Edge, and Firefox
- Best-effort browser sources including YouTube, YouTube Music, SoundCloud, Bandcamp, and generic browser playback

## Stack

- .NET 10
- ASP.NET Core backend
- WPF + WebView2 desktop host
- React + TypeScript frontend

## What It Does

- Detects active playback from Windows media sessions
- Falls back to the TIDAL window title when enabled
- Normalizes metadata into a shared now-playing model
- Publishes OBS-friendly text, JSON, image, and browser-overlay outputs
- Supports overlay styling, live preview, and persisted settings
- Includes browser-session debugging, source selection, and update checks
- Uses a Starsong-aligned compact shell with branded app framing and focused playback status

## Project Layout

- `backend/TideReader.Backend/` backend detection, settings, outputs, overlay, logging
- `desktop/TideReader.Desktop/` Windows host, tray behavior, startup integration, WebView2 shell
- `frontend/` React settings UI and app shell
- `docs/desktop-host.md` desktop runtime and packaging notes
- `TideReader.slnx` solution entrypoint

## Development

Install the .NET 10 SDK, then install frontend dependencies:

```bash
cd frontend
npm install
```

Useful local commands:

```bash
cd frontend
npm test
npm run build
npm run test:coverage
```

```bash
dotnet test backend/TideReader.Backend.Tests/TideReader.Backend.Tests.csproj -c Release
dotnet build TideReader.slnx -c Release
powershell -ExecutionPolicy Bypass -File .\scripts\test-backend-coverage.ps1
```

For desktop development against the Vite dev server:

1. Run `npm run dev` in `frontend/`
2. Set `TIDAL_DESKTOP_DEV_SERVER_URL=http://127.0.0.1:5173`
3. Run `dotnet run --project desktop/TideReader.Desktop`

## Quality Gates

The repo quality gate requires:

- Frontend coverage: `90%` statements/functions/lines and `85%` branches
- Frontend dependency audit: `npm audit --audit-level=high`
- Backend coverage: `85%` lines and `73%` branches
- Release build of `TideReader.slnx`

Local gate commands:

```bash
cd frontend
npm run test:coverage
npm audit --audit-level=high
```

```bash
powershell -ExecutionPolicy Bypass -File .\scripts\test-backend-coverage.ps1
dotnet build TideReader.slnx -c Release
```

## Publish

Create a local publish folder with:

```bash
powershell -ExecutionPolicy Bypass -File .\scripts\publish-desktop.ps1
```

Optional examples:

```bash
powershell -ExecutionPolicy Bypass -File .\scripts\publish-desktop.ps1 -Version 0.2.0
powershell -ExecutionPolicy Bypass -File .\scripts\publish-desktop.ps1 -Runtime win-x64 -SelfContained
```

The publish output includes:
- `TideReader.Desktop.exe`
- `TideReader.Backend.exe`
- bundled frontend under `frontend-dist/`

## Outputs

The app writes these files to the configured output folder:

- `nowplaying.json`
- `title.txt`
- `artist.txt`
- `album.txt`
- `track.txt`
- `status.txt`
- `cover.jpg` when artwork is available

When overlay is enabled, the local overlay server exposes:

- `http://127.0.0.1:17655/overlay`
- `http://127.0.0.1:17655/nowplaying.json`
- `http://127.0.0.1:17655/overlay-settings.json`
- `http://127.0.0.1:17655/cover.jpg`

Backend API default:
- `http://127.0.0.1:17656`

## Notes

- Browser support is metadata-driven and uses Windows media-session data only.
- Bandcamp support is best effort within current scope because metadata quality depends on what the browser exposes to Windows.
- The app only reads local now-playing session data and writes local outputs. It does not use browser extensions, scraping, account auth, or playback controls.
