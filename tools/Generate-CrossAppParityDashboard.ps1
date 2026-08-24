param(
    [string]$JsonPath = "docs\parity\avalonia-wpf-cross-app-dashboard.json",
    [string]$MarkdownPath = "docs\parity\avalonia-wpf-cross-app-dashboard.md",
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

# Keep acceptance evidence anchored to the source that was actually built and tested.
# The generated docs are committed afterward, so deriving this from the current HEAD
# would make the evidence self-referential and would change the claim on every refresh.
$wave194TestedSourceCommit = "3d60b7b421b388ccfa5c9c18dc4e25642b7a14c6"
$wave194AcceptanceRefreshNote = "This dashboard/report is an acceptance-only documentation/tooling refresh; it does not alter the tested source commit."

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
    $freeXWave193PhysicalResult = Read-ToolJson -Path "docs\parity\evidence\wave193-freex-autofilter-no-fill-20260823\physical-result.json" -RepoRoot $repoRoot -MissingMessage "Required Wave193 FreeX physical result is missing"
    $freeXWave193Manifest = Read-ToolJson -Path "docs\parity\evidence\wave193-freex-autofilter-no-fill-20260823\manifest.json" -RepoRoot $repoRoot -MissingMessage "Required Wave193 FreeX evidence manifest is missing"
    $freeXWave194PhysicalResult = Read-ToolJson -Path "docs\parity\evidence\wave194-freex-autofilter-mixed-type-20260823\physical-result.json" -RepoRoot $repoRoot -MissingMessage "Required Wave194 FreeX physical result is missing"
    $freeXWave194Manifest = Read-ToolJson -Path "docs\parity\evidence\wave194-freex-autofilter-mixed-type-20260823\manifest.json" -RepoRoot $repoRoot -MissingMessage "Required Wave194 FreeX evidence manifest is missing"
    $wave193PhysicalRun = $freeXWave193Manifest.physicalRun
    $wave193ExpectedArtifactCount = 18
    $wave193PopupOpenChangeThreshold = 300
    $wave193PopupRestoredChangeMaximum = 100
    if ($null -eq $wave193PhysicalRun) {
        throw "Wave193 FreeX evidence manifest is missing physicalRun transition metrics."
    }
    $requiredWave193PhysicalRunProperties = @(
        "popupOpenChangedPixels",
        "popupDismissedChangedPixels",
        "popupRestoredChangedPixels",
        "clickAcknowledged"
    )
    foreach ($propertyName in $requiredWave193PhysicalRunProperties) {
        if ($null -eq $wave193PhysicalRun.PSObject.Properties[$propertyName]) {
            throw "Wave193 FreeX evidence manifest is missing physicalRun.$propertyName."
        }
    }
    $wave193ArtifactCount = @($freeXWave193Manifest.files).Count
    if ($wave193ArtifactCount -ne $wave193ExpectedArtifactCount) {
        throw "Wave193 FreeX evidence manifest must contain $wave193ExpectedArtifactCount artifacts; found $wave193ArtifactCount."
    }
    if ([int]$wave193PhysicalRun.popupOpenChangedPixels -lt $wave193PopupOpenChangeThreshold -or
        [int]$wave193PhysicalRun.popupDismissedChangedPixels -lt $wave193PopupOpenChangeThreshold -or
        [int]$wave193PhysicalRun.popupRestoredChangedPixels -gt $wave193PopupRestoredChangeMaximum -or
        -not [bool]$wave193PhysicalRun.clickAcknowledged) {
        throw "Wave193 FreeX popup transition metrics did not satisfy open/dismiss/restoration thresholds."
    }
    $freew = Read-ToolJson -Path "docs\parity\freew-command-inventory.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freeWRouteInventory = Read-ToolJson -Path "docs\parity\freew-dialog-harness\freew_dialog_route_inventory.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freeWVisualComparison = Read-ToolJson -Path "docs\parity\freew-dialog-harness\freew_dialog_visual_comparison.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freeWFontProvenance = Read-ToolJson -Path "docs\parity\freew-dialog-harness\freew_font_visual_provenance.json" -RepoRoot $repoRoot -MissingMessage "Required FreeW Font provenance is missing"
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
    $freePWave193Metrics = Read-ToolJson -Path "docs\parity\evidence\avalonia-parity-wave193-freep-evidence-20260823\metrics.json" -RepoRoot $repoRoot -MissingMessage "Required Wave193 FreeP current-source metrics are missing"
    $freePWave193References = Read-ToolJson -Path "docs\parity\evidence\avalonia-parity-wave193-freep-evidence-20260823\references.json" -RepoRoot $repoRoot -MissingMessage "Required Wave193 FreeP Office references are missing"
    $freePWave193Images = Read-ToolJson -Path "docs\parity\evidence\avalonia-parity-wave193-freep-evidence-20260823\images.json" -RepoRoot $repoRoot -MissingMessage "Required Wave193 FreeP retained images are missing"
    $freePWave194Topology = Read-ToolJson -Path "docs\parity\evidence\freep-wave194-deck17-slide02-topology-20260823\topology.json" -RepoRoot $repoRoot -MissingMessage "Required Wave194 FreeP topology evidence is missing"
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
            "docs/parity/evidence/wave192-freex-autofilter-font-color-20260823/manifest.json",
            "docs/parity/avalonia-parity-wave193-freex-autofilter-no-fill-20260823.md",
            "docs/parity/evidence/wave193-freex-autofilter-no-fill-20260823/physical-result.json",
            "docs/parity/evidence/wave193-freex-autofilter-no-fill-20260823/manifest.json",
            "docs/parity/avalonia-parity-wave194-freex-autofilter-mixed-type-20260823.md",
            "docs/parity/evidence/wave194-freex-autofilter-mixed-type-20260823/physical-result.json",
            "docs/parity/evidence/wave194-freex-autofilter-mixed-type-20260823/manifest.json"
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
            linuxAutoFilterNoFillPassed = [int]$freeXWave193PhysicalResult.summary.passed
            linuxAutoFilterNoFillTotal = [int]$freeXWave193PhysicalResult.summary.total
            linuxAutoFilterNoFillStatus = "passed-production-x11"
            wave193FocusedAvaloniaGuardPassed = 3
            wave193FocusedCoreIoGuardPassed = 8
            wave193EvidenceArtifactCount = $wave193ArtifactCount
            wave193EvidenceArtifactExpectedCount = $wave193ExpectedArtifactCount
            wave193EvidenceProvenanceFileCount = @($freeXWave193Manifest.provenanceFiles).Count
            wave193PopupTransitions = [ordered]@{
                popupOpen = [ordered]@{
                    changedPixels = [int]$wave193PhysicalRun.popupOpenChangedPixels
                    minimumChangedPixels = $wave193PopupOpenChangeThreshold
                    passed = [int]$wave193PhysicalRun.popupOpenChangedPixels -ge $wave193PopupOpenChangeThreshold
                }
                popupDismissed = [ordered]@{
                    changedPixels = [int]$wave193PhysicalRun.popupDismissedChangedPixels
                    minimumChangedPixels = $wave193PopupOpenChangeThreshold
                    passed = [int]$wave193PhysicalRun.popupDismissedChangedPixels -ge $wave193PopupOpenChangeThreshold
                }
                restoration = [ordered]@{
                    changedPixels = [int]$wave193PhysicalRun.popupRestoredChangedPixels
                    maximumChangedPixels = $wave193PopupRestoredChangeMaximum
                    passed = [int]$wave193PhysicalRun.popupRestoredChangedPixels -le $wave193PopupRestoredChangeMaximum
                }
                clickAcknowledged = [bool]$wave193PhysicalRun.clickAcknowledged
                summary = "popup-open $([int]$wave193PhysicalRun.popupOpenChangedPixels) changed pixels (minimum $wave193PopupOpenChangeThreshold); popup-dismissed $([int]$wave193PhysicalRun.popupDismissedChangedPixels) changed pixels (minimum $wave193PopupOpenChangeThreshold); restoration $([int]$wave193PhysicalRun.popupRestoredChangedPixels) changed pixels (maximum $wave193PopupRestoredChangeMaximum); click acknowledged"
            }
            wave193PackageSemantics = "SourcePatch retained for the no-row-delta criterion-only case; package colorFilter/DXF semantics are verified after save and reopen."
            wave194 = [ordered]@{
                status = "passed"
                physicalPassed = [int]$freeXWave194PhysicalResult.summary.passed
                physicalTotal = [int]$freeXWave194PhysicalResult.summary.total
                focusedAvaloniaGuardPassed = 9
                focusedAvaloniaGuardTotal = 9
                focusedPresentationPassed = 1
                focusedPresentationTotal = 1
                focusedCoreIoPassed = 8
                focusedCoreIoTotal = 8
                evidenceArtifactCount = @($freeXWave194Manifest.files).Count
                evidenceArtifactExpectedCount = 20
                reachableProvenanceFileCount = @($freeXWave194Manifest.provenanceFiles).Count
                validationFileCount = @($freeXWave194Manifest.validationFiles).Count
                geometry = [ordered]@{
                    bounds = [string]$freeXWave194Manifest.physicalRun.targetBounds
                    click = [string]$freeXWave194Manifest.physicalRun.targetClick
                    contract = "One authoritative geometry contract is consumed by crop, readiness/transition checks, and the actual physical click; mutation guards reject hard-coded or unreachable substitutes."
                }
                visibleReadback = [string]$freeXWave194Manifest.physicalRun.visible
                semanticReadback = [string]$freeXWave194Manifest.physicalRun.semantic
                recalculation = [string]$freeXWave194Manifest.physicalRun.recalculation
                package = [string]$freeXWave194Manifest.physicalRun.package
                reopenedVisibleReadback = [string]$freeXWave194Manifest.physicalRun.reopenedVisible
                reopenedSemanticReadback = [string]$freeXWave194Manifest.physicalRun.reopenedSemantic
                reachableProvenanceSourceCommit = [string]$freeXWave194Manifest.sourceCommit
                physicalCaptureSourceCommit = [string]$freeXWave194Manifest.physicalCaptureSourceCommit
                evidenceUnchangedAfterGeometryRemediation = $true
                claimBoundary = "Bounded physical Linux/X11 evidence for one mixed-type AutoFilter workflow; it does not establish complete AutoFilter or Excel visual parity."
            }
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
                "The production Linux X11 No Fill AutoFilter save/reopen lane passes $($freeXWave193PhysicalResult.summary.passed)/$($freeXWave193PhysicalResult.summary.total). It selects the rendered No Fill swatch, proves popup-open and popup-dismissed transitions at $([int]$wave193PhysicalRun.popupOpenChangedPixels)/$([int]$wave193PhysicalRun.popupDismissedChangedPixels) changed pixels, restores the pre-popup target at $([int]$wave193PhysicalRun.popupRestoredChangedPixels) changed pixels, retains South/West, verifies the empty-DXF colorFilter package semantics, and reopens with matching rendered and semantic state. The committed Wave193 manifest contains $wave193ArtifactCount/$wave193ExpectedArtifactCount artifacts and $(@($freeXWave193Manifest.provenanceFiles).Count)/9 provenance files.",
                "Wave193 focused source/evidence coverage is 3/3 Avalonia guards and 8/8 Core.IO guards; the package tests retain SourcePatch semantics when a criterion-only change produces no row-visibility delta.",
                "Wave194 adds one physical mixed-type AutoFilter workflow at $($freeXWave194PhysicalResult.summary.passed)/$($freeXWave194PhysicalResult.summary.total): Select All is cleared, numeric 42 is selected, SUBTOTAL changes 5 -> 2, visible/readback is $($freeXWave194Manifest.physicalRun.visible), semantic labels are $($freeXWave194Manifest.physicalRun.semantic), and save/reopen preserves the exact package contract. Focused Wave194 source/evidence coverage is 9/9 Avalonia guards, 1/1 Presentation, and 8/8 Core.IO (Wave194 plus five foreground-capture guards).",
                "Wave194 retains $(@($freeXWave194Manifest.files).Count)/20 evidence artifacts, $(@($freeXWave194Manifest.provenanceFiles).Count)/10 reachable provenance files, and $(@($freeXWave194Manifest.validationFiles).Count)/2 current validation files. Geometry is $($freeXWave194Manifest.physicalRun.targetBounds) with click $($freeXWave194Manifest.physicalRun.targetClick), consumed through one authoritative contract; physical evidence bytes are unchanged by the geometry remediation.",
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
    $freeX.nextSlice = "$($freeX.nextSlice) Production Linux evidence now covers Name Box (1/1 visual, 8/8 interaction), AutoFilter apply/change/clear recalculation (1/1), sort/save/reopen persistence (1/1), text-criteria save/reopen persistence (2/2), numeric Greater Than/Equals persistence (2/2), date criteria (2/2), fill-color save/reopen (1/1), font-color save/reopen (1/1), No Fill save/reopen persistence ($($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterNoFillPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterNoFillTotal)), and mixed-type value persistence ($($freeX.renderedEvidence.physicalEvidence.wave194.physicalPassed)/$($freeX.renderedEvidence.physicalEvidence.wave194.physicalTotal)). Wave194 retains $(@($freeXWave194Manifest.files).Count)/20 artifacts and one authoritative geometry contract. Extend physical verification to multi-column and color criteria change/clear workflows."

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
            "docs/parity/avalonia-parity-wave192-freew-font-checkbox-20260823.md",
            "docs/parity/avalonia-parity-wave193-freew-font-checkbox-glyph-20260823.md",
            "docs/parity/avalonia-parity-wave194-freew-font-action-border-20260824.md"
        )
        canonicalComparison = [ordered]@{
            kind = [string]$freeWVisualComparison.scope.kind
            description = [string]$freeWVisualComparison.scope.description
            refreshInstruction = [string]$freeWVisualComparison.scope.refreshInstruction
        }
        wave193 = [ordered]@{
            baselineAggregateChangedPixels = [int]$freeWFontProvenance.wave192Baseline.aggregateChangedPixels
            aggregateChangedPixels = [int]$freeWFontProvenance.wave193Result.aggregateChangedPixels
            aggregateDelta = [int]$freeWFontProvenance.wave193Result.aggregateDelta
            relativeImprovement = [double]$freeWFontProvenance.wave193Result.relativeImprovement
            changedPixelsByState = [ordered]@{
                initial = [int](@($freeWFontProvenance.rows | Where-Object state -eq "initial")[0].metrics.changedPixels)
                populated = [int](@($freeWFontProvenance.rows | Where-Object state -eq "populated")[0].metrics.changedPixels)
                validationError = [int](@($freeWFontProvenance.rows | Where-Object state -eq "validation-error")[0].metrics.changedPixels)
            }
            perStateImprovement = 445
            paintedBounds = "421 x 321"
            nonFontRowsCompared = [int]$freeWFontProvenance.wave193Result.nonFontRowsCompared
            nonFontRowsChanged = [int]$freeWFontProvenance.wave193Result.nonFontRowsChanged
            classificationTotals = "141 mismatch / 80 pass / 70 extension"
            retainedPolicy = "shared opt-in 14px checkbox indicator, +1px vertical offset, #EBEBEB/#F6F6F6 frame; the 1px stroke probe was a no-op and was removed."
        }
        wave194 = [ordered]@{
            sourceRevision = [string]$freeWFontProvenance.generatedAtSourceRevision
            baselineAggregateChangedPixels = [int]$freeWFontProvenance.wave193Result.aggregateChangedPixels
            aggregateChangedPixels = [int]$freeWFontProvenance.wave194Result.aggregateChangedPixels
            aggregateDelta = [int]$freeWFontProvenance.wave194Result.aggregateDelta
            relativeImprovement = [double]$freeWFontProvenance.wave194Result.relativeImprovement
            changedPixelsByState = [ordered]@{
                initial = [int](@($freeWFontProvenance.rows | Where-Object state -eq "initial")[0].metrics.changedPixels)
                populated = [int](@($freeWFontProvenance.rows | Where-Object state -eq "populated")[0].metrics.changedPixels)
                validationError = [int](@($freeWFontProvenance.rows | Where-Object state -eq "validation-error")[0].metrics.changedPixels)
            }
            perStateImprovement = 183
            paintedBounds = "421 x 321"
            nonFontRowsCompared = [int]$freeWFontProvenance.wave194Result.nonFontRowsCompared
            nonFontRowsChanged = [int]$freeWFontProvenance.wave194Result.nonFontRowsChanged
            classificationTotals = "141 mismatch / 80 pass / 70 extension"
            correction = "Avalonia Font action-button border changed from #707070 to #C8C8C8 to match WPF; no other rows changed."
            claimBoundary = "Canonical FreeW Font-dialog WPF/Avalonia evidence only; remaining text/control raster differences do not establish Word visual parity."
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
                "Wave193 aligns the shared Font checkbox native frame. The three-state aggregate falls from 34196 to 32861 changed pixels, a further 3.9040% reduction; each state improves by 445 pixels, exact 421 x 321 painted bounds remain stable, and the 1px stroke probe was a no-op and was removed.",
                "Wave193's tracked Font provenance binds all three states and six host captures to dimensions, painted bounds, exact canonical comparison rows, source hashes, and external capture-manifest identities. Only the three Font rows changed; all 288 non-Font rows remain unchanged. The external PNGs remain uncommitted and require the capture hosts for pixel reproduction.",
                "Wave194 changes only the Avalonia Font action-button border to the WPF-style #C8C8C8 value. Aggregate changed pixels fall from $($freeWFontProvenance.wave193Result.aggregateChangedPixels) to $($freeWFontProvenance.wave194Result.aggregateChangedPixels), a delta of $($freeWFontProvenance.wave194Result.aggregateDelta) and relative improvement $([string]('{0:P4}' -f $freeWFontProvenance.wave194Result.relativeImprovement)); each of the three states improves by 183 pixels, painted bounds remain 421 x 321, and all 288 non-Font rows remain unchanged.",
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
        nextSlice = "A committed current-source Word PNG baseline bundle is available for $($freeWOfficeBaseline.comparison.comparableRows) comparable rows, but $($freeWOfficeBaseline.comparison.failedRows) comparisons remain outside tolerance. Font dialog captures now improve from 61396 to $($freeWFontProvenance.wave194Result.aggregateChangedPixels) aggregate changed pixels across Waves188-194 and match WPF painted bounds; continue with the remaining native checkbox/glyph raster tail, tab-template edges, Legal Notices glyph/template tail, or the next classified pagination, drawing/object, chart, table, or WordArt residual."
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
            "docs/parity/evidence/avalonia-parity-wave192-freep-evidence-20260823/metrics.json",
            "docs/parity/avalonia-parity-wave193-freep-render-residual-20260823.md",
            "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/metrics.json",
            "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/references.json",
            "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/images.json",
            "docs/parity/freep-wave194-deck17-slide02-topology-20260823.md",
            "docs/parity/evidence/freep-wave194-deck17-slide02-topology-20260823/topology.json"
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
        wave193Integrity = [ordered]@{
            status = "no-runtime-change-retained"
            evidencePath = "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/"
            retainedRowCount = @($freePWave193Metrics.rows).Count
            retainedOfficeReferenceCount = @($freePWave193References.rows).Count
            retainedImageCount = @($freePWave193Images.PSObject.Properties).Count
            workerRunWpfAvaloniaRenderCount = [int]$freePWave193Metrics.source.rendererOutputs
            workerRunComparisonCount = [int]$freePWave193Metrics.source.comparisons
            fullRenderArtifactsRetained = $false
            claim = "Retained proof covers 53 rows, 53 Office references, and 6 target images. The 106/106 renders and 159/159 comparisons are worker-run full-render results and their non-target PNGs are not retained."
        }
        wave194Topology = [ordered]@{
            status = "no-runtime-change-retained"
            schema = [string]$freePWave194Topology.schema
            sourceRevision = [string]$freePWave194Topology.sourceRevision
            sourceCorpusPath = [string]$freePWave194Topology.sourceCorpus.path
            sourceCorpusSha256 = [string]$freePWave194Topology.sourceCorpus.sha256
            sourceCorpusHashScope = [string]$freePWave194Topology.sourceCorpus.hashScope
            title = [ordered]@{
                autoFitKind = [string]$freePWave194Topology.model.title.autoFitKind
                effectiveFontFamily = [string]$freePWave194Topology.model.title.effectiveFontFamily
                effectiveFontSizePt = [int]$freePWave194Topology.model.title.effectiveFontSizePt
            }
            body = [ordered]@{
                autoFitKind = [string]$freePWave194Topology.model.body.autoFitKind
                effectiveFontFamily = [string]$freePWave194Topology.model.body.effectiveFontFamily
                effectiveFontSizePt = [int]$freePWave194Topology.model.body.effectiveFontSizePt
                paragraphCount = [int]$freePWave194Topology.model.body.paragraphCount
            }
            retainedMetrics = [ordered]@{
                rendererOutputs = [int]$freePWave194Topology.retainedMetrics.source.rendererOutputs
                comparisons = [int]$freePWave194Topology.retainedMetrics.source.comparisons
                wpfOfficeAverage = [double]$freePWave194Topology.retainedMetrics.aggregate.wpfOfficeAverage
                avaloniaOfficeAverage = [double]$freePWave194Topology.retainedMetrics.aggregate.avaloniaOfficeAverage
                wpfAvaloniaAverage = [double]$freePWave194Topology.retainedMetrics.aggregate.wpfAvaloniaAverage
                targetAvaloniaOffice = [double]$freePWave194Topology.retainedMetrics.target.avaloniaOffice
                targetWpfAvalonia = [double]$freePWave194Topology.retainedMetrics.target.wpfAvalonia
            }
            residualClaim = "The topology evidence rules out the investigated structural, autofit, and theme-inheritance hypotheses; the remaining render residual is unresolved and is not attributed to host fonts or raster behavior."
            claimBoundary = "Pinned Office topology and retained comparison metrics only; no PowerPoint visual-parity claim is made."
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
                "Wave193 retains no runtime rendering change. The worker run produced $($freePWave193Metrics.source.rendererOutputs)/$($freePWave193Metrics.source.rendererOutputs) current-source renders and $($freePWave193Metrics.source.comparisons)/$($freePWave193Metrics.source.comparisons) comparisons; retained integrity proof is limited to $(@($freePWave193Metrics.rows).Count) rows, $(@($freePWave193References.rows).Count) Office references, and $(@($freePWave193Images.PSObject.Properties).Count) target images.",
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
            currentSourceRevision = [string]$freePWave193Metrics.source.baseRevision
            wpfAverageMeanPercent = [double]$freePWave193Metrics.aggregate.wpfOfficeAverage
            wpfMaximumMeanPercent = [double]$freePWave193Metrics.aggregate.wpfOfficeMaximum
            avaloniaAverageMeanPercent = [double]$freePWave193Metrics.aggregate.avaloniaOfficeAverage
            avaloniaMaximumMeanPercent = [double]$freePWave193Metrics.aggregate.avaloniaOfficeMaximum
            rendererPairAverageMeanPercent = [double]$freePWave193Metrics.aggregate.wpfAvaloniaAverage
            rendererPairMaximumMeanPercent = [double]$freePWave193Metrics.aggregate.wpfAvaloniaMaximum
        }
        claimBoundary = "Route/scenario coverage, committed PNG manifests, and local WPF/Avalonia comparison results only; no PowerPoint visual-parity claim is made."
    }

    $freePNextSlice = "The tracked PowerPoint corpus has $($freePOfficeBaseline.artifactCount) COM-exported reference slides across $($freePOfficeBaseline.comparison.referenceReadyDecks) ready decks, with $($freePOfficeBaseline.comparison.missingReferenceDecks) deck missing references. Wave194 makes no runtime rendering change: topology evidence pins the complete deck17 slide02 source at $($freePWave194Topology.sourceCorpus.sha256), records $($freePWave194Topology.model.title.effectiveFontFamily) and $($freePWave194Topology.model.body.effectiveFontFamily) theme-resolved fonts, and leaves the residual unresolved. Continue renderer-level evidence against the existing corpus rather than introducing fixture-specific correction."

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

    $wave194IntegrationGateEvidence = [ordered]@{
        testedSourceCommit = $wave194TestedSourceCommit
        acceptanceRefreshNote = $wave194AcceptanceRefreshNote
        reintegration = "Merge commit 3d60b7b421b388ccfa5c9c18dc4e25642b7a14c6 contained current origin/main's six foreground-capture commits merged with the already accepted Wave194 histories; the merge contained zero overlapping paths between those inputs."
        focusedTests = "At tested source commit 3d60b7b421b388ccfa5c9c18dc4e25642b7a14c6: FreeX Avalonia Wave194 9/9; FreeX Presentation Wave194 1/1; FreeX Core.IO Wave194 plus five foreground-capture guards 8/8; FreeP Presentation Wave194 2/2."
        initialReintegrationPreflight = "Initial repository preflight reached the generated/dashboard guards and failed only because the prior tested-source anchor treated these three incoming paths as outside the acceptance allowlist: docs/testing/freex-excel-ux-parity-suite.md, tests/FreeX.Core.IO.Tests/ToolHarnessDedupSourceTests.cs, tools/FreeX.ForegroundCapture/Program.cs. Remediated by anchoring tested source at 3d60b7b421b388ccfa5c9c18dc4e25642b7a14c6, not by expanding the allowlist."
        initialIndependentReview = "Recorded: the initial independent review found two P2 findings: FreeX crop/readiness/transition and physical click geometry were duplicated instead of consuming one contract; FreeP topology evidence did not pin the complete source PPTX and initially over-attributed the residual."
        reviewRemediation = "Completed at prior source: FreeX uses one authoritative mixed-type geometry contract with mutation coverage and reachable-source provenance; FreeP topology schema v3 pins the complete PPTX SHA-256 and describes the remaining residual as unresolved. Reopened after source advancement because the color-geometry guard required remediation."
        independentReview = "Passed: final independent review found no findings. Reviewer verified f2a structurally scopes the color function before the mixed-type function, accepts the later decoy, rejects the internal assignment, verifies Wave191-193 retained hashes 11/11, 11/11, and 18/18, verifies Wave194 20 evidence plus 12 provenance/validation, and found FreeP and FreeW clean."
        repositoryPreflight = "Passed at tested source commit ${wave194TestedSourceCommit}: powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Test-RepositoryPreflight.ps1 exited 0. Validated 292 JSON files, 306 XML-backed files, and 13,862 text files for conflict markers; 117 PowerShell scripts, 10 workflows, 160 project files, 124 solution entries, 32 default-test entries, 51 FreeW entries, and 42 FreeP entries; Linux packaging passed; generated docs/dashboard and FreeW/FreeP inventories/evidence all current."
        fullReleaseBuild = "Passed at tested source commit ${wave194TestedSourceCommit}: dotnet build FreeX.slnx --configuration Release --disable-build-servers -p:UseSharedCompilation=false -p:NodeReuse=false /nr:false -m:1 passed with 0 warnings and 0 errors; elapsed 00:13:08.13."
        defaultNonUiTestLane = "Passed at tested source commit ${wave194TestedSourceCommit}: dotnet test FreeX.DefaultTests.slnx --configuration Release --no-build --no-restore --disable-build-servers -p:UseSharedCompilation=false -p:NodeReuse=false /nr:false -m:1 --logger `"trx;LogFileName=default-tests-wave194-final.trx`" exited 0 with 43,432 passed, 134 skipped/not-run, 0 failed, 43,566 total. 25 unique TRX files plus 31 additional passed captures overwritten into the shared capture TRX path across seven capture assemblies. Key totals: FreeP Avalonia 724/0; FreeP Host 2,409/0; FreeP Presentation 5,468/0; FreeX Avalonia 2,193/0; Host Logic 1,490 passed/4 skipped; Presentation 5,465/1; Core.IO 5,846/56; Core Model 6,317/41; Formula 5,199/7; Calc 1,982/24; Integration 661/1. Initial failed lane and remediation remain documented."
        initialDefaultLane = "Exited 1 solely because the Wave191/192/193 color-geometry guard was bounded through a later selector and counted Wave194 mixed_type_target_click_x_offset; at that pre-remediation source FreeX Avalonia reported 2,188 passed, 3 failed, 2,191 total."
        sourceTestRemediation = "Remediation commit f2aab993242fa6a6cc49d67c4b7770c23ce4c067 structurally scopes the old guard to probe_autofilter_color_persistence_physical and adds isolation and inside-function mutation tests."
        workerVerification = "Passed: failing classes 11/11, full color lane 17/17, Wave194 9/9, full Avalonia project 2,193/2,193, focused project build 0/0; no runtime harness or evidence change."
    }

    $dashboard = [ordered]@{
        schema = "freex.parity.cross-app-dashboard.v3"
        wave = 194
        cumulativeAppSlices = 582
        cumulativeAppSlicesStatus = "accepted-final-integration-gates"
        integrationGateStatus = "accepted"
        pendingIntegrationGates = @()
        integrationGateEvidence = $wave194IntegrationGateEvidence
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
                    "docs/parity/avalonia-parity-wave193-freex-autofilter-no-fill-20260823.md",
                    "docs/parity/evidence/wave193-freex-autofilter-no-fill-20260823/physical-result.json",
                    "docs/parity/evidence/wave193-freex-autofilter-no-fill-20260823/manifest.json",
                    "docs/parity/avalonia-parity-wave194-freex-autofilter-mixed-type-20260823.md",
                    "docs/parity/evidence/wave194-freex-autofilter-mixed-type-20260823/physical-result.json",
                    "docs/parity/evidence/wave194-freex-autofilter-mixed-type-20260823/manifest.json",
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
            "docs/parity/avalonia-parity-wave193-freew-font-checkbox-glyph-20260823.md",
            "docs/parity/avalonia-parity-wave194-freew-font-action-border-20260824.md",
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
            "docs/parity/evidence/avalonia-parity-wave192-freep-evidence-20260823/metrics.json",
            "docs/parity/avalonia-parity-wave193-freep-render-residual-20260823.md",
            "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/metrics.json",
            "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/references.json",
            "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/images.json",
            "docs/parity/freep-wave194-deck17-slide02-topology-20260823.md",
            "docs/parity/evidence/freep-wave194-deck17-slide02-topology-20260823/topology.json"
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
        "> Wave194 records an accepted cumulative **$($dashboard.cumulativeAppSlices)** app slices. All final integration gates passed against tested source commit ``$($dashboard.integrationGateEvidence.testedSourceCommit)``. $($dashboard.integrationGateEvidence.acceptanceRefreshNote)",
        "",
        "## Summary",
        "",
        "| App | Primary evidence | Current generated state | Next slice |",
        "|---|---|---|---|",
        "| FreeX | Functional matrix, classifier, dialog inventory, dialog visual evidence, command surface | $($freeX.functionalMatrix.totalCommands) functional commands; $($freeX.functionalMatrix.parity) command inventory parity; $($freeX.functionalMatrix.avaloniaMissing) Avalonia-missing; $($freeX.functionalMatrix.realBehaviorGaps) real classified binding gaps; $($freeX.functionalMatrix.pseudoCommandGalleryItems) catalog-backed pseudo-gallery rows ($($freeX.functionalMatrix.conditionalFormatPopupGalleryRows) conditional-format, $($freeX.functionalMatrix.fontBorderPopupGalleryRows) font/border, $($freeX.functionalMatrix.accountingSymbolPopupGalleryRows) accounting-symbol); $($freeX.dialogRoutes.totalRoutes)/$($freeX.dialogRoutes.totalRoutes) dialog routes captured on WPF and Avalonia; $($freeX.dialogVisualEvidence.pairedCapturedSurfaceIds) paired screenshot surface ids, $($freeX.dialogVisualEvidence.additionalAvaloniaCapturedSurfaceIds) Avalonia-only ids, $($freeX.dialogVisualEvidence.wpfManifestIdsWithoutAvaloniaPair) WPF-only ids; $($freeX.dialogVisualEvidence.pairedDimensionMismatches) scale-aware dimension mismatches; $($freeX.dialogVisualEvidence.visualReviewCandidateCount) unresolved high-delta visual review candidates at triage score >= $($freeX.dialogVisualEvidence.visualReviewTriageThreshold) (highest $($freeX.dialogVisualEvidence.highestTriageScore)); $($freeX.dialogVisualEvidence.pairedRawPixelDimensionMismatches) raw PNG pixel dimension mismatches, of which $($freeX.dialogVisualEvidence.pairedCaptureScaleNormalizedDimensionMatches) normalize by capture DPI. These are coverage/triage metrics, not a visual-parity claim. | $($freeX.nextSlice) |",
        "| FreeW | Generated command inventory plus dialog rendered evidence | $($freeW.commandInventory.totalCommands) commands; $($freeW.commandInventory.bothProfiles) shared-profile; $($freeW.commandInventory.actionableMissingWpf) actionable WPF-missing; $($freeW.commandInventory.actionableMissingAvalonia) actionable Avalonia-missing; $($freeW.commandInventory.profileShapeOnly) profile-shape-only; $($freeW.commandInventory.commandIdAliases) command-id aliases; $($freeW.commandInventory.platformOnly) platform-only; $($freeW.commandInventory.deferred) deferred; $($freeW.renderedEvidence.artifactCoverage.evidenceRowCount) rendered comparison rows; Wave194 Font aggregate $($freeW.renderedEvidence.wave194.aggregateChangedPixels) changed pixels (-$([math]::Abs($freeW.renderedEvidence.wave194.aggregateDelta)), $('{0:P4}' -f $freeW.renderedEvidence.wave194.relativeImprovement)) | $($freeW.nextSlice) |",
        "| FreeP | Generated command inventory plus dialog/whole-window rendered evidence | $($freeP.commandInventory.totalCommands) commands; $($freeP.commandInventory.bothProfiles) shared-profile; $($freeP.commandInventory.actionableMissingWpf) actionable WPF-missing; $($freeP.commandInventory.actionableMissingAvalonia) actionable Avalonia-missing; $($freeP.commandInventory.platformOnly) platform-only; $($freeP.commandInventory.workflowEvidenceRows) workflow evidence rows; $($freeP.renderedEvidence.pairedEvidence.pairedScenarioCount) paired rendered scenarios; Wave194 topology evidence pins deck17 slide02 and leaves its residual unresolved | $($freeP.nextSlice) |",
        "",
        "## Rendered Evidence Summary",
        "",
        "Route inventory, rendered/comparison rows, and committed PNG/file artifacts are separate measures. Office baseline availability is an artifact-availability statement, not a visual-parity claim.",
        "",
        "FreeW canonical comparison scope: **$($freeW.renderedEvidence.canonicalComparison.kind)**. $($freeW.renderedEvidence.canonicalComparison.description) $($freeW.renderedEvidence.canonicalComparison.refreshInstruction)",
        "",
        "FreeX Wave193 No Fill transition evidence: **$($freeX.renderedEvidence.physicalEvidence.wave193PopupTransitions.summary)**. Retained evidence includes **$($freeX.renderedEvidence.physicalEvidence.wave193EvidenceArtifactCount)/$($freeX.renderedEvidence.physicalEvidence.wave193EvidenceArtifactExpectedCount) artifacts** and **$($freeX.renderedEvidence.physicalEvidence.wave193EvidenceProvenanceFileCount)/9 provenance files**.",
        "FreeX Wave194 mixed-type evidence: **$($freeX.renderedEvidence.physicalEvidence.wave194.physicalPassed)/$($freeX.renderedEvidence.physicalEvidence.wave194.physicalTotal)** physical workflow, **$($freeX.renderedEvidence.physicalEvidence.wave194.focusedAvaloniaGuardPassed)/$($freeX.renderedEvidence.physicalEvidence.wave194.focusedAvaloniaGuardTotal)** Avalonia guards, **$($freeX.renderedEvidence.physicalEvidence.wave194.focusedPresentationPassed)/$($freeX.renderedEvidence.physicalEvidence.wave194.focusedPresentationTotal)** Presentation, and **$($freeX.renderedEvidence.physicalEvidence.wave194.focusedCoreIoPassed)/$($freeX.renderedEvidence.physicalEvidence.wave194.focusedCoreIoTotal)** Core.IO (Wave194 plus five foreground-capture guards); geometry $($freeX.renderedEvidence.physicalEvidence.wave194.geometry.bounds), click $($freeX.renderedEvidence.physicalEvidence.wave194.geometry.click), package readback remains exact.",
        "",
        "| App | Route coverage | Artifact coverage | Paired WPF/Avalonia evidence | Physical/no-COM limitation | Authoritative Microsoft Office baseline |",
        "|---|---|---|---|---|---|",
        "| FreeX | $($freeX.renderedEvidence.routeCoverage.inventoryRouteCount) inventoried dialog routes; $($freeX.renderedEvidence.routeCoverage.pairedRouteEvidenceCount) paired route evidence rows | $($freeX.renderedEvidence.artifactCoverage.wpfManifestSurfaceCount) WPF + $($freeX.renderedEvidence.artifactCoverage.avaloniaManifestSurfaceCount) Avalonia dialog surfaces; complete $($freeX.renderedEvidence.chromeCapture.excelReferenceCount)/$($freeX.renderedEvidence.chromeCapture.wpfCaptureCount)/$($freeX.renderedEvidence.chromeCapture.avaloniaCaptureCount) Excel/WPF/Avalonia ribbon matrices; $($freeX.renderedEvidence.gridCorpus.totalAvaloniaCaptureCount) Avalonia grid-corpus captures | $($freeX.renderedEvidence.pairedEvidence.pairedSurfaceCount) paired dialog surfaces; $($freeX.renderedEvidence.chromeCapture.fixedViewportComparisonCount) fixed-width chrome triage rows per host | $($freeX.renderedEvidence.physicalEvidence.status); Linux Name Box $($freeX.renderedEvidence.physicalEvidence.linuxNameBoxParityPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxNameBoxParityTotal) visual and $($freeX.renderedEvidence.physicalEvidence.linuxNameBoxInteractionPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxNameBoxInteractionTotal) interaction; AutoFilter recalculation $($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterRecalculationPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterRecalculationTotal); sort persistence $($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterSortPersistencePassed)/$($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterSortPersistenceTotal); text criteria $($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterTextCriteriaPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterTextCriteriaTotal); numeric criteria $($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterNumericCriteriaPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterNumericCriteriaTotal) ($($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterNumericCriteriaStatus)); date criteria $($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterDateCriteriaPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterDateCriteriaTotal) ($($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterDateCriteriaStatus)); fill-color $($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterFillColorPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterFillColorTotal); font-color $($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterFontColorPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterFontColorTotal); No Fill $($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterNoFillPassed)/$($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterNoFillTotal); app-owned render manifests, complete foreground chrome matrices, and committed Excel range references | $($freeX.renderedEvidence.authoritativeMicrosoftOfficeBaseline.product): $($freeX.renderedEvidence.authoritativeMicrosoftOfficeBaseline.status); $($freeX.renderedEvidence.authoritativeMicrosoftOfficeBaseline.artifactCount) artifacts. $($freeX.renderedEvidence.authoritativeMicrosoftOfficeBaseline.limitation) |",
        "| FreeW | $($freeW.renderedEvidence.routeCoverage.inventoryRouteCount) inventoried route families; $($freeW.renderedEvidence.routeCoverage.comparedRouteCount) represented in comparison rows; $($freeW.renderedEvidence.routeCoverage.pairedRouteCount) paired and $($freeW.renderedEvidence.routeCoverage.avaloniaOnlyRouteCount) Avalonia-only | $($freeW.renderedEvidence.artifactCoverage.evidenceRowCount) dialog comparison rows; $($freeW.renderedEvidence.shellChrome.pairedStaticCaptureCount) paired static and $($freeW.renderedEvidence.shellChrome.pairedContextualCaptureCount) paired contextual shell captures; $($freeW.renderedEvidence.shellChrome.wordOfficeChromeReferenceCount) native Word ribbon references | $($freeW.renderedEvidence.pairedEvidence.pairedScenarioCount) paired dialog rows; $($freeW.renderedEvidence.pairedEvidence.passCount) pass classifications; $($freeW.renderedEvidence.pairedEvidence.mismatchCount) genuine visual mismatch classifications; shell captures review-required | $($freeW.renderedEvidence.physicalEvidence.status); app-owned dialog/full-window shell captures plus committed Word canvas and ribbon references | $($freeW.renderedEvidence.authoritativeMicrosoftOfficeBaseline.product): $($freeW.renderedEvidence.authoritativeMicrosoftOfficeBaseline.status); $($freeW.renderedEvidence.authoritativeMicrosoftOfficeBaseline.artifactCount) artifacts. $($freeW.renderedEvidence.authoritativeMicrosoftOfficeBaseline.limitation) |",
        "| FreeP | Dialog lane: $($freeP.renderedEvidence.routeCoverage.laneEntries[0].routeInventoryCount) routes/$($freeP.renderedEvidence.routeCoverage.laneEntries[0].renderedScenarioCount) scenarios; whole-window lane: $($freeP.renderedEvidence.routeCoverage.laneEntries[1].renderedScenarioCount) scenarios without a separate route inventory | $($freeP.renderedEvidence.artifactCoverage.wpfPngCount) WPF PNGs; $($freeP.renderedEvidence.artifactCoverage.avaloniaPngCount) Avalonia PNGs; $($freeP.renderedEvidence.artifactCoverage.diffPngCount) diff PNGs; $($freeP.renderedEvidence.nativeOfficeChrome.capturedReferenceCount) native PowerPoint ribbon refs; $($freeP.renderedEvidence.responsiveAppChrome.capturedPairCount) responsive WPF/Avalonia pairs; Wave194 topology source SHA $($freeP.renderedEvidence.wave194Topology.sourceCorpusSha256), $($freeP.renderedEvidence.wave194Topology.status), residual unresolved | $($freeP.renderedEvidence.pairedEvidence.pairedScenarioCount) paired scenarios; $($freeP.renderedEvidence.pairedEvidence.passCount) local comparison passes; $($freeP.renderedEvidence.pairedEvidence.mismatchCount) mismatches; native Office/app chrome $($freeP.renderedEvidence.nativeOfficeChrome.captureStatus)/$($freeP.renderedEvidence.responsiveAppChrome.captureStatus) | $($freeP.renderedEvidence.physicalEvidence.status); visible app-owned render targets, complete responsive app and Office ribbon lanes, and a committed PowerPoint COM corpus | $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.product): $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.status); $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.artifactCount) tracked artifacts across $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.referenceReadyDecks) decks, with $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.missingReferenceDecks) deck missing references. Current-source WPF/Avalonia averages: $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.wpfAverageMeanPercent)% / $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.avaloniaAverageMeanPercent)%. $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.limitation) |",
        "",
        "## Integration Gates",
        "",
        "Wave194's cumulative 582 app-slice count is **accepted**. All final integration gates passed against tested source commit ``$($dashboard.integrationGateEvidence.testedSourceCommit)``. $($dashboard.integrationGateEvidence.acceptanceRefreshNote)",
        "",
        "- Initial independent review: $($dashboard.integrationGateEvidence.initialIndependentReview)",
        "- Reintegration: $($dashboard.integrationGateEvidence.reintegration)",
        "- Focused tests: $($dashboard.integrationGateEvidence.focusedTests)",
        "- Initial reintegration preflight: $($dashboard.integrationGateEvidence.initialReintegrationPreflight)",
        "- Review remediation: $($dashboard.integrationGateEvidence.reviewRemediation)",
        "- Independent review: $($dashboard.integrationGateEvidence.independentReview)",
        "- Repository preflight: $($dashboard.integrationGateEvidence.repositoryPreflight)",
        "- Tested source commit: ``$($dashboard.integrationGateEvidence.testedSourceCommit)``",
        "- Acceptance refresh: $($dashboard.integrationGateEvidence.acceptanceRefreshNote)",
        "- Full Release build: $($dashboard.integrationGateEvidence.fullReleaseBuild)",
        "- Final default non-UI test lane: $($dashboard.integrationGateEvidence.defaultNonUiTestLane)",
        "- Initial default-lane result: $($dashboard.integrationGateEvidence.initialDefaultLane)",
        "- Remediation: $($dashboard.integrationGateEvidence.sourceTestRemediation)",
        "- Worker verification: $($dashboard.integrationGateEvidence.workerVerification)",
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
        '- `docs/parity/avalonia-parity-wave193-freex-autofilter-no-fill-20260823.md`',
        '- `docs/parity/evidence/wave193-freex-autofilter-no-fill-20260823/physical-result.json`',
        '- `docs/parity/evidence/wave193-freex-autofilter-no-fill-20260823/manifest.json`',
        '- `docs/parity/avalonia-parity-wave194-freex-autofilter-mixed-type-20260823.md`',
        '- `docs/parity/evidence/wave194-freex-autofilter-mixed-type-20260823/physical-result.json`',
        '- `docs/parity/evidence/wave194-freex-autofilter-mixed-type-20260823/manifest.json`',
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
        '- `docs/parity/avalonia-parity-wave193-freew-font-checkbox-glyph-20260823.md`',
        '- `docs/parity/avalonia-parity-wave194-freew-font-action-border-20260824.md`',
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
        '- `docs/parity/evidence/avalonia-parity-wave192-freep-evidence-20260823/metrics.json`',
        '- `docs/parity/avalonia-parity-wave193-freep-render-residual-20260823.md`',
        '- `docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/metrics.json`',
        '- `docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/references.json`',
        '- `docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/images.json`',
        '- `docs/parity/freep-wave194-deck17-slide02-topology-20260823.md`',
        '- `docs/parity/evidence/freep-wave194-deck17-slide02-topology-20260823/topology.json`'
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
