param(
    [string]$EvidenceRoot = "docs\parity\freep-responsive-chrome-2026-08-16"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")
. (Join-Path $PSScriptRoot "VisualEvidenceScriptSupport.ps1")

$resolvedRoot = Resolve-ToolRepoPath -Path $EvidenceRoot -RepoRoot $repoRoot
$manifestPath = Join-Path $resolvedRoot "manifest.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$widths = @(1280, 1100, 900, 750)
$tabs = @("home", "insert", "design", "transitions", "animations", "view")
$hosts = @("wpf", "avalonia")
$expectedCaptureCount = $widths.Count * $tabs.Count * $hosts.Count

if ($manifest.captureStatus -ne "complete" -or $manifest.expectedCaptureCount -ne $expectedCaptureCount -or $manifest.actualCaptureCount -ne $expectedCaptureCount -or @($manifest.captures).Count -ne $expectedCaptureCount) {
    throw "FreeP responsive chrome manifest is incomplete."
}

$seen = @{}
foreach ($capture in $manifest.captures) {
    if ($capture.captureStatus -ne "complete" -or -not ($widths -contains $capture.logicalWidth) -or -not ($tabs -contains $capture.tabId) -or -not ($hosts -contains $capture.host)) {
        throw "FreeP responsive chrome capture metadata is invalid: $($capture.captureKey)"
    }
    if ($seen.ContainsKey($capture.captureKey)) {
        throw "FreeP responsive chrome contains duplicate capture key: $($capture.captureKey)"
    }
    $seen[$capture.captureKey] = $true
    foreach ($pair in @(@($capture.fullImagePath, $capture.fullImageSha256), @($capture.clientImagePath, $capture.clientImageSha256))) {
        $path = Join-Path $resolvedRoot ($pair[0].Replace('/', [IO.Path]::DirectorySeparatorChar))
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -le 0) {
            throw "FreeP responsive chrome image is missing or empty: $path"
        }
        if ((Get-VisualEvidenceFileSha256 -Path $path) -ne $pair[1]) {
            throw "FreeP responsive chrome hash is stale: $path"
        }
    }
}

foreach ($width in $widths) {
    foreach ($captureHost in $hosts) {
        foreach ($tab in $tabs) {
            $key = "ribbon:${width}:${captureHost}:$tab"
            if (-not $seen.ContainsKey($key)) {
                throw "FreeP responsive chrome is missing required capture: $key"
            }
        }
    }
}

Write-Host "FreeP responsive chrome evidence passed: $expectedCaptureCount/$expectedCaptureCount guarded WPF/Avalonia captures."
