#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Publish tester zip packages for FreeW or FreeP across the current tester runtimes.

.DESCRIPTION
    This is the explicit fallback lane for the sister apps while their hosted
    GitHub release publishers are being promoted to the same maturity as FreeX.
    Windows uses the WPF host. Linux and macOS use the Avalonia host.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Publish-SisterAppTesterPackages.ps1 -App FreeW -Version 0.8.149
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("FreeW", "FreeP")]
    [string]$App,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string[]]$Runtimes = @("win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"),

    [string]$Configuration = "Release",

    [string]$OutputDir
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDir "..")).Path
if (-not $OutputDir) {
    $OutputDir = Join-Path $repoRoot "artifacts\sister-tester-release-$Version"
}

$shortSha = (& git -C $repoRoot rev-parse --short=8 HEAD).Trim()
if (-not $shortSha) {
    throw "Could not resolve git commit."
}

$config = switch ($App) {
    "FreeW" {
        @{
            WpfProject = "freew\FreeW.App.Host\FreeW.App.Host.csproj"
            AvaloniaProject = "freew\FreeW.App.Avalonia\FreeW.App.Avalonia.csproj"
            WpfHost = "FreeW.App.Host"
            AvaloniaHost = "FreeW"
        }
    }
    "FreeP" {
        @{
            WpfProject = "freep\FreeP.App.Host\FreeP.App.Host.csproj"
            AvaloniaProject = "freep\FreeP.App.Avalonia\FreeP.App.Avalonia.csproj"
            WpfHost = "FreeP.App.Host"
            AvaloniaHost = "FreeP"
        }
    }
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$manifestRows = New-Object System.Collections.Generic.List[string]
$manifestRows.Add("app,version,commit,runtime,host,package,sha256")

foreach ($runtime in $Runtimes) {
    $isWindows = $runtime -like "win-*"
    $hostKind = if ($isWindows) { "wpf" } else { "avalonia" }
    $projectRelative = if ($isWindows) { $config.WpfProject } else { $config.AvaloniaProject }
    $hostName = if ($isWindows) { $config.WpfHost } else { $config.AvaloniaHost }
    $project = Join-Path $repoRoot $projectRelative
    if (-not (Test-Path -LiteralPath $project)) {
        throw "Could not find project '$project'."
    }

    $publishDir = Join-Path $OutputDir "publish\$App-$runtime-$hostKind"
    $zipName = "$App-$Version-$runtime-$hostKind-$shortSha.zip"
    $zipPath = Join-Path $OutputDir $zipName

    if (Test-Path -LiteralPath $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

    $publishArgs = @(
        "publish", $project,
        "--configuration", $Configuration,
        "--runtime", $runtime,
        "--self-contained", "true",
        "-p:UseAppHost=true",
        "-p:PublishSingleFile=false",
        "-p:Version=$Version",
        "-p:InformationalVersion=$Version+$shortSha",
        "--output", $publishDir
    )
    if (-not $isWindows) {
        $publishArgs = @(
            "publish", $project,
            "--configuration", $Configuration,
            "--framework", "net10.0",
            "--runtime", $runtime,
            "--self-contained", "true",
            "-p:UseAppHost=true",
            "-p:PublishSingleFile=false",
            "-p:Version=$Version",
            "-p:InformationalVersion=$Version+$shortSha",
            "--output", $publishDir
        )
    }

    Write-Host ""
    Write-Host "Publishing $App $runtime ($hostKind)"
    Write-Host "dotnet $($publishArgs -join ' ')"
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $App $runtime with exit code $LASTEXITCODE."
    }

    $expectedExe = if ($isWindows) { Join-Path $publishDir "$hostName.exe" } else { Join-Path $publishDir $hostName }
    if (-not (Test-Path -LiteralPath $expectedExe)) {
        throw "Publish output missing expected apphost '$expectedExe'."
    }

    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $shaPath = "$zipPath.sha256"
    "$hash  $zipName" | Set-Content -LiteralPath $shaPath -NoNewline -Encoding ascii
    $manifestRows.Add("$App,$Version,$shortSha,$runtime,$hostKind,$zipName,$hash")
    Write-Host "Produced $zipName"
}

$manifestPath = Join-Path $OutputDir "$App-$Version-$shortSha-manifest.csv"
$manifestRows | Set-Content -LiteralPath $manifestPath -Encoding ascii
Write-Host ""
Write-Host "Manifest: $manifestPath"
