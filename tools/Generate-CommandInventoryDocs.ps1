param(
    [string]$InventoryPath = "docs\COMMAND_INVENTORY.json",
    [string]$CommandSurfacePath = "docs\COMMAND_SURFACE_PARITY.md",
    [string]$MenuToolbarPath = "docs\MENU_TOOLBAR_PARITY.md",
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

function Get-CoveragePercent {
    param($Tab)

    $denominator = [int]$Tab.implemented + [int]$Tab.partial + [int]$Tab.notImplemented
    if ($denominator -eq 0) {
        return 100
    }

    return [int][Math]::Round(([int]$Tab.implemented + [int]$Tab.partial) * 100.0 / $denominator)
}

function New-CoverageRow {
    param(
        $Tab,
        [bool]$BoldLabel
    )

    $name = [string]$Tab.name
    $implemented = [string]$Tab.implemented
    $partial = [string]$Tab.partial
    $notImplemented = [string]$Tab.notImplemented
    $deferred = [string]$Tab.deferred
    $excluded = [string]$Tab.excluded

    if ($BoldLabel) {
        $name = "**$name**"
        $implemented = "**$implemented**"
        $partial = "**$partial**"
        $notImplemented = "**$notImplemented**"
        $deferred = "**$deferred**"
        $excluded = "**$excluded**"
    }

    $coverage = Get-CoveragePercent $Tab
    return "| $name | $implemented | $partial | $notImplemented | $deferred | $excluded | **$coverage%** |"
}

function New-CoverageSummary {
    param(
        [array]$Tabs,
        [bool]$BoldCoverageHeader
    )

    $coverageHeader = if ($BoldCoverageHeader) { "**Coverage**" } else { "Coverage" }
    $lines = @(
        "| Tab | Implemented | Partial | Not Implemented | Deferred | Excluded | $coverageHeader |",
        "|---|---:|---:|---:|---:|---:|---:|"
    )

    foreach ($tab in $Tabs) {
        $lines += New-CoverageRow $tab $false
    }

    $total = [pscustomobject]@{
        name = "TOTAL"
        implemented = ($Tabs | Measure-Object -Property implemented -Sum).Sum
        partial = ($Tabs | Measure-Object -Property partial -Sum).Sum
        notImplemented = ($Tabs | Measure-Object -Property notImplemented -Sum).Sum
        deferred = ($Tabs | Measure-Object -Property deferred -Sum).Sum
        excluded = ($Tabs | Measure-Object -Property excluded -Sum).Sum
    }
    $lines += New-CoverageRow $total $true
    return ($lines -join "`n")
}

function New-CommandRows {
    param($Section)

    if ($Section.PSObject.Properties.Name -contains "groups" -and $Section.groups) {
        $groupBlocks = @()
        foreach ($group in $Section.groups) {
            $groupBlocks += "### $($group.heading)`n`n$(New-CommandTable $Section.itemColumn $group.rows)"
        }

        return ($groupBlocks -join "`n`n")
    }

    return New-CommandTable $Section.itemColumn $Section.rows
}

function Test-JsonProperty {
    param(
        [Parameter(Mandatory = $true)]$InputObject,
        [Parameter(Mandatory = $true)][string]$Name
    )

    return $InputObject.PSObject.Properties.Name -contains $Name
}

function New-CommandTable {
    param(
        [string]$ItemColumn,
        [array]$Rows
    )

    $itemColumn = if ($ItemColumn) { [string]$ItemColumn } else { "Command" }
    $lines = @(
        "| $itemColumn | Status | Notes |",
        "|---|---|---|"
    )

    foreach ($row in $Rows) {
        $lines += "| $($row.name) | $($row.status) | $($row.notes) |"
    }

    return ($lines -join "`n")
}

function Set-GeneratedBlock {
    param(
        [string]$Path,
        [string]$Marker,
        [string]$Content,
        [bool]$CheckOnly
    )

    $startMarker = "<!-- ${Marker}:start -->"
    $endMarker = "<!-- ${Marker}:end -->"
    $text = Get-Content -LiteralPath $Path -Raw
    $pattern = "(?s)$([regex]::Escape($startMarker)).*?$([regex]::Escape($endMarker))"
    $newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
    $normalizedContent = $Content -replace "`r?`n", $newline
    $replacement = "$startMarker$newline$normalizedContent$newline$endMarker"

    if ($text -notmatch $pattern) {
        throw "Could not find generated block '$Marker' in $Path."
    }

    $updatedText = [regex]::Replace($text, $pattern, $replacement)
    if ($CheckOnly) {
        if ($updatedText -cne $text) {
            throw "Generated block '$Marker' is out of date in $Path. Run tools\Generate-CommandInventoryDocs.ps1 to refresh it."
        }

        return
    }

    [IO.File]::WriteAllText((Resolve-Path -LiteralPath $Path), $updatedText)
}

$inventoryPath = Resolve-RepoPath $InventoryPath
$commandSurfacePath = Resolve-RepoPath $CommandSurfacePath
$menuToolbarPath = Resolve-RepoPath $MenuToolbarPath

$inventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json
if ($inventory.schemaVersion -ne 1) {
    throw "Unsupported command inventory schema version '$($inventory.schemaVersion)'."
}

Set-GeneratedBlock $commandSurfacePath "command-inventory:coverage-summary" (New-CoverageSummary $inventory.commandSurfaceTabs $true) $Check.IsPresent
Set-GeneratedBlock $menuToolbarPath "command-inventory:coverage-summary" (New-CoverageSummary $inventory.menuToolbarTabs $false) $Check.IsPresent

if (Test-JsonProperty $inventory "commandSurfaceRows") {
    foreach ($section in $inventory.commandSurfaceRows) {
        $markerName = ($section.name -replace '[^A-Za-z0-9]+', '-').Trim('-').ToLowerInvariant()
        Set-GeneratedBlock $commandSurfacePath "command-inventory:command-surface:$markerName" (New-CommandRows $section) $Check.IsPresent
    }
}

if (Test-JsonProperty $inventory "menuToolbarRows") {
    foreach ($section in $inventory.menuToolbarRows) {
        $markerName = ($section.name -replace '[^A-Za-z0-9]+', '-').Trim('-').ToLowerInvariant()
        Set-GeneratedBlock $menuToolbarPath "command-inventory:menu-toolbar:$markerName" (New-CommandRows $section) $Check.IsPresent
    }
}

if ($Check) {
    Write-Host "Command inventory docs are up to date."
}
