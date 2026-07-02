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
        [Parameter(Mandatory = $true)]$DialogRoutes
    )

    $realBehaviorGaps = [int](Get-JsonPropertyValue $FunctionalClassificationSummary "real-behavior-gap")
    $pseudoGalleryItems = [int](Get-JsonPropertyValue $FunctionalClassificationSummary "pseudo-command-gallery-item")
    $nonClickControlRows = [int](Get-JsonPropertyValue $FunctionalClassificationSummary "non-click-control-inventory-row")
    $conditionalFormatPopupGalleryRows = [int](Get-JsonPropertyValue $FunctionalClassificationSummary "conditional-format-popup-gallery-row")
    $conditionalFormatPopupCatalogItems = [int](Get-JsonPropertyValue $FunctionalClassificationSummary "conditional-format-popup-catalog-item")
    $allDialogRoutesCaptured = $DialogRoutes.totalRoutes -eq $DialogRoutes.wpfCaptures -and
        $DialogRoutes.totalRoutes -eq $DialogRoutes.avaloniaCaptures

    if ($FunctionalMatrix.avaloniaMissing -eq 0 -and $realBehaviorGaps -eq 0 -and $allDialogRoutesCaptured) {
        return "Command/dialog parity is green; conditional-format popup/gallery classifier evidence now links $conditionalFormatPopupGalleryRows rows to $conditionalFormatPopupCatalogItems shared runtime catalog items. Next evidence gap is paired opened-state WPF/Avalonia/Excel captures, while $pseudoGalleryItems total pseudo-gallery rows and $nonClickControlRows non-click inventory rows remain tracked separately."
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
    }
    $freeXDialogRoutes = [ordered]@{
        totalRoutes = [int]$dialogInventory.summary.totalRoutes
        wpfCaptures = [int]$dialogInventory.summary.wpfCaptures
        avaloniaCaptures = [int]$dialogInventory.summary.avaloniaCaptures
        avaloniaHarnessRoutes = [int]$dialogInventory.summary.avaloniaHarnessRoutes
        sharedOrPresentationBacked = [int]$dialogInventory.summary.sharedOrPresentationBacked
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
        nextSlice = Get-FreeXNextSlice `
            -FunctionalMatrix $freeXFunctionalMatrix `
            -FunctionalClassificationSummary $functionalClassification.summary `
            -DialogRoutes $freeXDialogRoutes
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
        nextSlice = "Backstage safety/print/export planner evidence is deeper; keep closing renderer-edge evidence, especially native-print deferral and PDF/preview fidelity fixtures."
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
        }
        nextSlice = "Layout command routing now has a shared host intent; move to actual picker UI, slide-pane/editing, and workflow evidence depth."
    }

    $dashboard = [ordered]@{
        schema = "freex.parity.cross-app-dashboard.v1"
        sources = @(
            "docs/parity/command-inventory.json",
            "docs/parity/functional-parity.json",
            "docs/parity/functional-parity-classification.json",
            "docs/parity/dialog-parity-inventory.json",
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
        "| FreeX | Functional matrix, classifier, dialog inventory, command surface | $($freeX.functionalMatrix.totalCommands) functional commands; $($freeX.functionalMatrix.parity) parity; $($freeX.functionalMatrix.avaloniaMissing) Avalonia-missing; $($freeX.functionalMatrix.realBehaviorGaps) real classified binding gaps; $($freeX.functionalMatrix.pseudoCommandGalleryItems) classified pseudo-gallery rows; $($freeX.functionalMatrix.conditionalFormatPopupGalleryRows) conditional-format popup/gallery rows backed by $($freeX.functionalMatrix.conditionalFormatPopupCatalogItems) runtime catalog items; $($freeX.dialogRoutes.totalRoutes)/$($freeX.dialogRoutes.totalRoutes) dialog routes captured on WPF and Avalonia | $($freeX.nextSlice) |",
        "| FreeW | Generated command inventory | $($freeW.commandInventory.totalCommands) commands; $($freeW.commandInventory.bothProfiles) shared-profile; $($freeW.commandInventory.actionableMissingWpf) actionable WPF-missing; $($freeW.commandInventory.actionableMissingAvalonia) actionable Avalonia-missing; $($freeW.commandInventory.profileShapeOnly) profile-shape-only; $($freeW.commandInventory.commandIdAliases) command-id aliases; $($freeW.commandInventory.platformOnly) platform-only; $($freeW.commandInventory.deferred) deferred | $($freeW.nextSlice) |",
        "| FreeP | Generated command inventory | $($freeP.commandInventory.totalCommands) commands; $($freeP.commandInventory.bothProfiles) shared-profile; $($freeP.commandInventory.actionableMissingWpf) actionable WPF-missing; $($freeP.commandInventory.actionableMissingAvalonia) actionable Avalonia-missing; $($freeP.commandInventory.platformOnly) platform-only | $($freeP.nextSlice) |",
        "",
        "## Source Files",
        "",
        '- `docs/parity/command-inventory.json`',
        '- `docs/parity/functional-parity.json`',
        '- `docs/parity/functional-parity-classification.json`',
        '- `docs/parity/dialog-parity-inventory.json`',
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
