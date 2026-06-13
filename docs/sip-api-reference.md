# TideReader SIP API Reference

TideReader exposes SIP v1 for local Starsong module integration. This API is intended for same-machine tools such as LivePanel, not public network use.

## Runtime

TideReader starts a dedicated SIP HTTP listener on `127.0.0.1` using the reserved TideReader port range:

```text
47030-47039
```

The first available port is used. In a typical local run, the base URL is:

```text
http://127.0.0.1:47030
```

Supported launch modes:

```text
TideReader.exe
TideReader.exe --service
TideReader.exe --show
```

`--service` starts TideReader without showing the main window and reports SIP mode `service`. A normal launch reports `standalone`. `--show` asks an existing instance to restore its UI, or launches standalone if no instance is running.

## Transport Rules

- JSON request and response bodies
- Localhost only
- No authentication in SIP v1
- No generic command or action endpoints
- `POST /api/v1/profile` and `POST /api/v1/browser-support` require `Content-Type: application/json`
- Unknown request fields are rejected
- SIP request bodies are limited to 4096 bytes
- Responses include no-store and browser hardening headers

SIP is separate from TideReader's desktop UI API on `127.0.0.1:17656`.

## Security Posture

SIP is a local integration API. It is intentionally narrow and does not expose profile CRUD, full overlay profile settings, playback controls, filesystem output folders, log paths, credentials, or generic command execution.

Current safeguards:

- SIP binds to `127.0.0.1`, not a public interface.
- Requests with non-localhost `Host` headers are rejected.
- CORS is not enabled for the SIP listener.
- JSON `POST` requests reject unknown fields.
- Mutating SIP endpoints activate an existing overlay profile by name or toggle browser support.
- The SIP listener enforces a small request-body limit.
- Responses include `Cache-Control: no-store`, `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'; base-uri 'none'`, `Referrer-Policy: no-referrer`, and `X-Frame-Options: DENY`.

SIP v1 does not define authentication. If future endpoints expose higher-impact actions or data, add a fresh security review before implementation.

## Endpoints

### GET `/api/v1/app`

Returns application identity and runtime mode.

Response:

```json
{
  "appId": "tidereader",
  "appName": "TideReader",
  "name": "TideReader",
  "version": "0.5.0",
  "mode": "standalone",
  "protocolVersion": 1,
  "capabilities": [
    "profiles",
    "browser-support"
  ]
}
```

`mode` is either `standalone` or `service`.

### GET `/api/v1/health`

Returns SIP readiness and application health.

Response:

```json
{
  "status": "ready",
  "message": "TideReader operational"
}
```

If TideReader has a recent application error, health may report `degraded` with that message.

### GET `/api/v1/capabilities`

Returns SIP feature flags.

Response:

```json
{
  "protocolVersion": 1,
  "capabilities": [
    "profiles",
    "browser-support"
  ],
  "supportsProfiles": true,
  "supportsStatusReporting": true
}
```

Consumers should use `capabilities` for feature discovery and ignore unknown future capability names. The `supports*` fields are retained for older clients.

### GET `/api/v1/status`

Returns lightweight runtime status, active profile display details, and a compact now-playing preview. This endpoint does not expose full application settings, filesystem output folders, logs, tokens, browser debug sessions, or raw detection debug data.

Response:

```json
{
  "state": "active",
  "message": "Playing Starsong - Signal Bloom",
  "healthy": true,
  "activeProfile": "Starsong Main",
  "activeProfileId": "2033febc-1def-446f-971e-f01cd083aa33",
  "activeProfileName": "Starsong Main",
  "browserSupportEnabled": true,
  "source": "desktop",
  "overlayUrl": "http://127.0.0.1:17655/overlay",
  "overlayEnabled": true,
  "overlayPort": 17655,
  "layout": "Left",
  "albumArtVisible": true,
  "imageSizePx": 68,
  "statusPillVisible": true,
  "backgroundMode": "solid",
  "textAlign": "Left",
  "profileCount": 2,
  "nowPlaying": {
    "status": "playing",
    "title": "Signal Bloom",
    "artist": "Starsong",
    "album": "Local Skies",
    "durationMs": 214000,
    "hasArtwork": true,
    "artworkPath": "cover.jpg",
    "source": "TIDAL",
    "provider": "tidal",
    "browser": "",
    "site": "",
    "confidence": 0.98,
    "metadataSource": "MusicBrainz"
  }
}
```

`layout`, `albumArtVisible`, `imageSizePx`, `statusPillVisible`, `backgroundMode`, and `textAlign` are derived from the active overlay profile. `albumArtVisible` is true when the active profile uses a positive image size. `nowPlaying.hasArtwork` is true when TideReader has artwork bytes or a non-empty artwork path.

`source` reports where active playback metadata is coming from:

- `desktop`: the desktop TIDAL application
- `browser`: a supported browser source
- `none`: no active playback source is available

Known states include:

- `active`: playback is active
- `paused`: playback is paused
- `idle`: TideReader is ready but playback is not active
- `warning`: health is degraded

### GET `/api/v1/profiles`

Returns available overlay profile names.

Response:

```json
{
  "profiles": [
    "Default",
    "Listening Party"
  ]
}
```

Only profile names are returned. Profile settings and CRUD operations remain TideReader UI responsibilities.

### GET `/api/v1/profile/current`

Returns the active overlay profile.

Response:

```json
{
  "id": "default",
  "name": "Default"
}
```

Empty state:

```json
{
  "id": "",
  "name": ""
}
```

### POST `/api/v1/profile`

Activates an existing overlay profile by name.

Request:

```json
{
  "profile": "Listening Party"
}
```

Response:

```json
{
  "success": true,
  "profile": "Listening Party",
  "profileId": "listening-party"
}
```

Profile names are matched case-insensitively. Activation uses TideReader's existing settings path, so the active profile, preview state, and overlay output update as if the profile were selected through the UI.

### GET `/api/v1/browser-support`

Returns whether TideReader should monitor supported browser playback sources in addition to desktop playback sources.

Response:

```json
{
  "enabled": true
}
```

### POST `/api/v1/browser-support`

Enables or disables browser support. The setting is persisted through TideReader's normal settings path.

Request:

```json
{
  "enabled": false
}
```

Response:

```json
{
  "success": true
}
```

## Errors

Standard error response:

```json
{
  "success": false,
  "error": "Profile not found"
}
```

Common statuses:

- `400 Bad Request`: invalid JSON, unknown request fields, empty profile name, or missing browser support state
- `403 Forbidden`: non-localhost host header
- `404 Not Found`: requested profile does not exist
- `413 Payload Too Large`: request body exceeds the SIP body limit
- `415 Unsupported Media Type`: missing or non-JSON `Content-Type` for profile activation

## Newman Smoke Test

Run the automated SIP smoke collection against a service-mode desktop build:

```bash
./scripts/test-sip-newman.sh
```

To test an already running TideReader instance:

```bash
./scripts/test-sip-newman.sh --no-launch --base-url http://127.0.0.1:47030
```
