param(
    [string]$MarkdownPath = "docs\parity\freew-design-dialog-parity-20260720.md",
    [string]$JsonPath = "docs\parity\freew-design-dialog-parity-20260720.json",
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)
    $path = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Evidence source is missing: $RelativePath"
    }
    $path
}

function Get-RelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)
    [System.IO.Path]::GetRelativePath($repoRoot, $Path).Replace('\', '/')
}

function Get-SourceHashes {
    param([Parameter(Mandatory = $true)][string[]]$RelativePaths)
    $hashes = [ordered]@{}
    foreach ($relativePath in ($RelativePaths | Sort-Object -Unique)) {
        $resolved = Resolve-RepoPath $relativePath
        $hashes[$relativePath.Replace('\', '/')] = Get-ToolFileSha256Hash -LiteralPath $resolved
    }
    $hashes
}

$routes = @(
    [ordered]@{ Id = "design.themes"; DisplayName = "Themes gallery"; Status = "complete"; Authority = "freew/FreeW.App.Host/Ribbon/ThemeGallery.cs"; Implementation = "freew/FreeW.App.Avalonia/Ribbon/FreeWAvaloniaRibbonCommands.cs"; Tests = "freew/FreeW.App.Avalonia.Tests/DesignTabTests.cs"; Gap = "" },
    [ordered]@{ Id = "design.colors"; DisplayName = "Colors and Customize Colors"; Status = "complete"; Authority = "freew/FreeW.App.Host/CustomizeThemeColorsDialog.cs"; Implementation = "freew/FreeW.App.Avalonia/DesignDialogParity.cs"; Tests = "freew/FreeW.App.Presentation.Tests/DesignDialogPlannerTests.cs;freew/FreeW.App.Avalonia.Tests/DesignDialogParityTests.cs;freew/FreeW.App.Host.Tests/DesignDialogParitySourceTests.cs"; Gap = "" },
    [ordered]@{ Id = "design.fonts"; DisplayName = "Fonts and Customize Fonts"; Status = "complete"; Authority = "freew/FreeW.App.Host/CustomizeThemeFontsDialog.cs"; Implementation = "freew/FreeW.App.Avalonia/DesignDialogParity.cs"; Tests = "freew/FreeW.App.Presentation.Tests/DesignDialogPlannerTests.cs;freew/FreeW.App.Avalonia.Tests/DesignDialogParityTests.cs;freew/FreeW.App.Host.Tests/DesignDialogParitySourceTests.cs"; Gap = "" },
    [ordered]@{ Id = "design.paragraph-spacing"; DisplayName = "Custom Paragraph Spacing"; Status = "complete"; Authority = "freew/FreeW.App.Host/CustomParagraphSpacingDialog.cs"; Implementation = "freew/FreeW.App.Avalonia/PageLayoutDialogs.cs"; Tests = "freew/FreeW.App.Presentation.Tests/DesignDialogPlannerTests.cs;freew/FreeW.App.Avalonia.Tests/DesignDialogParityTests.cs;freew/FreeW.App.Avalonia.Tests/PageLayoutDialogParityTests.cs"; Gap = "" },
    [ordered]@{ Id = "design.effects"; DisplayName = "Effects gallery / selector"; Status = "complete"; Authority = "freew/FreeW.App.Host/Ribbon/ThemeGallery.cs"; Implementation = "freew/FreeW.App.Avalonia/DesignDialogParity.cs"; Tests = "freew/FreeW.App.Avalonia.Tests/DesignDialogParityTests.cs;freew/FreeW.App.Host.Tests/DocumentEffectRenderingTests.cs"; Gap = "" },
    [ordered]@{ Id = "design.style-sets"; DisplayName = "Style Sets gallery"; Status = "complete"; Authority = "freew/FreeW.App.Host/Ribbon/ThemeGallery.cs"; Implementation = "freew/FreeW.App.Avalonia/DesignDialogParity.cs"; Tests = "freew/FreeW.App.Avalonia.Tests/DesignDialogParityTests.cs;freew/FreeW.App.Avalonia.Tests/DesignTabTests.cs"; Gap = "" },
    [ordered]@{ Id = "design.default"; DisplayName = "Reset / Set as Default confirmation"; Status = "complete"; Authority = "freew/FreeW.App.Host/Ribbon/FreeWRibbonCommands.cs"; Implementation = "freew/FreeW.App.Avalonia/DesignDialogParity.cs"; Tests = "freew/FreeW.App.Presentation.Tests/DesignDialogPlannerTests.cs;freew/FreeW.App.Avalonia.Tests/DesignDialogParityTests.cs"; Gap = "" },
    [ordered]@{ Id = "design.watermark"; DisplayName = "Custom Watermark"; Status = "complete"; Authority = "freew/FreeW.App.Host/WatermarkOptionsDialog.cs"; Implementation = "freew/FreeW.App.Avalonia/DesignDialogs.cs"; Tests = "freew/FreeW.App.Avalonia.Tests/WatermarkDialogTests.cs;freew/FreeW.App.Avalonia.Tests/DesignTabTests.cs"; Gap = "" },
    [ordered]@{ Id = "design.page-color"; DisplayName = "Page Color / More Colors"; Status = "complete"; Authority = "freew/FreeW.App.Host/Ribbon/FreeWRibbonCommands.cs"; Implementation = "freew/FreeW.App.Avalonia/DesignDialogParity.cs"; Tests = "freew/FreeW.App.Presentation.Tests/DesignDialogPlannerTests.cs;freew/FreeW.App.Avalonia.Tests/DesignDialogParityTests.cs;freew/FreeW.App.Avalonia.Tests/DesignTabTests.cs"; Gap = "" },
    [ordered]@{ Id = "design.page-borders"; DisplayName = "Page Borders"; Status = "complete"; Authority = "freew/FreeW.App.Host/BordersAndShadingDialog.cs"; Implementation = "freew/FreeW.App.Avalonia/DesignDialogs.cs"; Tests = "freew/FreeW.App.Presentation.Tests/DesignDialogPlannerTests.cs;freew/FreeW.App.Avalonia.Tests/DesignTabTests.cs"; Gap = "" },
    [ordered]@{ Id = "design.borders-shading"; DisplayName = "Combined Borders and Shading"; Status = "authority-complete"; Authority = "freew/FreeW.App.Host/BordersAndShadingDialog.cs"; Implementation = "freew/FreeW.App.Presentation/Dialogs/BordersAndShadingDialogPlanner.cs"; Tests = "freew/FreeW.App.Presentation.Tests/BordersAndShadingDialogPlannerTests.cs"; Gap = "" }
)

$sourcePaths = @()
$sourcePaths += @("freew/FreeW.App.Avalonia/MainWindow.cs", "freew/FreeW.Ribbon.Definitions/FreeWAvaloniaRibbonDefinition.cs")
foreach ($route in $routes) {
    $sourcePaths += $route.Authority.Split(';')
    $sourcePaths += $route.Implementation.Split(';')
    $sourcePaths += $route.Tests.Split(';')
}
$sourceHashes = Get-SourceHashes $sourcePaths
$commit = (& git -C $repoRoot rev-parse HEAD).Trim()
$generatedAt = [DateTime]::UtcNow.ToString("o")
$schema = "freew.design-dialog-parity.v1"
$completeCount = @($routes | Where-Object { $_.Status -eq "complete" }).Count
$gapCount = @($routes | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Gap) }).Count

$document = [ordered]@{
    Schema = $schema
    GeneratedAtUtc = $generatedAt
    Commit = $commit
    RouteCounts = [ordered]@{ Total = $routes.Count; Complete = $completeCount; RemainingOwnedRoutes = 0; RecordedShellGaps = $gapCount }
    Routes = @($routes)
    SourceHashes = $sourceHashes
}

if ($Check) {
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $JsonPath) -PathType Leaf)) {
        throw "Generated JSON evidence is missing: $JsonPath"
    }
    $existing = Get-Content -LiteralPath (Join-Path $repoRoot $JsonPath) -Raw | ConvertFrom-Json
    foreach ($property in $sourceHashes.Keys) {
        $actual = $sourceHashes[$property]
        $recorded = $existing.SourceHashes.$property
        if ($actual -ne $recorded) {
            throw "Stale evidence for ${property}: expected $recorded, actual $actual"
        }
    }
    if ($existing.Schema -ne $document.Schema) {
        throw "Unexpected evidence schema: $($existing.Schema)"
    }
    Write-Output "Fresh: $JsonPath ($($sourceHashes.Count) source hashes)"
    exit 0
}

$jsonFullPath = Join-Path $repoRoot $JsonPath
$markdownFullPath = Join-Path $repoRoot $MarkdownPath
$jsonDirectory = Split-Path -Parent $jsonFullPath
$markdownDirectory = Split-Path -Parent $markdownFullPath
New-Item -ItemType Directory -Force -Path $jsonDirectory, $markdownDirectory | Out-Null
$document | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonFullPath -Encoding utf8

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("# FreeW Design Dialog Parity Evidence")
$lines.Add("")
$lines.Add("Generated at UTC: $generatedAt")
$lines.Add("Source commit: ``$commit``")
$lines.Add("Schema: ``$schema``")
$lines.Add("")
$lines.Add("Routes: $($routes.Count) total; $completeCount complete; 0 remaining in the owned dialog/planner scope; $gapCount shell gaps recorded.")
$lines.Add("")
$lines.Add("| Route | Status | WPF authority | Avalonia/shared implementation | Exact shell gap |")
$lines.Add("|---|---|---|---|---|")
foreach ($route in $routes) {
    $authority = $route.Authority -replace ';', '<br>'
    $implementation = $route.Implementation -replace ';', '<br>'
    $lines.Add("| $($route.DisplayName) | $($route.Status) | $authority | $implementation | $($route.Gap) |")
}
$lines.Add("")
$lines.Add("## Freshness")
$lines.Add("")
$lines.Add("`Generate-FreeWDesignDialogParityEvidence.ps1 -Check` recomputes SHA-256 for every authority, implementation, and focused-test source listed in the JSON. The check is expected to pass at handoff.")
$lines.Add("")
$lines.Add("| Source | SHA-256 |")
$lines.Add("|---|---|")
foreach ($property in $sourceHashes.Keys) {
    $lines.Add("| $property | $($sourceHashes[$property]) |")
}
$lines -join [Environment]::NewLine | Set-Content -LiteralPath $markdownFullPath -Encoding utf8
Write-Output "Generated: $MarkdownPath"
Write-Output "Generated: $JsonPath"
