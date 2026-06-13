#!/usr/bin/env bash
set -euo pipefail

launch=true
skip_build=false
base_url=""
collection="docs/postman/TideReader-SIP-v1-newman-smoke.postman_collection.json"
timeout_seconds=90
newman_package="newman@6.2.1"
configuration="Release"
runtime="win-x64"
launched_pid=""

to_windows_path() {
    if command -v wslpath >/dev/null 2>&1; then
        wslpath -w "$1"
    elif command -v cygpath >/dev/null 2>&1; then
        cygpath -w "$1"
    else
        powershell.exe -NoProfile -Command "\$ErrorActionPreference = 'Stop'; [System.IO.Path]::GetFullPath('$1')"
    fi
}

cleanup() {
    if [[ -n "$launched_pid" ]]; then
        powershell.exe -NoProfile -Command "\$ErrorActionPreference = 'SilentlyContinue'; Stop-Process -Id ${launched_pid} -Force" >/dev/null 2>&1 || true
    fi
}
trap cleanup EXIT

while [[ $# -gt 0 ]]; do
    case "$1" in
        --no-launch)
            launch=false
            shift
            ;;
        --skip-build)
            skip_build=true
            shift
            ;;
        --base-url)
            if [[ -z "${2:-}" ]]; then
                echo "--base-url requires a value" >&2
                exit 2
            fi
            base_url="$2"
            shift 2
            ;;
        --collection)
            if [[ -z "${2:-}" ]]; then
                echo "--collection requires a value" >&2
                exit 2
            fi
            collection="$2"
            shift 2
            ;;
        --timeout)
            if [[ -z "${2:-}" ]]; then
                echo "--timeout requires a value" >&2
                exit 2
            fi
            timeout_seconds="$2"
            shift 2
            ;;
        *)
            echo "unknown argument: $1" >&2
            exit 2
            ;;
    esac
done

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

if [[ "$launch" == true && "$skip_build" == false ]]; then
    echo "Building frontend..."
    (cd frontend && npm run build)

    echo "Building TideReader desktop host..."
    solution_windows="$(to_windows_path "$repo_root/TideReader.slnx")"
    powershell.exe -NoProfile -Command "\$ErrorActionPreference = 'Stop'; & dotnet build '${solution_windows}' -c '${configuration}'"
fi

if [[ "$launch" == true ]]; then
    exe_path="desktop/TideReader.Desktop/bin/${configuration}/net10.0-windows10.0.19041.0/TideReader.exe"
    if [[ ! -f "$exe_path" ]]; then
        exe_path="desktop/TideReader.Desktop/bin/${configuration}/net10.0-windows10.0.19041.0/${runtime}/TideReader.exe"
    fi
    if [[ ! -f "$exe_path" ]]; then
        echo "TideReader.exe was not found. Run without --skip-build or publish the desktop project first." >&2
        exit 1
    fi

    exe_windows="$(to_windows_path "$repo_root/$exe_path")"
    launched_pid="$(
        powershell.exe -NoProfile -Command "\$ErrorActionPreference = 'Stop'; \$process = Start-Process -FilePath '${exe_windows}' -ArgumentList '--service' -PassThru; \$process.Id" |
            tr -d '\r' |
            head -n 1
    )"
fi

run_newman_on_windows=false
if [[ -z "$base_url" ]]; then
    deadline=$((SECONDS + timeout_seconds))
    while [[ $SECONDS -lt $deadline ]]; do
        for port in {47030..47039}; do
            candidate="http://127.0.0.1:${port}"
            if curl --silent --fail --max-time 1 "${candidate}/api/v1/app" >/dev/null; then
                base_url="$candidate"
                break 2
            fi
        done

        discovered="$(
            powershell.exe -NoProfile -Command '$ErrorActionPreference = "SilentlyContinue"; foreach ($p in 47030..47039) { try { Invoke-RestMethod -Uri "http://127.0.0.1:$p/api/v1/app" -TimeoutSec 1 | Out-Null; Write-Output "http://127.0.0.1:$p"; exit 0 } catch {} }; exit 0' 2>/dev/null |
                tr -d '\r' |
                head -n 1
        )"
        if [[ -n "$discovered" ]]; then
            base_url="$discovered"
            run_newman_on_windows=true
            break
        fi

        sleep 1
    done
fi

if [[ -z "$base_url" ]]; then
    echo "Timed out waiting for TideReader SIP on 127.0.0.1:47030-47039" >&2
    exit 1
fi

echo "Running SIP Newman smoke checks against ${base_url}"
if [[ "$run_newman_on_windows" == true ]]; then
    collection_windows="$(to_windows_path "$collection")"
    powershell.exe -NoProfile -Command "\$ErrorActionPreference = 'Stop'; Set-Location \$env:TEMP; & npx.cmd --yes '${newman_package}' run '${collection_windows}' --env-var 'baseUrl=${base_url}'"
elif command -v newman >/dev/null 2>&1; then
    newman_cmd=(newman)
elif command -v npx >/dev/null 2>&1; then
    newman_cmd=(npx --yes "$newman_package")
else
    echo "Newman is not installed, and npx is unavailable. Install with: npm install -g newman" >&2
    exit 1
fi

if [[ "$run_newman_on_windows" != true ]]; then
    "${newman_cmd[@]}" run "$collection" --env-var "baseUrl=${base_url}"
fi
