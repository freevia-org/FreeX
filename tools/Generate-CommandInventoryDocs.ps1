param(
    [string]$InventoryPath = "docs\COMMAND_INVENTORY.json",
    [string]$CommandSurfacePath = "docs\COMMAND_SURFACE_PARITY.md",
    [string]$MenuToolbarPath = "docs\MENU_TOOLBAR_PARITY.md"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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

function Set-GeneratedBlock {
    param(
        [string]$Path,
        [string]$Marker,
        [string]$Content
    )

    $startMarker = "<!-- ${Marker}:start -->"
    $endMarker = "<!-- ${Marker}:end -->"
    $text = Get-Content -LiteralPath $Path -Raw
    $pattern = "(?s)$([regex]::Escape($startMarker)).*?$([regex]::Escape($endMarker))"
    $replacement = "$startMarker`n$Content`n$endMarker"

    if ($text -notmatch $pattern) {
        throw "Could not find generated block '$Marker' in $Path."
    }

    [IO.File]::WriteAllText((Resolve-Path -LiteralPath $Path), [regex]::Replace($text, $pattern, $replacement))
}

$inventory = Get-Content -LiteralPath $InventoryPath -Raw | ConvertFrom-Json
if ($inventory.schemaVersion -ne 1) {
    throw "Unsupported command inventory schema version '$($inventory.schemaVersion)'."
}

Set-GeneratedBlock $CommandSurfacePath "command-inventory:coverage-summary" (New-CoverageSummary $inventory.commandSurfaceTabs $true)
Set-GeneratedBlock $MenuToolbarPath "command-inventory:coverage-summary" (New-CoverageSummary $inventory.menuToolbarTabs $false)
