param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$SelfContained,
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$frontendDir = Join-Path $repoRoot "frontend"
$desktopProject = Join-Path $repoRoot "desktop\TideReader.Desktop\TideReader.Desktop.csproj"

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-Date -Format "yyyyMMdd-HHmmss"
}

$publishDir = Join-Path $repoRoot ("artifacts\publish\{0}-{1}" -f $Runtime, $Version)

Write-Host "Building frontend..."
Push-Location $frontendDir
try {
    npm run build
    if ($LASTEXITCODE -ne 0) {
        throw "Frontend build failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

Write-Host "Publishing desktop host to $publishDir ..."
dotnet publish $desktopProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained:$($SelfContained.IsPresent.ToString().ToLowerInvariant()) `
    -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "Desktop publish failed with exit code $LASTEXITCODE."
}

Write-Host ""
Write-Host "Publish complete:"
Write-Host "  $publishDir"
Write-Host "Executable:"
Write-Host "  $(Join-Path $publishDir 'TideReader.Desktop.exe')"
