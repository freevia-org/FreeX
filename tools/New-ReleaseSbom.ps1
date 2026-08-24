#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Name,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$CommitSha,
    [Parameter(Mandatory)][string]$Runtime,
    [Parameter(Mandatory)][string]$InputRoot,
    [Parameter(Mandatory)][string[]]$PayloadNames,
    [Parameter(Mandatory)][string]$OutputPath,
    [Parameter(Mandatory)][string]$SbomToolPath,
    [string]$RepositoryRoot
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$InputRoot = (Resolve-Path -LiteralPath $InputRoot).Path
$SbomToolPath = (Resolve-Path -LiteralPath $SbomToolPath).Path
if (-not $RepositoryRoot) { $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path }
$stage = Join-Path (Split-Path -Parent $OutputPath) ".sbom-$Name-$Runtime"
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
foreach ($payloadName in $PayloadNames) {
    $matches = @(Get-ChildItem -LiteralPath $InputRoot -Recurse -File -Filter $payloadName)
    if ($matches.Count -ne 1) { throw "Expected exactly one SBOM payload '$payloadName'; found $($matches.Count)." }
    Copy-Item -LiteralPath $matches[0].FullName -Destination (Join-Path $stage $matches[0].Name)
}
$namespace = "https://github.com/tony-xmelon/FreeX/releases/$CommitSha/$Name/$Runtime"
& $SbomToolPath generate -b $stage -bc $RepositoryRoot -pn $Name -pv $Version -ps 'FreeX contributors' -nsb $namespace
if ($LASTEXITCODE -ne 0) { throw "sbom-tool failed with exit code $LASTEXITCODE." }
$generated = Join-Path $stage '_manifest\spdx_2.2\manifest.spdx.json'
if (-not (Test-Path -LiteralPath $generated)) { throw "SBOM output missing: $generated" }
Copy-Item -LiteralPath $generated -Destination $OutputPath -Force
$outputName = Split-Path -Leaf $OutputPath
$hash = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $outputName" | Set-Content -LiteralPath "$OutputPath.sha256" -NoNewline -Encoding ascii
Write-Host "Produced $OutputPath"
