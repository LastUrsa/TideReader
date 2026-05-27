param(
    [string]$Configuration = "Release",
    [int]$LineThreshold = 85,
    [int]$BranchThreshold = 73
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repoRoot "backend\TideReader.Backend.Tests\TideReader.Backend.Tests.csproj"
$testProjectDir = Split-Path -Parent $testProject
$coverageOutput = "artifacts\coverage\backend\coverage"
$coverageReport = Join-Path $testProjectDir "$coverageOutput.cobertura.xml"
$coverageDir = Split-Path -Parent $coverageReport

New-Item -ItemType Directory -Path $coverageDir -Force | Out-Null

dotnet test $testProject `
    -c $Configuration `
    '/p:CollectCoverage=true' `
    '/p:CoverletOutputFormat=cobertura' `
    "/p:CoverletOutput=$coverageOutput"

if ($LASTEXITCODE -ne 0) {
    throw "Backend coverage run failed."
}

[xml]$coverage = Get-Content $coverageReport
$backendPackage = $coverage.coverage.packages.package | Where-Object { $_.name -eq 'TideReader.Backend' } | Select-Object -First 1

if ($null -eq $backendPackage) {
    throw "Backend coverage report did not contain the TideReader.Backend package."
}

$lineCoverage = [Math]::Round(([double]$backendPackage.'line-rate' * 100), 2)
$branchCoverage = [Math]::Round(([double]$backendPackage.'branch-rate' * 100), 2)

if ($lineCoverage -lt $LineThreshold) {
    throw "Backend line coverage gate failed. Actual: $lineCoverage%. Required: $LineThreshold%."
}

if ($branchCoverage -lt $BranchThreshold) {
    throw "Backend branch coverage gate failed. Actual: $branchCoverage%. Required: $BranchThreshold%."
}

Write-Host ""
Write-Host "Backend coverage gate passed."
Write-Host "  Line coverage:   $lineCoverage%"
Write-Host "  Branch coverage: $branchCoverage%"
Write-Host "Coverage file:"
Write-Host "  $coverageReport"
