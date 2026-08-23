param(
    [string]$EvidenceRoot = "docs\parity\freep-powerpoint-chrome-2026-08-16",
    [string]$CaptureScriptPath = "tools\Capture-FreePPowerPointChrome.ps1",
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "VisualEvidenceScriptSupport.ps1")

$resolvedRoot = Resolve-ToolRepoPath -Path $EvidenceRoot -RepoRoot $repoRoot
$resolvedCaptureScript = Resolve-ToolRepoPath -Path $CaptureScriptPath -RepoRoot $repoRoot
if (-not (Test-Path -LiteralPath $resolvedCaptureScript -PathType Leaf)) {
    throw "FreeP PowerPoint chrome capture script is missing: $resolvedCaptureScript"
}

$captureSource = Get-Content -LiteralPath $resolvedCaptureScript -Raw
foreach ($requiredText in @(
        "ScreenshotCaptureSupport.ps1",
        "Assert-ForegroundWindowOwnership",
        "AttachThreadInput",
        "CopyFromScreen-window-rectangle-top-band",
        "Clear-CurrentCapture",
        "PowerPoint.Application")) {
    if ($captureSource.IndexOf($requiredText, [StringComparison]::Ordinal) -lt 0) {
        throw "FreeP PowerPoint chrome capture script is missing required guard or provenance '$requiredText'."
    }
}

$manifestPath = Join-Path $resolvedRoot "manifest.json"
$blockerPath = Join-Path $resolvedRoot "blocker-manifest.json"
$hasManifest = Test-Path -LiteralPath $manifestPath -PathType Leaf
$hasBlocker = Test-Path -LiteralPath $blockerPath -PathType Leaf
if ($hasManifest -eq $hasBlocker) {
    throw "FreeP PowerPoint chrome evidence must contain exactly one of manifest.json or blocker-manifest.json."
}

$readmePath = Join-Path $resolvedRoot "README.md"
if (-not (Test-Path -LiteralPath $readmePath -PathType Leaf)) {
    throw "FreeP PowerPoint chrome evidence README is missing: $readmePath"
}
$readme = Get-Content -LiteralPath $readmePath -Raw
foreach ($requiredText in @("WPF", "Avalonia", "not a raw", "foreground")) {
    if ($readme.IndexOf($requiredText, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "FreeP PowerPoint chrome evidence README is missing required scope text '$requiredText'."
    }
}

if ($hasBlocker) {
    $blocker = Get-Content -LiteralPath $blockerPath -Raw | ConvertFrom-Json
    if ($blocker.schemaVersion -ne 1 -or $blocker.captureStatus -ne "blocked" -or [string]::IsNullOrWhiteSpace($blocker.reason)) {
        throw "FreeP PowerPoint chrome blocker manifest is malformed."
    }
    $pngs = @(Get-ChildItem -LiteralPath $resolvedRoot -Filter "powerpoint_*.png" -File -ErrorAction SilentlyContinue)
    if ($pngs.Count -ne 0) {
        throw "Blocked PowerPoint chrome evidence must not retain partial PNG artifacts."
    }
    Write-Host "FreeP PowerPoint chrome capture is explicitly blocked with no partial artifacts retained."
    exit 0
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$expectedTabs = @("Home", "Insert", "Design", "Transitions", "Animations", "Slide Show", "View")
$expectedWidths = @(1280, 1100, 900, 750)
if ($manifest.schemaVersion -ne 1 -or $manifest.captureStatus -ne "complete" -or
    @($manifest.mappedFreePTabs).Count -ne $expectedTabs.Count -or
    @($manifest.widths).Count -ne $expectedWidths.Count -or
    $manifest.expectedCaptureCount -ne 28 -or $manifest.actualCaptureCount -ne 28 -or
    @($manifest.captures).Count -ne 28) {
    throw "FreeP PowerPoint chrome capture matrix is incomplete or has an unexpected contract."
}
if ((Compare-Object $expectedTabs @($manifest.mappedFreePTabs) -SyncWindow 0) -or
    (Compare-Object $expectedWidths @($manifest.widths) -SyncWindow 0)) {
    throw "FreeP PowerPoint chrome capture matrix has unexpected tabs or widths."
}

$captureKeys = @{}
foreach ($capture in $manifest.captures) {
    if ($capture.captureStatus -ne "complete" -or [string]::IsNullOrWhiteSpace($capture.captureKey) -or
        [string]::IsNullOrWhiteSpace($capture.fileName) -or [string]::IsNullOrWhiteSpace($capture.sha256)) {
        throw "FreeP PowerPoint chrome capture metadata is incomplete."
    }
    if ($captureKeys.ContainsKey($capture.captureKey)) {
        throw "FreeP PowerPoint chrome capture has duplicate key '$($capture.captureKey)'."
    }
    $captureKeys[$capture.captureKey] = $true
    $imagePath = Join-Path $resolvedRoot $capture.fileName
    if (-not (Test-Path -LiteralPath $imagePath -PathType Leaf) -or (Get-Item -LiteralPath $imagePath).Length -le 0) {
        throw "FreeP PowerPoint chrome PNG is missing or empty: $($capture.fileName)"
    }
    $actualHash = Get-VisualEvidenceFileSha256 -Path $imagePath
    if ($actualHash -ne $capture.sha256) {
        throw "FreeP PowerPoint chrome PNG hash is stale: $($capture.fileName)"
    }
}

Write-Host "FreeP PowerPoint chrome evidence is complete: 28/28 mapped native ribbon states."
