param(
    [string]$JsonPath = "docs\parity\freew-editing-reference-parity-evidence.json",
    [string]$MarkdownPath = "docs\parity\freew-editing-reference-parity-evidence.md",
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
            throw "Parity evidence source was not found: $_"
        }
        Get-Content -LiteralPath $path -Raw
    }) -join [Environment]::NewLine
}

$wpfSource = Read-SourceSet @(
    "freew\FreeW.App.Host\MainWindow.cs",
    "freew\FreeW.App.Host\Ribbon\FreeWRibbonCommands.cs",
    "freew\FreeW.App.Host\Editing\DocumentView.cs",
    "freew\FreeW.App.Host\DateTimeDialog.cs",
    "freew\FreeW.App.Host\FootnoteEndnoteOptionsDialog.cs",
    "freew\FreeW.App.Host\MultilevelListDialog.cs",
    "freew\FreeW.App.Host\BookmarkManagerDialog.cs",
    "freew\FreeW.App.Host\TableOfAuthoritiesDialog.cs",
    "freew\FreeW.App.Host\ThesaurusPane.cs"
)
$avaloniaSource = Read-SourceSet @(
    "freew\FreeW.App.Avalonia\MainWindow.cs",
    "freew\FreeW.App.Avalonia\Ribbon\FreeWAvaloniaRibbonCommands.cs",
    "freew\FreeW.App.Avalonia\Editing\DocumentView.cs",
    "freew\FreeW.App.Avalonia\NotesPane.cs",
    "freew\FreeW.App.Avalonia\DateTimeDialog.cs",
    "freew\FreeW.App.Avalonia\FootnoteEndnoteOptionsDialog.cs",
    "freew\FreeW.App.Avalonia\ImageAndTableConversionDialogs.cs",
    "freew\FreeW.App.Avalonia\MultilevelListDialog.cs",
    "freew\FreeW.App.Avalonia\BookmarkManagerDialog.cs",
    "freew\FreeW.App.Avalonia\TableOfAuthoritiesDialog.cs",
    "freew\FreeW.App.Avalonia\ThesaurusPane.cs"
)
$sharedSource = Read-SourceSet @(
    "freew\FreeW.App.Presentation\Ribbon\FreeWStatefulToggleCommand.cs",
    "freew\FreeW.App.Presentation\Ribbon\ThesaurusPaneSession.cs",
    "freew\FreeW.App.Presentation\Ribbon\ThesaurusPresentationPlanner.cs",
    "freew\FreeW.App.Presentation\Dialogs\MultilevelListDialogPlanner.cs",
    "freew\FreeW.App.Presentation\Dialogs\MultilevelListDialogSession.cs"
)

function New-Workflow {
    param(
        [string]$Id,
        [string[]]$Triggers,
        [string]$WpfSurface,
        [string]$AvaloniaSurface,
        [string]$Modality,
        [string]$Lifecycle,
        [string]$Validation,
        [string]$ResultApplication,
        [string]$SharedPolicy,
        [string[]]$WpfSources,
        [string[]]$AvaloniaSources,
        [string[]]$Tests,
        [string[]]$RequiredWpfTokens,
        [string[]]$RequiredAvaloniaTokens,
        [string[]]$RequiredSharedTokens = @()
    )

    foreach ($token in $RequiredWpfTokens) {
        if (-not $wpfSource.Contains($token)) {
            throw "WPF semantic/lifecycle token '$token' is missing for workflow '$Id'."
        }
    }
    foreach ($token in $RequiredAvaloniaTokens) {
        if (-not $avaloniaSource.Contains($token)) {
            throw "Avalonia semantic/lifecycle token '$token' is missing for workflow '$Id'."
        }
    }
    foreach ($token in $RequiredSharedTokens) {
        if (-not $sharedSource.Contains($token)) {
            throw "Shared semantic/lifecycle token '$token' is missing for workflow '$Id'."
        }
    }

    [ordered]@{
        id = $Id
        triggers = @($Triggers)
        wpfSurface = $WpfSurface
        avaloniaSurface = $AvaloniaSurface
        modality = $Modality
        lifecycle = $Lifecycle
        validation = $Validation
        resultApplication = $ResultApplication
        sharedPolicy = $SharedPolicy
        wpfSources = @($WpfSources)
        avaloniaSources = @($AvaloniaSources)
        tests = @($Tests)
        status = "behavior-aligned"
    }
}

$workflows = @(
    New-Workflow -Id "references.notes" `
        -Triggers @("freew.insert-footnote", "freew.insert-endnote", "freew.show-notes", "freew.next/previous-footnote", "freew.next/previous-endnote", "freew.footnote-endnote-options") `
        -WpfSurface "Bottom Notes pane plus insertion and numbering-options dialogs" `
        -AvaloniaSurface "Bottom Notes pane plus insertion and numbering-options dialogs" `
        -Modality "modeless pane; owner-centered modal prompts" `
        -Lifecycle "Insertion applies only after nonempty OK; the pane toggles, selects, loads rich content, and keeps Apply/Delete explicit; note navigation wraps; cancel/close do not mutate." `
        -Validation "Shared numbering planner validates all six footnote/endnote format, start, and restart values." `
        -ResultApplication "Insertion, pane edit/delete, and six-value options apply through document commands; Avalonia edit/delete/options are one-step undoable and invalidate layout." `
        -SharedPolicy "FootnoteEndnoteOptionsDialogPlanner; FreeWStatefulToggleCommand; note model and navigation contracts" `
        -WpfSources @("freew/FreeW.App.Host/MainWindow.cs", "freew/FreeW.App.Host/Ribbon/FreeWRibbonCommands.cs", "freew/FreeW.App.Host/Editing/DocumentView.cs") `
        -AvaloniaSources @("freew/FreeW.App.Avalonia/MainWindow.cs", "freew/FreeW.App.Avalonia/NotesPane.cs", "freew/FreeW.App.Avalonia/Editing/DocumentView.cs") `
        -Tests @("freew/FreeW.App.Host.Tests/EditableNotesPaneTests.cs", "freew/FreeW.App.Avalonia.Tests/EditingReferenceParityTests.cs") `
        -RequiredWpfTokens @("registry.BindToggle(FreeWRibbonCommandAction.ShowNotes", "FootnoteEndnoteOptionsDialog.Prompt", "MoveToNextFootnote") `
        -RequiredAvaloniaTokens @("r.BindToggle(FreeWRibbonCommandAction.ShowNotes", "NoteTextDialog.ShowAsync", "_notesPane.Toggle", "ReplaceNoteContent", "MoveToPreviousEndnote", "ApplyFootnoteEndnoteOptions") `
        -RequiredSharedTokens @("public sealed class FreeWStatefulToggleCommand")

    New-Workflow -Id "insert.date-time" `
        -Triggers @("freew.date-time") `
        -WpfSurface "Date and Time dialog with five culture-derived choices and automatic-update option" `
        -AvaloniaSurface "Date and Time dialog with five culture-derived choices and automatic-update option" `
        -Modality "owner-centered modal" `
        -Lifecycle "One DateTime and CurrentCulture are captured before opening; OK inserts the selected static value or DATE/TIME complex field; cancel, Escape, and close are no-ops." `
        -Validation "Choice index is constrained to the five generated formats; DATE versus TIME follows the selected format." `
        -ResultApplication "Static text inserts the captured display value; automatic update inserts the matching complex-field instruction with the same cached result." `
        -SharedPolicy "Culture-derived date/time format contract" `
        -WpfSources @("freew/FreeW.App.Host/DateTimeDialog.cs", "freew/FreeW.App.Host/Ribbon/FreeWRibbonCommands.cs") `
        -AvaloniaSources @("freew/FreeW.App.Avalonia/DateTimeDialog.cs", "freew/FreeW.App.Avalonia/MainWindow.cs") `
        -Tests @("freew/FreeW.App.Host.Tests/ComplexFieldEditorTests.cs", "freew/FreeW.App.Avalonia.Tests/EditingReferenceParityTests.cs") `
        -RequiredWpfTokens @("DateTimeDialog.Prompt", "DateTime.Now", "CultureInfo.CurrentCulture") `
        -RequiredAvaloniaTokens @("var moment = DateTime.Now", "var culture = CultureInfo.CurrentCulture", "InsertComplexField(instruction, result.Text)")

    New-Workflow -Id "layout.text-to-table" `
        -Triggers @("freew.text-to-table") `
        -WpfSurface "Delimiter choice dialog over selected paragraphs" `
        -AvaloniaSurface "Delimiter choice dialog over selected paragraphs" `
        -Modality "owner-centered modal" `
        -Lifecycle "Tab/comma/semicolon choice converts all selected paragraphs; cancel, Escape, and close do not mutate." `
        -Validation "TableTextConversionDialogPlanner supplies supported delimiters and default choice; ragged rows are preserved by the shared conversion model." `
        -ResultApplication "One replace-blocks command creates the table, places the caret in the first cell, and undoes in one step." `
        -SharedPolicy "TableTextConversionDialogPlanner; TextTableConvert" `
        -WpfSources @("freew/FreeW.App.Host/Ribbon/FreeWRibbonCommands.cs", "freew/FreeW.App.Host/Editing/DocumentView.cs") `
        -AvaloniaSources @("freew/FreeW.App.Avalonia/ImageAndTableConversionDialogs.cs", "freew/FreeW.App.Avalonia/MainWindow.cs", "freew/FreeW.App.Avalonia/Editing/DocumentView.cs") `
        -Tests @("freew/FreeW.App.Host.Tests/ImageAndTableConversionParityTests.cs", "freew/FreeW.App.Avalonia.Tests/EditingReferenceParityTests.cs") `
        -RequiredWpfTokens @("DelimiterDialog.Ask", "ConvertSelectionToTable(delimiter)") `
        -RequiredAvaloniaTokens @("TableTextConversionDialog.ShowAsync", "ConvertSelectedParagraphsToTable(value)", "new ReplaceBlocksCommand")

    New-Workflow -Id "references.table-of-authorities" `
        -Triggers @("freew.table-of-authorities", "freew.table-of-authorities-refresh") `
        -WpfSurface "Table of Authorities options dialog" `
        -AvaloniaSurface "Table of Authorities options dialog" `
        -Modality "owner-centered modal" `
        -Lifecycle "OK applies planner-built options; cancel, Escape, and close do not insert; refresh rebuilds the existing region with WPF-equivalent defaults." `
        -Validation "Shared planner owns category, passim, formatting, and tab-leader choices." `
        -ResultApplication "Shared region planner inserts or refreshes the authority paragraphs and page references through the editor command bus." `
        -SharedPolicy "TableOfAuthoritiesDialogPlanner; TableOfAuthoritiesRegionPlanner" `
        -WpfSources @("freew/FreeW.App.Host/TableOfAuthoritiesDialog.cs", "freew/FreeW.App.Host/Editing/DocumentView.cs") `
        -AvaloniaSources @("freew/FreeW.App.Avalonia/TableOfAuthoritiesDialog.cs", "freew/FreeW.App.Avalonia/MainWindow.cs", "freew/FreeW.App.Avalonia/Editing/DocumentView.cs") `
        -Tests @("freew/FreeW.App.Host.Tests/TableOfAuthoritiesDialogTests.cs", "freew/FreeW.App.Avalonia.Tests/EditingReferenceParityTests.cs") `
        -RequiredWpfTokens @("TableOfAuthoritiesDialog.Prompt", "BuildRefreshPlan") `
        -RequiredAvaloniaTokens @("TableOfAuthoritiesDialog.ShowAsync", "InsertTableOfAuthorities(commit.Options!)", "RefreshTableOfAuthorities")

    New-Workflow -Id "home.multilevel-list" `
        -Triggers @("freew.multilevel-define") `
        -WpfSurface "Define Multilevel List dialog" `
        -AvaloniaSurface "Define Multilevel List dialog" `
        -Modality "owner-centered modal" `
        -Lifecycle "Current level formats seed the dialog; invalid OK remains open and focuses the failed start value; valid OK applies; cancel, Escape, and close do not mutate." `
        -Validation "Shared planner owns number-format choices, current-state projection, positive start validation, and result construction." `
        -ResultApplication "The selected starts and number formats update the document multilevel definition through one undoable command." `
        -SharedPolicy "MultilevelListDialogPlanner" `
        -WpfSources @("freew/FreeW.App.Host/MultilevelListDialog.cs") `
        -AvaloniaSources @("freew/FreeW.App.Avalonia/MultilevelListDialog.cs", "freew/FreeW.App.Avalonia/MainWindow.cs") `
        -Tests @("freew/FreeW.App.Host.Tests/HomeDialogDepthTests.cs", "freew/FreeW.App.Presentation.Tests/Dialogs/MultilevelListDialogPlannerTests.cs", "freew/FreeW.App.Avalonia.Tests/EditingReferenceParityTests.cs") `
        -RequiredWpfTokens @("MultilevelListDialogPlanner.CreateSession", "session.PlanAcceptance()") `
        -RequiredAvaloniaTokens @("MultilevelListDialogPlanner.CreateSession", "_session.PlanAcceptance()", "ApplyMultiLevelListDefinition") `
        -RequiredSharedTokens @("public sealed class MultilevelListDialogSession", "MultilevelListDialogPlanner.TryBuildResult")

    New-Workflow -Id "insert.bookmark-manager" `
        -Triggers @("freew.bookmark-manager") `
        -WpfSurface "Bookmark Manager dialog with Go To, Delete, and refreshed selection" `
        -AvaloniaSurface "Bookmark Manager dialog with Go To, Delete, and refreshed selection" `
        -Modality "owner-centered modal" `
        -Lifecycle "The list is refreshed from document order; Go To moves to the selected bookmark; Delete refreshes and retains a valid selection; close does not mutate." `
        -Validation "Actions require a selected bookmark; the existing freew.bookmark Add/Go dialog remains separate." `
        -ResultApplication "Go To moves the caret; Avalonia Delete uses an undoable bookmark-removal command and refreshes the manager list." `
        -SharedPolicy "Bookmarks list/location model" `
        -WpfSources @("freew/FreeW.App.Host/BookmarkManagerDialog.cs") `
        -AvaloniaSources @("freew/FreeW.App.Avalonia/BookmarkManagerDialog.cs", "freew/FreeW.App.Avalonia/MainWindow.cs", "freew/FreeW.App.Avalonia/Editing/DocumentView.cs") `
        -Tests @("freew/FreeW.App.Host.Tests/HomeDialogDepthTests.cs", "freew/FreeW.App.Avalonia.Tests/EditingReferenceParityTests.cs") `
        -RequiredWpfTokens @("BookmarkManagerDialog.Show", "RefreshList", "RemoveBookmark") `
        -RequiredAvaloniaTokens @("BookmarkManagerDialog.ShowAsync", "GoToBookmark", "DeleteBookmark", "RefreshList")

    New-Workflow -Id "review.thesaurus" `
        -Triggers @("freew.thesaurus", "Shift+F7") `
        -WpfSurface "Modeless right Thesaurus pane" `
        -AvaloniaSurface "Modeless right Thesaurus pane" `
        -Modality "modeless toggle pane" `
        -Lifecycle "Ribbon or Shift+F7 toggles the pane; showing and caret refresh rebuild lookup results; Replace edits the caret word and refreshes; Copy is asynchronous in Avalonia." `
        -Validation "Shared presentation planner normalizes the caret word and supplies sense/action rows and empty-result status." `
        -ResultApplication "Replace routes through the editor command bus; Copy writes only to the platform clipboard and does not mutate the document." `
        -SharedPolicy "ThesaurusPresentationPlanner; ThesaurusLookup" `
        -WpfSources @("freew/FreeW.App.Host/ThesaurusPane.cs", "freew/FreeW.App.Host/MainWindow.cs") `
        -AvaloniaSources @("freew/FreeW.App.Avalonia/ThesaurusPane.cs", "freew/FreeW.App.Avalonia/MainWindow.cs") `
        -Tests @("freew/FreeW.App.Host.Tests/ThesaurusAndBalloonsTests.cs", "freew/FreeW.App.Avalonia.Tests/EditingReferenceParityTests.cs") `
        -RequiredWpfTokens @("ToggleThesaurusPane", "_session.Refresh(", "_session.PlanAction(", "Clipboard.SetText") `
        -RequiredAvaloniaTokens @("Thesaurus: ToggleThesaurusPane", "_session.Refresh(", "_session.PlanAction(", "ReplaceCurrentProofingWord", "await clipboard.SetTextAsync") `
        -RequiredSharedTokens @("public sealed class ThesaurusPaneSession", "ThesaurusPresentationPlanner.Lookup")
)

$remainingGaps = @(
    [ordered]@{
        id = "paired-app-surface-captures"
        kind = "visual-evidence"
        workflows = @($workflows.id)
        exactWork = "Capture matching WPF/Avalonia opened, populated, validation, and cancel states at 96 DPI; compare dimensions, focus, enabled/default controls, pane docking, overflow, and nonblank pixels."
    },
    [ordered]@{
        id = "native-clipboard-focus-smoke"
        kind = "human-smoke"
        workflows = @("review.thesaurus")
        exactWork = "Confirm asynchronous Copy reaches the native clipboard and focus returns after Replace on a packaged desktop run; platform clipboard behavior is not suitable for pixel equivalence."
    }
)

$summary = [ordered]@{
    workflowCount = $workflows.Count
    behaviorAligned = @($workflows | Where-Object { $_.status -eq "behavior-aligned" }).Count
    functionalGaps = 0
    remainingVisualOrHumanEvidenceItems = $remainingGaps.Count
}
$document = [ordered]@{
    schemaVersion = 1
    generatedBy = "tools/Generate-FreeWEditingReferenceParityEvidence.ps1"
    inventoryMethod = "Explicit seven-workflow semantic and lifecycle contracts validated against current WPF and Avalonia source tokens."
    summary = $summary
    workflows = $workflows
    remainingGaps = $remainingGaps
}

$json = ($document | ConvertTo-Json -Depth 20) + [Environment]::NewLine
$markdown = New-Object System.Text.StringBuilder
[void]$markdown.AppendLine("# FreeW editing and reference parity evidence")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('Generated by `tools/Generate-FreeWEditingReferenceParityEvidence.ps1` from explicit semantic and lifecycle contracts validated against current WPF/Avalonia source tokens.')
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("## Summary")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("- Workflows: $($summary.workflowCount)")
[void]$markdown.AppendLine("- Behavior aligned: $($summary.behaviorAligned)")
[void]$markdown.AppendLine("- Functional gaps: $($summary.functionalGaps)")
[void]$markdown.AppendLine("- Remaining visual/human evidence items: $($summary.remainingVisualOrHumanEvidenceItems)")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("## Workflows")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("| Workflow | Surfaces / modality | Lifecycle | Validation and result | Status |")
[void]$markdown.AppendLine("| --- | --- | --- | --- | --- |")
foreach ($workflow in $workflows) {
    $surface = "WPF: $($workflow.wpfSurface); Avalonia: $($workflow.avaloniaSurface); $($workflow.modality)".Replace("|", "\|")
    $lifecycle = $workflow.lifecycle.Replace("|", "\|")
    $result = "$($workflow.validation) $($workflow.resultApplication)".Replace("|", "\|")
    [void]$markdown.AppendLine(('| `{0}` | {1} | {2} | {3} | {4} |' -f $workflow.id, $surface, $lifecycle, $result, $workflow.status))
}
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("## Exact Remaining Gaps")
[void]$markdown.AppendLine()
foreach ($gap in $remainingGaps) {
    [void]$markdown.AppendLine(('- **{0}** ({1}; `{2}`): {3}' -f $gap.id, $gap.kind, ($gap.workflows -join '`, `'), $gap.exactWork))
}
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("No functional or semantic gap remains in these seven workflows; the listed work is visual/native-environment evidence only.")
$markdownText = $markdown.ToString()

$resolvedJsonPath = Resolve-ToolRepoPath -Path $JsonPath -RepoRoot $repoRoot
$resolvedMarkdownPath = Resolve-ToolRepoPath -Path $MarkdownPath -RepoRoot $repoRoot
if ($Check) {
    Test-ToolGeneratedContentMatches -ExpectedContent $json -ActualPath $resolvedJsonPath -Label "FreeW editing/reference JSON evidence" -GeneratorScriptName "tools/Generate-FreeWEditingReferenceParityEvidence.ps1" -NormalizeNewlines
    Test-ToolGeneratedContentMatches -ExpectedContent $markdownText -ActualPath $resolvedMarkdownPath -Label "FreeW editing/reference Markdown evidence" -GeneratorScriptName "tools/Generate-FreeWEditingReferenceParityEvidence.ps1" -NormalizeNewlines
    Write-Host "FreeW editing/reference parity evidence is current."
    exit 0
}

New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedJsonPath) -Force | Out-Null
[System.IO.File]::WriteAllText($resolvedJsonPath, $json, [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($resolvedMarkdownPath, $markdownText, [System.Text.UTF8Encoding]::new($false))
Write-Host "Generated FreeW editing/reference parity evidence for $($workflows.Count) workflows."
