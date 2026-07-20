param(
    [string]$EvidenceRoot = "docs\parity\freep-whole-window-visual-evidence",
    [string]$ManifestPath = "docs\parity\freep-whole-window-visual-evidence\artifact-manifest.json",
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

$resolvedRoot = Resolve-ToolRepoPath -Path $EvidenceRoot -RepoRoot $repoRoot
$resolvedManifest = Resolve-ToolRepoPath -Path $ManifestPath -RepoRoot $repoRoot
$summaryPath = Join-Path $resolvedRoot "summary.json"
$requiredPaths = @(
    $summaryPath,
    (Join-Path $resolvedRoot "report.md"),
    (Join-Path $resolvedRoot "report.html"),
    (Join-Path $resolvedRoot "wpf\manifest.json"),
    (Join-Path $resolvedRoot "avalonia\manifest.json")
)
foreach ($path in $requiredPaths) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "FreeP whole-window evidence artifact is missing: $path"
    }
}

$summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
if ($summary.schemaVersion -ne 1 -or $summary.scenarioCount -ne 31 -or $summary.pairedCaptureCount -ne 31) {
    throw "FreeP whole-window evidence has an unexpected schema or paired scenario count."
}
if ($summary.limitationCount -ne 0 -or ($summary.passCount + $summary.mismatchCount) -ne 31) {
    throw "FreeP whole-window evidence is incomplete: pass=$($summary.passCount), mismatch=$($summary.mismatchCount), limitation=$($summary.limitationCount)."
}
if (@($summary.comparisons).Count -ne 31 -or @($summary.wpf.captures).Count -ne 31 -or @($summary.avalonia.captures).Count -ne 31) {
    throw "FreeP whole-window evidence does not contain 31 comparison rows and 31 captures per host."
}
if ($summary.duplicateCaptureCount -ne 0) {
    throw "FreeP whole-window evidence contains $($summary.duplicateCaptureCount) unexpected duplicate capture(s)."
}

function Get-EvidencePath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)
    Join-Path $resolvedRoot ($RelativePath.Replace('/', [IO.Path]::DirectorySeparatorChar))
}

function Test-RecordedImage {
    param(
        [Parameter(Mandatory = $true)]$Capture,
        [Parameter(Mandatory = $true)][string]$Label
    )

    foreach ($property in @(@("fullImagePath", "fullImageSha256"), @("clientImagePath", "clientImageSha256"))) {
        $relativePath = $Capture.($property[0])
        $expectedHash = $Capture.($property[1])
        $path = Get-EvidencePath -RelativePath $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -le 0) {
            throw "$Label image is missing or empty: $relativePath"
        }
        $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $expectedHash) {
            throw "$Label hash is stale for '$relativePath': expected $expectedHash, actual $actualHash."
        }
    }

    if ($Capture.captureStatus -ne "complete" -or
        $Capture.pixelWidth -ne 1280 -or $Capture.pixelHeight -ne 760 -or
        [Math]::Abs([double]$Capture.dpiX - 96) -gt 0.1 -or [Math]::Abs([double]$Capture.dpiY - 96) -gt 0.1 -or
        $Capture.nonBackgroundPixelCount -le 0) {
        throw "$Label capture metadata is incomplete or not normalized to 1280x760 at 96 DPI."
    }
}

$wpfCaptures = @{}
$avaloniaCaptures = @{}
foreach ($capture in $summary.wpf.captures) { $wpfCaptures[$capture.scenarioId] = $capture }
foreach ($capture in $summary.avalonia.captures) { $avaloniaCaptures[$capture.scenarioId] = $capture }

foreach ($comparison in $summary.comparisons) {
    $scenarioId = $comparison.scenarioId
    if (-not $wpfCaptures.ContainsKey($scenarioId) -or -not $avaloniaCaptures.ContainsKey($scenarioId)) {
        throw "FreeP whole-window evidence is missing a host capture for '$scenarioId'."
    }
    Test-RecordedImage -Capture $wpfCaptures[$scenarioId] -Label "WPF $scenarioId"
    Test-RecordedImage -Capture $avaloniaCaptures[$scenarioId] -Label "Avalonia $scenarioId"

    if ($null -eq $comparison.wpfContentValidation -or -not $comparison.wpfContentValidation.isValid -or
        $null -eq $comparison.avaloniaContentValidation -or -not $comparison.avaloniaContentValidation.isValid) {
        throw "Decoded pixel-content validation is missing or failing for '$scenarioId'."
    }
    if ($null -eq $comparison.wpfTitleBarRasterValidation -or $null -eq $comparison.avaloniaTitleBarRasterValidation) {
        throw "Titlebar raster validation is missing for '$scenarioId'."
    }
    if ((-not $comparison.wpfTitleBarRasterValidation.isValid -or -not $comparison.avaloniaTitleBarRasterValidation.isValid) -and
        -not (@($comparison.mismatchCategories) -contains "app-owned-titlebar-raster")) {
        throw "A missing titlebar raster is not explicitly classified for '$scenarioId'."
    }
    if ($null -eq $comparison.pixelMetrics) {
        throw "Normalized pixel metrics are missing for '$scenarioId'."
    }
    foreach ($metricPath in @(
        @($comparison.wpfClientImagePath, $comparison.pixelMetrics.wpfImageSha256, "WPF client"),
        @($comparison.avaloniaClientImagePath, $comparison.pixelMetrics.avaloniaImageSha256, "Avalonia client"),
        @($comparison.pixelMetrics.heatmapPath, $comparison.pixelMetrics.heatmapSha256, "heatmap"))) {
        $path = Get-EvidencePath -RelativePath $metricPath[0]
        $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $metricPath[1]) {
            throw "$($metricPath[2]) hash is stale for '$scenarioId'."
        }
    }
    if (@($comparison.mismatchCategories) -contains "duplicate-capture" -and $comparison.classification -ne "mismatch") {
        throw "Duplicate-image evidence was allowed to pass for '$scenarioId'."
    }
}

$markdown = Get-Content -LiteralPath (Join-Path $resolvedRoot "report.md") -Raw
foreach ($requiredText in @("app-owned titlebar", "QAT", "status bar", "WPF full", "Avalonia full")) {
    if ($markdown.IndexOf($requiredText, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "FreeP whole-window report is missing required full-shell evidence text: $requiredText"
    }
}

function Get-RelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $Path.Substring($resolvedRoot.Length).TrimStart('\', '/').Replace('\', '/')
}

$files = Get-ChildItem -LiteralPath $resolvedRoot -Recurse -File |
    Where-Object { $_.FullName -ne $resolvedManifest } |
    Sort-Object { Get-RelativePath -Path $_.FullName } |
    ForEach-Object {
        if ($_.Length -le 0) { throw "FreeP whole-window evidence contains an empty artifact: $($_.FullName)" }
        [ordered]@{
            path = Get-RelativePath -Path $_.FullName
            bytes = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }

$inputPaths = @(
    "freep\FreeP.App.Presentation\WholeWindowVisualEvidenceContract.cs",
    "freep\FreeP.App.Presentation\FreePShellVisualMetrics.cs",
    "freep\FreeP.App.Host\WpfWholeWindowVisualEvidenceCapture.cs",
    "freep\FreeP.App.Host\MainWindow.cs",
    "freep\FreeP.App.Host\MainWindow.WholeWindowVisualEvidence.cs",
    "freep\FreeP.App.Avalonia\AvaloniaWholeWindowVisualEvidenceCapture.cs",
    "freep\FreeP.App.Avalonia\MainWindow.cs",
    "freep\FreeP.App.Avalonia\MainWindow.WholeWindowVisualEvidence.cs",
    "shared\Free.Shared.Ribbon\RibbonVisualMetrics.cs",
    "shared\Free.Shared.Ribbon.Wpf\RibbonTabControlFactory.cs",
    "shared\Free.Shared.Ribbon.Wpf\RibbonWpfRenderer.cs",
    "shared\Free.Shared.Ribbon.Avalonia\AvaloniaRibbonRenderer.cs",
    "tools\FreeP.RenderCompare\WholeWindowVisualEvidence.cs",
    "tools\FreeP.RenderCompare\ImageDiff.cs"
)
$inputs = foreach ($relativePath in $inputPaths) {
    $path = Join-Path $repoRoot $relativePath
    [ordered]@{
        path = $relativePath.Replace('\', '/')
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$artifact = [ordered]@{
    schemaVersion = 1
    generatedBy = "tools/Generate-FreePWholeWindowVisualEvidenceManifest.ps1"
    evidenceGeneratedAtUtc = $summary.generatedAtUtc
    scenarioCount = 31
    pairedCaptureCount = 31
    passCount = $summary.passCount
    mismatchCount = $summary.mismatchCount
    limitationCount = 0
    duplicateCaptureCount = $summary.duplicateCaptureCount
    fullPngCount = @($files | Where-Object path -Match '/full/.*\.png$').Count
    clientPngCount = @($files | Where-Object path -Match '/client/.*\.png$').Count
    diffPngCount = @($files | Where-Object path -Match '^diff/.*\.png$').Count
    fileCount = @($files).Count
    inputs = @($inputs)
    files = @($files)
}
if ($artifact.fullPngCount -ne 62 -or $artifact.clientPngCount -ne 62 -or $artifact.diffPngCount -ne 31) {
    throw "FreeP whole-window evidence PNG counts are incomplete."
}

$json = ($artifact | ConvertTo-Json -Depth 8) + [Environment]::NewLine
if ($Check) {
    Test-ToolGeneratedContentMatches -ExpectedContent $json -ActualPath $resolvedManifest -Label "FreeP whole-window visual evidence artifact manifest" -GeneratorScriptName "tools\Generate-FreePWholeWindowVisualEvidenceManifest.ps1" -NormalizeNewlines
    Write-Host "FreeP whole-window evidence is current: 31/31 paired, $($artifact.mismatchCount) explicit product mismatches, zero capture limitations."
    exit 0
}

[IO.File]::WriteAllText($resolvedManifest, $json, [Text.UTF8Encoding]::new($false))
Write-Host "Generated FreeP whole-window evidence manifest for $($artifact.fileCount) artifacts."
