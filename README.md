# TideReader

Windows desktop app for publishing now-playing data and an OBS-friendly overlay from Windows media sessions.

## Highlights

- Detects TIDAL desktop playback and browser media sessions from Chrome, Edge, and Firefox.
- Publishes text, JSON, artwork, and browser-overlay outputs for OBS workflows.
- Includes overlay styling, Smart Text overflow handling, color picking, live preview, saved profiles, source selection, browser-session diagnostics, and update checks.
- Exposes a localhost-only SIP v1 API for same-machine integrations such as LivePanel.

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

Run SIP Newman smoke checks against a service-mode desktop build:

```bash
./scripts/test-sip-newman.sh
```

The release workflow runs the SIP Newman smoke before packaging release artifacts.

For desktop UI work, run `npm run dev` in `frontend/`, set `TIDAL_DESKTOP_DEV_SERVER_URL=http://127.0.0.1:5173`, then run `dotnet run --project desktop/TideReader.Desktop`. Set `TIDEREADER_KEEP_WINDOW_VISIBLE=1` to keep close/minimize actions from sending the app to the tray.

## Quality Gates

- Frontend coverage: at least `90%` statements/functions/lines and `85%` branches.
- Backend coverage: at least `85%` lines and `73%` branches.
- Frontend dependency audit: `npm audit --audit-level=high`.
- Release build: `dotnet build TideReader.slnx -c Release`.
- SIP contract smoke: `./scripts/test-sip-newman.sh`.

## Publish

Create a local publish folder:

```bash
powershell -ExecutionPolicy Bypass -File .\scripts\publish-desktop.ps1 -Version 0.6.0
```

The publish output includes `TideReader.exe`, `TideReader.Backend.exe`, and bundled frontend assets under `frontend-dist/`.

Create release artifacts:

```bash
powershell -ExecutionPolicy Bypass -File .\scripts\package-release.ps1 -Version 0.6.0
```

Signed releases require an Authenticode code-signing certificate. For GitHub Releases, configure `WINDOWS_CODESIGN_PFX_BASE64` with a base64-encoded PFX and `WINDOWS_CODESIGN_PFX_PASSWORD` with its password. Local release packaging can sign with either `-SigningCertificatePfxPath` or `-SigningCertificateThumbprint`.

When packaging from WSL, copy or check out the repo to a Windows-local path first, such as `C:\Temp\TideReaderBuild`, then run the packaging script there. Windows PowerShell and `npm` do not reliably build from a `\\wsl.localhost\...` UNC working directory.

The installer follows the Starsong Installer Standard: fresh Windows installs default to `%ProgramFiles%\Starsong Tools\TideReader`, while existing installations upgrade in place when Inno Setup detects the prior app location.

## Runtime Outputs

The configured output folder receives `nowplaying.json`, `title.txt`, `artist.txt`, `album.txt`, `track.txt`, `status.txt`, and `cover.jpg` when artwork is available.

The overlay server defaults to `http://127.0.0.1:17655/overlay`; the backend API defaults to `http://127.0.0.1:17656`.

Overlay text fields support Smart Text overflow modes per profile: default clipping, horizontal scrolling, two-line wrapping, and automatic font sizing. Character limits still apply first, so set a field's character limit to `0` when the full title, artist, or album should be handled by Smart Text.

## Settings Safety

TideReader validates user-entered settings before saving. Overlay ports must be in the TCP range `1-65535`, polling must be at least `250` ms, browser source cooldowns cannot be negative, and stale-session timeouts must be positive. If the overlay cannot be started on the requested port, the save fails and the previous settings remain active.

Older persisted settings are still normalized on startup where possible. For example, an invalid persisted overlay port falls back to `17655` so the app can recover from stale or hand-edited settings files.

## Notes

- Browser support is metadata-driven and depends on what Windows media sessions expose.
- For TIDAL desktop playback, Windows media sessions provide the authoritative title and playback state. When the TIDAL window title exposes a richer artist credit for the same track, TideReader uses that artist list for outputs and overlays.
- The app only reads local now-playing session data and writes local outputs. It does not use browser extensions, scraping, account auth, or playback controls.
