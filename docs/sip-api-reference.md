# TideReader SIP API Reference

TideReader exposes SIP v1.1 for local Starsong module integration. This API is intended for same-machine tools such as LivePanel, not public network use.

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
- No authentication in SIP v1.1
- No generic command or action endpoints
- `POST /api/v1/profile` requires `Content-Type: application/json`
- Unknown request fields are rejected
- SIP request bodies are limited to 4096 bytes
- Responses include no-store and browser hardening headers

SIP is separate from TideReader's desktop UI API on `127.0.0.1:17656`.

## Security Posture

SIP is a local integration API. It is intentionally narrow and does not expose profile CRUD, individual overlay settings, playback controls, filesystem paths, credentials, or generic command execution.

Current safeguards:

- SIP binds to `127.0.0.1`, not a public interface.
- Requests with non-localhost `Host` headers are rejected.
- CORS is not enabled for the SIP listener.
- JSON `POST` requests reject unknown fields.
- The only mutating SIP endpoint activates an existing overlay profile by name.
- The SIP listener enforces a small request-body limit.
- Responses include `Cache-Control: no-store`, `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'; base-uri 'none'`, `Referrer-Policy: no-referrer`, and `X-Frame-Options: DENY`.

SIP v1.1 does not define authentication. If future endpoints expose higher-impact actions or data, add a fresh security review before implementation.

## Endpoints

### GET `/api/v1/app`

Returns application identity and runtime mode.

Response:

```json
{
  "appId": "tidereader",
  "name": "TideReader",
  "version": "0.4.0",
  "mode": "standalone",
  "protocolVersion": "1.1"
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
  "supportsProfiles": true,
  "supportsStatusReporting": true
}
```

Consumers should ignore unknown future capability flags.

### GET `/api/v1/status`

Returns lightweight runtime status. This endpoint does not expose full application settings.

Response:

```json
{
  "state": "idle",
  "message": "Waiting for TIDAL",
  "healthy": true,
  "activeProfile": "Default",
  "activeProfileId": "default"
}
```

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

## Errors

Standard error response:

```json
{
  "success": false,
  "error": "Profile not found"
}
```

Common statuses:

- `400 Bad Request`: invalid JSON, unknown request fields, or empty profile name
- `403 Forbidden`: non-localhost host header
- `404 Not Found`: requested profile does not exist
- `413 Payload Too Large`: request body exceeds the SIP body limit
- `415 Unsupported Media Type`: missing or non-JSON `Content-Type` for profile activation
