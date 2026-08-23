param(
    [string]$JsonPath = "docs\parity\avalonia-wpf-cross-app-dashboard.json",
    [string]$MarkdownPath = "docs\parity\avalonia-wpf-cross-app-dashboard.md",
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

function Get-JsonPropertyValue {
    param(
        [Parameter(Mandatory = $true)]$InputObject,
        [Parameter(Mandatory = $true)][string]$Name,
        $Default = 0
    )

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $Default
    }

    return $property.Value
}

function Get-CommandSurfaceRows {
    param([Parameter(Mandatory = $true)]$Sections)

    $rows = @()
    foreach ($section in @($Sections)) {
        if ($section.PSObject.Properties.Name -contains "groups" -and $section.groups) {
            foreach ($group in @($section.groups)) {
                $rows += @($group.rows)
            }
        }
        else {
            $rows += @($section.rows)
        }
    }

    return $rows
}

function Get-StatusCount {
    param(
        [Parameter(Mandatory = $true)]$Rows,
        [Parameter(Mandatory = $true)][string]$Status
    )

    return @($Rows | Where-Object { $_.status -eq $Status }).Count
}

function Get-UniqueValueCount {
    param(
        [Parameter(Mandatory = $true)]$Values
    )

    return @($Values | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object -Unique).Count
}

function Get-ScenarioRouteId {
    param(
        [Parameter(Mandatory = $true)][string]$ScenarioId
    )

    $lastDot = $ScenarioId.LastIndexOf('.')
    if ($lastDot -lt 1) {
        return $ScenarioId
    }

    return $ScenarioId.Substring(0, $lastDot)
}

function Get-ManifestFileCount {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$Pattern
    )

    return @($Manifest.files | Where-Object { [string]$_.path -like $Pattern }).Count
}

function Get-ResidualById {
    param(
        [Parameter(Mandatory = $true)]$Residuals,
        [Parameter(Mandatory = $true)][string]$Id
    )

    return @($Residuals | Where-Object { $_.Id -eq $Id } | Select-Object -First 1)[0]
}

function Get-FreeXNextSlice {
    param(
        [Parameter(Mandatory = $true)]$FunctionalMatrix,
        [Parameter(Mandatory = $true)]$FunctionalClassificationSummary,
        [Parameter(Mandatory = $true)]$DialogRoutes,
        [Parameter(Mandatory = $true)]$DialogVisualEvidence
    )

    $realBehaviorGaps = [int](Get-JsonPropertyValue $FunctionalClassificationSummary "real-behavior-gap")
    $pseudoGalleryItems = [int](Get-JsonPropertyValue $FunctionalClassificationSummary "pseudo-command-gallery-item")
    $nonClickControlRows = [int](Get-JsonPropertyValue $FunctionalClassificationSummary "non-click-control-inventory-row")
    $conditionalFormatPopupGalleryRows = [int](Get-JsonPropertyValue $FunctionalClassificationSummary "conditional-format-popup-gallery-row")
    $conditionalFormatPopupCatalogItems = [int](Get-JsonPropertyValue $FunctionalClassificationSummary "conditional-format-popup-catalog-item")
    $accountingSymbolPopupGalleryRows = [int](Get-JsonPropertyValue $FunctionalClassificationSummary "accounting-symbol-popup-gallery-row")
    $fontBorderPopupGalleryRows = [int](Get-JsonPropertyValue $FunctionalClassificationSummary "font-border-popup-gallery-row")
    $handlerQualifiedHelpRoutes = [int](Get-JsonPropertyValue $FunctionalClassificationSummary "handler-qualified-help-route")
    $sharedRibbonComboBoxControls = [int](Get-JsonPropertyValue $FunctionalClassificationSummary "shared-ribbon-combo-box-control")
    $catalogBackedPseudoGalleryRows = $conditionalFormatPopupGalleryRows + $accountingSymbolPopupGalleryRows + $fontBorderPopupGalleryRows
    $allDialogRoutesCaptured = $DialogRoutes.totalRoutes -eq $DialogRoutes.wpfCaptures -and
        $DialogRoutes.totalRoutes -eq $DialogRoutes.avaloniaCaptures
    $allDialogManifestSurfacesPaired = $DialogVisualEvidence.additionalAvaloniaCapturedSurfaceIds -eq 0 -and
        $DialogVisualEvidence.wpfManifestIdsWithoutAvaloniaPair -eq 0
    $visualReviewCandidateCount = [int]$DialogVisualEvidence.visualReviewCandidateCount
    $visualReviewTriageThreshold = [double]$DialogVisualEvidence.visualReviewTriageThreshold
    $highestTriageScore = [double]$DialogVisualEvidence.highestTriageScore

    if ($FunctionalMatrix.avaloniaMissing -eq 0 -and
        $realBehaviorGaps -eq 0 -and
        $allDialogRoutesCaptured -and
        $allDialogManifestSurfacesPaired -and
        $catalogBackedPseudoGalleryRows -eq $pseudoGalleryItems) {
        return "Command/dialog route coverage is complete for the generated inputs: all $pseudoGalleryItems pseudo-gallery rows are catalog-backed in classifier evidence ($conditionalFormatPopupGalleryRows conditional-format rows over $conditionalFormatPopupCatalogItems runtime catalog items, $fontBorderPopupGalleryRows font/border rows, and $accountingSymbolPopupGalleryRows accounting-symbol rows), and dialog screenshot evidence has $($DialogVisualEvidence.pairedCapturedSurfaceIds) paired WPF/Avalonia manifest surface ids with $($DialogVisualEvidence.additionalAvaloniaCapturedSurfaceIds) Avalonia-only ids. This is coverage and size-comparability evidence, not visual parity. The paired screenshots retain $visualReviewCandidateCount unresolved high-delta visual review candidates at triage score >= $visualReviewTriageThreshold (highest $highestTriageScore); keep those candidates separate from $($DialogVisualEvidence.pairedDimensionMismatches) scale-aware dimension mismatch rows and $($DialogVisualEvidence.pairedRawPixelDimensionMismatches) raw PNG pixel dimension mismatches."
    }

    if ($FunctionalMatrix.avaloniaMissing -eq 0 -and $realBehaviorGaps -eq 0 -and $allDialogRoutesCaptured) {
        return "Command/dialog route coverage is complete for the generated inputs; $catalogBackedPseudoGalleryRows of $pseudoGalleryItems pseudo-gallery rows are catalog-backed in classifier evidence, and dialog screenshot evidence currently has $($DialogVisualEvidence.additionalAvaloniaCapturedSurfaceIds) Avalonia-only manifest ids plus $($DialogVisualEvidence.wpfManifestIdsWithoutAvaloniaPair) WPF-only manifest ids. Continue catalog/evidence cleanup and visual review before claiming full visual parity."
    }

    if ($FunctionalMatrix.avaloniaMissing -gt 0 -or $realBehaviorGaps -gt 0) {
        return "Resolve generated Avalonia command-binding or real behavior gaps before taking additional evidence slices."
    }

    return "Refresh paired WPF/Avalonia dialog evidence until every generated dialog route has current captures."
}

$resolvedJsonPath = Resolve-ToolRepoPath -Path $JsonPath -RepoRoot $repoRoot
$resolvedMarkdownPath = Resolve-ToolRepoPath -Path $MarkdownPath -RepoRoot $repoRoot
$tempRoot = New-ToolTemporaryDirectory -Prefix "freex-cross-app-parity-dashboard-"
$tempJsonPath = Join-Path $tempRoot "avalonia-wpf-cross-app-dashboard.json"
$tempMarkdownPath = Join-Path $tempRoot "avalonia-wpf-cross-app-dashboard.md"

try {
    $commandInventory = Read-ToolJson -Path "docs\parity\command-inventory.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $functional = Read-ToolJson -Path "docs\parity\functional-parity.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $functionalClassification = Read-ToolJson -Path "docs\parity\functional-parity-classification.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $dialogInventory = Read-ToolJson -Path "docs\parity\dialog-parity-inventory.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $dialogVisualEvidence = Read-ToolJson -Path "docs\parity\dialog-visual-evidence-summary.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freeXWpfManifest = Read-ToolJson -Path "docs\parity\dialog-visual-assets\wpf-capture\manifest.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freeXAvaloniaManifest = Read-ToolJson -Path "docs\parity\dialog-visual-assets\avalonia-capture\manifest.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freeXOfficeBaseline = Read-ToolJson -Path "docs\parity\freex-excel-com-baseline-2026-08-14\manifest.json" -RepoRoot $repoRoot -MissingMessage "Required Excel Office baseline manifest is missing"
    $freeXAvaloniaGridCorpus = Read-ToolJson -Path "docs\parity\freex-avalonia-grid-corpus-2026-08-16\manifest.json" -RepoRoot $repoRoot -MissingMessage "Required FreeX Avalonia grid corpus manifest is missing"
    $freeXWpfRibbonManifest = Read-ToolJson -Path "tools\screenshots\screenshot_manifest.json" -RepoRoot $repoRoot -MissingMessage "Required FreeX WPF ribbon capture manifest is missing"
    $freeXAvaloniaRibbonManifest = Read-ToolJson -Path "tools\screenshots_avalonia_ribbon\screenshot_manifest.json" -RepoRoot $repoRoot -MissingMessage "Required FreeX Avalonia ribbon capture manifest is missing"
    $freew = Read-ToolJson -Path "docs\parity\freew-command-inventory.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freeWRouteInventory = Read-ToolJson -Path "docs\parity\freew-dialog-harness\freew_dialog_route_inventory.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freeWVisualComparison = Read-ToolJson -Path "docs\parity\freew-dialog-harness\freew_dialog_visual_comparison.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    if ($null -eq $freeWVisualComparison.scope -or [string]$freeWVisualComparison.scope.kind -ne "canonical-inputs-only") {
        throw "FreeW visual comparison must declare canonical-inputs-only scope before the cross-app dashboard can be generated."
    }
    $freeWOfficeBaseline = Read-ToolJson -Path "docs\parity\freew-word-baseline-2026-08-16\manifest.json" -RepoRoot $repoRoot -MissingMessage "Required Word Office baseline manifest is missing"
    $freeWShellVisualEvidence = Read-ToolJson -Path "docs\parity\freew-shell-visual-2026-08-16\freew_shell_visual_evidence.json" -RepoRoot $repoRoot -MissingMessage "Required FreeW shell visual evidence is missing"
    $freep = Read-ToolJson -Path "docs\parity\freep-command-parity-inventory.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freePDialogVisualEvidence = Read-ToolJson -Path "docs\parity\freep-dialog-pane-visual-evidence\summary.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freePDialogArtifactManifest = Read-ToolJson -Path "docs\parity\freep-dialog-pane-visual-evidence\artifact-manifest.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freePWholeWindowVisualEvidence = Read-ToolJson -Path "docs\parity\freep-whole-window-visual-evidence\summary.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freePWholeWindowArtifactManifest = Read-ToolJson -Path "docs\parity\freep-whole-window-visual-evidence\artifact-manifest.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freePRenderParity = Read-ToolJson -Path "docs\parity\freep-render-slideshow-media-parity-20260720.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freePNativePickerEvidence = Read-ToolJson -Path "docs\parity\freep-native-picker-human-evidence.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freePOfficeBaseline = Read-ToolJson -Path "docs\parity\freep-powerpoint-baseline-2026-08-14.json" -RepoRoot $repoRoot -MissingMessage "Required PowerPoint Office baseline manifest is missing"
    $freePOfficeRecalibration = Read-ToolJson -Path "docs\parity\freep-powerpoint-recalibration-2026-08-15.json" -RepoRoot $repoRoot -MissingMessage "Required PowerPoint current-source recalibration is missing"
    $freePWave192Metrics = Read-ToolJson -Path "docs\parity\evidence\avalonia-parity-wave192-freep-evidence-20260823\metrics.json" -RepoRoot $repoRoot -MissingMessage "Required Wave192 FreeP current-source metrics are missing"
    $freePPowerPointChrome = Read-ToolJson -Path "docs\parity\freep-powerpoint-chrome-2026-08-16\manifest.json" -RepoRoot $repoRoot -MissingMessage "Required PowerPoint chrome capture manifest is missing"
    $freePResponsiveChrome = Read-ToolJson -Path "docs\parity\freep-responsive-chrome-2026-08-16\manifest.json" -RepoRoot $repoRoot -MissingMessage "Required FreeP responsive chrome capture manifest is missing"

    $commandRows = Get-CommandSurfaceRows $commandInventory.commandSurfaceRows
    $freeXFunctionalMatrix = [ordered]@{
        totalCommands = [int]$functional.summary.totalCommands
        parity = [int]$functional.summary.parity
        avaloniaMissing = [int]$functional.summary.avaloniaMissing
        wpfMissing = [int]$functional.summary.wpfMissing
        bothMissing = [int]$functional.summary.bothMissing
        intentionalLinuxOmissions = [int]$functional.summary.intentionalLinuxOmissions
        realBehaviorGaps = [int](Get-JsonPropertyValue $functionalClassification.summary "real-behavior-gap")
        nonClickControlInventoryRows = [int](Get-JsonPropertyValue $functionalClassification.summary "non-click-control-inventory-row")
        pseudoCommandGalleryItems = [int](Get-JsonPropertyValue $functionalClassification.summary "pseudo-command-gallery-item")
        conditionalFormatPopupGalleryRows = [int](Get-JsonPropertyValue $functionalClassification.summary "conditional-format-popup-gallery-row")
        conditionalFormatPopupCatalogItems = [int](Get-JsonPropertyValue $functionalClassification.summary "conditional-format-popup-catalog-item")
        accountingSymbolPopupGalleryRows = [int](Get-JsonPropertyValue $functionalClassification.summary "accounting-symbol-popup-gallery-row")
        accountingSymbolPopupCatalogItems = [int](Get-JsonPropertyValue $functionalClassification.summary "accounting-symbol-popup-catalog-item")
        fontBorderPopupGalleryRows = [int](Get-JsonPropertyValue $functionalClassification.summary "font-border-popup-gallery-row")
        fontBorderPopupCatalogItems = [int](Get-JsonPropertyValue $functionalClassification.summary "font-border-popup-catalog-item")
        handlerQualifiedHelpRoutes = [int](Get-JsonPropertyValue $functionalClassification.summary "handler-qualified-help-route")
        sharedRibbonComboBoxControls = [int](Get-JsonPropertyValue $functionalClassification.summary "shared-ribbon-combo-box-control")
    }
    $freeXDialogRoutes = [ordered]@{
        totalRoutes = [int]$dialogInventory.summary.totalRoutes
        wpfCaptures = [int]$dialogInventory.summary.wpfCaptures
        avaloniaCaptures = [int]$dialogInventory.summary.avaloniaCaptures
        avaloniaHarnessRoutes = [int]$dialogInventory.summary.avaloniaHarnessRoutes
        sharedOrPresentationBacked = [int]$dialogInventory.summary.sharedOrPresentationBacked
    }
    $dimensionMismatchBuckets = $dialogVisualEvidence.summary.dimensionMismatchBuckets
    $visualReviewCandidates = Get-JsonPropertyValue $dialogVisualEvidence "visualReviewCandidates" @()
    $freeXDialogVisualEvidence = [ordered]@{
        wpfCapturedManifestSurfaces = [int]$dialogVisualEvidence.summary.wpfCapturedManifestSurfaces
        avaloniaCapturedManifestSurfaces = [int]$dialogVisualEvidence.summary.avaloniaCapturedManifestSurfaces
        pairedCapturedSurfaceIds = [int]$dialogVisualEvidence.summary.pairedCapturedSurfaceIds
        wpfManifestIdsWithoutAvaloniaPair = [int]$dialogVisualEvidence.summary.wpfManifestIdsWithoutAvaloniaPair
        additionalAvaloniaCapturedSurfaceIds = [int]$dialogVisualEvidence.summary.additionalAvaloniaCapturedSurfaceIds
        pairedDimensionMismatches = [int]$dialogVisualEvidence.summary.pairedDimensionMismatches
        pairedRawPixelDimensionMismatches = [int]$dialogVisualEvidence.summary.pairedRawPixelDimensionMismatches
        pairedCaptureScaleNormalizedDimensionMatches = [int]$dialogVisualEvidence.summary.pairedCaptureScaleNormalizedDimensionMatches
        pairedExpectedSizeMismatches = [int]$dialogVisualEvidence.summary.pairedExpectedSizeMismatches
        stalePromotedExpectedSizeEvidence = [int]$dialogVisualEvidence.summary.stalePromotedExpectedSizeEvidence
        contentVisualDimensionMismatchRows = [int](Get-JsonPropertyValue $dimensionMismatchBuckets "content/visual mismatch")
        evidenceLimitationDimensionMismatchRows = [int](Get-JsonPropertyValue $dimensionMismatchBuckets "evidence limitation")
        expectedPlatformNativeDimensionMismatchRows = [int](Get-JsonPropertyValue $dimensionMismatchBuckets "expected platform/native difference")
        realLogicalSizeMismatchRows = [int](Get-JsonPropertyValue $dimensionMismatchBuckets "real logical-size mismatch")
        policyAcceptedNativeDifferences = [int](Get-JsonPropertyValue $dialogVisualEvidence.summary "policyAcceptedNativeDifferences")
        visualReviewTriageThreshold = [double](Get-JsonPropertyValue $dialogVisualEvidence.summary "visualReviewTriageThreshold")
        visualReviewTriageThresholdRationale = [string](Get-JsonPropertyValue $dialogVisualEvidence.summary "visualReviewTriageThresholdRationale" "")
        visualReviewCandidateCount = [int](Get-JsonPropertyValue $dialogVisualEvidence.summary "visualReviewCandidateCount")
        highestTriageScore = [double](Get-JsonPropertyValue $dialogVisualEvidence.summary "highestTriageScore")
        visualReviewCandidateSurfaceIds = @($visualReviewCandidates | ForEach-Object { [string]$_.id })
        visualReviewCandidates = @($visualReviewCandidates | ForEach-Object {
                [ordered]@{
                    id = [string]$_.id
                    triageScore = [double]$_.triageScore
                    reviewStatus = [string]$_.reviewStatus
                    logicalDimensionMatch = [bool]$_.logicalDimensionMatch
                    dimensionMismatchBucket = $_.dimensionMismatchBucket
                }
            })
    }
    $freeXRenderedEvidence = [ordered]@{
        sourceFiles = @(
            "docs/parity/dialog-parity-inventory.json",
            "docs/parity/dialog-visual-evidence-summary.json",
            "docs/parity/dialog-visual-assets/wpf-capture/manifest.json",
            "docs/parity/dialog-visual-assets/avalonia-capture/manifest.json",
            "docs/parity/freex-excel-foreground-capture-2026-08-16.md",
            "docs/parity/freex-excel-chrome-comparison.md",
            "tools/screenshots/screenshot_manifest.json",
            "tools/screenshots_avalonia_ribbon/screenshot_manifest.json",
            "docs/parity/freex-excel-com-baseline-2026-08-14/manifest.json",
            "docs/parity/freex-avalonia-grid-corpus-2026-08-16/manifest.json",
            "docs/parity/avalonia-parity-wave183-freex-namebox-overlay-20260823.md",
            "docs/parity/avalonia-parity-wave184-freex-autofilter-20260823.md",
            "docs/parity/avalonia-parity-wave185-freex-autofilter-sort-20260823.md",
            "docs/parity/avalonia-parity-wave186-freex-autofilter-text-20260823.md",
            "docs/parity/avalonia-parity-wave187-freex-autofilter-numeric-20260823.md",
            "docs/parity/avalonia-parity-wave188-freex-autofilter-numeric-20260823.md",
            "docs/parity/avalonia-parity-wave189-freex-autofilter-date-20260823.md",
            "docs/parity/avalonia-parity-wave190-freex-autofilter-date-20260823.md",
            "docs/parity/evidence/wave190-freex-autofilter-date-20260823/manifest.json",
            "docs/parity/avalonia-parity-wave191-freex-autofilter-color-20260823.md",
            "docs/parity/evidence/wave191-freex-autofilter-color-20260823/manifest.json",
            "docs/parity/avalonia-parity-wave192-freex-autofilter-font-color-20260823.md",
            "docs/parity/evidence/wave192-freex-autofilter-font-color-20260823/manifest.json"
        )
        routeCoverage = [ordered]@{
            inventoryRouteCount = $freeXDialogRoutes.totalRoutes
            wpfRouteEvidenceCount = $freeXDialogRoutes.wpfCaptures
            avaloniaRouteEvidenceCount = $freeXDialogRoutes.avaloniaCaptures
            pairedRouteEvidenceCount = [Math]::Min($freeXDialogRoutes.wpfCaptures, $freeXDialogRoutes.avaloniaCaptures)
        }
        artifactCoverage = [ordered]@{
            wpfManifestSurfaceCount = @($freeXWpfManifest.surfaces).Count
            avaloniaManifestSurfaceCount = @($freeXAvaloniaManifest.surfaces).Count
            pairedManifestSurfaceCount = $freeXDialogVisualEvidence.pairedCapturedSurfaceIds
            nonBlankPngFailures = [int](Get-JsonPropertyValue $dialogVisualEvidence.summary "nonBlankPngFailures")
            rawPngDimensionMismatchRows = $freeXDialogVisualEvidence.pairedRawPixelDimensionMismatches
            dpiNormalizedDimensionMatches = $freeXDialogVisualEvidence.pairedCaptureScaleNormalizedDimensionMatches
        }
        pairedEvidence = [ordered]@{
            pairedSurfaceCount = $freeXDialogVisualEvidence.pairedCapturedSurfaceIds
            scaleAwareDimensionMismatchCount = $freeXDialogVisualEvidence.pairedDimensionMismatches
            unresolvedVisualReviewCandidateCount = $freeXDialogVisualEvidence.visualReviewCandidateCount
            highestTriageScore = $freeXDialogVisualEvidence.highestTriageScore
            evidenceClassification = "paired WPF/Avalonia app-owned dialog screenshots and manifest metadata; not a visual-parity result"
        }
        chromeCapture = [ordered]@{
            excelReferenceCount = 36
            wpfCaptureCount = @($freeXWpfRibbonManifest.Captures).Count
            avaloniaCaptureCount = @($freeXAvaloniaRibbonManifest.Captures).Count
            fixedViewportComparisonCount = 27
            coverageOnlyComparisonCount = 9
            wpfMeanPixelDiffPercent = 13.936596740910273
            avaloniaMeanPixelDiffPercent = 15.639134449147328
            evidenceClassification = "complete foreground Excel/WPF/Avalonia ribbon matrices; fixed-width deltas are triage only"
        }
        gridCorpus = [ordered]@{
            captureStatus = [string]$freeXAvaloniaGridCorpus.captureStatus
            chartCaptureCount = [int]$freeXAvaloniaGridCorpus.counts.chart
            cellStyleCaptureCount = [int]$freeXAvaloniaGridCorpus.counts.'cell-style'
            pivotCaptureCount = [int]$freeXAvaloniaGridCorpus.counts.pivot
            totalAvaloniaCaptureCount = [int]$freeXAvaloniaGridCorpus.counts.total
            evidenceClassification = [string]$freeXAvaloniaGridCorpus.comparisonBoundary
        }
        physicalEvidence = [ordered]@{
            status = "available-interactive-foreground"
            captureMode = "foreground-owned Excel ribbon/dialog capture, generated WPF/Avalonia dialog render manifests, and authoritative Linux X11 input probes"
            noComStatus = "not-applicable"
            linuxNameBoxParityPassed = 1
            linuxNameBoxParityTotal = 1
            linuxNameBoxInteractionPassed = 8
            linuxNameBoxInteractionTotal = 8
            linuxAutoFilterRecalculationPassed = 1
            linuxAutoFilterRecalculationTotal = 1
            linuxAutoFilterSortPersistencePassed = 1
            linuxAutoFilterSortPersistenceTotal = 1
            linuxAutoFilterTextCriteriaPassed = 2
            linuxAutoFilterTextCriteriaTotal = 2
            linuxAutoFilterNumericCriteriaPassed = 2
            linuxAutoFilterNumericCriteriaTotal = 2
            linuxAutoFilterNumericCriteriaStatus = "passed-production-x11"
            linuxAutoFilterDateCriteriaPassed = 2
            linuxAutoFilterDateCriteriaTotal = 2
            linuxAutoFilterDateCriteriaStatus = "passed-production-x11"
            linuxAutoFilterFillColorPassed = 1
            linuxAutoFilterFillColorTotal = 1
            linuxAutoFilterFillColorStatus = "passed-production-x11"
            linuxAutoFilterFontColorPassed = 1
            linuxAutoFilterFontColorTotal = 1
            linuxAutoFilterFontColorStatus = "passed-production-x11"
            limitations = @(
                "The 2026-08-16 interactive run captured 36 foreground ribbon states for each of Excel, WPF, and Avalonia, including Draw at all four widths, plus six guarded Excel popup/dialog surfaces. The 27 fixed-viewport triage rows average 13.937% RGB delta for WPF and 15.639% for Avalonia versus Excel; nine maximized rows are coverage-only, not an acceptance threshold.",
                "The ribbon harness creates a blank workbook before tab discovery so the enabled Draw tab is materialized; the current manifest records no unavailable-tab skips.",
                "WPF captured 116/116 app-host surfaces and Avalonia captured 180/181; the managed popup.nameBoxDropdown surface remains diagnostic-only, while the production Linux X11 Name Box crop and interaction lanes pass 1/1 and 8/8 respectively.",
                "The production Linux X11 AutoFilter apply/change/clear workflow passes 1/1 with exact SUBTOTAL postconditions 30 -> 10 -> 20 -> 30.",
                "The production Linux X11 AutoFilter sort/save/reopen workflow passes 1/1 with exact ascending and descending visible-order and package-state postconditions.",
                "The production Linux X11 AutoFilter text-criteria save/reopen workflows pass 2/2 for Begins With and Equals with exact visible-row and package-state postconditions.",
                "The production Linux X11 numeric AutoFilter save/reopen workflows pass 2/2 for Greater Than 50 and Equals 50 through the rendered non-first-column B1 glyph, with exact visible-row and package-state postconditions. Wave188 also makes fixture readiness deterministic and restricts the glyph bridge to left-click while retaining Button.Click for keyboard/accessibility activation.",
                "The production Linux X11 date AutoFilter lane passes 2/2 end to end. Before February 1 retains Jan01/Jan15 and saves lessThan=45323; After February 1 retains Mar15 and saves greaterThan=45323. Both use identity-checked Open Workbook dialogs and reopen with matching rendered-grid and semantic state, with a compact committed evidence bundle.",
                "The production Linux X11 fill-color AutoFilter save/reopen lane passes 1/1. It selects the rendered #00B050 swatch, retains North/East, reopens through the identity-checked production picker, reads semantic A4=East, and saves explicit Excel-compatible cellColor=1 with fill FF00B050. Four rendered captures, the saved package, and hash-verified source/image provenance are committed.",
                "The production Linux X11 font-color AutoFilter save/reopen lane passes 1/1. It pixel-gates and selects the rendered #00B050 font swatch, retains North/East, reopens through the identity-checked production picker, reads semantic A4=East, and independently verifies cellColor=0 with DXF font FF00B050. Four rendered captures, the saved package, and hash-verified source/image provenance are committed.",
                "The current-source range corpus retains 35 Avalonia grid captures: eight charts, seven cell styles, and 20 native PivotTable surfaces. These are renderer coverage evidence, not raw-pixel Office acceptance rows.",
                [string]$freeXOfficeBaseline.limitation
            )
        }
        authoritativeMicrosoftOfficeBaseline = [ordered]@{
            product = [string]$freeXOfficeBaseline.product
            available = [bool]$freeXOfficeBaseline.available
            status = [string]$freeXOfficeBaseline.status
            artifactCount = [int]$freeXOfficeBaseline.artifactCount
            limitation = [string]$freeXOfficeBaseline.limitation
            captureMode = [string]$freeXOfficeBaseline.captureMode
        }
        claimBoundary = "Coverage, manifest pairing, and DPI-normalized size comparability only; do not read these fields as visual parity."
    }
    $freeX = [ordered]@{
        app = "FreeX"
        commandSurface = [ordered]@{
            implemented = Get-StatusCount $commandRows "Implemented"
            partial = Get-StatusCount $commandRows "Partial"
            notImplemented = Get-StatusCount $commandRows "Not Implemented"
            deferred = Get-StatusCount $commandRows "Deferred"
            excluded = Get-StatusCount $commandRows "Excluded"
        }
        functionalMatrix = $freeXFunctionalMatrix
        dialogRoutes = $freeXDialogRoutes
        dialogVisualEvidence = $freeXDialogVisualEvidence
        renderedEvidence = $freeXRenderedEvidence
        nextSlice = Get-FreeXNextSlice `
            -FunctionalMatrix $freeXFunctionalMatrix `
            -FunctionalClassificationSummary $functionalClassification.summary `
            -DialogRoutes $freeXDialogRoutes `
            -DialogVisualEvidence $freeXDialogVisualEvidence
    }
    $freeX.nextSlice = "$($freeX.nextSlice) Production Linux evidence now covers Name Box (1/1 visual, 8/8 interaction), AutoFilter apply/change/clear recalculation (1/1), sort/save/reopen persistence (1/1), text-criteria save/reopen persistence (2/2), numeric Greater Than/Equals persistence (2/2), date criteria (2/2), fill-color save/reopen (1/1), and font-color save/reopen (1/1). Extend physical verification to No Fill, mixed-type, multi-column, and color criteria change/clear workflows."

    $freeWComparisonRows = @($freeWVisualComparison.rows)
    $freeWPairedComparisonRows = @($freeWComparisonRows | Where-Object { $_.captureStatus -eq "captured/captured" })
    $freeWAvaloniaExtensionRows = @($freeWComparisonRows | Where-Object { $_.classification -eq "avalonia-extension" })
    $freeWStateNotApplicableRows = @($freeWComparisonRows | Where-Object { $_.classification -eq "state-not-applicable" })
    $freeWPairedRouteIds = @($freeWPairedComparisonRows | ForEach-Object { Get-ScenarioRouteId ([string]$_.scenarioId) } | Sort-Object -Unique)
    $freeWAvaloniaExtensionRouteIds = @($freeWAvaloniaExtensionRows | ForEach-Object { Get-ScenarioRouteId ([string]$_.scenarioId) } | Sort-Object -Unique)
    $freeWRenderedEvidence = [ordered]@{
        sourceFiles = @(
            "docs/parity/freew-dialog-harness/freew_dialog_route_inventory.json",
            "docs/parity/freew-dialog-harness/freew_dialog_visual_comparison.json",
            "docs/parity/freew-dialog-harness/freew_font_visual_provenance.json",
            "docs/parity/freew-word-baseline-2026-08-16/manifest.json",
            "docs/parity/freew-shell-visual-2026-08-16/freew_shell_visual_evidence.json",
            "docs/parity/freew-word-chrome-2026-08-16/manifest.json",
            "docs/parity/avalonia-parity-wave184-freew-table-properties-cell-20260823.md",
            "freew/docs/parity/avalonia-parity-wave185-freew-page-setup-text-raster-20260823.md",
            "freew/docs/parity/avalonia-parity-wave186-freew-table-properties-text-raster-20260823.md",
            "docs/parity/avalonia-parity-wave187-freew-legal-notices-20260823.md",
            "docs/parity/avalonia-parity-wave188-freew-font-20260823.md",
            "docs/parity/avalonia-parity-wave189-freew-font-antialias-20260823.md",
            "docs/parity/avalonia-parity-wave190-freew-visual-20260823.md",
            "docs/parity/avalonia-parity-wave191-freew-font-template-20260823.md",
            "docs/parity/avalonia-parity-wave192-freew-font-checkbox-20260823.md"
        )
        canonicalComparison = [ordered]@{
            kind = [string]$freeWVisualComparison.scope.kind
            description = [string]$freeWVisualComparison.scope.description
            refreshInstruction = [string]$freeWVisualComparison.scope.refreshInstruction
        }
        routeCoverage = [ordered]@{
            inventoryScenarioCount = @($freeWRouteInventory.scenarios).Count
            inventoryRouteCount = Get-UniqueValueCount @($freeWRouteInventory.scenarios | ForEach-Object { $_.routeId })
            comparisonScenarioCount = $freeWComparisonRows.Count
            comparedRouteCount = Get-UniqueValueCount @($freeWComparisonRows | ForEach-Object { Get-ScenarioRouteId ([string]$_.scenarioId) })
            pairedRouteCount = $freeWPairedRouteIds.Count
            avaloniaOnlyRouteCount = $freeWAvaloniaExtensionRouteIds.Count
            stateNotApplicableRouteCount = Get-UniqueValueCount @($freeWStateNotApplicableRows | ForEach-Object { Get-ScenarioRouteId ([string]$_.scenarioId) })
        }
        artifactCoverage = [ordered]@{
            evidenceRowCount = $freeWComparisonRows.Count
            pairedComparisonRowCount = $freeWPairedComparisonRows.Count
            pairedWpfArtifactRowCount = $freeWPairedComparisonRows.Count
            pairedAvaloniaArtifactRowCount = $freeWPairedComparisonRows.Count
            avaloniaOnlyArtifactRowCount = $freeWAvaloniaExtensionRows.Count
            stateNotApplicableRowCount = $freeWStateNotApplicableRows.Count
            otherComparisonRowCount = $freeWComparisonRows.Count - $freeWPairedComparisonRows.Count - $freeWAvaloniaExtensionRows.Count - $freeWStateNotApplicableRows.Count
            artifactManifestAvailable = $false
            artifactKind = "embedded comparison-row content metrics and classifications"
        }
        shellChrome = [ordered]@{
            pairedStaticCaptureCount = [int]$freeWShellVisualEvidence.counts.pairedStaticChrome
            pairedContextualCaptureCount = [int]$freeWShellVisualEvidence.counts.pairedContextualChrome
            avaloniaContextualMissingCount = [int]$freeWShellVisualEvidence.counts.avaloniaContextualMissing
            wordOfficeChromeReferenceCount = [int]$freeWShellVisualEvidence.counts.wordOfficeChromeReferences
            wordOfficeChromeStatus = [string]$freeWShellVisualEvidence.nativeWordChrome.captureStatus
            evidenceClassification = "paired static and contextual WPF/Avalonia shell captures plus native Word ribbon references; cross-host pixels remain review-only"
        }
        pairedEvidence = [ordered]@{
            pairedScenarioCount = $freeWPairedComparisonRows.Count
            passCount = @($freeWPairedComparisonRows | Where-Object { $_.classification -eq "pass" }).Count
            mismatchCount = @($freeWPairedComparisonRows | Where-Object { $_.classification -eq "genuine-visual-mismatch" }).Count
            limitationCount = @($freeWPairedComparisonRows | Where-Object { $_.classification -in @("state-not-applicable", "limitation") }).Count
            avaloniaOnlyScenarioCount = $freeWAvaloniaExtensionRows.Count
            evidenceClassification = "paired WPF/Avalonia dialog comparison rows; mismatch classifications remain unresolved visual evidence, not a parity pass/fail"
        }
        physicalEvidence = [ordered]@{
            status = "available-app-owned-shell-captures"
            captureMode = "generated WPF/Avalonia dialog comparison rows plus committed full-window shell capture matrix"
            noComStatus = "not-present-in-inputs"
            limitations = @(
                "The committed shell matrix contains $($freeWShellVisualEvidence.counts.pairedStaticChrome) paired static and $($freeWShellVisualEvidence.counts.pairedContextualChrome) paired contextual WPF/Avalonia captures; no contextual Avalonia gap remains.",
                "Native Word chrome references are complete at $($freeWShellVisualEvidence.counts.wordOfficeChromeReferences) states; they are semantic review references, not raw Office-to-FreeW pixel-equivalence results.",
                "The focused table-properties.tab-cell pair improved from 12.240179% to 12.100893% changed pixels; it remains classified as a genuine visual mismatch.",
                "The six-state Page Setup family improved from 8.317758% to 8.196280% average changed pixels; all six states remain genuine visual mismatches.",
                "The seven-state Table Properties family improved from 8.259991% to 8.136310% average changed pixels; six states remain genuine visual mismatches and one remains a pass.",
                "Fresh current-source Legal Notices captures improved from 326094 to 324936 aggregate changed pixels; all four overflowing states improved, both short states were unchanged, and all six remain genuine visual mismatches.",
                "Fresh current-source Font dialog captures improved from 61396 to 58705 aggregate changed pixels across three states, a 4.383% relative reduction; WPF and Avalonia painted bounds now both measure 421 x 321, and all three states remain genuine visual mismatches.",
                "Wave189's route-local grayscale text raster reduces the three-state Font dialog aggregate from 58705 to 57620 changed pixels, a further 1.848% reduction; all three states improve and remain genuine visual mismatches.",
                "Wave190 aligns the Font dialog's Avalonia-only vertical cadence and reduces the three-state aggregate from 57620 to 44687 changed pixels, a 22.4453% relative reduction; all three states improve in both changed pixels and mean channel delta while retaining exact 421 x 321 painted bounds and genuine-mismatch classification.",
                "Wave191 aligns the Avalonia Font route's selected combo template with the WPF gradient, neutral border, and one-DIP cadence. The three-state aggregate falls from 44687 to 36053 changed pixels, a further 19.321055% reduction; all states improve, exact 421 x 321 painted bounds remain stable, and all three remain genuine mismatches.",
                "Wave192 aligns the Font route's checkbox/effect-lane registration and measured trailing margins. The three-state aggregate falls from 36053 to 34196 changed pixels, a further 5.1508% reduction; all states improve, exact 421 x 321 painted bounds remain stable, and all three remain genuine mismatches.",
                "Wave192's tracked Font provenance binds all three states and six host captures to dimensions, painted bounds, exact canonical comparison rows, source hashes, and external capture-manifest identities. The external PNGs remain uncommitted and require the capture hosts for pixel reproduction.",
                "Avalonia-only route/state rows are reported separately and are outside the WPF-authority pairing set.",
                [string]$freeWOfficeBaseline.limitation
            )
        }
        authoritativeMicrosoftOfficeBaseline = [ordered]@{
            product = [string]$freeWOfficeBaseline.product
            available = [bool]$freeWOfficeBaseline.available
            status = [string]$freeWOfficeBaseline.status
            artifactCount = [int]$freeWOfficeBaseline.artifactCount
            limitation = [string]$freeWOfficeBaseline.limitation
            captureMode = [string]$freeWOfficeBaseline.captureMode
        }
        claimBoundary = "Route and evidence-row coverage only; paired comparison rows and mismatch classifications do not establish Word visual parity."
    }
    $freeW = [ordered]@{
        app = "FreeW"
        commandInventory = [ordered]@{
            totalCommands = [int]$freew.summary.totalCommands
            bothProfiles = [int]$freew.summary.both
            wpfProfileOnly = [int]$freew.summary.wpfOnly
            avaloniaProfileOnly = [int]$freew.summary.avaloniaOnly
            missingWpfProfile = [int]$freew.summary.missingWpf
            missingAvaloniaProfile = [int]$freew.summary.missingAvalonia
            actionableMissingWpf = [int]$freew.summary.actionableMissingWpf
            actionableMissingAvalonia = [int]$freew.summary.actionableMissingAvalonia
            profileShapeOnly = [int]$freew.summary.profileShapeOnly
            commandIdAliases = [int]$freew.summary.commandIdAliases
            platformOnly = [int]$freew.summary.platformOnly
            deferred = [int]$freew.summary.deferred
            actionableGaps = [int]$freew.summary.actionableGaps
            classifiedRows = $true
        }
        renderedEvidence = $freeWRenderedEvidence
        nextSlice = "A committed current-source Word PNG baseline bundle is available for $($freeWOfficeBaseline.comparison.comparableRows) comparable rows, but $($freeWOfficeBaseline.comparison.failedRows) comparisons remain outside tolerance. Font dialog captures now improve from 61396 to 34196 aggregate changed pixels across Waves188-192 and match WPF painted bounds; continue with the remaining native checkbox/glyph raster tail, action-row/tab template edges, Legal Notices glyph/template tail, or the next classified pagination, drawing/object, chart, table, or WordArt residual."
    }

    $freePExternalPowerPointResidual = Get-ResidualById -Residuals $freePRenderParity.Residuals -Id "external-powerpoint-baseline"
    $freePRecordingHardwareResidual = Get-ResidualById -Residuals $freePRenderParity.Residuals -Id "real-recording-hardware"
    $freePDialogArtifactCoverage = [ordered]@{
        lane = "dialog-pane"
        routeInventoryCount = [int]$freePDialogVisualEvidence.routeCount
        renderedScenarioCount = [int]$freePDialogVisualEvidence.scenarioCount
        pairedCaptureCount = [int]$freePDialogVisualEvidence.pairedCaptureCount
        wpfPngCount = Get-ManifestFileCount -Manifest $freePDialogArtifactManifest -Pattern "wpf/*.png"
        avaloniaPngCount = Get-ManifestFileCount -Manifest $freePDialogArtifactManifest -Pattern "avalonia/*.png"
        diffPngCount = Get-ManifestFileCount -Manifest $freePDialogArtifactManifest -Pattern "diff/*.png"
        pngCount = [int]$freePDialogArtifactManifest.pngCount
        fileCount = [int]$freePDialogArtifactManifest.fileCount
    }
    $freePWholeWindowArtifactCoverage = [ordered]@{
        lane = "whole-window"
        routeInventoryCount = $null
        routeInventoryAvailable = $false
        renderedScenarioCount = [int]$freePWholeWindowVisualEvidence.scenarioCount
        pairedCaptureCount = [int]$freePWholeWindowVisualEvidence.pairedCaptureCount
        wpfFullPngCount = Get-ManifestFileCount -Manifest $freePWholeWindowArtifactManifest -Pattern "wpf/full/*.png"
        avaloniaFullPngCount = Get-ManifestFileCount -Manifest $freePWholeWindowArtifactManifest -Pattern "avalonia/full/*.png"
        wpfClientPngCount = Get-ManifestFileCount -Manifest $freePWholeWindowArtifactManifest -Pattern "wpf/client/*.png"
        avaloniaClientPngCount = Get-ManifestFileCount -Manifest $freePWholeWindowArtifactManifest -Pattern "avalonia/client/*.png"
        diffPngCount = Get-ManifestFileCount -Manifest $freePWholeWindowArtifactManifest -Pattern "diff/*.png"
        fullPngCount = [int]$freePWholeWindowArtifactManifest.fullPngCount
        clientPngCount = [int]$freePWholeWindowArtifactManifest.clientPngCount
        pngCount = [int]$freePWholeWindowArtifactManifest.fullPngCount + [int]$freePWholeWindowArtifactManifest.clientPngCount + [int]$freePWholeWindowArtifactManifest.diffPngCount
        fileCount = [int]$freePWholeWindowArtifactManifest.fileCount
    }
    $freePPairedPassCount = [int]$freePDialogVisualEvidence.passCount + [int]$freePWholeWindowVisualEvidence.passCount
    $freePPairedMismatchCount = [int]$freePDialogVisualEvidence.mismatchCount + [int]$freePWholeWindowVisualEvidence.mismatchCount
    $freePPairedLimitationCount = [int]$freePDialogVisualEvidence.limitationCount + [int]$freePWholeWindowVisualEvidence.limitationCount
    $freePRenderedEvidence = [ordered]@{
        sourceFiles = @(
            "docs/parity/freep-dialog-pane-visual-evidence/summary.json",
            "docs/parity/freep-dialog-pane-visual-evidence/artifact-manifest.json",
            "docs/parity/freep-whole-window-visual-evidence/summary.json",
            "docs/parity/freep-whole-window-visual-evidence/artifact-manifest.json",
            "docs/parity/freep-render-slideshow-media-parity-20260720.json",
            "docs/parity/freep-native-picker-human-evidence.json",
            "docs/parity/freep-powerpoint-baseline-2026-08-14.json",
            "docs/parity/freep-powerpoint-recalibration-2026-08-15.json",
            "docs/parity/freep-powerpoint-chrome-2026-08-16/README.md",
            "docs/parity/freep-powerpoint-chrome-2026-08-16/manifest.json",
            "docs/parity/freep-responsive-chrome-2026-08-16/README.md",
            "docs/parity/freep-responsive-chrome-2026-08-16/manifest.json",
            "docs/parity/freep-wave184-smartart-2026-08-23.md",
            "docs/parity/freep-wave185-bullets-autofit-20260823.md",
            "docs/parity/freep-wave186-surface3d-smartart-20260823.md",
            "docs/parity/avalonia-parity-wave187-freep-surface3d-20260823.md",
            "docs/parity/avalonia-parity-wave188-freep-smartart-text-20260823.md",
            "docs/parity/avalonia-parity-wave189-freep-smartart-text-20260823.md",
            "docs/parity/avalonia-parity-wave190-freep-smartart-text-origin-20260823.md",
            "docs/parity/avalonia-parity-wave190-freep-evidence-20260823/metrics.json",
            "docs/parity/avalonia-parity-wave191-freep-smartart-color-gate-20260823.md",
            "docs/parity/avalonia-parity-wave191-freep-evidence-20260823/metrics.json",
            "docs/parity/avalonia-parity-wave192-freep-render-residual-20260823.md",
            "docs/parity/evidence/avalonia-parity-wave192-freep-evidence-20260823/metrics.json"
        )
        routeCoverage = [ordered]@{
            laneEntries = @(
                [ordered]@{
                    lane = "dialog-pane"
                    routeInventoryCount = $freePDialogArtifactCoverage.routeInventoryCount
                    renderedScenarioCount = $freePDialogArtifactCoverage.renderedScenarioCount
                    pairedScenarioCount = $freePDialogArtifactCoverage.pairedCaptureCount
                },
                [ordered]@{
                    lane = "whole-window"
                    routeInventoryCount = $null
                    routeInventoryAvailable = $false
                    renderedScenarioCount = $freePWholeWindowArtifactCoverage.renderedScenarioCount
                    pairedScenarioCount = $freePWholeWindowArtifactCoverage.pairedCaptureCount
                }
            )
            renderedScenarioCount = $freePDialogArtifactCoverage.renderedScenarioCount + $freePWholeWindowArtifactCoverage.renderedScenarioCount
            pairedScenarioCount = $freePDialogArtifactCoverage.pairedCaptureCount + $freePWholeWindowArtifactCoverage.pairedCaptureCount
            routeCountBoundary = "Dialog route count is authoritative; whole-window source supplies scenario coverage but no separate route inventory."
        }
        artifactCoverage = [ordered]@{
            laneEntries = @($freePDialogArtifactCoverage, $freePWholeWindowArtifactCoverage)
            wpfPngCount = $freePDialogArtifactCoverage.wpfPngCount + $freePWholeWindowArtifactCoverage.wpfFullPngCount + $freePWholeWindowArtifactCoverage.wpfClientPngCount
            avaloniaPngCount = $freePDialogArtifactCoverage.avaloniaPngCount + $freePWholeWindowArtifactCoverage.avaloniaFullPngCount + $freePWholeWindowArtifactCoverage.avaloniaClientPngCount
            diffPngCount = $freePDialogArtifactCoverage.diffPngCount + $freePWholeWindowArtifactCoverage.diffPngCount
            pngCount = $freePDialogArtifactCoverage.pngCount + $freePWholeWindowArtifactCoverage.pngCount
            fileCount = $freePDialogArtifactCoverage.fileCount + $freePWholeWindowArtifactCoverage.fileCount
        }
        pairedEvidence = [ordered]@{
            pairedScenarioCount = $freePDialogArtifactCoverage.pairedCaptureCount + $freePWholeWindowArtifactCoverage.pairedCaptureCount
            passCount = $freePPairedPassCount
            mismatchCount = $freePPairedMismatchCount
            limitationCount = $freePPairedLimitationCount
            duplicateCaptureCount = [int]$freePWholeWindowVisualEvidence.duplicateCaptureCount
            evidenceClassification = "paired WPF/Avalonia app-owned dialog/pane and whole-window render evidence; pass counts are local comparison-gate results, not Microsoft Office parity"
        }
        nativeOfficeChrome = [ordered]@{
            expectedCaptureCount = [int]$freePPowerPointChrome.expectedCaptureCount
            captureStatus = [string]$freePPowerPointChrome.captureStatus
            capturedReferenceCount = [int]$freePPowerPointChrome.actualCaptureCount
            comparisonBoundary = [string]$freePPowerPointChrome.comparisonBoundary
            evidenceClassification = "guarded native PowerPoint ribbon reference lane; complete foreground capture is semantic review evidence, not raw pixel equivalence"
        }
        responsiveAppChrome = [ordered]@{
            expectedCaptureCount = [int]$freePResponsiveChrome.expectedCaptureCount
            captureStatus = [string]$freePResponsiveChrome.captureStatus
            capturedPairCount = [int]($freePResponsiveChrome.actualCaptureCount / 2)
            wpfCaptureCount = @($freePResponsiveChrome.captures | Where-Object { $_.host -eq "wpf" }).Count
            avaloniaCaptureCount = @($freePResponsiveChrome.captures | Where-Object { $_.host -eq "avalonia" }).Count
            tabCount = @($freePResponsiveChrome.mappedFreePTabs).Count
            widthCount = @($freePResponsiveChrome.widths).Count
            evidenceClassification = "complete responsive app-owned WPF/Avalonia ribbon/chrome capture matrix; paired state coverage, not a pixel-equivalence claim"
        }
        physicalEvidence = [ordered]@{
            status = "available-app-and-native-office-chrome"
            captureMode = "visible app-owned render targets with scenario-isolated processes plus guarded native PowerPoint ribbon capture"
            noComStatus = [string]$freePOfficeBaseline.captureMode
            limitations = @(
                [string]$freePOfficeBaseline.limitation,
                "Native PowerPoint ribbon capture status is '$($freePPowerPointChrome.captureStatus)' with $($freePPowerPointChrome.actualCaptureCount)/$($freePPowerPointChrome.expectedCaptureCount) mapped tab/width references.",
                "Responsive FreeP app chrome is '$($freePResponsiveChrome.captureStatus)' with $($freePResponsiveChrome.actualCaptureCount)/$($freePResponsiveChrome.expectedCaptureCount) WPF/Avalonia tab/width captures ($(@($freePResponsiveChrome.mappedFreePTabs).Count) tabs at $(@($freePResponsiveChrome.widths).Count) widths).",
                "The directly measured Wave187 authored-camera Surface3D target improved from 2.7438% to 2.7032% WPF/Office, 2.6220% to 2.5815% Avalonia/Office, and 1.0805% to 1.0804% WPF/Avalonia; deck26 and four ordinary-chart controls were preserved. Diagnostic reconstructed corpus estimates remain outside the canonical recalibration summary.",
                "The directly measured Wave188 imported IncreasingCircleProcess slide-09 target improved from 1.6516% to 0.9662% WPF/Office and from 1.6609% to 1.6009% WPF/Avalonia; Avalonia/Office remained 1.6879%. The WPF raster compensation is gated by an explicit imported-cache semantic flag and exact 12-shape topology, with ordinary authored phase-label controls excluded.",
                "Wave189 consumes the same semantic IncreasingCircle cache flag on Avalonia and improves slide-09 Avalonia/Office from 1.6879% to 1.5440% and WPF/Avalonia from 1.6009% to 1.3657%; WPF/Office remains 0.9662%, and neighboring SmartArt plus Surface3D controls are unchanged.",
                "Wave190 introduced an exact source-signature-gated Avalonia text-origin correction, but its white text-color discriminator did not match the imported cache's resolved black source. Current-source measurement before Wave191 therefore remained 1.6879% Avalonia/Office and 1.6009% WPF/Avalonia rather than activating the intended correction.",
                "Wave191 corrects that semantic color gate to the source-proven black run. Slide-09 Avalonia/Office improves from 1.6879% to 0.8675% and WPF/Avalonia from 1.6009% to 0.8540%; WPF/Office remains 0.9662%. Across all 53 slides, Avalonia/Office improves from 1.0117% to 0.9962% average and WPF/Avalonia from 0.6238% to 0.6097%, with measured controls unchanged.",
                "Wave192 retains no new runtime rendering change. Fresh 27-deck/53-slide evidence confirms the Wave191 aggregate, rejects an IncreasingCircle text-policy probe that regressed Avalonia/Office from 0.8675% to 0.8775%, and records that a general Surface3D correction needs a new Office-authored topology rather than a fixture-specific overlay.",
                [string]$freePRecordingHardwareResidual.Description,
                [string]$freePRecordingHardwareResidual.ArtifactStatus,
                [string]$freePNativePickerEvidence.reason
            )
        }
        authoritativeMicrosoftOfficeBaseline = [ordered]@{
            product = [string]$freePOfficeBaseline.product
            available = [bool]$freePOfficeBaseline.available
            status = [string]$freePOfficeBaseline.status
            artifactCount = [int]$freePOfficeBaseline.artifactCount
            limitation = [string]$freePOfficeBaseline.limitation
            captureMode = [string]$freePOfficeBaseline.captureMode
            referenceReadyDecks = [int]$freePOfficeBaseline.comparison.referenceReadyDecks
            missingReferenceDecks = [int]$freePOfficeBaseline.comparison.missingReferenceDecks
            currentSourceRevision = [string]$freePWave192Metrics.source.baseRevision
            wpfAverageMeanPercent = [double]$freePWave192Metrics.aggregate.wpfOfficeAverage
            wpfMaximumMeanPercent = [double]$freePWave192Metrics.aggregate.wpfOfficeMaximum
            avaloniaAverageMeanPercent = [double]$freePWave192Metrics.aggregate.avaloniaOfficeAverage
            avaloniaMaximumMeanPercent = [double]$freePWave192Metrics.aggregate.avaloniaOfficeMaximum
            rendererPairAverageMeanPercent = [double]$freePWave192Metrics.aggregate.wpfAvaloniaAverage
            rendererPairMaximumMeanPercent = [double]$freePWave192Metrics.aggregate.wpfAvaloniaMaximum
        }
        claimBoundary = "Route/scenario coverage, committed PNG manifests, and local WPF/Avalonia comparison results only; no PowerPoint visual-parity claim is made."
    }

    $freePNextSlice = "The tracked PowerPoint corpus has $($freePOfficeBaseline.artifactCount) COM-exported reference slides across $($freePOfficeBaseline.comparison.referenceReadyDecks) ready decks, with $($freePOfficeBaseline.comparison.missingReferenceDecks) deck missing references. Wave192's fresh 53-slide rerender preserves averages of $($freePWave192Metrics.aggregate.wpfOfficeAverage)% for WPF and $($freePWave192Metrics.aggregate.avaloniaOfficeAverage)% for Avalonia, with maxima of $($freePWave192Metrics.aggregate.wpfOfficeMaximum)% / $($freePWave192Metrics.aggregate.avaloniaOfficeMaximum)%. Surface3D deck25 remains the Avalonia/Office maximum at 2.5815% but needs a new authored topology for a general correction; the next executable existing-corpus residual is deck17 slide02 at 2.5360% Avalonia/Office and 2.9091% WPF/Avalonia."

    $freeP = [ordered]@{
        app = "FreeP"
        commandInventory = [ordered]@{
            totalCommands = [int]$freep.summary.totalCommands
            bothProfiles = [int]$freep.summary.both
            wpfOnly = [int]$freep.summary.wpfOnly
            avaloniaOnly = [int]$freep.summary.avaloniaOnly
            actionableMissingWpf = [int]$freep.summary.actionableMissingWpf
            actionableMissingAvalonia = [int]$freep.summary.actionableMissingAvalonia
            avaloniaGaps = [int]$freep.summary.avaloniaGaps
            knownDeferred = [int]$freep.summary.knownDeferred
            platformOnly = [int]$freep.summary.platformOnly
            commandIdAliases = [int]$freep.summary.commandIdAliases
            workflowEvidenceRows = [int](Get-JsonPropertyValue $freep.summary "workflowEvidenceRows")
        }
        renderedEvidence = $freePRenderedEvidence
        nextSlice = $freePNextSlice
    }

    $dashboard = [ordered]@{
        schema = "freex.parity.cross-app-dashboard.v3"
        scopeBoundary = "Generated counts prove command/profile routing, route and artifact coverage, screenshot manifest coverage, and DPI-normalized size comparability only. They do not prove visual parity, workflow completeness, or pixel-level equivalence. High-delta paired screenshot candidates, physical/no-COM limitations, and authoritative Microsoft Office baseline availability remain explicitly separate from coverage metrics."
        sources = @(
            "docs/parity/command-inventory.json",
            "docs/parity/functional-parity.json",
            "docs/parity/functional-parity-classification.json",
            "docs/parity/dialog-parity-inventory.json",
            "docs/parity/dialog-visual-evidence-summary.json",
            "docs/parity/dialog-visual-assets/wpf-capture/manifest.json",
            "docs/parity/dialog-visual-assets/avalonia-capture/manifest.json",
            "docs/parity/freex-excel-foreground-capture-2026-08-16.md",
            "docs/parity/freex-excel-chrome-comparison.md",
            "tools/screenshots/screenshot_manifest.json",
            "tools/screenshots_avalonia_ribbon/screenshot_manifest.json",
                    "docs/parity/freex-excel-com-baseline-2026-08-14/manifest.json",
                    "docs/parity/freex-avalonia-grid-corpus-2026-08-16/manifest.json",
                    "docs/parity/avalonia-parity-wave183-freex-namebox-overlay-20260823.md",
                    "docs/parity/avalonia-parity-wave184-freex-autofilter-20260823.md",
                    "docs/parity/avalonia-parity-wave185-freex-autofilter-sort-20260823.md",
                    "docs/parity/avalonia-parity-wave186-freex-autofilter-text-20260823.md",
                    "docs/parity/avalonia-parity-wave187-freex-autofilter-numeric-20260823.md",
                    "docs/parity/avalonia-parity-wave188-freex-autofilter-numeric-20260823.md",
                    "docs/parity/avalonia-parity-wave189-freex-autofilter-date-20260823.md",
                    "docs/parity/avalonia-parity-wave190-freex-autofilter-date-20260823.md",
                    "docs/parity/evidence/wave190-freex-autofilter-date-20260823/manifest.json",
                    "docs/parity/avalonia-parity-wave191-freex-autofilter-color-20260823.md",
                    "docs/parity/evidence/wave191-freex-autofilter-color-20260823/manifest.json",
                    "docs/parity/avalonia-parity-wave192-freex-autofilter-font-color-20260823.md",
                    "docs/parity/evidence/wave192-freex-autofilter-font-color-20260823/manifest.json",
            "docs/parity/freew-command-inventory.json",
            "docs/parity/freew-dialog-harness/freew_dialog_route_inventory.json",
            "docs/parity/freew-dialog-harness/freew_dialog_visual_comparison.json",
            "docs/parity/freew-dialog-harness/freew_font_visual_provenance.json",
            "docs/parity/freew-word-baseline-2026-08-16/manifest.json",
            "docs/parity/freew-shell-visual-2026-08-16/freew_shell_visual_evidence.json",
            "docs/parity/freew-word-chrome-2026-08-16/manifest.json",
            "docs/parity/avalonia-parity-wave184-freew-table-properties-cell-20260823.md",
            "freew/docs/parity/avalonia-parity-wave185-freew-page-setup-text-raster-20260823.md",
            "freew/docs/parity/avalonia-parity-wave186-freew-table-properties-text-raster-20260823.md",
            "docs/parity/avalonia-parity-wave187-freew-legal-notices-20260823.md",
            "docs/parity/avalonia-parity-wave188-freew-font-20260823.md",
            "docs/parity/avalonia-parity-wave189-freew-font-antialias-20260823.md",
            "docs/parity/avalonia-parity-wave190-freew-visual-20260823.md",
            "docs/parity/avalonia-parity-wave191-freew-font-template-20260823.md",
            "docs/parity/avalonia-parity-wave192-freew-font-checkbox-20260823.md",
            "docs/parity/freep-command-parity-inventory.json",
            "docs/parity/freep-dialog-pane-visual-evidence/summary.json",
            "docs/parity/freep-dialog-pane-visual-evidence/artifact-manifest.json",
            "docs/parity/freep-whole-window-visual-evidence/summary.json",
            "docs/parity/freep-whole-window-visual-evidence/artifact-manifest.json",
            "docs/parity/freep-render-slideshow-media-parity-20260720.json",
            "docs/parity/freep-native-picker-human-evidence.json",
            "docs/parity/freep-powerpoint-baseline-2026-08-14.json",
            "docs/parity/freep-powerpoint-recalibration-2026-08-15.json",
            "docs/parity/freep-powerpoint-chrome-2026-08-16/README.md",
            "docs/parity/freep-powerpoint-chrome-2026-08-16/manifest.json",
            "docs/parity/freep-responsive-chrome-2026-08-16/README.md",
            "docs/parity/freep-responsive-chrome-2026-08-16/manifest.json",
            "docs/parity/freep-wave184-smartart-2026-08-23.md",
            "docs/parity/freep-wave185-bullets-autofit-20260823.md",
            "docs/parity/freep-wave186-surface3d-smartart-20260823.md",
            "docs/parity/avalonia-parity-wave187-freep-surface3d-20260823.md",
            "docs/parity/avalonia-parity-wave188-freep-smartart-text-20260823.md",
            "docs/parity/avalonia-parity-wave189-freep-smartart-text-20260823.md",
            "docs/parity/avalonia-parity-wave190-freep-smartart-text-origin-20260823.md",
            "docs/parity/avalonia-parity-wave190-freep-evidence-20260823/metrics.json",
            "docs/parity/avalonia-parity-wave191-freep-smartart-color-gate-20260823.md",
            "docs/parity/avalonia-parity-wave191-freep-evidence-20260823/metrics.json",
            "docs/parity/avalonia-parity-wave192-freep-render-residual-20260823.md",
            "docs/parity/evidence/avalonia-parity-wave192-freep-evidence-20260823/metrics.json"
        )
        apps = @($freeX, $freeW, $freeP)
    }

    $json = ($dashboard | ConvertTo-Json -Depth 12) + "`n"
    Set-Content -LiteralPath $tempJsonPath -Value $json -NoNewline -Encoding UTF8

    $freeXVisualReviewMarkdownRows = @(
        foreach ($candidate in @($freeX.dialogVisualEvidence.visualReviewCandidates)) {
            "| $(ConvertTo-ToolMarkdownCell $candidate.id) | $($candidate.triageScore) | $($candidate.logicalDimensionMatch) | $(ConvertTo-ToolMarkdownCell $(if ([string]::IsNullOrWhiteSpace([string]$candidate.dimensionMismatchBucket)) { "none" } else { $candidate.dimensionMismatchBucket })) | $(ConvertTo-ToolMarkdownCell $candidate.reviewStatus) |"
        }
    )

    $md = @(
        "# Avalonia/WPF Cross-App Parity Dashboard",
        "",
        'Generated by `tools/Generate-CrossAppParityDashboard.ps1` from existing generated parity JSON. Do not edit by hand.',
        "",
        "> Generated counts prove command/profile routing, route and artifact coverage, screenshot manifest coverage, and DPI-normalized size comparability only. They do not prove visual parity, workflow completeness, or pixel-level equivalence. High-delta paired screenshot candidates, physical/no-COM limitations, and authoritative Microsoft Office baseline availability remain explicitly separate from coverage metrics.",
        "",
        "## Summary",
        "",
        "| App | Primary evidence | Current generated state | Next slice |",
        "|---|---|---|---|",
        "| FreeX | Functional matrix, classifier, dialog inventory, dialog visual evidence, command surface | $($freeX.functionalMatrix.totalCommands) functional commands; $($freeX.functionalMatrix.parity) command inventory parity; $($freeX.functionalMatrix.avaloniaMissing) Avalonia-missing; $($freeX.functionalMatrix.realBehaviorGaps) real classified binding gaps; $($freeX.functionalMatrix.pseudoCommandGalleryItems) catalog-backed pseudo-gallery rows ($($freeX.functionalMatrix.conditionalFormatPopupGalleryRows) conditional-format, $($freeX.functionalMatrix.fontBorderPopupGalleryRows) font/border, $($freeX.functionalMatrix.accountingSymbolPopupGalleryRows) accounting-symbol); $($freeX.dialogRoutes.totalRoutes)/$($freeX.dialogRoutes.totalRoutes) dialog routes captured on WPF and Avalonia; $($freeX.dialogVisualEvidence.pairedCapturedSurfaceIds) paired screenshot surface ids, $($freeX.dialogVisualEvidence.additionalAvaloniaCapturedSurfaceIds) Avalonia-only ids, $($freeX.dialogVisualEvidence.wpfManifestIdsWithoutAvaloniaPair) WPF-only ids; $($freeX.dialogVisualEvidence.pairedDimensionMismatches) scale-aware dimension mismatches; $($freeX.dialogVisualEvidence.visualReviewCandidateCount) unresolved high-delta visual review candidates at triage score >= $($freeX.dialogVisualEvidence.visualReviewTriageThreshold) (highest $($freeX.dialogVisualEvidence.highestTriageScore)); $($freeX.dialogVisualEvidence.pairedRawPixelDimensionMismatches) raw PNG pixel dimension mismatches, of which $($freeX.dialogVisualEvidence.pairedCaptureScaleNormalizedDimensionMatches) normalize by capture DPI. These are coverage/triage metrics, not a visual-parity claim. | $($freeX.nextSlice) |",
        "| FreeW | Generated command inventory plus dialog rendered evidence | $($freeW.commandInventory.totalCommands) commands; $($freeW.commandInventory.bothProfiles) shared-profile; $($freeW.commandInventory.actionableMissingWpf) actionable WPF-missing; $($freeW.commandInventory.actionableMissingAvalonia) actionable Avalonia-missing; $($freeW.commandInventory.profileShapeOnly) profile-shape-only; $($freeW.commandInventory.commandIdAliases) command-id aliases; $($freeW.commandInventory.platformOnly) platform-only; $($freeW.commandInventory.deferred) deferred; $($freeW.renderedEvidence.artifactCoverage.evidenceRowCount) rendered comparison rows | $($freeW.nextSlice) |",
        "| FreeP | Generated command inventory plus dialog/whole-window rendered evidence | $($freeP.commandInventory.totalCommands) commands; $($freeP.commandInventory.bothProfiles) shared-profile; $($freeP.commandInventory.actionableMissingWpf) actionable WPF-missing; $($freeP.commandInventory.actionableMissingAvalonia) actionable Avalonia-missing; $($freeP.commandInventory.platformOnly) platform-only; $($freeP.commandInventory.workflowEvidenceRows) workflow evidence rows; $($freeP.renderedEvidence.pairedEvidence.pairedScenarioCount) paired rendered scenarios | $($freeP.nextSlice) |",
        "",
        "## Rendered Evidence Summary",
        "",
        "Route inventory, rendered/comparison rows, and committed PNG/file artifacts are separate measures. Office baseline availability is an artifact-availability statement, not a visual-parity claim.",
        "",
        "FreeW canonical comparison scope: **$($freeW.renderedEvidence.canonicalComparison.kind)**. $($freeW.renderedEvidence.canonicalComparison.description) $($freeW.renderedEvidence.canonicalComparison.refreshInstruction)",
        "",
        "| App | Route coverage | Artifact coverage | Paired WPF/Avalonia evidence | Physical/no-COM limitation | Authoritative Microsoft Office baseline |",
        "|---|---|---|---|---|---|",
        "| FreeX | $($freeX.renderedEvidence.routeCoverage.inventoryRouteCount) inventoried dialog routes; $($freeX.renderedEvidence.routeCoverage.pairedRouteEvidenceCount) paired route evidence rows | $($freeX.renderedEvidence.artifactCoverage.wpfManifestSurfaceCount) WPF + $($freeX.renderedEvidence.artifactCoverage.avaloniaManifestSurfaceCount) Avalonia dialog surfaces; complete $($freeX.renderedEvidence.chromeCapture.excelReferenceCount)/$($freeX.renderedEvidence.chromeCapture.wpfCaptureCount)/$($freeX.renderedEvidence.chromeCapture.avaloniaCaptureCount) Excel/WPF/Avalonia ribbon matrices; $($freeX.renderedEvidence.gridCorpus.totalAvaloniaCaptureCount) Avalonia grid-corpus captures | $($freeX.renderedEvidence.pairedEvidence.pairedSurfaceCount) paired dialog surfaces; $($freeX.renderedEvidence.chromeCapture.fixedViewportComparisonCount) fixed-width chrome triage rows per host | $($freeX.renderedEvidence.physicalEvidence.status); Linux Name Box $($freeX.renderedEvidence.physicalEvidence.linuxNameBoxParityPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxNameBoxParityTotal) visual and $($freeX.renderedEvidence.physicalEvidence.linuxNameBoxInteractionPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxNameBoxInteractionTotal) interaction; AutoFilter recalculation $($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterRecalculationPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterRecalculationTotal); AutoFilter sort persistence $($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterSortPersistencePassed)/$($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterSortPersistenceTotal); AutoFilter text criteria $($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterTextCriteriaPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterTextCriteriaTotal); AutoFilter numeric criteria $($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterNumericCriteriaPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterNumericCriteriaTotal) ($($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterNumericCriteriaStatus)); AutoFilter date criteria $($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterDateCriteriaPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterDateCriteriaTotal) ($($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterDateCriteriaStatus)); app-owned render manifests, complete foreground chrome matrices, and committed Excel range references | $($freeX.renderedEvidence.authoritativeMicrosoftOfficeBaseline.product): $($freeX.renderedEvidence.authoritativeMicrosoftOfficeBaseline.status); $($freeX.renderedEvidence.authoritativeMicrosoftOfficeBaseline.artifactCount) artifacts. $($freeX.renderedEvidence.authoritativeMicrosoftOfficeBaseline.limitation) |",
        "| FreeW | $($freeW.renderedEvidence.routeCoverage.inventoryRouteCount) inventoried route families; $($freeW.renderedEvidence.routeCoverage.comparedRouteCount) represented in comparison rows; $($freeW.renderedEvidence.routeCoverage.pairedRouteCount) paired and $($freeW.renderedEvidence.routeCoverage.avaloniaOnlyRouteCount) Avalonia-only | $($freeW.renderedEvidence.artifactCoverage.evidenceRowCount) dialog comparison rows; $($freeW.renderedEvidence.shellChrome.pairedStaticCaptureCount) paired static and $($freeW.renderedEvidence.shellChrome.pairedContextualCaptureCount) paired contextual shell captures; $($freeW.renderedEvidence.shellChrome.wordOfficeChromeReferenceCount) native Word ribbon references | $($freeW.renderedEvidence.pairedEvidence.pairedScenarioCount) paired dialog rows; $($freeW.renderedEvidence.pairedEvidence.passCount) pass classifications; $($freeW.renderedEvidence.pairedEvidence.mismatchCount) genuine visual mismatch classifications; shell captures review-required | $($freeW.renderedEvidence.physicalEvidence.status); app-owned dialog/full-window shell captures plus committed Word canvas and ribbon references | $($freeW.renderedEvidence.authoritativeMicrosoftOfficeBaseline.product): $($freeW.renderedEvidence.authoritativeMicrosoftOfficeBaseline.status); $($freeW.renderedEvidence.authoritativeMicrosoftOfficeBaseline.artifactCount) artifacts. $($freeW.renderedEvidence.authoritativeMicrosoftOfficeBaseline.limitation) |",
        "| FreeP | Dialog lane: $($freeP.renderedEvidence.routeCoverage.laneEntries[0].routeInventoryCount) routes/$($freeP.renderedEvidence.routeCoverage.laneEntries[0].renderedScenarioCount) scenarios; whole-window lane: $($freeP.renderedEvidence.routeCoverage.laneEntries[1].renderedScenarioCount) scenarios without a separate route inventory | $($freeP.renderedEvidence.artifactCoverage.wpfPngCount) WPF PNGs; $($freeP.renderedEvidence.artifactCoverage.avaloniaPngCount) Avalonia PNGs; $($freeP.renderedEvidence.artifactCoverage.diffPngCount) diff PNGs; $($freeP.renderedEvidence.nativeOfficeChrome.capturedReferenceCount) native PowerPoint ribbon refs; $($freeP.renderedEvidence.responsiveAppChrome.capturedPairCount) responsive WPF/Avalonia pairs | $($freeP.renderedEvidence.pairedEvidence.pairedScenarioCount) paired scenarios; $($freeP.renderedEvidence.pairedEvidence.passCount) local comparison passes; $($freeP.renderedEvidence.pairedEvidence.mismatchCount) mismatches; native Office/app chrome $($freeP.renderedEvidence.nativeOfficeChrome.captureStatus)/$($freeP.renderedEvidence.responsiveAppChrome.captureStatus) | $($freeP.renderedEvidence.physicalEvidence.status); visible app-owned render targets, complete responsive app and Office ribbon lanes, and a committed PowerPoint COM corpus | $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.product): $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.status); $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.artifactCount) tracked artifacts across $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.referenceReadyDecks) decks, with $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.missingReferenceDecks) deck missing references. Current-source WPF/Avalonia averages: $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.wpfAverageMeanPercent)% / $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.avaloniaAverageMeanPercent)%. $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.limitation) |",
        "",
        "## FreeX Visual Review Queue",
        "",
        "This is a deterministic human-review queue, not a pass/fail result. The threshold and rationale are generated in `docs/parity/dialog-visual-evidence-summary.json`; equal dimensions or paired ids do not establish visual parity.",
        "",
        "| Surface id | Triage score | Logical dimensions match | Dimension bucket | Review status |",
        "|---|---:|---|---|---|"
    ) + $freeXVisualReviewMarkdownRows + @(
        "",
        "## Source Files",
        "",
        '- `docs/parity/command-inventory.json`',
        '- `docs/parity/functional-parity.json`',
        '- `docs/parity/functional-parity-classification.json`',
        '- `docs/parity/dialog-parity-inventory.json`',
        '- `docs/parity/dialog-visual-evidence-summary.json`',
        '- `docs/parity/dialog-visual-assets/wpf-capture/manifest.json`',
        '- `docs/parity/dialog-visual-assets/avalonia-capture/manifest.json`',
        '- `docs/parity/freex-excel-foreground-capture-2026-08-16.md`',
        '- `docs/parity/freex-excel-chrome-comparison.md`',
        '- `tools/screenshots/screenshot_manifest.json`',
        '- `tools/screenshots_avalonia_ribbon/screenshot_manifest.json`',
        '- `docs/parity/freex-excel-com-baseline-2026-08-14/manifest.json`',
        '- `docs/parity/freex-avalonia-grid-corpus-2026-08-16/manifest.json`',
        '- `docs/parity/avalonia-parity-wave183-freex-namebox-overlay-20260823.md`',
        '- `docs/parity/avalonia-parity-wave184-freex-autofilter-20260823.md`',
        '- `docs/parity/avalonia-parity-wave185-freex-autofilter-sort-20260823.md`',
        '- `docs/parity/avalonia-parity-wave186-freex-autofilter-text-20260823.md`',
        '- `docs/parity/avalonia-parity-wave187-freex-autofilter-numeric-20260823.md`',
        '- `docs/parity/avalonia-parity-wave188-freex-autofilter-numeric-20260823.md`',
        '- `docs/parity/avalonia-parity-wave189-freex-autofilter-date-20260823.md`',
        '- `docs/parity/avalonia-parity-wave190-freex-autofilter-date-20260823.md`',
        '- `docs/parity/evidence/wave190-freex-autofilter-date-20260823/manifest.json`',
        '- `docs/parity/avalonia-parity-wave191-freex-autofilter-color-20260823.md`',
        '- `docs/parity/evidence/wave191-freex-autofilter-color-20260823/manifest.json`',
        '- `docs/parity/avalonia-parity-wave192-freex-autofilter-font-color-20260823.md`',
        '- `docs/parity/evidence/wave192-freex-autofilter-font-color-20260823/manifest.json`',
        '- `docs/parity/freew-command-inventory.json`',
        '- `docs/parity/freew-dialog-harness/freew_dialog_route_inventory.json`',
        '- `docs/parity/freew-dialog-harness/freew_dialog_visual_comparison.json`',
        '- `docs/parity/freew-dialog-harness/freew_font_visual_provenance.json`',
        '- `docs/parity/freew-word-baseline-2026-08-16/manifest.json`',
        '- `docs/parity/freew-shell-visual-2026-08-16/freew_shell_visual_evidence.json`',
        '- `docs/parity/freew-word-chrome-2026-08-16/manifest.json`',
        '- `docs/parity/avalonia-parity-wave184-freew-table-properties-cell-20260823.md`',
        '- `freew/docs/parity/avalonia-parity-wave185-freew-page-setup-text-raster-20260823.md`',
        '- `freew/docs/parity/avalonia-parity-wave186-freew-table-properties-text-raster-20260823.md`',
        '- `docs/parity/avalonia-parity-wave187-freew-legal-notices-20260823.md`',
        '- `docs/parity/avalonia-parity-wave188-freew-font-20260823.md`',
        '- `docs/parity/avalonia-parity-wave189-freew-font-antialias-20260823.md`',
        '- `docs/parity/avalonia-parity-wave190-freew-visual-20260823.md`',
        '- `docs/parity/avalonia-parity-wave191-freew-font-template-20260823.md`',
        '- `docs/parity/avalonia-parity-wave192-freew-font-checkbox-20260823.md`',
        '- `docs/parity/freep-command-parity-inventory.json`',
        '- `docs/parity/freep-dialog-pane-visual-evidence/summary.json`',
        '- `docs/parity/freep-dialog-pane-visual-evidence/artifact-manifest.json`',
        '- `docs/parity/freep-whole-window-visual-evidence/summary.json`',
        '- `docs/parity/freep-whole-window-visual-evidence/artifact-manifest.json`',
        '- `docs/parity/freep-render-slideshow-media-parity-20260720.json`',
        '- `docs/parity/freep-native-picker-human-evidence.json`',
        '- `docs/parity/freep-powerpoint-baseline-2026-08-14.json`',
        '- `docs/parity/freep-powerpoint-recalibration-2026-08-15.json`',
        '- `docs/parity/freep-powerpoint-chrome-2026-08-16/README.md`',
        '- `docs/parity/freep-powerpoint-chrome-2026-08-16/manifest.json`',
        '- `docs/parity/freep-responsive-chrome-2026-08-16/README.md`',
        '- `docs/parity/freep-responsive-chrome-2026-08-16/manifest.json`',
        '- `docs/parity/freep-wave184-smartart-2026-08-23.md`',
        '- `docs/parity/freep-wave185-bullets-autofit-20260823.md`',
        '- `docs/parity/freep-wave186-surface3d-smartart-20260823.md`',
        '- `docs/parity/avalonia-parity-wave187-freep-surface3d-20260823.md`',
        '- `docs/parity/avalonia-parity-wave188-freep-smartart-text-20260823.md`',
        '- `docs/parity/avalonia-parity-wave189-freep-smartart-text-20260823.md`',
        '- `docs/parity/avalonia-parity-wave190-freep-smartart-text-origin-20260823.md`',
        '- `docs/parity/avalonia-parity-wave190-freep-evidence-20260823/metrics.json`',
        '- `docs/parity/avalonia-parity-wave191-freep-smartart-color-gate-20260823.md`',
        '- `docs/parity/avalonia-parity-wave191-freep-evidence-20260823/metrics.json`',
        '- `docs/parity/avalonia-parity-wave192-freep-render-residual-20260823.md`',
        '- `docs/parity/evidence/avalonia-parity-wave192-freep-evidence-20260823/metrics.json`'
    ) -join "`n"
    Set-Content -LiteralPath $tempMarkdownPath -Value ($md + "`n") -NoNewline -Encoding UTF8

    if ($Check) {
        Test-ToolGeneratedFileContentMatches -ExpectedPath $tempJsonPath -ActualPath $resolvedJsonPath -Label "Cross-app parity dashboard JSON" -GeneratorScriptName "tools\Generate-CrossAppParityDashboard.ps1" -NormalizeNewlines
        Test-ToolGeneratedFileContentMatches -ExpectedPath $tempMarkdownPath -ActualPath $resolvedMarkdownPath -Label "Cross-app parity dashboard Markdown" -GeneratorScriptName "tools\Generate-CrossAppParityDashboard.ps1" -NormalizeNewlines
    }
    else {
        Copy-Item -LiteralPath $tempJsonPath -Destination $resolvedJsonPath -Force
        Copy-Item -LiteralPath $tempMarkdownPath -Destination $resolvedMarkdownPath -Force
        Write-Host "Wrote $JsonPath and $MarkdownPath."
    }
}
finally {
    Remove-ToolTemporaryDirectory -Path $tempRoot
}
