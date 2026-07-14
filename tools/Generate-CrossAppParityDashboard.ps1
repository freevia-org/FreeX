param(
    [string]$JsonPath = "docs\parity\avalonia-wpf-cross-app-dashboard.json",
    [string]$MarkdownPath = "docs\parity\avalonia-wpf-cross-app-dashboard.md",
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

function Read-GeneratedJson {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolvedPath = Resolve-RepoPath $Path
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
        throw "Required generated parity input is missing: $resolvedPath"
    }

    Get-Content -LiteralPath $resolvedPath -Raw | ConvertFrom-Json
}

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

    if ($FunctionalMatrix.avaloniaMissing -eq 0 -and
        $realBehaviorGaps -eq 0 -and
        $allDialogRoutesCaptured -and
        $allDialogManifestSurfacesPaired -and
        $catalogBackedPseudoGalleryRows -eq $pseudoGalleryItems) {
        return "Command/dialog route coverage is green; all $pseudoGalleryItems pseudo-gallery rows are catalog-backed in classifier evidence ($conditionalFormatPopupGalleryRows conditional-format rows over $conditionalFormatPopupCatalogItems runtime catalog items, $fontBorderPopupGalleryRows font/border rows, and $accountingSymbolPopupGalleryRows accounting-symbol rows), and dialog screenshot evidence has $($DialogVisualEvidence.pairedCapturedSurfaceIds) paired WPF/Avalonia manifest surface ids with $($DialogVisualEvidence.additionalAvaloniaCapturedSurfaceIds) Avalonia-only ids. Keep the $($DialogVisualEvidence.pairedRawPixelDimensionMismatches) raw PNG pixel mismatches separate from missing-pair status: $($DialogVisualEvidence.pairedCaptureScaleNormalizedDimensionMatches) normalize away by capture DPI, while $($DialogVisualEvidence.pairedDimensionMismatches) scale-aware paired mismatches remain, with $($DialogVisualEvidence.policyAcceptedNativeDifferences) policy-accepted native/control rows, $($DialogVisualEvidence.contentVisualMismatches) content/visual mismatches, $($DialogVisualEvidence.evidenceLimitations) evidence limitations, $($DialogVisualEvidence.realLogicalSizeMismatches) real logical-size mismatches, and $($DialogVisualEvidence.stalePromotedExpectedSizeEvidence) stale promoted expected-size evidence rows."
    }

    if ($FunctionalMatrix.avaloniaMissing -eq 0 -and $realBehaviorGaps -eq 0 -and $allDialogRoutesCaptured) {
        return "Command/dialog route coverage is green; $catalogBackedPseudoGalleryRows of $pseudoGalleryItems pseudo-gallery rows are catalog-backed in classifier evidence, and dialog screenshot evidence currently has $($DialogVisualEvidence.additionalAvaloniaCapturedSurfaceIds) Avalonia-only manifest ids plus $($DialogVisualEvidence.wpfManifestIdsWithoutAvaloniaPair) WPF-only manifest ids. Continue catalog/evidence cleanup before claiming full visual parity."
    }

    if ($FunctionalMatrix.avaloniaMissing -gt 0 -or $realBehaviorGaps -gt 0) {
        return "Resolve generated Avalonia command-binding or real behavior gaps before taking additional evidence slices."
    }

    return "Refresh paired WPF/Avalonia dialog evidence until every generated dialog route has current captures."
}

function Test-FileContentMatches {
    param(
        [Parameter(Mandatory = $true)][string]$ExpectedPath,
        [Parameter(Mandatory = $true)][string]$ActualPath,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $ActualPath -PathType Leaf)) {
        throw "$Label is missing. Run tools\Generate-CrossAppParityDashboard.ps1 to create it."
    }

    $expected = (Get-Content -LiteralPath $ExpectedPath -Raw) -replace "`r`n", "`n"
    $actual = (Get-Content -LiteralPath $ActualPath -Raw) -replace "`r`n", "`n"
    if ($expected -cne $actual) {
        throw "$Label is out of date. Run tools\Generate-CrossAppParityDashboard.ps1 to refresh it."
    }
}

$resolvedJsonPath = Resolve-RepoPath $JsonPath
$resolvedMarkdownPath = Resolve-RepoPath $MarkdownPath
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("freex-cross-app-parity-dashboard-" + [System.Guid]::NewGuid().ToString("N"))
$tempJsonPath = Join-Path $tempRoot "avalonia-wpf-cross-app-dashboard.json"
$tempMarkdownPath = Join-Path $tempRoot "avalonia-wpf-cross-app-dashboard.md"

New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $commandInventory = Read-GeneratedJson "docs\parity\command-inventory.json"
    $functional = Read-GeneratedJson "docs\parity\functional-parity.json"
    $functionalClassification = Read-GeneratedJson "docs\parity\functional-parity-classification.json"
    $dialogInventory = Read-GeneratedJson "docs\parity\dialog-parity-inventory.json"
    $dialogVisualEvidence = Read-GeneratedJson "docs\parity\dialog-visual-evidence-summary.json"
    $freew = Read-GeneratedJson "docs\parity\freew-command-inventory.json"
    $freep = Read-GeneratedJson "docs\parity\freep-command-parity-inventory.json"

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
        contentVisualMismatches = [int](Get-JsonPropertyValue $dimensionMismatchBuckets "content/visual mismatch")
        evidenceLimitations = [int](Get-JsonPropertyValue $dimensionMismatchBuckets "evidence limitation")
        expectedPlatformNativeDifferences = [int](Get-JsonPropertyValue $dimensionMismatchBuckets "expected platform/native difference")
        realLogicalSizeMismatches = [int](Get-JsonPropertyValue $dimensionMismatchBuckets "real logical-size mismatch")
        policyAcceptedNativeDifferences = [int](Get-JsonPropertyValue $dialogVisualEvidence.summary "policyAcceptedNativeDifferences")
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
        nextSlice = Get-FreeXNextSlice `
            -FunctionalMatrix $freeXFunctionalMatrix `
            -FunctionalClassificationSummary $functionalClassification.summary `
            -DialogRoutes $freeXDialogRoutes `
            -DialogVisualEvidence $freeXDialogVisualEvidence
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
        nextSlice = "Backstage print/export and WordArt/watermark visual proof now have strict paired WPF/Avalonia renderer contracts plus no-Word real-capture smoke runs. Next FreeW evidence slices are the same runs with real Word PNG baselines on a Word-capable host, cleanup of unrelated all-up visual runner drift, and broader visual proof for drawing/object/chart/table surfaces."
    }

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
        nextSlice = "Layout picker, table picker, rich inline table/text editing, shared visible list galleries, picture-bullet import/rendering plus picker/media authoring, slide-pane reorder, bottom New Slide, notes-page preview/PDF rendering, fixed-layout PDF connector, image, triangle-arrowhead, rotation, ellipse, picture-frame/crop/alpha/color-effect, and shape-opacity export, export/backstage package-handoff, presenter recording backend contracts plus capture-adapter readiness and paired backend injection, captured PPTX media and generated WebVTT caption artifact authoring, focused PowerPoint-native caption package retention including external caption relationship metadata, shared chart number/date label rendering plus edge/mixed manual-layout bounds, surface grid plus projected contour/wireframe/3-D surface geometry plus PowerPoint/WPF/Avalonia chart baseline capture readiness, SmartArt basic hierarchy plus vertical bullet list plus basic cycle plus radial cycle plus gear cycle plus text cycle plus basic, continuous block, segmented, and chevron process plus basic block list plus vertical box list plus stacked list plus basic pyramid plus bounded pictureCaptionList live layout plus shared outline reorder/promote/demote editing and data-part authoring, header/footer placeholder creation plus inherited layout/master geometry, title-slide suppression, fixed/auto date options, shared OMML matrix spacing/base alignment, delimiter grow semantics, accent/bar render-plan coverage, manual line breaks, boxed equation-array alignment points, transparent phantom spacing classes, and pre-sub/superscript layout, ink execution with repeated custom-show route retention, modern comments/review with shared mention candidate and insertion planning, accessibility/proofing workflow depth, and animation-pane row/playback workflow evidence now have WPF/Avalonia no-COM evidence paths. Continue workflow-depth slices in real OS microphone/camera implementations, real PowerPoint chart PNG captures and remaining type-specific chart decisions, broader SmartArt layout families plus text-pane/cache-regeneration authoring, remaining OMML structure/layout and PowerPoint-authoritative math visual-baseline coverage, broader PowerPoint-native media/caption package baselines, broader PowerPoint-authoritative PDF visual baselines, and PowerPoint-authoritative animation-pane visual/playback baselines."
      }

    $dashboard = [ordered]@{
        schema = "freex.parity.cross-app-dashboard.v1"
        sources = @(
            "docs/parity/command-inventory.json",
            "docs/parity/functional-parity.json",
            "docs/parity/functional-parity-classification.json",
            "docs/parity/dialog-parity-inventory.json",
            "docs/parity/dialog-visual-evidence-summary.json",
            "docs/parity/freew-command-inventory.json",
            "docs/parity/freep-command-parity-inventory.json"
        )
        apps = @($freeX, $freeW, $freeP)
    }

    $json = ($dashboard | ConvertTo-Json -Depth 12) + "`n"
    Set-Content -LiteralPath $tempJsonPath -Value $json -NoNewline -Encoding UTF8

    $md = @(
        "# Avalonia/WPF Cross-App Parity Dashboard",
        "",
        'Generated by `tools/Generate-CrossAppParityDashboard.ps1` from existing generated parity JSON. Do not edit by hand.',
        "",
        "## Summary",
        "",
        "| App | Primary evidence | Current generated state | Next slice |",
        "|---|---|---|---|",
        "| FreeX | Functional matrix, classifier, dialog inventory, dialog visual evidence, command surface | $($freeX.functionalMatrix.totalCommands) functional commands; $($freeX.functionalMatrix.parity) parity; $($freeX.functionalMatrix.avaloniaMissing) Avalonia-missing; $($freeX.functionalMatrix.realBehaviorGaps) real classified binding gaps; $($freeX.functionalMatrix.pseudoCommandGalleryItems) catalog-backed pseudo-gallery rows ($($freeX.functionalMatrix.conditionalFormatPopupGalleryRows) conditional-format, $($freeX.functionalMatrix.fontBorderPopupGalleryRows) font/border, $($freeX.functionalMatrix.accountingSymbolPopupGalleryRows) accounting-symbol); $($freeX.dialogRoutes.totalRoutes)/$($freeX.dialogRoutes.totalRoutes) dialog routes captured on WPF and Avalonia; $($freeX.dialogVisualEvidence.pairedCapturedSurfaceIds) paired screenshot surface ids, $($freeX.dialogVisualEvidence.additionalAvaloniaCapturedSurfaceIds) Avalonia-only ids, $($freeX.dialogVisualEvidence.wpfManifestIdsWithoutAvaloniaPair) WPF-only ids; $($freeX.dialogVisualEvidence.pairedRawPixelDimensionMismatches) raw PNG pixel mismatches, $($freeX.dialogVisualEvidence.pairedCaptureScaleNormalizedDimensionMatches) DPI-normalized matches, $($freeX.dialogVisualEvidence.policyAcceptedNativeDifferences) policy-accepted native/control differences, $($freeX.dialogVisualEvidence.contentVisualMismatches) content/visual mismatches, $($freeX.dialogVisualEvidence.evidenceLimitations) evidence limitations, $($freeX.dialogVisualEvidence.realLogicalSizeMismatches) real logical-size mismatches | $($freeX.nextSlice) |",
        "| FreeW | Generated command inventory | $($freeW.commandInventory.totalCommands) commands; $($freeW.commandInventory.bothProfiles) shared-profile; $($freeW.commandInventory.actionableMissingWpf) actionable WPF-missing; $($freeW.commandInventory.actionableMissingAvalonia) actionable Avalonia-missing; $($freeW.commandInventory.profileShapeOnly) profile-shape-only; $($freeW.commandInventory.commandIdAliases) command-id aliases; $($freeW.commandInventory.platformOnly) platform-only; $($freeW.commandInventory.deferred) deferred | $($freeW.nextSlice) |",
        "| FreeP | Generated command/evidence inventory | $($freeP.commandInventory.totalCommands) commands; $($freeP.commandInventory.bothProfiles) shared-profile; $($freeP.commandInventory.actionableMissingWpf) actionable WPF-missing; $($freeP.commandInventory.actionableMissingAvalonia) actionable Avalonia-missing; $($freeP.commandInventory.platformOnly) platform-only; $($freeP.commandInventory.workflowEvidenceRows) workflow evidence rows | $($freeP.nextSlice) |",
        "",
        "## Source Files",
        "",
        '- `docs/parity/command-inventory.json`',
        '- `docs/parity/functional-parity.json`',
        '- `docs/parity/functional-parity-classification.json`',
        '- `docs/parity/dialog-parity-inventory.json`',
        '- `docs/parity/dialog-visual-evidence-summary.json`',
        '- `docs/parity/freew-command-inventory.json`',
        '- `docs/parity/freep-command-parity-inventory.json`'
    ) -join "`n"
    Set-Content -LiteralPath $tempMarkdownPath -Value ($md + "`n") -NoNewline -Encoding UTF8

    if ($Check) {
        Test-FileContentMatches -ExpectedPath $tempJsonPath -ActualPath $resolvedJsonPath -Label "Cross-app parity dashboard JSON"
        Test-FileContentMatches -ExpectedPath $tempMarkdownPath -ActualPath $resolvedMarkdownPath -Label "Cross-app parity dashboard Markdown"
    }
    else {
        Copy-Item -LiteralPath $tempJsonPath -Destination $resolvedJsonPath -Force
        Copy-Item -LiteralPath $tempMarkdownPath -Destination $resolvedMarkdownPath -Force
        Write-Host "Wrote $JsonPath and $MarkdownPath."
    }
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
