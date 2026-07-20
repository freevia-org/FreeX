[CmdletBinding()]
param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$wpfRoot = Join-Path $root 'freew/FreeW.App.Host'
$avaloniaRoot = Join-Path $root 'freew/FreeW.App.Avalonia'
$outRoot = Join-Path $root 'docs/parity'
$jsonPath = Join-Path $outRoot 'freew-media-dialog-parity-inventory.json'
$markdownPath = Join-Path $outRoot 'freew-media-dialog-parity-inventory.md'

$routes = @(
    @{ id = 'image-adjust'; name = 'Picture adjust'; wpf = 'ImageAdjustDialog.cs'; avalonia = 'MediaDialogParity.cs'; wired = $false; followUp = 'Wire freew.image-adjust-dialog in the Avalonia command callback; shell-owned.' },
    @{ id = 'image-border'; name = 'Picture border'; wpf = 'ImageBorderDialog.cs'; avalonia = 'PictureFormattingDialogs.cs'; wired = $true; followUp = '' },
    @{ id = 'image-crop'; name = 'Picture crop'; wpf = 'ImageCropDialog.cs'; avalonia = 'ImageAndTableConversionDialogs.cs'; wired = $true; followUp = '' },
    @{ id = 'image-position'; name = 'Picture position'; wpf = 'ImagePositionDialog.cs'; avalonia = 'MediaDialogParity.cs'; wired = $false; followUp = 'Wire freew.image-position in the Avalonia command callback; shell-owned.' },
    @{ id = 'image-size'; name = 'Picture size'; wpf = 'ImageSizeDialog.cs'; avalonia = 'PictureFormattingDialogs.cs'; wired = $true; followUp = '' },
    @{ id = 'image-alt-text'; name = 'Image Alt Text'; wpf = 'Ribbon/FreeWRibbonCommands.cs'; avalonia = 'PictureFormattingDialogs.cs'; wired = $true; followUp = 'Keep the existing WPF TextPrompt and Avalonia ImageAltTextDialog launchers under shell ownership.' },
    @{ id = 'image-table-conversion'; name = 'Image/table conversion'; wpf = 'Ribbon/FreeWRibbonCommands.cs'; avalonia = 'ImageAndTableConversionDialogs.cs'; wired = $true; followUp = 'Keep the existing Avalonia conversion launchers under MainWindow ownership.' },
    @{ id = 'insert-chart'; name = 'Insert Chart'; wpf = 'InsertChartDialog.cs'; avalonia = 'MediaDialogParity.cs'; wired = $false; followUp = 'Add the Avalonia Insert Chart callback and result application in shell-owned files.' },
    @{ id = 'chart-title'; name = 'Chart title'; wpf = 'ChartTitleDialog.cs'; avalonia = 'MediaDialogParity.cs'; wired = $false; followUp = 'Add the Avalonia chart-title callback and result application in shell-owned files.' },
    @{ id = 'chart-axis-titles'; name = 'Chart axis titles'; wpf = 'ChartAxisTitlesDialog.cs'; avalonia = 'MediaDialogParity.cs'; wired = $false; followUp = 'Add the Avalonia axis-title callback and result application in shell-owned files.' },
    @{ id = 'chart-size'; name = 'Chart size'; wpf = 'ChartSizeDialog.cs'; avalonia = 'MediaDialogParity.cs'; wired = $false; followUp = 'Add the Avalonia chart-size callback and result application in shell-owned files.' },
    @{ id = 'insert-smartart'; name = 'Insert SmartArt'; wpf = 'InsertSmartArtDialog.cs'; avalonia = 'MediaDialogParity.cs'; wired = $false; followUp = 'Add the Avalonia Insert SmartArt callback and result application in shell-owned files.' },
    @{ id = 'smartart-edit'; name = 'SmartArt edit text'; wpf = 'InsertSmartArtDialog.cs'; avalonia = 'SmartArtEditDialog.cs'; wired = $true; followUp = '' },
    @{ id = 'icon-picker'; name = 'Icon picker'; wpf = 'IconPickerDialog.cs'; avalonia = 'IconPickerDialog.cs'; wired = $false; followUp = 'Wire the picker in shell-owned files and provide the platform rasterizer/result application; Avalonia currently returns IconPickerSelection.' }
)

$items = foreach ($route in $routes) {
    $wpfPath = Join-Path $wpfRoot $route.wpf
    $avaloniaPath = Join-Path $avaloniaRoot $route.avalonia
    $wpfExists = Test-Path -LiteralPath $wpfPath
    $avaloniaExists = Test-Path -LiteralPath $avaloniaPath
    $status = if (-not $wpfExists) { 'authority-missing' } elseif (-not $avaloniaExists) { 'missing-avalonia-surface' } elseif ($route.id -eq 'icon-picker') { 'selection-surface-only' } elseif ($route.wired) { 'implemented-and-wired' } else { 'implemented-awaiting-shell-wiring' }
    [ordered]@{
        id = $route.id
        name = $route.name
        wpfAuthority = $route.wpf
        avaloniaSurface = $route.avalonia
        wpfPresent = $wpfExists
        avaloniaPresent = $avaloniaExists
        status = $status
        shellWired = [bool]$route.wired
        followUp = $route.followUp
        wpfSha256 = if ($wpfExists) { (Get-FileHash -Algorithm SHA256 -LiteralPath $wpfPath).Hash } else { $null }
        avaloniaSha256 = if ($avaloniaExists) { (Get-FileHash -Algorithm SHA256 -LiteralPath $avaloniaPath).Hash } else { $null }
    }
}

$inventory = [ordered]@{
    schema = 'freew-media-dialog-parity/v1'
    authority = 'FreeW WPF dialog lifecycle, defaults, validation, focus, cancel, and result contracts'
    ownershipBoundary = @('FreeW MainWindow files', 'ribbon construction/command registry/profile files', 'Backstage', 'page-layout files/planners', 'shared shell files')
    routeCount = $items.Count
    wiredCount = @($items | Where-Object shellWired).Count
    shellFollowUpCount = @($items | Where-Object { -not $_.shellWired }).Count
    generatedAtUtc = [DateTime]::UtcNow.ToString('o')
    routes = @($items)
}

if ($Check) {
    if (-not (Test-Path -LiteralPath $jsonPath) -or -not (Test-Path -LiteralPath $markdownPath)) { throw 'Generated media parity evidence files are missing.' }
    $existing = Get-Content -Raw -LiteralPath $jsonPath | ConvertFrom-Json
    if ($existing.schema -ne $inventory.schema -or $existing.routeCount -ne $inventory.routeCount) { throw 'Media parity evidence schema or route count is stale.' }
    foreach ($item in $existing.routes) {
        $current = $items | Where-Object id -eq $item.id
        if ($null -eq $current -or $current.wpfSha256 -ne $item.wpfSha256 -or $current.avaloniaSha256 -ne $item.avaloniaSha256) { throw "Media parity evidence is stale for route $($item.id)." }
    }
    if (-not (Select-String -LiteralPath $markdownPath -Pattern 'FreeW Media Dialog Parity Inventory' -Quiet)) { throw 'Generated Markdown evidence heading is missing.' }
    Write-Output "Fresh: $($existing.routeCount) routes; $($existing.wiredCount) wired; $($existing.shellFollowUpCount) shell follow-ups."
    exit 0
}

New-Item -ItemType Directory -Force -Path $outRoot | Out-Null
$inventory | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 -LiteralPath $jsonPath
$md = @('# FreeW Media Dialog Parity Inventory', '', "Generated: $($inventory.generatedAtUtc)", '', "Routes: $($inventory.routeCount) | Shell-wired: $($inventory.wiredCount) | Shell follow-ups: $($inventory.shellFollowUpCount)", '', '| Route | WPF authority | Avalonia surface | Status | Follow-up |', '|---|---|---|---|---|')
foreach ($item in $items) {
    $followUp = if ([string]::IsNullOrWhiteSpace($item.followUp)) { '' } else { $item.followUp }
    $md += "| $($item.name) | ``$($item.wpfAuthority)`` | ``$($item.avaloniaSurface)`` | $($item.status) | $followUp |"
}
$md += @('', 'Ownership boundary: this inventory intentionally records shell-owned wiring gaps without changing MainWindow, ribbon, Backstage, page-layout, or shared-shell files.', '', 'Run ``powershell -File tools/Generate-FreeWMediaDialogParityEvidence.ps1 -Check`` to verify source fingerprints are fresh.')
$md -join "`n" | Set-Content -Encoding utf8 -LiteralPath $markdownPath
Write-Output "Generated $($inventory.routeCount) routes at $jsonPath and $markdownPath."
