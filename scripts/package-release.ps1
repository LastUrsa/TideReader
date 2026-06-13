param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$SelfContained,
    [string]$InnoSetupCompilerPath = "",
    [string]$SignToolPath = "",
    [string]$SigningCertificateThumbprint = "",
    [string]$SigningCertificatePfxPath = "",
    [string]$SigningCertificatePfxPassword = "",
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishScript = Join-Path $PSScriptRoot "publish-desktop.ps1"
$installerScript = Join-Path $PSScriptRoot "installer\TideReader.iss"
$publishDir = Join-Path $repoRoot ("artifacts\publish\{0}-{1}" -f $Runtime, $Version)
$releaseDir = Join-Path $repoRoot ("artifacts\release\{0}-{1}" -f $Runtime, $Version)
$zipPath = Join-Path $releaseDir ("TideReader-{0}-{1}.zip" -f $Version, $Runtime)
$installerBaseName = "TideReader-{0}-{1}-Setup" -f $Version, $Runtime

function Resolve-InnoSetupCompiler([string]$explicitPath) {
    if (-not [string]::IsNullOrWhiteSpace($explicitPath)) {
        if (-not (Test-Path -LiteralPath $explicitPath)) {
            throw "Inno Setup compiler was not found at '$explicitPath'."
        }

        return (Resolve-Path -LiteralPath $explicitPath).Path
    }

    $candidates = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw "Inno Setup 6 was not found. Install Inno Setup 6 or pass -InnoSetupCompilerPath."
}

function Resolve-SignTool([string]$explicitPath) {
    if (-not [string]::IsNullOrWhiteSpace($explicitPath)) {
        if (-not (Test-Path -LiteralPath $explicitPath)) {
            throw "SignTool was not found at '$explicitPath'."
        }

        return (Resolve-Path -LiteralPath $explicitPath).Path
    }

    $kitRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path -LiteralPath $kitRoot) {
        $candidate = Get-ChildItem -LiteralPath $kitRoot -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) {
            return $candidate.FullName
        }
    }

    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    throw "SignTool was not found. Install the Windows SDK or pass -SignToolPath."
}

function Test-CodeSigningEnabled {
    return -not [string]::IsNullOrWhiteSpace($SigningCertificateThumbprint) -or
        -not [string]::IsNullOrWhiteSpace($SigningCertificatePfxPath)
}

function Invoke-CodeSign([string[]]$Paths) {
    if (-not (Test-CodeSigningEnabled)) {
        return
    }

    $signtool = Resolve-SignTool $SignToolPath
    foreach ($path in $Paths) {
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Expected signing target was not found at '$path'."
        }

        $args = @("sign", "/fd", "SHA256", "/tr", $TimestampUrl, "/td", "SHA256")
        if (-not [string]::IsNullOrWhiteSpace($SigningCertificatePfxPath)) {
            $args += @("/f", $SigningCertificatePfxPath)
            if (-not [string]::IsNullOrWhiteSpace($SigningCertificatePfxPassword)) {
                $args += @("/p", $SigningCertificatePfxPassword)
            }
        }
        else {
            $args += @("/sha1", $SigningCertificateThumbprint)
        }
        $args += $path

        Write-Host "Signing $path ..."
        & $signtool @args
        if ($LASTEXITCODE -ne 0) {
            throw "Code signing failed for '$path' with exit code $LASTEXITCODE."
        }
    }
}

Write-Host "Publishing desktop app..."
& $publishScript -Runtime $Runtime -Configuration $Configuration -SelfContained:$SelfContained.IsPresent -Version $Version

if (-not (Test-Path -LiteralPath $publishDir)) {
    throw "Expected publish output was not found at '$publishDir'."
}

if (Test-CodeSigningEnabled) {
    $publishSigningTargets = Get-ChildItem -LiteralPath $publishDir -Recurse -File |
        Where-Object { $_.Extension -eq ".exe" } |
        Select-Object -ExpandProperty FullName
    Invoke-CodeSign $publishSigningTargets
}

New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Write-Host "Creating zip package..."
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -Force

$iscc = Resolve-InnoSetupCompiler $InnoSetupCompilerPath

Write-Host "Building installer..."
& $iscc `
    "/DMyAppVersion=$Version" `
    "/DMyAppSourceDir=$publishDir" `
    "/DMyOutputDir=$releaseDir" `
    "/DMyOutputBaseFilename=$installerBaseName" `
    $installerScript

$installerPath = Join-Path $releaseDir ($installerBaseName + ".exe")
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Expected installer output was not found at '$installerPath'."
}

Invoke-CodeSign @($installerPath)

Write-Host ""
Write-Host "Release packaging complete:"
Write-Host "  Zip:       $zipPath"
Write-Host "  Installer: $installerPath"
