param(
    [string]$JsonPath = "docs\parity\conditional-format-opened-state-evidence.json",
    [string]$MarkdownPath = "docs\parity\conditional-format-opened-state-evidence.md",
    [switch]$Check
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

function ConvertTo-RepoRelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd('\')
    if ($fullPath.StartsWith($fullRoot + "\", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($fullRoot.Length + 1).Replace('/', '\')
    }

    return $fullPath.Replace('/', '\')
}

function Read-JsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolvedPath = Resolve-RepoPath $Path
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
        throw "Required generated parity input is missing: $resolvedPath"
    }

    Get-Content -LiteralPath $resolvedPath -Raw | ConvertFrom-Json
}

function Escape-MarkdownCell {
    param([string]$Value)

    if ($null -eq $Value -or $Value.Length -eq 0) {
        return ""
    }

    $Value.Replace('|', '\|')
}

function Resolve-ScreenshotPath {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$ManifestPath
    )

    if ($null -eq $Manifest -or [string]::IsNullOrWhiteSpace($Manifest.ScreenshotPath)) {
        return ""
    }

    if (Test-Path -LiteralPath $Manifest.ScreenshotPath -PathType Leaf) {
        return ConvertTo-RepoRelativePath $Manifest.ScreenshotPath
    }

    $manifestDirectory = Split-Path -Parent $ManifestPath
    $localCandidate = Join-Path $manifestDirectory ([System.IO.Path]::GetFileName($Manifest.ScreenshotPath))
    if (Test-Path -LiteralPath $localCandidate -PathType Leaf) {
        return ConvertTo-RepoRelativePath $localCandidate
    }

    return $Manifest.ScreenshotPath
}

function Get-CaptureTargetStatus {
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Subject,
        [Parameter(Mandatory = $true)][string]$Scenario,
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$ExpectedOpenedState,
        [string]$FallbackEvidencePath = "",
        [string]$FallbackEvidenceNote = ""
    )

    $resolvedManifestPath = Resolve-RepoPath $ManifestPath
    $fallbackEvidence = @()
    if (-not [string]::IsNullOrWhiteSpace($FallbackEvidencePath)) {
        $resolvedFallbackPath = Resolve-RepoPath $FallbackEvidencePath
        if (Test-Path -LiteralPath $resolvedFallbackPath -PathType Leaf) {
            $fallbackEvidence += ConvertTo-RepoRelativePath $resolvedFallbackPath
        }
    }

    if (-not (Test-Path -LiteralPath $resolvedManifestPath -PathType Leaf)) {
        return [ordered]@{
            id = $Id
            subject = $Subject
            scenario = $Scenario
            expectedOpenedState = $ExpectedOpenedState
            manifestPath = $ManifestPath
            captureStatus = "missing-manifest"
            screenshotPath = ""
            screenshotExists = $false
            retentionStatus = "needs-capture"
            blockReason = "No committed foreground capture manifest exists for this opened-state target."
            fallbackEvidence = @($fallbackEvidence)
            fallbackEvidenceNote = $FallbackEvidenceNote
        }
    }

    $manifest = Get-Content -LiteralPath $resolvedManifestPath -Raw | ConvertFrom-Json
    $resolvedScreenshot = Resolve-ScreenshotPath -Manifest $manifest -ManifestPath $resolvedManifestPath
    $screenshotExists = -not [string]::IsNullOrWhiteSpace($resolvedScreenshot) -and
        (Test-Path -LiteralPath (Resolve-RepoPath $resolvedScreenshot) -PathType Leaf -ErrorAction SilentlyContinue)
    if (-not $screenshotExists -and -not [string]::IsNullOrWhiteSpace($resolvedScreenshot)) {
        $screenshotExists = Test-Path -LiteralPath $resolvedScreenshot -PathType Leaf -ErrorAction SilentlyContinue
    }

    $captureStatus = if ([string]::IsNullOrWhiteSpace($manifest.CaptureStatus)) { "unknown" } else { [string]$manifest.CaptureStatus }
    $retentionStatus = if ($captureStatus -eq "complete" -and $screenshotExists) {
        "retained-opened-state-capture"
    }
    elseif ($captureStatus -eq "complete") {
        "manifest-missing-screenshot"
    }
    else {
        "blocked-or-incomplete"
    }

    $blockReason = if ([string]::IsNullOrWhiteSpace($manifest.BlockReason)) { "" } else { [string]$manifest.BlockReason }

    [ordered]@{
        id = $Id
        subject = $Subject
        scenario = $Scenario
        expectedOpenedState = $ExpectedOpenedState
        manifestPath = ConvertTo-RepoRelativePath $resolvedManifestPath
        captureStatus = $captureStatus
        screenshotPath = $resolvedScreenshot
        screenshotExists = [bool]$screenshotExists
        retentionStatus = $retentionStatus
        blockReason = $blockReason
        fallbackEvidence = @($fallbackEvidence)
        fallbackEvidenceNote = $FallbackEvidenceNote
    }
}

function Test-FileContentMatches {
    param(
        [Parameter(Mandatory = $true)][string]$ExpectedContent,
        [Parameter(Mandatory = $true)][string]$ActualPath,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $ActualPath -PathType Leaf)) {
        throw "$Label is missing. Run tools\Generate-ConditionalFormatOpenedStateEvidence.ps1 to create it."
    }

    $actual = (Get-Content -LiteralPath $ActualPath -Raw) -replace "`r`n", "`n"
    $expected = $ExpectedContent -replace "`r`n", "`n"
    if ($expected -cne $actual) {
        throw "$Label is out of date. Run tools\Generate-ConditionalFormatOpenedStateEvidence.ps1 to refresh it."
    }
}

$classification = Read-JsonFile "docs\parity\functional-parity-classification.json"
$conditionalRows = @($classification.rows | Where-Object {
        $_.classification -eq "pseudo-command-gallery-item" -and
        $_.tab -eq "Home" -and
        $_.group -eq "Styles" -and
        $_.nextAction -eq "Use the conditional-format popup/gallery parity lane for richer evidence instead of adding placeholder handlers."
    } | Sort-Object -Property id)

$expectedConditionalRows = [int]$classification.summary.'conditional-format-popup-gallery-row'
$runtimeCatalogItems = [int]$classification.summary.'conditional-format-popup-catalog-item'
if ($conditionalRows.Count -ne $expectedConditionalRows) {
    throw "Conditional-format popup/gallery row count mismatch: summary says $expectedConditionalRows, found $($conditionalRows.Count)."
}

$captureTargets = @(
    Get-CaptureTargetStatus `
        -Id "excel.conditional-formatting-gallery.opened" `
        -Subject "excel" `
        -Scenario "excel-conditional-formatting-gallery" `
        -ManifestPath "tools\foreground-captures\excel-conditional-formatting-gallery\excel-conditional-formatting-gallery_manifest.json" `
        -ExpectedOpenedState "Excel Home > Conditional Formatting opened popup/gallery"
    Get-CaptureTargetStatus `
        -Id "wpf.conditional-formatting-gallery.opened" `
        -Subject "wpf" `
        -Scenario "freex-conditional-formatting-gallery" `
        -ManifestPath "tools\foreground-captures\freex-conditional-formatting-gallery\freex-conditional-formatting-gallery_manifest.json" `
        -ExpectedOpenedState "FreeX WPF Home > Conditional Formatting opened popup/gallery"
    Get-CaptureTargetStatus `
        -Id "avalonia.conditional-formatting-gallery.opened" `
        -Subject "avalonia" `
        -Scenario "avalonia-conditional-formatting-gallery" `
        -ManifestPath "tools\foreground-captures\avalonia-conditional-formatting-gallery\avalonia-conditional-formatting-gallery_manifest.json" `
        -ExpectedOpenedState "FreeX Avalonia Home > Conditional Formatting opened popup/gallery" `
        -FallbackEvidencePath "docs\parity\dialog-visual-assets\avalonia-capture\dialog.ConditionalFormatNewRule.png" `
        -FallbackEvidenceNote "Existing Avalonia dialog route evidence is retained for dialog parity, but it is not opened popup/gallery evidence."
)

$completeOpenedStateTargets = @($captureTargets | Where-Object {
        $_.captureStatus -eq "complete" -and $_.screenshotExists -eq $true
    }).Count
$openedStateStatus = if ($completeOpenedStateTargets -eq $captureTargets.Count) {
    "paired-opened-state-captured"
}
else {
    "needs-paired-opened-state-capture"
}

$rows = foreach ($row in $conditionalRows) {
    [ordered]@{
        id = [string]$row.id
        tab = [string]$row.tab
        group = [string]$row.group
        classification = [string]$row.classification
        runtimeCatalogBacked = $true
        openedStateEvidenceStatus = $openedStateStatus
    }
}

$report = [ordered]@{
    schema = "freex.parity.conditional-format-opened-state-evidence.v1"
    generatedBy = "tools/Generate-ConditionalFormatOpenedStateEvidence.ps1"
    source = "docs/parity/functional-parity-classification.json"
    summary = [ordered]@{
        conditionalFormatPopupGalleryRows = $conditionalRows.Count
        conditionalFormatPopupCatalogItems = $runtimeCatalogItems
        captureTargets = $captureTargets.Count
        completeOpenedStateCaptureTargets = $completeOpenedStateTargets
        missingOrIncompleteOpenedStateCaptureTargets = $captureTargets.Count - $completeOpenedStateTargets
        openedStateStatus = $openedStateStatus
    }
    captureTargets = @($captureTargets)
    rows = @($rows)
}

$json = ($report | ConvertTo-Json -Depth 8) + [Environment]::NewLine

$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("# Conditional-format opened-state evidence")
[void]$md.AppendLine()
[void]$md.AppendLine("Generated by tools/Generate-ConditionalFormatOpenedStateEvidence.ps1. Do not edit by hand.")
[void]$md.AppendLine()
[void]$md.AppendLine("This report retains real opened-state capture evidence for the conditional-format popup/gallery lane. A target only counts as retained when its foreground manifest is complete and the referenced PNG resolves in the repo. Missing or blocked manifests stay visible as blockers; dialog-route fallback evidence is listed separately and is not counted as opened popup/gallery evidence.")
[void]$md.AppendLine()
[void]$md.AppendLine("## Summary")
[void]$md.AppendLine()
[void]$md.AppendLine("| Metric | Count |")
[void]$md.AppendLine("|---|---:|")
[void]$md.AppendLine("| Conditional-format popup/gallery classifier rows | $($conditionalRows.Count) |")
[void]$md.AppendLine("| Shared runtime popup catalog items | $runtimeCatalogItems |")
[void]$md.AppendLine("| Opened-state capture targets | $($captureTargets.Count) |")
[void]$md.AppendLine("| Complete opened-state capture targets | $completeOpenedStateTargets |")
[void]$md.AppendLine("| Missing or incomplete opened-state capture targets | $($captureTargets.Count - $completeOpenedStateTargets) |")
[void]$md.AppendLine()
[void]$md.AppendLine("## Capture Targets")
[void]$md.AppendLine()
[void]$md.AppendLine("| Target | Subject | Scenario | Status | PNG | Blocker |")
[void]$md.AppendLine("|---|---|---|---|---|---|")
foreach ($target in $captureTargets) {
    $png = if ($target.screenshotExists) { $target.screenshotPath } else { "" }
    [void]$md.AppendLine("| $(Escape-MarkdownCell $target.id) | $(Escape-MarkdownCell $target.subject) | $(Escape-MarkdownCell $target.scenario) | $(Escape-MarkdownCell $target.retentionStatus) | $(Escape-MarkdownCell $png) | $(Escape-MarkdownCell $target.blockReason) |")
}
[void]$md.AppendLine()
[void]$md.AppendLine("## Classifier Rows")
[void]$md.AppendLine()
[void]$md.AppendLine("All rows below remain runtime-catalog backed and await the paired Excel/WPF/Avalonia opened-state capture set before the lane can claim opened-state evidence.")
[void]$md.AppendLine()
[void]$md.AppendLine("| Command | Status |")
[void]$md.AppendLine("|---|---|")
foreach ($row in $rows) {
    [void]$md.AppendLine("| $(Escape-MarkdownCell $row.id) | $(Escape-MarkdownCell $row.openedStateEvidenceStatus) |")
}

$markdown = $md.ToString()

$resolvedJsonPath = Resolve-RepoPath $JsonPath
$resolvedMarkdownPath = Resolve-RepoPath $MarkdownPath

if ($Check) {
    Test-FileContentMatches -ExpectedContent $json -ActualPath $resolvedJsonPath -Label "Conditional-format opened-state evidence JSON"
    Test-FileContentMatches -ExpectedContent $markdown -ActualPath $resolvedMarkdownPath -Label "Conditional-format opened-state evidence Markdown"
    Write-Host "Conditional-format opened-state evidence is up to date."
    return
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedJsonPath) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedMarkdownPath) | Out-Null
Set-Content -LiteralPath $resolvedJsonPath -Value $json -Encoding utf8 -NoNewline
Set-Content -LiteralPath $resolvedMarkdownPath -Value $markdown -Encoding utf8 -NoNewline

Write-Host "Conditional-format popup/gallery rows: $($conditionalRows.Count)"
Write-Host "Complete opened-state capture targets: $completeOpenedStateTargets/$($captureTargets.Count)"
Write-Host "Wrote $(ConvertTo-RepoRelativePath $resolvedJsonPath)"
Write-Host "Wrote $(ConvertTo-RepoRelativePath $resolvedMarkdownPath)"
