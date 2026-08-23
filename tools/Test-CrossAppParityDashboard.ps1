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
Assert-DashboardCondition ($dashboard.wave -eq 193) "Cross-app dashboard must describe Wave193."
Assert-DashboardCondition ($dashboard.cumulativeAppSlices -eq 579) "Wave193 cumulative app-slice count must be 579."
Assert-DashboardCondition ([string]$dashboard.cumulativeAppSlicesStatus -eq "pending-final-integration-gates") "Wave193 app-slice count must remain contingent on final integration gates."
Assert-DashboardCondition ((@($dashboard.pendingIntegrationGates) -join ",") -eq "independent review,repository preflight,full Release build,default non-UI test lane") "Wave193 pending integration gates must remain explicit."
Assert-DashboardCondition ($dashboard.scopeBoundary -match "visual parity") "Cross-app dashboard scope boundary must retain the no-visual-parity claim."

$requiredSources = @(
    "docs/parity/freew-dialog-harness/freew_dialog_route_inventory.json",
    "docs/parity/freew-dialog-harness/freew_dialog_visual_comparison.json",
    "docs/parity/freew-dialog-harness/freew_font_visual_provenance.json",
    "docs/parity/freew-word-baseline-2026-08-16/manifest.json",
    "docs/parity/freew-shell-visual-2026-08-16/freew_shell_visual_evidence.json",
    "docs/parity/freew-word-chrome-2026-08-16/manifest.json",
    "docs/parity/freex-excel-chrome-comparison.md",
    "docs/parity/freex-excel-com-baseline-2026-08-14/manifest.json",
    "docs/parity/freex-avalonia-grid-corpus-2026-08-16/manifest.json",
    "tools/screenshots/screenshot_manifest.json",
    "tools/screenshots_avalonia_ribbon/screenshot_manifest.json",
    "docs/parity/avalonia-parity-wave192-freex-autofilter-font-color-20260823.md",
    "docs/parity/evidence/wave192-freex-autofilter-font-color-20260823/manifest.json",
    "docs/parity/avalonia-parity-wave193-freex-autofilter-no-fill-20260823.md",
    "docs/parity/evidence/wave193-freex-autofilter-no-fill-20260823/physical-result.json",
    "docs/parity/evidence/wave193-freex-autofilter-no-fill-20260823/manifest.json",
    "docs/parity/avalonia-parity-wave192-freew-font-checkbox-20260823.md",
    "docs/parity/avalonia-parity-wave193-freew-font-checkbox-glyph-20260823.md",
    "docs/parity/freep-dialog-pane-visual-evidence/summary.json",
    "docs/parity/freep-dialog-pane-visual-evidence/artifact-manifest.json",
    "docs/parity/freep-whole-window-visual-evidence/summary.json",
    "docs/parity/freep-whole-window-visual-evidence/artifact-manifest.json",
    "docs/parity/freep-render-slideshow-media-parity-20260720.json",
    "docs/parity/freep-powerpoint-baseline-2026-08-14.json",
    "docs/parity/freep-powerpoint-recalibration-2026-08-15.json",
    "docs/parity/freep-powerpoint-chrome-2026-08-16/README.md",
    "docs/parity/freep-powerpoint-chrome-2026-08-16/manifest.json",
    "docs/parity/freep-responsive-chrome-2026-08-16/README.md",
    "docs/parity/freep-responsive-chrome-2026-08-16/manifest.json",
    "docs/parity/avalonia-parity-wave192-freep-render-residual-20260823.md",
    "docs/parity/evidence/avalonia-parity-wave192-freep-evidence-20260823/metrics.json",
    "docs/parity/avalonia-parity-wave193-freep-render-residual-20260823.md",
    "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/metrics.json",
    "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/references.json",
    "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/images.json"
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
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterFontColorPassed -eq 1) "FreeX must report its passing production font-color AutoFilter lane."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterFontColorTotal -eq 1) "FreeX font-color AutoFilter total must remain explicit."
Assert-DashboardCondition ([string]$freeX.renderedEvidence.physicalEvidence.linuxAutoFilterFontColorStatus -eq "passed-production-x11") "FreeX font-color AutoFilter status must remain production-X11."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterNoFillPassed -eq 1) "FreeX No Fill AutoFilter lane must pass 1/1."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterNoFillTotal -eq 1) "FreeX No Fill AutoFilter total must remain explicit."
Assert-DashboardCondition ([string]$freeX.renderedEvidence.physicalEvidence.linuxAutoFilterNoFillStatus -eq "passed-production-x11") "FreeX No Fill AutoFilter status must remain production-X11."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave193FocusedAvaloniaGuardPassed -eq 3) "FreeX Wave193 Avalonia guard count must remain 3/3."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave193FocusedCoreIoGuardPassed -eq 8) "FreeX Wave193 Core.IO guard count must remain 8/8."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave193EvidenceArtifactCount -eq 15) "FreeX Wave193 evidence artifact count must remain 15/15."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave193EvidenceProvenanceFileCount -eq 9) "FreeX Wave193 provenance count must remain 9/9."
Assert-DashboardCondition ([string]$freeX.renderedEvidence.physicalEvidence.wave193PackageSemantics -match "SourcePatch.*no-row-delta") "FreeX Wave193 package semantics must retain SourcePatch/no-row-delta coverage."

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
Assert-DashboardCondition ($freeP.renderedEvidence.responsiveAppChrome.capturedPairCount -eq 24) "FreeP must report its 24 paired responsive app-chrome states."
Assert-DashboardCondition ($freeP.renderedEvidence.responsiveAppChrome.wpfCaptureCount -eq 24) "FreeP must report its 24 WPF responsive app-chrome captures."
Assert-DashboardCondition ($freeP.renderedEvidence.responsiveAppChrome.avaloniaCaptureCount -eq 24) "FreeP must report its 24 Avalonia responsive app-chrome captures."
Assert-DashboardCondition ([string]$freeP.renderedEvidence.responsiveAppChrome.captureStatus -eq "complete") "FreeP responsive app-chrome evidence must be complete."
Assert-DashboardCondition ($freeW.renderedEvidence.wave193.aggregateChangedPixels -eq 32861) "FreeW Wave193 Font aggregate must remain 32,861 changed pixels."
Assert-DashboardCondition ($freeW.renderedEvidence.wave193.aggregateDelta -eq -1335) "FreeW Wave193 Font delta must remain -1,335 changed pixels."
Assert-DashboardCondition ($freeW.renderedEvidence.wave193.relativeImprovement -eq 0.0390396537606738) "FreeW Wave193 Font relative improvement must remain 3.9040%."
Assert-DashboardCondition ($freeW.renderedEvidence.wave193.nonFontRowsCompared -eq 288 -and $freeW.renderedEvidence.wave193.nonFontRowsChanged -eq 0) "FreeW Wave193 non-Font row stability must remain 0/288 changes."
Assert-DashboardCondition ([string]$freeW.renderedEvidence.wave193.paintedBounds -eq "421 x 321") "FreeW Wave193 painted bounds must remain exact."
Assert-DashboardCondition ($freeP.renderedEvidence.wave193Integrity.status -eq "no-runtime-change-retained") "FreeP Wave193 must retain the no-runtime-change result."
Assert-DashboardCondition ($freeP.renderedEvidence.wave193Integrity.retainedRowCount -eq 53) "FreeP Wave193 retained row count must remain 53."
Assert-DashboardCondition ($freeP.renderedEvidence.wave193Integrity.retainedOfficeReferenceCount -eq 53) "FreeP Wave193 retained Office reference count must remain 53."
Assert-DashboardCondition ($freeP.renderedEvidence.wave193Integrity.retainedImageCount -eq 6) "FreeP Wave193 retained image count must remain 6."
Assert-DashboardCondition ($freeP.renderedEvidence.wave193Integrity.workerRunWpfAvaloniaRenderCount -eq 106 -and $freeP.renderedEvidence.wave193Integrity.workerRunComparisonCount -eq 159) "FreeP Wave193 worker-run full-render counts must remain explicit."
Assert-DashboardCondition ($freeP.renderedEvidence.wave193Integrity.fullRenderArtifactsRetained -eq $false) "FreeP Wave193 must distinguish worker-run renders from retained artifacts."
Assert-DashboardCondition ($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.avaloniaAverageMeanPercent -eq 0.9962) "FreeP Wave193 Avalonia/Office aggregate must remain explicit."
Assert-DashboardCondition ($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.avaloniaMaximumMeanPercent -eq 2.5815) "FreeP Wave193 Avalonia/Office maximum must remain explicit."

Write-Host "Cross-app parity dashboard schema and evidence aggregation guards passed."
