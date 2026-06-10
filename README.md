# TideReader

Windows desktop app for publishing now-playing data and an OBS-friendly overlay from Windows media sessions.

## Highlights

- Detects TIDAL desktop playback and browser media sessions from Chrome, Edge, and Firefox.
- Publishes text, JSON, artwork, and browser-overlay outputs for OBS workflows.
- Includes overlay styling, color picking, live preview, saved profiles, source selection, browser-session diagnostics, and update checks.
- Exposes a localhost-only SIP v1.2 API for same-machine integrations such as LivePanel.

## Stack

- .NET 10, ASP.NET Core, WPF, WebView2
- React, TypeScript, Vite

## Project Layout

- `backend/TideReader.Backend/` detection, settings, outputs, overlay, SIP, logging
- `desktop/TideReader.Desktop/` Windows host, tray behavior, startup integration
- `frontend/` React settings UI and overlay shell
- `docs/desktop-host.md` desktop runtime and packaging notes
- `docs/sip-api-reference.md` SIP v1 API reference
- `docs/postman/TideReader-SIP-v1.postman_collection.json` SIP Postman collection
- `TideReader.slnx` solution entrypoint

## Development

Install the .NET 10 SDK and frontend dependencies:

```bash
cd frontend
npm install
```

Useful commands:

```bash
cd frontend
npm test
npm run test:coverage
npm run build
npm audit --audit-level=high
```

```bash
dotnet test backend/TideReader.Backend.Tests/TideReader.Backend.Tests.csproj -c Release
dotnet build TideReader.slnx -c Release
powershell -ExecutionPolicy Bypass -File .\scripts\test-backend-coverage.ps1
```

For desktop UI work, run `npm run dev` in `frontend/`, set `TIDAL_DESKTOP_DEV_SERVER_URL=http://127.0.0.1:5173`, then run `dotnet run --project desktop/TideReader.Desktop`. Set `TIDEREADER_KEEP_WINDOW_VISIBLE=1` to keep close/minimize actions from sending the app to the tray.

## Quality Gates

- Frontend coverage: at least `90%` statements/functions/lines and `85%` branches.
- Backend coverage: at least `85%` lines and `73%` branches.
- Frontend dependency audit: `npm audit --audit-level=high`.
- Release build: `dotnet build TideReader.slnx -c Release`.

## Publish

Create a local publish folder:

```bash
powershell -ExecutionPolicy Bypass -File .\scripts\publish-desktop.ps1 -Version 0.5.0
```

The publish output includes `TideReader.Desktop.exe`, `TideReader.Backend.exe`, and bundled frontend assets under `frontend-dist/`.

## Runtime Outputs

The configured output folder receives `nowplaying.json`, `title.txt`, `artist.txt`, `album.txt`, `track.txt`, `status.txt`, and `cover.jpg` when artwork is available.

The overlay server defaults to `http://127.0.0.1:17655/overlay`; the backend API defaults to `http://127.0.0.1:17656`.

## Notes

- Browser support is metadata-driven and depends on what Windows media sessions expose.
- The app only reads local now-playing session data and writes local outputs. It does not use browser extensions, scraping, account auth, or playback controls.
