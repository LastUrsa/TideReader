# Desktop Host Architecture

## Why React Stays

The React frontend already talks to the backend over a clean local HTTP API boundary. That separation lets the desktop host change without forcing a UI rewrite. Keeping React avoids redoing the current settings, status, and debug surface while the runtime and packaging model are still being established.

## Why WPF + WebView2

The app is Windows-only today. The backend already depends on Windows-specific platform features such as media session detection, Windows startup registration, and native folder dialogs. WPF plus WebView2 gives the project a native Windows process, tray integration, predictable lifecycle control, and a simple way to host the existing web UI without reintroducing Go or Wails.

## Runtime Shape

There are three layers:

- `desktop/TideReader.Desktop`: native Windows host, tray behavior, startup ownership, and WebView2 shell
- `backend/TideReader.Backend`: in-process ASP.NET Core backend bound to `127.0.0.1:17656`
- `frontend/`: React app built to static files for production

The desktop host starts the backend in-process by calling the shared backend host builder. In production, the backend serves the built frontend files from `frontend-dist`, and WebView2 loads `http://127.0.0.1:17656/`. The React app then calls the backend with relative `/api/*` requests on the same origin.

## Development Flow

Development keeps the HTTP API boundary but allows the UI to run from Vite.

- Start the backend alone with `dotnet run --project backend/TideReader.Backend`
- Or start the desktop host and set `TIDAL_DESKTOP_DEV_SERVER_URL=http://127.0.0.1:5173`
- Run `npm run dev` in `frontend/`

When `TIDAL_DESKTOP_DEV_SERVER_URL` is set, WebView2 navigates to that URL and the backend enables CORS for that origin only. Production does not depend on Vite.

Set `TIDEREADER_KEEP_WINDOW_VISIBLE=1` while iterating on the desktop shell when the window needs to stay visible. With that flag enabled, minimize and close requests do not hide the app to the tray.

## Production Flow

Production requires a frontend build before publishing the desktop app.

1. Build the frontend with `npm run build` in `frontend/`
2. Publish the desktop host
3. Launch the desktop executable

The desktop project copies `frontend/dist` into the publish output as `frontend-dist`. On launch, the desktop app starts the backend, syncs startup-on-login behavior, opens the WPF shell, and keeps running in the tray when minimized or closed.

## Settings Validation

The React settings UI validates numeric fields before sending changes to the backend, including overlay port range, poll interval, browser source cooldown, and stale-session timeout. The save button is disabled while these values are invalid.

The backend repeats the important checks before persisting settings. User-initiated saves reject invalid overlay ports and verify that the overlay listener can be configured on the requested port before writing `settings.json`. This keeps a failed or occupied overlay port from replacing the last working configuration. During startup, invalid persisted settings are still normalized where possible so TideReader can recover from older or manually edited settings files.

## Service Mode

`TideReader.exe --service` starts the same in-process backend and SIP listener without opening the main window. Service startup verifies that the backend health endpoint on `127.0.0.1:17656` and one SIP discovery port in `47030-47039` become reachable. If either endpoint does not become ready, the desktop process logs the startup failure and exits instead of remaining alive without usable local APIs.

Early startup diagnostics are written to `%APPDATA%\TideReader\logs\startup.log`, including launch argument parsing, single-instance ownership, settings load, backend host build/start, and service readiness checks. Runtime backend and SIP logs continue to use `%APPDATA%\TideReader\logs\bridge.log`.

## Metadata Enrichment

TIDAL playback data reaches the app through Windows media-session APIs first. That path is authoritative for current playback state, but it does not consistently include album or artwork metadata. The app therefore supports configurable enrichment modes in settings:

- `Off`: trust direct detection only
- `MusicBrainzOnly`: use MusicBrainz for missing album and artwork
- `MusicBrainzWithFallbacks`: use MusicBrainz first, then fallback providers when needed

The backend logs which provider supplied metadata so track-level issues can be diagnosed from the AppData log file without attaching a debugger.
