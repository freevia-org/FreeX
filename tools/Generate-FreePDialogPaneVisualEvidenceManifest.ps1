param(
    [string]$EvidenceRoot = "docs\parity\freep-dialog-pane-visual-evidence",
    [string]$ManifestPath = "docs\parity\freep-dialog-pane-visual-evidence\artifact-manifest.json",
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")
. (Join-Path $PSScriptRoot "VisualEvidenceScriptSupport.ps1")

$resolvedRoot = Resolve-ToolRepoPath -Path $EvidenceRoot -RepoRoot $repoRoot
$resolvedManifest = Resolve-ToolRepoPath -Path $ManifestPath -RepoRoot $repoRoot
$summaryPath = Join-Path $resolvedRoot "summary.json"
$markdownPath = Join-Path $resolvedRoot "report.md"
$htmlPath = Join-Path $resolvedRoot "report.html"

foreach ($requiredPath in @($summaryPath, $markdownPath, $htmlPath, (Join-Path $resolvedRoot "wpf\manifest.json"), (Join-Path $resolvedRoot "avalonia\manifest.json"))) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "FreeP dialog/pane visual evidence artifact is missing: $requiredPath"
    }
}

$summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
if ($summary.schemaVersion -lt 3 -or $summary.scenarioCount -ne 28 -or $summary.routeCount -ne 19 -or $summary.pairedCaptureCount -ne 28) {
    throw "FreeP dialog/pane visual evidence summary has an unexpected schema or route/capture count."
}
if (($summary.passCount + $summary.mismatchCount) -ne 28 -or $summary.limitationCount -ne 0) {
    throw "FreeP dialog/pane visual evidence is incomplete: pass=$($summary.passCount), mismatch=$($summary.mismatchCount), limitation=$($summary.limitationCount)."
}
if (@($summary.comparisons).Count -ne 28 -or @($summary.wpf.captures).Count -ne 28 -or @($summary.avalonia.captures).Count -ne 28) {
    throw "FreeP dialog/pane visual evidence does not contain 28 comparison and host-capture rows."
}

function Test-RecordedImageHash {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string]$ExpectedHash,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $resolvedPath = Join-Path $resolvedRoot ($RelativePath.Replace('/', [IO.Path]::DirectorySeparatorChar))
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
        throw "$Label is missing: $resolvedPath"
    }
    $actualHash = Get-VisualEvidenceFileSha256 -Path $resolvedPath
    if ($actualHash -ne $ExpectedHash) {
        throw "$Label hash is stale for '$RelativePath': expected $ExpectedHash, actual $actualHash."
    }
}

$wpfCaptures = @{}
$avaloniaCaptures = @{}
foreach ($capture in $summary.wpf.captures) { $wpfCaptures[$capture.scenarioId] = $capture }
foreach ($capture in $summary.avalonia.captures) { $avaloniaCaptures[$capture.scenarioId] = $capture }

foreach ($comparison in $summary.comparisons) {
    if ($comparison.classification -notin @("pass", "mismatch")) {
        throw "FreeP dialog/pane comparison is not classified for '$($comparison.scenarioId)'."
    }
    if ($null -eq $comparison.pixelMetrics) {
        throw "Target pixel metrics are missing for '$($comparison.scenarioId)'."
    }

    $wpfCapture = $wpfCaptures[$comparison.scenarioId]
    $avaloniaCapture = $avaloniaCaptures[$comparison.scenarioId]
    Test-RecordedImageHash -RelativePath $wpfCapture.pixelComparisonImagePath -ExpectedHash $comparison.pixelMetrics.wpfImageSha256 -Label "WPF target image"
    Test-RecordedImageHash -RelativePath $avaloniaCapture.pixelComparisonImagePath -ExpectedHash $comparison.pixelMetrics.avaloniaImageSha256 -Label "Avalonia target image"
    Test-RecordedImageHash -RelativePath $comparison.pixelMetrics.heatmapPath -ExpectedHash $comparison.pixelMetrics.heatmapSha256 -Label "Target heatmap"

    if ($null -ne $comparison.shellContextPixelMetrics) {
        Test-RecordedImageHash -RelativePath $wpfCapture.imagePath -ExpectedHash $comparison.shellContextPixelMetrics.wpfImageSha256 -Label "WPF shell-context image"
        Test-RecordedImageHash -RelativePath $avaloniaCapture.imagePath -ExpectedHash $comparison.shellContextPixelMetrics.avaloniaImageSha256 -Label "Avalonia shell-context image"
        Test-RecordedImageHash -RelativePath $comparison.shellContextPixelMetrics.heatmapPath -ExpectedHash $comparison.shellContextPixelMetrics.heatmapSha256 -Label "Shell-context heatmap"
    }
}

$nativeManifest = Resolve-ToolRepoPath -Path "docs\parity\freep-native-picker-human-evidence.json" -RepoRoot $repoRoot
if (-not (Test-Path -LiteralPath $nativeManifest -PathType Leaf)) {
    throw "FreeP native picker human-evidence manifest is missing: $nativeManifest"
}

$files = Get-VisualEvidenceArtifactInventory -EvidenceRoot $resolvedRoot -ExcludedPaths @($resolvedManifest)

$artifact = [ordered]@{
    schemaVersion = 1
    generatedBy = "tools/Generate-FreePDialogPaneVisualEvidenceManifest.ps1"
    evidenceGeneratedAtUtc = $summary.generatedAtUtc
    scenarioCount = 28
    routeCount = 19
    pairedCaptureCount = 28
    passCount = $summary.passCount
    mismatchCount = $summary.mismatchCount
    limitationCount = 0
    pngCount = @($files | Where-Object { $_.path.EndsWith('.png', [StringComparison]::OrdinalIgnoreCase) }).Count
    fileCount = @($files).Count
    files = @($files)
}
$json = ($artifact | ConvertTo-Json -Depth 8) + [Environment]::NewLine

if ($Check) {
    Test-ToolGeneratedContentMatches -ExpectedContent $json -ActualPath $resolvedManifest -Label "FreeP dialog/pane visual evidence artifact manifest" -GeneratorScriptName "tools\Generate-FreePDialogPaneVisualEvidenceManifest.ps1" -NormalizeNewlines
    Write-Host "FreeP dialog/pane visual evidence artifact is current: $($artifact.passCount) pass, $($artifact.mismatchCount) mismatch, zero capture limitations across $($artifact.pngCount) PNG files."
    exit 0
}

New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedManifest) -Force | Out-Null
[IO.File]::WriteAllText($resolvedManifest, $json, [Text.UTF8Encoding]::new($false))
Write-Host "Generated FreeP dialog/pane visual evidence artifact manifest for $($artifact.fileCount) files."
