# TideReader 1.0.0 Release Validation

Candidate validation date: 2026-11-07 (America/New_York). This records automated evidence, not a claim that every hardware/account/OS combination has been manually tested.

## Local automated verification

- Frontend: 72 tests passed with coverage (92.14% lines; configured frontend thresholds passed); TypeScript/Vite production build passed.
- Native Windows backend: 217 tests; 86.82% lines / 75.25% branches (gate 85% / 73%). Desktop: 3 tests. Release solution build: zero warnings/errors. NuGet audit includes all four projects and transitive packages.
- Frontend `npm audit`: zero reported vulnerabilities.
- Dependency scans reported no known vulnerabilities in the tested dependency graph. This is a point-in-time result, not a guarantee against unknown issues.
- Release workflow YAML passed actionlint. Version metadata and current API examples are aligned to 1.0.0; SIP protocol remains version 1.

## Packaging validation

Before pushing `v1.0.0`, run the release workflow manually with publishing disabled. Its packaging/SIP smoke steps must pass on the candidate revision. Tag workflows repeat the release checks and publish assets only after their prerequisites pass. GitHub Actions run history and artifacts provide the packaging evidence for the exact commit.

## Validation boundaries

Tests use isolated fixtures/source mirrors. Local validation did not launch the production app, change personal configuration/credentials, install over a production copy, or modify live OBS/Twitch/media state. Manual first-install/upgrade acceptance, real OBS scenes/rewards, browser/media behavior and OS trust prompts require an interactive platform check; automated tests do not establish those results.

The release workflow may use isolated app instances for SIP smoke. TideReader history is in memory and resets on restart. The default package requires .NET 10 Desktop Runtime and WebView2; signing is conditional on configured release secrets.
