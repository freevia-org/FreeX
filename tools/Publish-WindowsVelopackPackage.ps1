#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds standalone and Velopack Windows packages for a Free family app.

.DESCRIPTION
    Produces the canonical App-vVersion-win-x64.exe standalone payload and
    App-vVersion-win-x64-setup.exe installer, plus the Velopack portable archive and update feed.
    When ArtifactSigningMetadataPath is supplied, the standalone executable, Velopack executable
    payloads, and generated Setup executable are signed and verified before checksums are written.

.EXAMPLE
    pwsh -NoProfile -File tools/Publish-WindowsVelopackPackage.ps1 -App FreeW -Version 0.8.185 -OutputDir artifacts/release
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("FreeX", "FreeW", "FreeP")]
    [string]$App,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$OutputDir,

    [ValidateSet("Release")]
    [string]$Configuration = "Release",

    [ValidatePattern('^win-[A-Za-z0-9]+$')]
    [string]$RuntimeIdentifier = "win-x64",

    [string]$ArtifactSigningMetadataPath,

    [string]$VpkPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

if (-not (Test-ToolIsWindows)) {
    throw "Velopack Windows packages must be built on Windows."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$config = switch ($App) {
    "FreeX" {
        @{
            Project = "src/FreeX.App.Host/FreeX.App.Host.csproj"
            MainExe = "FreeX.App.Host.exe"
            PackId = "FreeXApp"
        }
    }
    "FreeW" {
        @{
            Project = "freew/FreeW.App.Host/FreeW.App.Host.csproj"
            MainExe = "FreeW.App.Host.exe"
            PackId = "FreeWApp"
        }
    }
    "FreeP" {
        @{
            Project = "freep/FreeP.App.Host/FreeP.App.Host.csproj"
            MainExe = "FreeP.App.Host.exe"
            PackId = "FreePApp"
        }
    }
}

$projectPath = Join-Path $repoRoot $config.Project
if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Could not find $App host project '$projectPath'."
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$outputRoot = (Resolve-Path -LiteralPath $OutputDir).Path
$publishDir = Join-Path $outputRoot "publish/$App-$RuntimeIdentifier-velopack"
$singleFilePublishDir = Join-Path $outputRoot "publish/$App-$RuntimeIdentifier-singlefile"
$velopackDir = Join-Path $outputRoot "velopack-$App-$RuntimeIdentifier"
foreach ($directory in @($publishDir, $singleFilePublishDir, $velopackDir)) {
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

$commitSha = (& git -C $repoRoot rev-parse HEAD).Trim().ToLowerInvariant()
if ($commitSha -notmatch '^[0-9a-f]{40}$') {
    throw "Could not resolve the source commit."
}

$publishArguments = @(
    "publish", $projectPath,
    "--configuration", $Configuration,
    "--runtime", $RuntimeIdentifier,
    "--self-contained", "true",
    "-p:UseAppHost=true",
    "-p:PublishSingleFile=false",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:Optimize=true",
    "-p:Version=$Version",
    "-p:InformationalVersion=$Version+$($commitSha.Substring(0, 8))",
    "--output", $publishDir
)
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed for $App $RuntimeIdentifier with exit code $LASTEXITCODE."
}

$mainExecutable = Join-Path $publishDir $config.MainExe
if (-not (Test-Path -LiteralPath $mainExecutable -PathType Leaf)) {
    throw "Published output is missing '$mainExecutable'."
}

$singleFileArguments = @(
    "publish", $projectPath,
    "--configuration", $Configuration,
    "--runtime", $RuntimeIdentifier,
    "--self-contained", "true",
    "-p:UseAppHost=true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:IncludeAllContentForSelfExtract=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:Optimize=true",
    "-p:Version=$Version",
    "-p:InformationalVersion=$Version+$($commitSha.Substring(0, 8))",
    "--output", $singleFilePublishDir
)
& dotnet @singleFileArguments
if ($LASTEXITCODE -ne 0) {
    throw "Single-file dotnet publish failed for $App $RuntimeIdentifier with exit code $LASTEXITCODE."
}

$publishedSingleFile = Join-Path $singleFilePublishDir $config.MainExe
if (-not (Test-Path -LiteralPath $publishedSingleFile -PathType Leaf)) {
    throw "Single-file output is missing '$publishedSingleFile'."
}
$unexpectedSingleFileSidecars = @(
    Get-ChildItem -LiteralPath $singleFilePublishDir -File |
        Where-Object { $_.FullName -ne $publishedSingleFile -and $_.Extension -ne ".pdb" }
)
if ($unexpectedSingleFileSidecars.Count -gt 0) {
    $unexpectedNames = ($unexpectedSingleFileSidecars | ForEach-Object Name) -join ", "
    throw "Single-file Windows publish produced runtime sidecars: $unexpectedNames"
}

$standalonePath = Join-Path $outputRoot "$App-v$Version-$RuntimeIdentifier.exe"
Copy-Item -LiteralPath $publishedSingleFile -Destination $standalonePath -Force

if ([string]::IsNullOrWhiteSpace($VpkPath)) {
    $vpk = Get-Command vpk -ErrorAction SilentlyContinue
    if ($null -eq $vpk) {
        & dotnet tool install --global vpk --version 1.2.0
        if ($LASTEXITCODE -ne 0) {
            throw "Could not install the pinned Velopack CLI."
        }
        $vpk = Get-Command vpk -ErrorAction SilentlyContinue
    }
    if ($null -eq $vpk -or [string]::IsNullOrWhiteSpace($vpk.Source)) {
        throw "vpk was not found after installing the pinned Velopack CLI."
    }
    $VpkPath = $vpk.Source
} else {
    $VpkPath = (Resolve-Path -LiteralPath $VpkPath -ErrorAction Stop).Path
}

$vpkArguments = @(
    "pack",
    "--packId", $config.PackId,
    "--packVersion", $Version,
    "--packDir", $publishDir,
    "--mainExe", $config.MainExe,
    "--outputDir", $velopackDir,
    "--packTitle", $App,
    "--channel", "win"
)

if (-not [string]::IsNullOrWhiteSpace($ArtifactSigningMetadataPath)) {
    $metadataPath = (Resolve-Path -LiteralPath $ArtifactSigningMetadataPath -ErrorAction Stop).Path
    & (Join-Path $PSScriptRoot "Invoke-WindowsArtifactSigning.ps1") `
        -Files $standalonePath `
        -MetadataPath $metadataPath
    if ($LASTEXITCODE -ne 0) {
        throw "Artifact Signing failed for $App standalone executable."
    }

    $powerShellPath = Get-ToolPowerShellPath
    $signingScriptPath = Join-Path $PSScriptRoot "Invoke-WindowsArtifactSigning.ps1"
    $signingTemplate = '"{0}" -NoProfile -File "{1}" -Files {{{{file}}}} -MetadataPath "{2}"' -f `
        $powerShellPath, $signingScriptPath, $metadataPath
    $vpkArguments += @("--signTemplate", $signingTemplate)
}

& $VpkPath @vpkArguments
if ($LASTEXITCODE -ne 0) {
    throw "vpk pack failed for $App $RuntimeIdentifier with exit code $LASTEXITCODE."
}

$setupExecutables = @(Get-ChildItem -LiteralPath $velopackDir -Filter "*-Setup.exe" -File)
if ($setupExecutables.Count -ne 1) {
    throw "Expected exactly one Velopack Setup executable, found $($setupExecutables.Count)."
}
$portableArchives = @(Get-ChildItem -LiteralPath $velopackDir -Filter "*-Portable.zip" -File)
if ($portableArchives.Count -ne 1) {
    throw "Expected exactly one Velopack portable archive, found $($portableArchives.Count)."
}
$fullPackages = @(Get-ChildItem -LiteralPath $velopackDir -Filter "*-full.nupkg" -File)
if ($fullPackages.Count -ne 1) {
    throw "Expected exactly one Velopack full package, found $($fullPackages.Count)."
}

$setupPath = Join-Path $outputRoot "$App-v$Version-$RuntimeIdentifier-setup.exe"
Copy-Item -LiteralPath $setupExecutables[0].FullName -Destination $setupPath -Force

if (-not [string]::IsNullOrWhiteSpace($ArtifactSigningMetadataPath)) {
    & (Join-Path $PSScriptRoot "Invoke-WindowsArtifactSigning.ps1") `
        -Files $standalonePath,$mainExecutable,$setupPath `
        -MetadataPath $ArtifactSigningMetadataPath `
        -VerifyOnly
    if ($LASTEXITCODE -ne 0) {
        throw "Authenticode verification failed for $App Velopack output."
    }
}

foreach ($artifactPath in @($standalonePath, $setupPath)) {
    $artifactName = Split-Path -Leaf $artifactPath
    $hash = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $artifactName" | Set-Content -LiteralPath "$artifactPath.sha256" -NoNewline -Encoding ascii
}

Write-Host "Produced $App Windows release payloads:"
Write-Host "  $(Split-Path -Leaf $standalonePath)"
Write-Host "  $(Split-Path -Leaf $setupPath)"
Write-Host "Velopack portable/update artifacts: $velopackDir"
Get-ChildItem -LiteralPath $velopackDir -File | Sort-Object Name | ForEach-Object {
    Write-Host "  $($_.Name)"
}
Write-Host "Source commit: $commitSha"
