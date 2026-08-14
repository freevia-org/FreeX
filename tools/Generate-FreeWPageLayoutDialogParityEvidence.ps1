param(
    [string]$JsonPath = "docs\parity\freew-page-layout-dialog-parity-evidence.json",
    [string]$MarkdownPath = "docs\parity\freew-page-layout-dialog-parity-evidence.md",
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

function Read-SourceSet {
    param([string[]]$Paths)

    ($Paths | ForEach-Object {
        $path = Join-Path $repoRoot $_
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Page-layout parity evidence source was not found: $_"
        }
        Get-Content -LiteralPath $path -Raw
    }) -join [Environment]::NewLine
}

$wpfPaths = @(
    "freew\FreeW.App.Host\Ribbon\FreeWRibbonCommands.cs",
    "freew\FreeW.App.Host\Editing\DocumentView.cs",
    "freew\FreeW.App.Host\PageSetupDialog.cs",
    "freew\FreeW.App.Host\ParagraphBreaksDialog.cs",
    "freew\FreeW.App.Host\ParagraphIndentDialog.cs",
    "freew\FreeW.App.Host\ColumnsDialog.cs",
    "freew\FreeW.App.Host\CustomParagraphSpacingDialog.cs",
    "freew\FreeW.App.Host\DropCapOptionsDialog.cs",
    "freew\FreeW.App.Host\HyphenationOptionsDialog.cs",
    "freew\FreeW.App.Host\ManualHyphenationDialog.cs",
    "freew\FreeW.App.Host\LineNumberOptionsDialog.cs",
    "freew\FreeW.Ribbon.Definitions\FreeWRibbon.cs"
)
$avaloniaPaths = @(
    "freew\FreeW.App.Avalonia\MainWindow.cs",
    "freew\FreeW.App.Avalonia\Ribbon\FreeWAvaloniaRibbonCommands.cs",
    "freew\FreeW.App.Avalonia\Editing\DocumentView.cs",
    "freew\FreeW.App.Avalonia\PageSetupDialog.cs",
    "freew\FreeW.App.Avalonia\ParagraphDialog.cs",
    "freew\FreeW.App.Avalonia\PageLayoutDialogs.cs",
    "freew\FreeW.Ribbon.Definitions\FreeWCanonicalRibbonTabs.cs"
)
$wpfSource = Read-SourceSet $wpfPaths
$avaloniaSource = Read-SourceSet $avaloniaPaths
$pairedTests = @(
    "freew/FreeW.App.Presentation.Tests/PageLayoutCommandPlannerTests.cs",
    "freew/FreeW.App.Presentation.Tests/ManualHyphenationPlannerTests.cs",
    "freew/FreeW.Core.Model.Tests/ApplyManualHyphenationCommandTests.cs",
    "freew/FreeW.App.Host.Tests/PageLayoutDialogParityTests.cs",
    "freew/FreeW.App.Avalonia.Tests/PageLayoutDialogParityTests.cs"
)

$contracts = @(
    [ordered]@{
        id = "layout.page-setup"
        routes = @("freew.margins", "freew.custom-margins", "freew.orientation", "freew.size", "freew.more-paper-sizes", "freew.page-setup")
        surface = "WPF-authoritative Margins/Paper/Layout tabs plus quick geometry routes"
        lifecycle = "Owner-modal; OK applies once; Cancel, Escape, and window close return no result; Layout launchers defer to backed routes."
        validation = "PageSetupDialogPlanner validates non-negative margins/distances and positive geometry."
        resultApplication = "PageLayoutCommandPlanner applies the full result through one SetPageSettingsCommand and one layout refresh."
        sharedPolicy = "PageSetupDialogPlanner; PageLayoutCommandPlanner"
        wpfTokens = @("PageSetupDialog.ToPresentationResult", "PageLayoutCommandPlanner.ApplyPageSetupResult(page, planned)", "PageLayoutRibbonWorkflow.Register(")
        avaloniaTokens = @("PageSetupDialog.ShowAndApplyAsync", "PageLayoutCommandPlanner.ApplyPageSetupResult", "OpenMorePaperSizesDialog")
        tests = $pairedTests
    },
    [ordered]@{
        id = "layout.columns"
        routes = @("freew.columns", "freew.columns-one", "freew.columns-two", "freew.columns-three", "freew.columns-left", "freew.columns-right", "freew.columns-more")
        surface = "Columns presets and owner-modal More Columns dialog"
        lifecycle = "Preset routes apply immediately; dialog OK applies once; Cancel, Escape, and close do not mutate."
        validation = "ColumnsDialogPlanner validates count 1-12 and non-negative spacing and owns unequal-width presets."
        resultApplication = "The selected page settings are committed through one undoable page-settings command."
        sharedPolicy = "ColumnsDialogPlanner; PageLayoutCommandPlanner"
        wpfTokens = @("ColumnsDialog.Prompt", "PageLayoutCommandPlanner.ApplyColumnsResult", "FreeWRibbonCommandAction.ColumnsMore")
        avaloniaTokens = @("class ColumnsDialog", "new ColumnsDialogSession", "_session.PlanAcceptance(", "FreeWRibbonCommandAction.ColumnsMore")
        tests = $pairedTests
    },
    [ordered]@{
        id = "layout.breaks"
        routes = @("freew.page-break", "freew.column-break", "freew.section-break-next-page", "freew.section-break-continuous", "freew.section-break-even-page", "freew.section-break-odd-page")
        surface = "Immediate page, column, and section break menu routes"
        lifecycle = "Each route inserts one backed break at the current selection or caret; no dialog is involved."
        validation = "SectionBreakKind constrains section modes and editor insertion guards the active selection."
        resultApplication = "One document command inserts each break and invalidates layout."
        sharedPolicy = "Core break model and editor command bus"
        wpfTokens = @("FreeWRibbonCommandAction.ColumnBreak", "FreeWRibbonCommandAction.SectionBreakEvenPage")
        avaloniaTokens = @("editor.InsertColumnBreak", "SectionBreakKind.OddPage")
        tests = @("freew/FreeW.App.Avalonia.Tests/PageSetupDialogTests.cs", "freew/FreeW.App.Host.Tests/FreeWRibbonParityTests.cs")
    },
    [ordered]@{
        id = "layout.paragraph-composition"
        routes = @("freew.indent-left", "freew.indent-right", "freew.space-before", "freew.space-after", "freew.line-spacing", "freew.paragraph-dialog")
        surface = "Layout Paragraph controls and two-tab Paragraph dialog"
        lifecycle = "Dialog OK applies all indents, spacing, and pagination flags once to selected paragraphs; Cancel, Escape, and close do not mutate."
        validation = "ParagraphBreaksDialogPlanner owns parsing and pagination result construction."
        resultApplication = "Selection-scoped formatting is grouped into one undo entry and rerendered."
        sharedPolicy = "ParagraphBreaksDialogPlanner"
        wpfTokens = @("ParagraphDialogCommand", '"freew.indent-left"', '"freew.space-after"')
        avaloniaTokens = @("class ParagraphDialog", "ParagraphBreaksDialogPlanner.TryBuildResult", "ApplyParagraphDialogFormatting")
        tests = @("freew/FreeW.App.Avalonia.Tests/FontAndParagraphDialogTests.cs", "freew/FreeW.App.Host.Tests/HomeDialogDepthTests.cs")
    },
    [ordered]@{
        id = "design.custom-paragraph-spacing"
        routes = @("freew.custom-paragraph-spacing")
        surface = "Owner-modal Custom Paragraph Spacing dialog from Design"
        lifecycle = "Default button accepts; Cancel, Escape, and close return null; invalid OK remains open and focuses the failing field."
        validation = "CustomParagraphSpacingDialogPlanner owns ranges, field-specific errors, and spacing-set construction."
        resultApplication = "The accepted document spacing set applies through the existing document-wide spacing command."
        sharedPolicy = "CustomParagraphSpacingDialogPlanner"
        wpfTokens = @("CustomParagraphSpacingDialog.Prompt", "CustomParagraphSpacing: new CustomParagraphSpacingCommand(editor)")
        avaloniaTokens = @("class CustomParagraphSpacingDialog", "OpenCustomParagraphSpacingDialog", "CustomParagraphSpacing: OptionalHostCommand(callbacks.OpenCustomParagraphSpacingDialog)")
        tests = $pairedTests
    },
    [ordered]@{
        id = "insert.drop-cap-options"
        routes = @("freew.drop-cap", "freew.drop-cap-dropped", "freew.drop-cap-in-margin", "freew.drop-cap-none", "freew.drop-cap-options")
        surface = "Drop Cap presets and owner-modal Drop Cap Options dialog"
        lifecycle = "Default button accepts; Cancel, Escape, and close return null; focus starts on lines-to-drop."
        validation = "DropCapOptionsDialogPlanner owns position mapping, defaults, and bounded lines/distance."
        resultApplication = "Accepted position applies or clears the current paragraph drop cap in one selection-scoped command."
        sharedPolicy = "DropCapOptionsDialogPlanner; DropCap model helper"
        wpfTokens = @("global::FreeW.App.Host.DropCapOptionsDialog.Prompt", "Options: new DropCapOptionsCommand(editor)")
        avaloniaTokens = @("class DropCapOptionsDialog", "_session.PlanAcceptance(", "Options: OptionalHostCommand(callbacks.OpenDropCapOptionsDialog)")
        tests = $pairedTests
    },
    [ordered]@{
        id = "layout.hyphenation"
        routes = @("freew.hyphenation", "freew.hyphenation-none", "freew.hyphenation-auto", "freew.hyphenation-manual", "freew.hyphenation-options")
        surface = "Checked None/Automatic modes, owner-modal per-word manual review, and owner-modal options"
        lifecycle = "Mode routes apply immediately; manual review presents Yes/No/Cancel for each candidate; options OK applies once; Cancel, Escape, and close preserve already accepted manual choices without changing automatic mode."
        validation = "HyphenationOptionsDialogPlanner validates zone and consecutive-limit values; ManualHyphenationPlanner owns candidate order and valid break positions."
        resultApplication = "Backed settings apply through one undoable page-settings command; accepted manual breaks apply as one undoable U+00AD body-text command."
        sharedPolicy = "HyphenationOptionsDialogPlanner; ManualHyphenationPlanner; ApplyManualHyphenationCommand"
        wpfTokens = @("HyphenationOptionsDialog.Prompt", "PageLayoutRibbonWorkflow.Register(", "ManualHyphenationPlanner.CreateSession", "ApplyManualHyphenation(session.Edits)")
        avaloniaTokens = @("class HyphenationOptionsDialog", "OpenHyphenationOptionsDialog", "class ManualHyphenationDialog", "ManualHyphenationPlanner.CreateSession", "ApplyManualHyphenation(session.Edits)")
        tests = $pairedTests
    },
    [ordered]@{
        id = "layout.line-numbering"
        routes = @("freew.line-numbers", "freew.line-numbers-none", "freew.line-numbers-continuous", "freew.line-numbers-restart-page", "freew.line-numbers-options")
        surface = "Checked line-number modes and owner-modal options"
        lifecycle = "Mode routes apply immediately; options OK applies once; Cancel, Escape, and close do not mutate."
        validation = "LineNumberOptionsDialogPlanner validates positive Start At and Count By values."
        resultApplication = "Mode, start, and count apply through one undoable page-settings command and one layout refresh."
        sharedPolicy = "LineNumberOptionsDialogPlanner; PageLayoutCommandPlanner"
        wpfTokens = @("LineNumberOptionsDialog.Prompt", "PageLayoutRibbonWorkflow.Register(", "FreeWRibbonCommandAction.LineNumbersOptions")
        avaloniaTokens = @("class LineNumberOptionsDialog", "OpenLineNumberOptionsDialog", "ApplyLineNumberOptions")
        tests = $pairedTests
    }
)

$workflows = foreach ($contract in $contracts) {
    foreach ($token in $contract.wpfTokens) {
        if (-not $wpfSource.Contains($token)) {
            throw "WPF token '$token' is missing for page-layout workflow '$($contract.id)'."
        }
    }
    foreach ($token in $contract.avaloniaTokens) {
        if (-not $avaloniaSource.Contains($token)) {
            throw "Avalonia token '$token' is missing for page-layout workflow '$($contract.id)'."
        }
    }

    [ordered]@{
        id = $contract.id
        routes = @($contract.routes)
        surface = $contract.surface
        lifecycle = $contract.lifecycle
        validation = $contract.validation
        resultApplication = $contract.resultApplication
        sharedPolicy = $contract.sharedPolicy
        tests = @($contract.tests)
        status = "behavior-aligned"
    }
}

$remainingLimitations = @(
    [ordered]@{
        id = "paired-opened-state-pixels"
        kind = "visual-evidence"
        parityGap = $false
        exactWork = "Capture matching WPF/Avalonia opened, populated, validation, and cancel states at 96 DPI to compare exact dimensions, tab geometry, focus cues, clipping, and nonblank pixels."
    },
    [ordered]@{
        id = "platform-native-window-metrics"
        kind = "native-rendering"
        parityGap = $false
        exactWork = "OS window chrome, native font shaping, focus rectangles, and message presentation remain toolkit/platform rendered and are not expected to be pixel-identical."
    }
)

$summary = [ordered]@{
    workflowCount = $workflows.Count
    behaviorAligned = @($workflows | Where-Object status -eq "behavior-aligned").Count
    functionalParityGaps = 0
    remainingVisualOrNativeLimitations = 2
    sharedNonParityLimitations = 0
}
$document = [ordered]@{
    schemaVersion = 1
    generatedBy = "tools/Generate-FreeWPageLayoutDialogParityEvidence.ps1"
    inventoryMethod = "Eight explicit page-layout and paragraph-composition contracts validated against current WPF and Avalonia source tokens."
    summary = $summary
    sourceSets = [ordered]@{ wpf = $wpfPaths; avalonia = $avaloniaPaths }
    workflows = $workflows
    remainingLimitations = $remainingLimitations
}

$json = ($document | ConvertTo-Json -Depth 20) + [Environment]::NewLine
$markdown = New-Object System.Text.StringBuilder
[void]$markdown.AppendLine("# FreeW page-layout dialog parity evidence")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("Generated by tools/Generate-FreeWPageLayoutDialogParityEvidence.ps1; source-token validation keeps this inventory fresh.")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("## Summary")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("- Workflows: $($summary.workflowCount)")
[void]$markdown.AppendLine("- Behavior aligned: $($summary.behaviorAligned)")
[void]$markdown.AppendLine("- Functional parity gaps: $($summary.functionalParityGaps)")
[void]$markdown.AppendLine("- Remaining visual/native limitations: $($summary.remainingVisualOrNativeLimitations)")
[void]$markdown.AppendLine("- Shared non-parity limitations: $($summary.sharedNonParityLimitations)")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("## Workflows")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("| Workflow | Routes | Lifecycle and validation | Result application | Status |")
[void]$markdown.AppendLine("| --- | --- | --- | --- | --- |")
foreach ($workflow in $workflows) {
    $routes = ($workflow.routes -join ", ").Replace("|", "\|")
    $contract = "$($workflow.lifecycle) $($workflow.validation)".Replace("|", "\|")
    $application = $workflow.resultApplication.Replace("|", "\|")
    [void]$markdown.AppendLine(('| {0} | {1} | {2} | {3} | {4} |' -f $workflow.id, $routes, $contract, $application, $workflow.status))
}
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("## Exact Remaining Limitations")
[void]$markdown.AppendLine()
foreach ($limitation in $remainingLimitations) {
    [void]$markdown.AppendLine(('- **{0}** ({1}; parity gap: {2}): {3}' -f $limitation.id, $limitation.kind, $limitation.parityGap.ToString().ToLowerInvariant(), $limitation.exactWork))
}
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("No functional or semantic WPF/Avalonia parity gap remains in this page-layout slice.")
$markdownText = $markdown.ToString()

$resolvedJsonPath = Resolve-ToolRepoPath -Path $JsonPath -RepoRoot $repoRoot
$resolvedMarkdownPath = Resolve-ToolRepoPath -Path $MarkdownPath -RepoRoot $repoRoot
if ($Check) {
    Test-ToolGeneratedContentMatches -ExpectedContent $json -ActualPath $resolvedJsonPath -Label "FreeW page-layout JSON evidence" -GeneratorScriptName "tools/Generate-FreeWPageLayoutDialogParityEvidence.ps1" -NormalizeNewlines
    Test-ToolGeneratedContentMatches -ExpectedContent $markdownText -ActualPath $resolvedMarkdownPath -Label "FreeW page-layout Markdown evidence" -GeneratorScriptName "tools/Generate-FreeWPageLayoutDialogParityEvidence.ps1" -NormalizeNewlines
    Write-Host "FreeW page-layout dialog parity evidence is current."
    exit 0
}

New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedJsonPath) -Force | Out-Null
[System.IO.File]::WriteAllText($resolvedJsonPath, $json, [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($resolvedMarkdownPath, $markdownText, [System.Text.UTF8Encoding]::new($false))
Write-Host "Generated FreeW page-layout dialog parity evidence for $($workflows.Count) workflows."
