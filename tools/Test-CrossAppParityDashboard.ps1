param(
    [string]$DashboardPath = "docs\parity\avalonia-wpf-cross-app-dashboard.json"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

function Assert-DashboardCondition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

$resolvedDashboardPath = Resolve-ToolRepoPath -Path $DashboardPath -RepoRoot $repoRoot
$dashboard = Read-ToolJson -Path $DashboardPath -RepoRoot $repoRoot -MissingMessage "Required generated cross-app dashboard is missing"

Assert-DashboardCondition ($dashboard.schema -eq "freex.parity.cross-app-dashboard.v3") "Cross-app dashboard schema must be v3."
Assert-DashboardCondition ($dashboard.scopeBoundary -match "visual parity") "Cross-app dashboard scope boundary must retain the no-visual-parity claim."

$requiredSources = @(
    "docs/parity/freew-dialog-harness/freew_dialog_route_inventory.json",
    "docs/parity/freew-dialog-harness/freew_dialog_visual_comparison.json",
    "docs/parity/freew-word-baseline-2026-08-16/manifest.json",
    "docs/parity/freew-shell-visual-2026-08-16/freew_shell_visual_evidence.json",
    "docs/parity/freew-word-chrome-2026-08-16/manifest.json",
    "docs/parity/freex-excel-chrome-comparison.md",
    "docs/parity/freex-excel-com-baseline-2026-08-14/manifest.json",
    "docs/parity/freex-avalonia-grid-corpus-2026-08-16/manifest.json",
    "tools/screenshots/screenshot_manifest.json",
    "tools/screenshots_avalonia_ribbon/screenshot_manifest.json",
    "docs/parity/freep-dialog-pane-visual-evidence/summary.json",
    "docs/parity/freep-dialog-pane-visual-evidence/artifact-manifest.json",
    "docs/parity/freep-whole-window-visual-evidence/summary.json",
    "docs/parity/freep-whole-window-visual-evidence/artifact-manifest.json",
    "docs/parity/freep-render-slideshow-media-parity-20260720.json",
    "docs/parity/freep-powerpoint-baseline-2026-08-14.json",
    "docs/parity/freep-powerpoint-recalibration-2026-08-15.json",
    "docs/parity/freep-powerpoint-chrome-2026-08-16/README.md",
    "docs/parity/freep-powerpoint-chrome-2026-08-16/manifest.json"
)
foreach ($source in $requiredSources) {
    Assert-DashboardCondition (@($dashboard.sources) -contains $source) "Cross-app dashboard is missing authoritative source '$source'."
}

$apps = @{}
foreach ($app in @($dashboard.apps)) {
    $apps[[string]$app.app] = $app
    foreach ($property in @("routeCoverage", "artifactCoverage", "pairedEvidence", "physicalEvidence", "authoritativeMicrosoftOfficeBaseline", "claimBoundary")) {
        Assert-DashboardCondition ($null -ne $app.renderedEvidence.PSObject.Properties[$property]) "$($app.app) rendered evidence is missing '$property'."
    }

    Assert-DashboardCondition ([bool]$app.renderedEvidence.authoritativeMicrosoftOfficeBaseline.PSObject.Properties["available"]) "$($app.app) baseline availability must be explicit."
    Assert-DashboardCondition ($app.renderedEvidence.claimBoundary -match "not|only") "$($app.app) rendered evidence must retain a coverage-only claim boundary."
}

Assert-DashboardCondition ($apps.ContainsKey("FreeX") -and $apps.ContainsKey("FreeW") -and $apps.ContainsKey("FreeP")) "Cross-app dashboard must contain FreeX, FreeW, and FreeP."

$freeX = $apps["FreeX"]
Assert-DashboardCondition ($freeX.renderedEvidence.routeCoverage.inventoryRouteCount -eq $freeX.dialogRoutes.totalRoutes) "FreeX route coverage must come from the dialog inventory."
Assert-DashboardCondition ($freeX.renderedEvidence.artifactCoverage.pairedManifestSurfaceCount -le $freeX.renderedEvidence.artifactCoverage.wpfManifestSurfaceCount) "FreeX paired manifest surfaces cannot exceed WPF manifest surfaces."
Assert-DashboardCondition ($freeX.renderedEvidence.artifactCoverage.pairedManifestSurfaceCount -le $freeX.renderedEvidence.artifactCoverage.avaloniaManifestSurfaceCount) "FreeX paired manifest surfaces cannot exceed Avalonia manifest surfaces."

$freeW = $apps["FreeW"]
$freeWArtifacts = $freeW.renderedEvidence.artifactCoverage
$freeWPaired = $freeW.renderedEvidence.pairedEvidence
Assert-DashboardCondition ([string]$freeW.renderedEvidence.canonicalComparison.kind -eq "canonical-inputs-only") "FreeW dashboard must expose the canonical comparison scope."
Assert-DashboardCondition ([string]$freeW.renderedEvidence.canonicalComparison.refreshInstruction -match "baseline.*refresh-route") "FreeW dashboard must expose the route refresh instruction."
Assert-DashboardCondition (($freeWArtifacts.pairedComparisonRowCount + $freeWArtifacts.avaloniaOnlyArtifactRowCount + $freeWArtifacts.stateNotApplicableRowCount + $freeWArtifacts.otherComparisonRowCount) -eq $freeWArtifacts.evidenceRowCount) "FreeW comparison rows must partition into paired, Avalonia-only, not-applicable, and non-paired classifications."
Assert-DashboardCondition ($freeWPaired.pairedScenarioCount -eq $freeWArtifacts.pairedComparisonRowCount) "FreeW paired evidence must use the paired comparison-row count."
Assert-DashboardCondition ($freeWPaired.mismatchCount -gt 0 -or $freeWPaired.passCount -gt 0) "FreeW paired evidence must retain comparison classifications."
Assert-DashboardCondition ($freeW.renderedEvidence.authoritativeMicrosoftOfficeBaseline.available -eq $true) "FreeW must report its committed Word reference baseline as available."
Assert-DashboardCondition ($freeW.renderedEvidence.authoritativeMicrosoftOfficeBaseline.artifactCount -eq 65) "FreeW Word baseline artifact count must remain explicit."
Assert-DashboardCondition ($freeW.renderedEvidence.shellChrome.pairedStaticCaptureCount -eq 40) "FreeW must report its 40 paired static shell captures."
Assert-DashboardCondition ($freeW.renderedEvidence.shellChrome.pairedContextualCaptureCount -eq 32) "FreeW must report its 32 paired contextual shell captures."
Assert-DashboardCondition ($freeW.renderedEvidence.shellChrome.avaloniaContextualMissingCount -eq 0) "FreeW must not retain contextual shell gaps after the paired capture."
Assert-DashboardCondition ($freeW.renderedEvidence.shellChrome.wordOfficeChromeReferenceCount -eq 36) "FreeW must report its 36 native Word chrome references."
Assert-DashboardCondition ([string]$freeW.renderedEvidence.shellChrome.wordOfficeChromeStatus -eq "complete") "FreeW Word chrome evidence must be complete."

Assert-DashboardCondition ($freeX.renderedEvidence.authoritativeMicrosoftOfficeBaseline.available -eq $true) "FreeX must report its committed Excel reference baseline as available."
Assert-DashboardCondition ($freeX.renderedEvidence.authoritativeMicrosoftOfficeBaseline.artifactCount -eq 45) "FreeX Excel baseline artifact count must remain explicit."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.status -eq "available-interactive-foreground") "FreeX must report its interactive foreground Excel evidence."
Assert-DashboardCondition ((@($freeX.renderedEvidence.physicalEvidence.limitations) -join " ") -match "36 Excel ribbon states") "FreeX dashboard must report the complete interactive ribbon capture."
Assert-DashboardCondition ((@($freeX.renderedEvidence.physicalEvidence.limitations) -join " ") -notmatch "unavailable during the 2026-08-16 refresh") "FreeX dashboard must not retain the resolved foreground-capture blocker."
Assert-DashboardCondition ($freeX.renderedEvidence.chromeCapture.wpfCaptureCount -eq 36) "FreeX must report its complete WPF chrome matrix."
Assert-DashboardCondition ($freeX.renderedEvidence.chromeCapture.avaloniaCaptureCount -eq 36) "FreeX must report its complete Avalonia chrome matrix."
Assert-DashboardCondition ($freeX.renderedEvidence.chromeCapture.fixedViewportComparisonCount -eq 27) "FreeX must report its fixed-viewport chrome comparison count."
Assert-DashboardCondition ([string]$freeX.renderedEvidence.gridCorpus.captureStatus -eq "complete") "FreeX Avalonia grid corpus must be complete."
Assert-DashboardCondition ($freeX.renderedEvidence.gridCorpus.totalAvaloniaCaptureCount -eq 35) "FreeX must report all 35 Avalonia grid corpus captures."

$freeP = $apps["FreeP"]
$freePLanes = @($freeP.renderedEvidence.routeCoverage.laneEntries)
Assert-DashboardCondition ($freePLanes.Count -eq 2) "FreeP rendered evidence must retain dialog-pane and whole-window lanes."
Assert-DashboardCondition ($freeP.renderedEvidence.routeCoverage.pairedScenarioCount -eq ($freePLanes | Measure-Object -Property pairedScenarioCount -Sum).Sum) "FreeP paired scenario total must equal the lane sum."
Assert-DashboardCondition ($freeP.renderedEvidence.artifactCoverage.wpfPngCount -gt 0 -and $freeP.renderedEvidence.artifactCoverage.avaloniaPngCount -gt 0) "FreeP artifact coverage must retain both WPF and Avalonia PNG counts."
Assert-DashboardCondition ($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.available -eq $true) "FreeP must report its committed PowerPoint reference baseline as available."
Assert-DashboardCondition ($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.artifactCount -eq 53) "FreeP tracked PowerPoint baseline artifact count must remain explicit."
Assert-DashboardCondition ($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.referenceReadyDecks -eq 27) "FreeP tracked PowerPoint ready-deck count must remain explicit."
Assert-DashboardCondition ($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.missingReferenceDecks -eq 0) "FreeP missing PowerPoint reference deck count must remain explicit."
Assert-DashboardCondition ($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.wpfAverageMeanPercent -gt 0) "FreeP current-source WPF recalibration must remain explicit."
Assert-DashboardCondition ($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.avaloniaAverageMeanPercent -gt 0) "FreeP current-source Avalonia recalibration must remain explicit."
Assert-DashboardCondition ($freeP.renderedEvidence.nativeOfficeChrome.expectedCaptureCount -eq 28) "FreeP must report its 28-state native PowerPoint chrome capture contract."
Assert-DashboardCondition ($freeP.renderedEvidence.nativeOfficeChrome.capturedReferenceCount -eq 28) "FreeP must report its 28 captured native PowerPoint chrome references."
Assert-DashboardCondition ([string]$freeP.renderedEvidence.nativeOfficeChrome.captureStatus -eq "complete") "FreeP native PowerPoint chrome evidence must be complete."

Write-Host "Cross-app parity dashboard schema and evidence aggregation guards passed."
