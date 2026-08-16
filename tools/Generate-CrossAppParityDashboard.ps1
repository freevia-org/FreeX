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
    $freew = Read-ToolJson -Path "docs\parity\freew-command-inventory.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freeWRouteInventory = Read-ToolJson -Path "docs\parity\freew-dialog-harness\freew_dialog_route_inventory.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freeWVisualComparison = Read-ToolJson -Path "docs\parity\freew-dialog-harness\freew_dialog_visual_comparison.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    if ($null -eq $freeWVisualComparison.scope -or [string]$freeWVisualComparison.scope.kind -ne "canonical-inputs-only") {
        throw "FreeW visual comparison must declare canonical-inputs-only scope before the cross-app dashboard can be generated."
    }
    $freeWOfficeBaseline = Read-ToolJson -Path "docs\parity\freew-word-baseline-2026-08-14\manifest.json" -RepoRoot $repoRoot -MissingMessage "Required Word Office baseline manifest is missing"
    $freep = Read-ToolJson -Path "docs\parity\freep-command-parity-inventory.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freePDialogVisualEvidence = Read-ToolJson -Path "docs\parity\freep-dialog-pane-visual-evidence\summary.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freePDialogArtifactManifest = Read-ToolJson -Path "docs\parity\freep-dialog-pane-visual-evidence\artifact-manifest.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freePWholeWindowVisualEvidence = Read-ToolJson -Path "docs\parity\freep-whole-window-visual-evidence\summary.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freePWholeWindowArtifactManifest = Read-ToolJson -Path "docs\parity\freep-whole-window-visual-evidence\artifact-manifest.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freePRenderParity = Read-ToolJson -Path "docs\parity\freep-render-slideshow-media-parity-20260720.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freePNativePickerEvidence = Read-ToolJson -Path "docs\parity\freep-native-picker-human-evidence.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
    $freePOfficeBaseline = Read-ToolJson -Path "docs\parity\freep-powerpoint-baseline-2026-08-14.json" -RepoRoot $repoRoot -MissingMessage "Required PowerPoint Office baseline manifest is missing"
    $freePOfficeRecalibration = Read-ToolJson -Path "docs\parity\freep-powerpoint-recalibration-2026-08-15.json" -RepoRoot $repoRoot -MissingMessage "Required PowerPoint current-source recalibration is missing"

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
            "docs/parity/freex-excel-com-baseline-2026-08-14/manifest.json"
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
        physicalEvidence = [ordered]@{
            status = "not-present-in-inputs"
            captureMode = "generated WPF/Avalonia dialog render manifests"
            noComStatus = "not-present-in-inputs"
            limitations = @(
                "The consumed FreeX inputs contain app-owned WPF/Avalonia render evidence only; no physical-device capture manifest is included.",
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
            "docs/parity/freew-word-baseline-2026-08-14/manifest.json"
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
            artifactManifestAvailable = $false
            artifactKind = "embedded comparison-row content metrics and classifications"
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
            status = "not-present-in-inputs"
            captureMode = "generated WPF/Avalonia dialog comparison rows"
            noComStatus = "not-present-in-inputs"
            limitations = @(
                "The consumed FreeW JSON contains app-owned WPF/Avalonia comparison rows, not physical-device capture evidence.",
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
        nextSlice = "A committed Word PNG baseline bundle is now available for 89 comparable rows, but 87 comparisons remain outside tolerance. Triage font metrics, pagination, drawing/object, chart, table, and WordArt deltas against the captured references before refreshing this cohort on the current source revision."
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
            "docs/parity/freep-powerpoint-recalibration-2026-08-15.json"
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
        physicalEvidence = [ordered]@{
            status = "not-available"
            captureMode = "visible app-owned render targets with scenario-isolated processes"
            noComStatus = [string]$freePOfficeBaseline.captureMode
            limitations = @(
                [string]$freePOfficeBaseline.limitation,
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
            currentSourceRevision = [string]$freePOfficeRecalibration.sourceRevision
            wpfAverageMeanPercent = [double]$freePOfficeRecalibration.summary.wpfAverageMeanPercent
            wpfMaximumMeanPercent = [double]$freePOfficeRecalibration.summary.wpfMaximumMeanPercent
            avaloniaAverageMeanPercent = [double]$freePOfficeRecalibration.summary.avaloniaAverageMeanPercent
            avaloniaMaximumMeanPercent = [double]$freePOfficeRecalibration.summary.avaloniaMaximumMeanPercent
            rendererPairAverageMeanPercent = [double]$freePOfficeRecalibration.summary.rendererPairAverageMeanPercent
            rendererPairMaximumMeanPercent = [double]$freePOfficeRecalibration.summary.rendererPairMaximumMeanPercent
        }
        claimBoundary = "Route/scenario coverage, committed PNG manifests, and local WPF/Avalonia comparison results only; no PowerPoint visual-parity claim is made."
    }

    $freePNextSlice = "The tracked PowerPoint corpus now has $($freePOfficeBaseline.artifactCount) COM-exported reference slides across $($freePOfficeBaseline.comparison.referenceReadyDecks) ready decks, with $($freePOfficeBaseline.comparison.missingReferenceDecks) deck missing references. The last current-source recalibration covers $($freePOfficeRecalibration.summary.officeReferenceSlides) paired Office slides and averages $($freePOfficeRecalibration.summary.wpfAverageMeanPercent)% for WPF and $($freePOfficeRecalibration.summary.avaloniaAverageMeanPercent)% for Avalonia, with maxima of $($freePOfficeRecalibration.summary.wpfMaximumMeanPercent)% / $($freePOfficeRecalibration.summary.avaloniaMaximumMeanPercent)%. Recalibrate against all tracked references, then prioritize bullets/text-autofit and 3-D charts."

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
            "docs/parity/freex-excel-com-baseline-2026-08-14/manifest.json",
            "docs/parity/freew-command-inventory.json",
            "docs/parity/freew-dialog-harness/freew_dialog_route_inventory.json",
            "docs/parity/freew-dialog-harness/freew_dialog_visual_comparison.json",
            "docs/parity/freew-word-baseline-2026-08-14/manifest.json",
            "docs/parity/freep-command-parity-inventory.json",
            "docs/parity/freep-dialog-pane-visual-evidence/summary.json",
            "docs/parity/freep-dialog-pane-visual-evidence/artifact-manifest.json",
            "docs/parity/freep-whole-window-visual-evidence/summary.json",
            "docs/parity/freep-whole-window-visual-evidence/artifact-manifest.json",
            "docs/parity/freep-render-slideshow-media-parity-20260720.json",
            "docs/parity/freep-native-picker-human-evidence.json",
            "docs/parity/freep-powerpoint-baseline-2026-08-14.json",
            "docs/parity/freep-powerpoint-recalibration-2026-08-15.json"
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
        "| FreeX | $($freeX.renderedEvidence.routeCoverage.inventoryRouteCount) inventoried dialog routes; $($freeX.renderedEvidence.routeCoverage.pairedRouteEvidenceCount) paired route evidence rows | $($freeX.renderedEvidence.artifactCoverage.wpfManifestSurfaceCount) WPF + $($freeX.renderedEvidence.artifactCoverage.avaloniaManifestSurfaceCount) Avalonia manifest surfaces; $($freeX.renderedEvidence.artifactCoverage.pairedManifestSurfaceCount) paired | $($freeX.renderedEvidence.pairedEvidence.pairedSurfaceCount) paired surfaces; $($freeX.renderedEvidence.pairedEvidence.unresolvedVisualReviewCandidateCount) unresolved high-delta candidates | $($freeX.renderedEvidence.physicalEvidence.status); app-owned render manifests plus committed Excel range references | $($freeX.renderedEvidence.authoritativeMicrosoftOfficeBaseline.product): $($freeX.renderedEvidence.authoritativeMicrosoftOfficeBaseline.status); $($freeX.renderedEvidence.authoritativeMicrosoftOfficeBaseline.artifactCount) artifacts. $($freeX.renderedEvidence.authoritativeMicrosoftOfficeBaseline.limitation) |",
        "| FreeW | $($freeW.renderedEvidence.routeCoverage.inventoryRouteCount) inventoried route families; $($freeW.renderedEvidence.routeCoverage.comparedRouteCount) represented in comparison rows; $($freeW.renderedEvidence.routeCoverage.pairedRouteCount) paired and $($freeW.renderedEvidence.routeCoverage.avaloniaOnlyRouteCount) Avalonia-only | $($freeW.renderedEvidence.artifactCoverage.evidenceRowCount) comparison rows; $($freeW.renderedEvidence.artifactCoverage.pairedComparisonRowCount) paired rows; $($freeW.renderedEvidence.artifactCoverage.avaloniaOnlyArtifactRowCount) Avalonia-only rows; committed Word reference PNGs | $($freeW.renderedEvidence.pairedEvidence.pairedScenarioCount) paired rows; $($freeW.renderedEvidence.pairedEvidence.passCount) pass classifications; $($freeW.renderedEvidence.pairedEvidence.mismatchCount) genuine visual mismatch classifications | $($freeW.renderedEvidence.physicalEvidence.status); app-owned comparison rows plus committed Word references | $($freeW.renderedEvidence.authoritativeMicrosoftOfficeBaseline.product): $($freeW.renderedEvidence.authoritativeMicrosoftOfficeBaseline.status); $($freeW.renderedEvidence.authoritativeMicrosoftOfficeBaseline.artifactCount) artifacts. $($freeW.renderedEvidence.authoritativeMicrosoftOfficeBaseline.limitation) |",
        "| FreeP | Dialog lane: $($freeP.renderedEvidence.routeCoverage.laneEntries[0].routeInventoryCount) routes/$($freeP.renderedEvidence.routeCoverage.laneEntries[0].renderedScenarioCount) scenarios; whole-window lane: $($freeP.renderedEvidence.routeCoverage.laneEntries[1].renderedScenarioCount) scenarios without a separate route inventory | $($freeP.renderedEvidence.artifactCoverage.wpfPngCount) WPF PNGs; $($freeP.renderedEvidence.artifactCoverage.avaloniaPngCount) Avalonia PNGs; $($freeP.renderedEvidence.artifactCoverage.diffPngCount) diff PNGs; $($freeP.renderedEvidence.artifactCoverage.fileCount) manifest files | $($freeP.renderedEvidence.pairedEvidence.pairedScenarioCount) paired scenarios; $($freeP.renderedEvidence.pairedEvidence.passCount) local comparison passes; $($freeP.renderedEvidence.pairedEvidence.mismatchCount) mismatches; $($freeP.renderedEvidence.pairedEvidence.limitationCount) limitations | $($freeP.renderedEvidence.physicalEvidence.status); visible app-owned render targets plus a committed PowerPoint COM corpus | $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.product): $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.status); $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.artifactCount) tracked artifacts across $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.referenceReadyDecks) decks, with $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.missingReferenceDecks) deck missing references. Current-source WPF/Avalonia averages: $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.wpfAverageMeanPercent)% / $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.avaloniaAverageMeanPercent)%. $($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.limitation) |",
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
        '- `docs/parity/freex-excel-com-baseline-2026-08-14/manifest.json`',
        '- `docs/parity/freew-command-inventory.json`',
        '- `docs/parity/freew-dialog-harness/freew_dialog_route_inventory.json`',
        '- `docs/parity/freew-dialog-harness/freew_dialog_visual_comparison.json`',
        '- `docs/parity/freew-word-baseline-2026-08-14/manifest.json`',
        '- `docs/parity/freep-command-parity-inventory.json`',
        '- `docs/parity/freep-dialog-pane-visual-evidence/summary.json`',
        '- `docs/parity/freep-dialog-pane-visual-evidence/artifact-manifest.json`',
        '- `docs/parity/freep-whole-window-visual-evidence/summary.json`',
        '- `docs/parity/freep-whole-window-visual-evidence/artifact-manifest.json`',
        '- `docs/parity/freep-render-slideshow-media-parity-20260720.json`',
        '- `docs/parity/freep-native-picker-human-evidence.json`',
        '- `docs/parity/freep-powerpoint-baseline-2026-08-14.json`',
        '- `docs/parity/freep-powerpoint-recalibration-2026-08-15.json`'
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
