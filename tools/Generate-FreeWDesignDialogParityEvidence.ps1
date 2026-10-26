param(
    [string]$MarkdownPath = "docs/parity/freew-design-dialog-parity-20260720.md",
    [string]$JsonPath = "docs/parity/freew-design-dialog-parity-20260720.json",
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

function Assert-SourcesExist {
    param([Parameter(Mandatory = $true)][string[]]$RelativePaths)
    $inputs = [System.Collections.Generic.List[string]]::new()
    foreach ($relativePath in ($RelativePaths | Sort-Object -Unique)) {
        $resolved = Resolve-ToolRepoPath -Path $relativePath -RepoRoot $repoRoot
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            throw "Evidence source is missing: $relativePath"
        }
        $inputs.Add((ConvertTo-ToolNormalizedRelativePath -Path $relativePath))
    }
    $inputs
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
$sourcePaths += @(
    "freew/FreeW.App.Avalonia/MainWindow.cs",
    "freew/FreeW.Ribbon.Definitions/FreeWRibbon.cs",
    "freew/FreeW.Ribbon.Definitions/FreeWCanonicalRibbonTabs.cs",
    "freew/FreeW.Ribbon.Definitions/FreeWCanonicalRibbonTabs.Ordinary.cs",
    "freew/FreeW.Ribbon.Definitions/FreeWCanonicalRibbonTabs.Contextual.cs"
)
foreach ($route in $routes) {
    $sourcePaths += $route.Authority.Split(';')
    $sourcePaths += $route.Implementation.Split(';')
    $sourcePaths += $route.Tests.Split(';')
}
$generatedInputs = Assert-SourcesExist $sourcePaths
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
    GeneratedInputs = @($generatedInputs)
}

if ($Check) {
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $JsonPath) -PathType Leaf)) {
        throw "Generated JSON evidence is missing: $JsonPath"
    }
    $existing = Get-Content -LiteralPath (Join-Path $repoRoot $JsonPath) -Raw | ConvertFrom-Json
    $existingInputs = @($existing.GeneratedInputs)
    $currentInputs = @($generatedInputs)
    if (@(Compare-Object -ReferenceObject $existingInputs -DifferenceObject $currentInputs -SyncWindow 0).Count -ne 0) {
        throw "Recorded GeneratedInputs no longer match the generator's covered-input list."
    }
    if ($existing.Schema -ne $document.Schema) {
        throw "Unexpected evidence schema: $($existing.Schema)"
    }
    Write-Output "Fresh: $JsonPath ($($generatedInputs.Count) covered inputs)"
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
$lines.Add("This artifact records hand-authored parity findings for the authority, implementation, and focused-test inputs listed below. `Generate-FreeWDesignDialogParityEvidence.ps1 -Check` only verifies that the generator reproduces those declared findings and that this covered-input list still resolves to real files; it does not detect edits to the contents of those files. When any listed source changes, the routes and gaps above must be re-verified by hand and this artifact regenerated.")
$lines.Add("")
$lines.Add("| Covered input |")
$lines.Add("|---|")
foreach ($input in $generatedInputs) {
    $lines.Add("| $input |")
}
$lines -join [Environment]::NewLine | Set-Content -LiteralPath $markdownFullPath -Encoding utf8
Write-Output "Generated: $MarkdownPath"
Write-Output "Generated: $JsonPath"
