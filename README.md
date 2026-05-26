# TideReader

Windows desktop app for detecting the current TIDAL track and publishing OBS-friendly now-playing outputs.

## Stack

- C# / .NET 10
- ASP.NET Core backend
- WPF + WebView2 desktop host
- React + TypeScript frontend
- Windows desktop target

## Current MVP Scope

- Poll current playback state about once per second
- Detect TIDAL track data from the Windows media session API first
- Fall back to the TIDAL window title when enabled
- Accept optional manual debug input
- Show current state in the desktop UI
- Write OBS output files only when values change
- Serve an optional local overlay and local `nowplaying.json`

## Project Layout

- `backend/TideReader.Backend/`: Windows-native backend for media detection, settings, file output, overlay, and logging
- `desktop/TideReader.Desktop/`: WPF host, tray behavior, backend startup, and WebView2 shell
- `frontend/`: React frontend using the local backend HTTP API
- `docs/desktop-host.md`: desktop runtime and packaging notes
- `TideReader.slnx`: .NET solution entrypoint

## Development Setup

1. Install the .NET 10 SDK.
2. Install frontend dependencies:

```bash
cd frontend
npm install
```

3. Run the frontend tests:

```bash
cd frontend
npm test
```

4. Build the frontend:

```bash
cd frontend
npm run build
```

5. Run the backend tests:

```bash
dotnet test backend/TideReader.Backend.Tests/TideReader.Backend.Tests.csproj -c Release
```

6. Run the local backend coverage gate:

```bash
powershell -ExecutionPolicy Bypass -File .\scripts\test-backend-coverage.ps1
```

7. Build the full solution:

```bash
dotnet build TideReader.slnx -c Release
```

## Desktop Development Mode

If you want the desktop shell to use the Vite dev server instead of the production bundle:

1. Start the frontend dev server in `frontend/` with `npm run dev`
2. Set `TIDAL_DESKTOP_DEV_SERVER_URL=http://127.0.0.1:5173`
3. Run `dotnet run --project desktop/TideReader.Desktop`

## Local Quality Gates

Run these before packaging:

```bash
cd frontend
npm run test:coverage
```

```bash
powershell -ExecutionPolicy Bypass -File .\scripts\test-backend-coverage.ps1
```

- Frontend coverage is enforced through Vitest across `src/**/*.ts` and `src/**/*.tsx`, excluding test files, test setup, and `vite-env.d.ts`.
- The current frontend thresholds are `90%` lines/functions/statements and `85%` branch coverage.
- Backend coverage is enforced through `scripts/test-backend-coverage.ps1`.
- The current backend threshold is `85%` line coverage and `73%` branch coverage for the backend assembly.

## GitHub Actions

- `Quality Gates`: runs on every push and pull request, enforces the frontend coverage gate, backend coverage gate, and a release build of `TideReader.slnx`.
- `Release`: runs on `v*` tags or manual dispatch, reruns the quality gates, packages the desktop app, and publishes both a zip and an installer as release assets.

Use the `quality-gates` job as the required status check for branch protection.

## Publish

For a local Windows publish folder:

```bash
powershell -ExecutionPolicy Bypass -File .\scripts\publish-desktop.ps1
```

Optional flags:

```bash
powershell -ExecutionPolicy Bypass -File .\scripts\publish-desktop.ps1 -Version 0.1.0
powershell -ExecutionPolicy Bypass -File .\scripts\publish-desktop.ps1 -Runtime win-x64 -SelfContained
```

The publish output contains the desktop executable, the in-process backend, and the bundled React frontend under `frontend-dist/`. Each run publishes to a versioned folder such as `artifacts/publish/win-x64-20260525-104500/`.

## Release Packaging

To create both a release zip and a Windows installer locally:

1. Install Inno Setup 6.
2. Run:

```bash
powershell -ExecutionPolicy Bypass -File .\scripts\package-release.ps1 -Version 0.1.0
```

Optional flags:

```bash
powershell -ExecutionPolicy Bypass -File .\scripts\package-release.ps1 -Version 0.1.0 -Runtime win-x64
powershell -ExecutionPolicy Bypass -File .\scripts\package-release.ps1 -Version 0.1.0 -SelfContained
powershell -ExecutionPolicy Bypass -File .\scripts\package-release.ps1 -Version 0.1.0 -InnoSetupCompilerPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

This creates:

- `artifacts/release/<runtime>-<version>/TideReader-<version>-<runtime>.zip`
- `artifacts/release/<runtime>-<version>/TideReader-<version>-<runtime>-Setup.exe`

The GitHub `Release` workflow produces the same two deliverables and attaches them to the GitHub Release.

## Local Ports

- Backend API: `http://127.0.0.1:17656`
- Overlay server: `http://127.0.0.1:17655`

## OBS Setup Examples

### Text Sources

Point OBS text sources at:

- `title.txt`
- `artist.txt`
- `album.txt`
- `track.txt`
- `status.txt`

### Image Source

Point an OBS image source at `cover.jpg`.

### Browser Source

Enable the local overlay and point an OBS browser source at:

- `http://127.0.0.1:17655/overlay`

## Output Files

The app writes these files to the configured output folder:

- `nowplaying.json`
- `title.txt`
- `artist.txt`
- `album.txt`
- `track.txt`
- `status.txt`
- `cover.jpg` when artwork is available

## Overlay

When enabled, the local server exposes:

- `/overlay`
- `/nowplaying.json`
- `/cover.jpg`

Default port: `17655`

## Troubleshooting Detection

- If TIDAL is not publishing a media session, enable the window title fallback.
- If both automatic methods fail, use manual debug input to test the output pipeline.
- If the output folder is unavailable, the app will keep polling and log write failures.
- If the overlay port is occupied, disable overlay or choose another port.
- If launch-at-startup is enabled from a transient development run, the stored Run entry may not point at the final packaged executable.

## Privacy

The app only reads local now-playing information available from the Windows session and writes local output files or a local overlay. It does not capture account credentials or inspect private TIDAL library data.
