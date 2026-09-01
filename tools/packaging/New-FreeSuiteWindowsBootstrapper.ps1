#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds the non-Inno Free Suite Windows bootstrapper from signed Velopack installers.

.DESCRIPTION
    The resulting single-file executable embeds the exact FreeX, FreeW, and FreeP
    Velopack Setup executables supplied by the release workflow. The outer executable
    must be Artifact Signed after this script returns; checksums and manifests must be
    generated only after that signature is applied.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$InputRoot,

    [Parameter(Mandatory = $true)]
    [string]$OutputDir,

    [ValidateSet("Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path (Split-Path -Parent $PSScriptRoot) "ToolScriptSupport.ps1")

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$InputRoot = (Resolve-Path -LiteralPath $InputRoot).Path
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$OutputDir = (Resolve-Path -LiteralPath $OutputDir).Path
$payloadRoot = Join-Path $OutputDir ".suite-payload"
$publishRoot = Join-Path $OutputDir ".suite-publish"

foreach ($path in @($payloadRoot, $publishRoot)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $path | Out-Null
}

try {
    foreach ($app in @("FreeX", "FreeW", "FreeP")) {
        $source = Find-ToolReleaseArtifact -InputRoot $InputRoot -Name "$app-v$Version-win-x64-setup.exe"
        Copy-Item -LiteralPath $source.FullName -Destination (Join-Path $payloadRoot "$app-Setup.exe")
    }

    $project = Join-Path $repoRoot "tools/FreeSuite.Bootstrapper/FreeSuite.Bootstrapper.csproj"
    $publishOutput = @(& dotnet publish $project `
        --configuration $Configuration `
        --runtime win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:Version=$Version `
        -p:SuitePayloadDir=$payloadRoot `
        --output $publishRoot 2>&1)
    $publishExitCode = $LASTEXITCODE
    $publishOutput | ForEach-Object { Write-Host $_ }
    if ($publishExitCode -ne 0) {
        throw "Free Suite bootstrapper publish failed with exit code $publishExitCode."
    }

    $published = Join-Path $publishRoot "FreeSuite.exe"
    if (-not (Test-Path -LiteralPath $published)) {
        throw "Free Suite bootstrapper output is missing: $published"
    }

    $result = Join-Path $OutputDir "FreeSuite-v$Version-win-x64-setup.exe"
    Copy-Item -LiteralPath $published -Destination $result -Force
    Write-Output $result
}
finally {
    foreach ($path in @($payloadRoot, $publishRoot)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }
}
