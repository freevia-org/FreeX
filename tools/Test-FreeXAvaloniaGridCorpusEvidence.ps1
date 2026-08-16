param(
    [string]$ManifestPath = "docs\parity\freex-avalonia-grid-corpus-2026-08-16\manifest.json"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

$manifest = Read-ToolJson -Path $ManifestPath -RepoRoot $repoRoot -MissingMessage "Required FreeX Avalonia grid corpus manifest is missing"

if ([string]$manifest.schema -ne "freex.avalonia-grid-corpus.v1") {
    throw "Unexpected FreeX Avalonia grid corpus schema '$($manifest.schema)'."
}
if ([string]$manifest.captureStatus -ne "complete") {
    throw "FreeX Avalonia grid corpus capture status must be complete."
}

$captures = @($manifest.captures)
if ($captures.Count -ne 35) {
    throw "FreeX Avalonia grid corpus must retain 35 captures; found $($captures.Count)."
}

$familyCounts = @{}
foreach ($capture in $captures) {
    if (-not [bool]$capture.captured) {
        throw "Grid corpus capture '$($capture.id)' is not marked captured."
    }
    if ([string]::IsNullOrWhiteSpace([string]$capture.sheet) -or [string]::IsNullOrWhiteSpace([string]$capture.range)) {
        throw "Grid corpus capture '$($capture.id)' must record its worksheet and range."
    }
    $pngPath = Resolve-ToolRepoPath -Path ([string]$capture.png) -RepoRoot $repoRoot
    if (-not (Test-Path -LiteralPath $pngPath) -or (Get-Item -LiteralPath $pngPath).Length -le 0) {
        throw "Grid corpus capture '$($capture.id)' is missing a non-empty PNG."
    }
    $hash = (Get-FileHash -LiteralPath $pngPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne [string]$capture.sha256) {
        throw "Grid corpus capture '$($capture.id)' PNG hash does not match the manifest."
    }
    $family = [string]$capture.family
    $existingFamilyCount = if ($familyCounts.ContainsKey($family)) { [int]$familyCounts[$family] } else { 0 }
    $familyCounts[$family] = 1 + $existingFamilyCount
}

if ($familyCounts["chart"] -ne 8 -or $familyCounts["cell-style"] -ne 7 -or $familyCounts["pivot"] -ne 20) {
    throw "Grid corpus family counts must be chart=8, cell-style=7, pivot=20."
}

Write-Host "FreeX Avalonia grid corpus evidence passed: chart=8, cell-style=7, pivot=20, total=35."
