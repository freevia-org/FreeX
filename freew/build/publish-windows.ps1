#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Publish the FreeW WPF word processor as a self-contained Windows build and
    zip the output into artifacts/FreeW-win-x64[-<version>].zip.

.DESCRIPTION
    Produces a self-contained, framework-dependent-free folder publish of
    freew/FreeW.App.Host (net10.0-windows..., WinExe/UseWPF) for win-x64, then
    packages the publish folder into a versioned zip under the artifacts
    directory. Self-contained means the target machine needs no .NET install.

    PublishSingleFile is intentionally left OFF: single-file packaging with WPF
    is finicky (native COM/WinRT and XAML resource extraction), so a plain
    self-contained folder publish + zip is the safe, reproducible default.

    The script is idempotent (it cleans its own publish + zip outputs before
    each run) and CI-friendly (no interactive prompts; non-zero exit on
    failure). It echoes the produced artifact path on success.

.PARAMETER Version
    Version stamp embedded in the zip name and passed to the build as
    Version/InformationalVersion. Defaults to 0.1.0.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER Runtime
    Target runtime identifier. Defaults to win-x64.

.PARAMETER OutputDir
    Directory the final zip is written to. Defaults to <repo>/artifacts.

.PARAMETER PublishDir
    Intermediate publish staging directory. Defaults to
    <repo>/artifacts/publish/FreeW-<runtime>.

.EXAMPLE
    pwsh freew/build/publish-windows.ps1

.EXAMPLE
    pwsh freew/build/publish-windows.ps1 -Version 1.2.3 -OutputDir C:\out
#>
[CmdletBinding()]
param(
    [string]$Version = "0.1.0",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir,
    [string]$PublishDir
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Resolve repo root: this script lives at <repo>/freew/build/publish-windows.ps1.
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDir "..\..")).Path
$project = Join-Path $repoRoot "freew\FreeW.App.Host\FreeW.App.Host.csproj"

if (-not (Test-Path -LiteralPath $project)) {
    throw "Could not find FreeW.App.Host.csproj at '$project'."
}

if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot "artifacts" }
if (-not $PublishDir) { $PublishDir = Join-Path $OutputDir "publish\FreeW-$Runtime" }

$zipName = "FreeW-$Runtime-$Version.zip"
$zipPath = Join-Path $OutputDir $zipName

Write-Host "FreeW Windows packaging"
Write-Host "  project       = $project"
Write-Host "  configuration = $Configuration"
Write-Host "  runtime       = $Runtime"
Write-Host "  version       = $Version"
Write-Host "  publish dir   = $PublishDir"
Write-Host "  output zip    = $zipPath"

# Idempotent: clean prior publish + zip outputs so reruns are reproducible.
if (Test-Path -LiteralPath $PublishDir) {
    Remove-Item -LiteralPath $PublishDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

# Self-contained folder publish. Pass publish properties via /p: rather than
# editing the csproj so the app project stays untouched.
$publishArgs = @(
    "publish", $project,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "/p:PublishSingleFile=false",
    "/p:Version=$Version",
    "/p:InformationalVersion=$Version",
    "-o", $PublishDir
)

Write-Host ""
Write-Host "dotnet $($publishArgs -join ' ')"
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# Sanity-check the native apphost is present (assembly name = FreeW.App.Host).
$appHost = Join-Path $PublishDir "FreeW.App.Host.exe"
if (-not (Test-Path -LiteralPath $appHost)) {
    throw "Publish output missing expected apphost '$appHost'."
}

Write-Host ""
Write-Host "Compressing publish output -> $zipPath"
Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $zipPath -Force

if (-not (Test-Path -LiteralPath $zipPath)) {
    throw "Expected zip '$zipPath' was not produced."
}

$zipFull = (Resolve-Path -LiteralPath $zipPath).Path
$sizeMb = [math]::Round((Get-Item -LiteralPath $zipFull).Length / 1MB, 1)
Write-Host ""
Write-Host "Artifact produced: $zipFull ($sizeMb MB)"

# Emit the artifact path as the final line for CI consumption.
Write-Output $zipFull
