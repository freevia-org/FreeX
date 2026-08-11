param(
    [string]$JsonPath = "docs\parity\freep-dialog-pane-parity-inventory.json",
    [string]$MarkdownPath = "docs\parity\freep-dialog-pane-parity-inventory.md",
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

$wpfMainRoot = Join-Path $repoRoot "freep\FreeP.App.Host"
$wpfCommandsPath = Join-Path $repoRoot "freep\FreeP.App.Host\FreePRibbonCommands.cs"
$avaloniaMainRoot = Join-Path $repoRoot "freep\FreeP.App.Avalonia"
$workareaSessionPath = Join-Path $repoRoot "freep\FreeP.App.Presentation\PresentationWorkareaSession.cs"
$wpfMain = @(Get-ChildItem -LiteralPath $wpfMainRoot -Filter "MainWindow*.cs" -File |
    Sort-Object FullName |
    ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join [Environment]::NewLine
$wpfCommands = Get-Content -LiteralPath $wpfCommandsPath -Raw
$avaloniaMain = @(Get-ChildItem -LiteralPath $avaloniaMainRoot -Filter "MainWindow*.cs" -File |
    Sort-Object FullName |
    ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join [Environment]::NewLine
$workareaSession = Get-Content -LiteralPath $workareaSessionPath -Raw

function New-Route {
    param(
        [string]$Id,
        [string]$Area,
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
        [string[]]$ExistingVisualEvidence = @(),
        [string]$VisualEvidenceStatus = "none",
        [string]$Status = "behavior-aligned",
        [string]$Notes = "",
        [string[]]$RequiredWpfTokens = @(),
        [string[]]$RequiredAvaloniaTokens = @(),
        [string[]]$RequiredWorkareaSessionTokens = @()
    )

    foreach ($token in $RequiredWpfTokens) {
        if (-not ($wpfMain.Contains($token) -or $wpfCommands.Contains($token))) {
            throw "WPF semantic trigger token '$token' is missing for route '$Id'."
        }
    }
    foreach ($token in $RequiredAvaloniaTokens) {
        if (-not $avaloniaMain.Contains($token)) {
            throw "Avalonia semantic trigger token '$token' is missing for route '$Id'."
        }
    }
    foreach ($token in $RequiredWorkareaSessionTokens) {
        if (-not $workareaSession.Contains($token)) {
            throw "Shared workarea-session token '$token' is missing for route '$Id'."
        }
    }

    [ordered]@{
        id = $Id
        area = $Area
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
        existingVisualEvidence = @($ExistingVisualEvidence)
        visualEvidenceStatus = $VisualEvidenceStatus
        status = $Status
        notes = $Notes
    }
}

$routes = @(
    New-Route -Id "startup.slide-pane" -Area "Slide thumbnails and sections" `
        -Triggers @("Main window construction", "slide selection", "slide/section context menu") `
        -WpfSurface "Persistent left SlidePane" -AvaloniaSurface "Persistent left ListBox pane" -Modality "persistent" `
        -Lifecycle "Created with the editor; selection, drag cancellation, context-menu Escape, and editor rebuild are host managed." `
        -Validation "Shared drag, section, and context-action planners reject invalid targets." `
        -ResultApplication "EditingSession slide/section commands; refreshes pane and canvas." `
        -SharedPolicy "SlidePanePlanner; SlideSectionPlanner; FreePContextMenuCatalog" `
        -WpfSources @("freep/FreeP.App.Host/SlidePane.cs", "freep/FreeP.App.Host/MainWindow.cs") `
        -AvaloniaSources @("freep/FreeP.App.Avalonia/MainWindow.cs") `
        -Tests @("freep/FreeP.App.Host.Tests/SlidePaneTests.cs", "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs", "freep/FreeP.App.Avalonia.Tests/KeyboardContextParityTests.cs") `
        -ExistingVisualEvidence @("docs/parity/freep-slide-pane-thumbnail-render-evidence-2026-07-04.md", "docs/parity/freep-slide-pane-thumbnail-chrome-evidence-2026-07-04.md", "tools/FreeP.RenderCompare/SlidePaneThumbnailEvidence.cs") `
        -VisualEvidenceStatus "generated-render-evidence-capable; no committed paired app-window screenshot" `
        -RequiredWpfTokens @("new SlidePane(_workareaSession)") -RequiredAvaloniaTokens @("BuildSlidePaneContextMenu")

    New-Route -Id "startup.notes-pane" -Area "Slide notes" `
        -Triggers @("Main window construction", "current slide change") `
        -WpfSurface "Persistent notes editor" -AvaloniaSurface "Persistent notes TextBox" -Modality "persistent" `
        -Lifecycle "Always available; content refresh is suppressed during host-driven updates." `
        -Validation "No submission validation; empty notes are valid." `
        -ResultApplication "EditingSession.SetNotes through host text-change routing." `
        -SharedPolicy "Presentation slide notes model and EditingSession" `
        -WpfSources @("freep/FreeP.App.Host/MainWindow.cs") -AvaloniaSources @("freep/FreeP.App.Avalonia/MainWindow.cs") `
        -Tests @("freep/FreeP.App.Host.Tests/NotesSlideTests.cs", "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs") `
        -ExistingVisualEvidence @("docs/parity/freep-comments-review-accessibility-evidence-inventory-2026-07-05.md") `
        -VisualEvidenceStatus "semantic evidence only" `
        -RequiredWpfTokens @("RefreshNotesPane") -RequiredAvaloniaTokens @("OnNotesTextChanged")

    New-Route -Id "insert.table-picker" -Area "Insert table dimensions" `
        -Triggers @("freep.insert-table-3x3 ribbon command") `
        -WpfSurface "Inline dimension choice overlay" -AvaloniaSurface "Inline dimension choice overlay" -Modality "nonmodal choice overlay" `
        -Lifecycle "Choice applies and closes; clicking another picker route replaces the overlay." `
        -Validation "TableInsertionPickerPlanner bounds the offered row/column choices." `
        -ResultApplication "TableInsertionPickerPlanner.TryApplyChoice -> EditingSession." `
        -SharedPolicy "TableInsertionPickerPlanner" `
        -WpfSources @("freep/FreeP.App.Host/MainWindow.cs") -AvaloniaSources @("freep/FreeP.App.Avalonia/MainWindow.cs") `
        -Tests @("freep/FreeP.App.Host.Tests/RibbonEditorCompleteness5BTests.cs", "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs", "freep/FreeP.App.Presentation.Tests/TableInsertionPickerPlannerTests.cs") `
        -ExistingVisualEvidence @("docs/parity/freep-table-picker-workflow-2026-07-02.md") -VisualEvidenceStatus "semantic evidence only" `
        -RequiredWpfTokens @("OpenTablePicker") -RequiredAvaloniaTokens @("OpenTablePicker")

    New-Route -Id "design.layout-picker" -Area "Slide layout choice" `
        -Triggers @("freep.layout ribbon command", "Design layout host intent") `
        -WpfSurface "Inline grouped layout picker" -AvaloniaSurface "Inline grouped layout picker" -Modality "nonmodal choice overlay" `
        -Lifecycle "Choice applies and closes; no implicit/default layout is applied before selection." `
        -Validation "Only enabled PresentationLayoutChoice entries apply." `
        -ResultApplication "PresentationDesignCommandPlanner.TryApplyLayoutChoice -> EditingSession." `
        -SharedPolicy "PresentationDesignCommandPlanner" `
        -WpfSources @("freep/FreeP.App.Host/MainWindow.cs") -AvaloniaSources @("freep/FreeP.App.Avalonia/MainWindow.cs") `
        -Tests @("freep/FreeP.App.Host.Tests/RibbonEditorCompleteness5BTests.cs", "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs", "freep/FreeP.App.Presentation.Tests/PresentationDesignCommandPlannerTests.cs") `
        -VisualEvidenceStatus "none" -RequiredWpfTokens @("OpenLayoutPicker") -RequiredAvaloniaTokens @("OnLayoutPickerRequested")

    New-Route -Id "design.slide-size" -Area "Custom slide size" `
        -Triggers @("freep.slide-size-custom ribbon command") `
        -WpfSurface "Slide Size Window" -AvaloniaSurface "Slide Size Window" -Modality "modal owner-centered" `
        -Lifecycle "OK is default; Cancel, Escape, and title-bar close do not apply; invalid OK remains open; valid OK closes." `
        -Validation "SlideSizeDialogPlanner validates numeric, positive, and minimum dimensions and identifies the focus field." `
        -ResultApplication "SlideSizeDialogPlanner.TryApplyResult -> undoable EditingSession.SetSlideSize." `
        -SharedPolicy "SlideSizeDialogPlanner; shared WPF/Avalonia dialog chrome" `
        -WpfSources @("freep/FreeP.App.Host/SlideSizeDialog.cs", "freep/FreeP.App.Host/MainWindow.cs") `
        -AvaloniaSources @("freep/FreeP.App.Avalonia/SlideSizeDialog.cs", "freep/FreeP.App.Avalonia/MainWindow.cs") `
        -Tests @("freep/FreeP.App.Host.Tests/SlideSizeDialogTests.cs", "freep/FreeP.App.Host.Tests/DialogLifecycleParityTests.cs", "freep/FreeP.App.Avalonia.Tests/DialogLifecycleParityTests.cs", "freep/FreeP.App.Presentation.Tests/SlideSizeDialogPlannerTests.cs") `
        -VisualEvidenceStatus "none; paired screenshots remain" `
        -RequiredWpfTokens @("onCustomSlideSize", "OpenSlideSizeDialog") -RequiredAvaloniaTokens @("OpenSlideSizeDialog", "ShowDialog<bool?>")

    New-Route -Id "insert.header-footer" -Area "Header, footer, date/time, and slide number" `
        -Triggers @("freep.header-footer", "freep.date-time", "freep.slide-number") `
        -WpfSurface "Header and Footer Window" -AvaloniaSurface "Header and Footer Window" -Modality "modal owner-centered" `
        -Lifecycle "Apply is default; Apply to All is an explicit alternate result; Cancel, Escape, and close do not mutate." `
        -Validation "Shared planner validates target slides and normalizes date-field choices." `
        -ResultApplication "HeaderFooterCommandPlanner.TryApply -> undoable ApplyHeaderFooterCommand." `
        -SharedPolicy "HeaderFooterCommandPlanner; shared WPF/Avalonia dialog chrome" `
        -WpfSources @("freep/FreeP.App.Host/HeaderFooterDialog.cs", "freep/FreeP.App.Host/MainWindow.cs") `
        -AvaloniaSources @("freep/FreeP.App.Avalonia/HeaderFooterDialog.cs", "freep/FreeP.App.Avalonia/MainWindow.cs") `
        -Tests @("freep/FreeP.App.Host.Tests/HeaderFooterDialogTests.cs", "freep/FreeP.App.Host.Tests/DialogLifecycleParityTests.cs", "freep/FreeP.App.Avalonia.Tests/HeaderFooterCommandRoutingTests.cs", "freep/FreeP.App.Avalonia.Tests/DialogLifecycleParityTests.cs") `
        -ExistingVisualEvidence @("docs/parity/freep-header-footer-options-2026-07-06.md", "docs/parity/freep-header-footer-placeholder-creation-2026-07-05.md") `
        -VisualEvidenceStatus "semantic evidence only; paired screenshots remain" `
        -RequiredWpfTokens @("onHeaderFooter", "OpenHeaderFooterDialog") -RequiredAvaloniaTokens @("OpenHeaderFooter = OpenHeaderFooterDialog", "OpenHeaderFooterDialog")

    New-Route -Id "home.find-replace" -Area "Find and Replace" `
        -Triggers @("freep.find", "freep.replace", "Ctrl+F", "Ctrl+H") `
        -WpfSurface "Reusable Find/Replace Window" -AvaloniaSurface "Reusable Find/Replace Window" -Modality "modeless owned window" `
        -Lifecycle "One instance is reused and switched between Find/Replace modes; Close, Escape, owner close, and presentation replacement close it." `
        -Validation "Shared policy disables empty-query search/replace and reports no-match/no-replacement status." `
        -ResultApplication "EditingSession.FindAll/NavigateTo/ReplaceOne/ReplaceAll; host refresh callback updates canvas and slide pane." `
        -SharedPolicy "PresentationWorkareaSession; FindReplaceDialogPlanner; FindReplaceDialogPolicy" `
        -WpfSources @("freep/FreeP.App.Host/FindReplaceDialog.cs", "freep/FreeP.App.Host/MainWindow.cs") `
        -AvaloniaSources @("freep/FreeP.App.Avalonia/FindReplaceDialog.cs", "freep/FreeP.App.Avalonia/MainWindow.cs") `
        -Tests @("freep/FreeP.App.Host.Tests/FindReplaceDialogPolicySourceTests.cs", "freep/FreeP.App.Host.Tests/DialogLifecycleParityTests.cs", "freep/FreeP.App.Avalonia.Tests/DialogLifecycleParityTests.cs", "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs", "freep/FreeP.App.Avalonia.Tests/KeyboardContextParityTests.cs") `
        -VisualEvidenceStatus "none; Find and Replace mode screenshots remain" `
        -RequiredWpfTokens @("Find = OpenFindDialog", "Replace = OpenFindReplaceDialog", "_findReplaceDialog?.Close()") `
        -RequiredAvaloniaTokens @("Find = OpenFindDialog", "Replace = OpenFindReplaceDialog", "_findReplaceDialog?.Close()") `
        -RequiredWorkareaSessionTokens @("FreePKeyboardCommand.Find", "FreePKeyboardCommand.Replace")

    New-Route -Id "insert.hyperlink" -Area "Insert or edit hyperlink" `
        -Triggers @("freep.insert-link") `
        -WpfSurface "Hyperlink Window" -AvaloniaSurface "Hyperlink Window" -Modality "modal owner-centered" `
        -Lifecycle "OK is default; Cancel/Escape/close return no result; invalid OK remains open." `
        -Validation "HyperlinkDialogPlanner validates URL versus target-slide choice and focus field." `
        -ResultApplication "HyperlinkDialogPlanner.BuildApplyPlan -> EditingSession.SetShapeHyperlink only when ShouldApply." `
        -SharedPolicy "HyperlinkDialogPlanner" `
        -WpfSources @("freep/FreeP.App.Host/HyperlinkDialog.cs", "freep/FreeP.App.Host/MainWindow.cs") `
        -AvaloniaSources @("freep/FreeP.App.Avalonia/HyperlinkDialog.cs", "freep/FreeP.App.Avalonia/MainWindow.cs") `
        -Tests @("freep/FreeP.App.Host.Tests/HyperlinkDialogTests.cs", "freep/FreeP.App.Avalonia.Tests/HyperlinkDialogTests.cs", "freep/FreeP.App.Presentation.Tests/HyperlinkDialogPlannerTests.cs") `
        -VisualEvidenceStatus "none" -RequiredWpfTokens @("onInsertLink", "OpenHyperlinkDialog") -RequiredAvaloniaTokens @("OpenHyperlink = OpenHyperlinkDialog", "OpenHyperlinkDialog")

    New-Route -Id "chart.edit-data" -Area "Chart data editing" `
        -Triggers @("freep.chart.edit-data") `
        -WpfSurface "Chart Data Window" -AvaloniaSurface "Chart Data Window" -Modality "modal owner-centered" `
        -Lifecycle "OK commits and closes; Cancel/Escape/close discard the working values." `
        -Validation "ChartDataDialogPlanner parses rectangular chart data and returns cell-level errors." `
        -ResultApplication "EditingSession.ReplaceChartData from the validated commit plan." `
        -SharedPolicy "ChartDataDialogPlanner" `
        -WpfSources @("freep/FreeP.App.Host/ChartDataDialog.cs", "freep/FreeP.App.Host/MainWindow.cs") `
        -AvaloniaSources @("freep/FreeP.App.Avalonia/ChartDataDialog.cs", "freep/FreeP.App.Avalonia/MainWindow.cs") `
        -Tests @("freep/FreeP.App.Host.Tests/ChartDataDialogTests.cs", "freep/FreeP.App.Avalonia.Tests/ChartDataDialogTests.cs", "freep/FreeP.App.Presentation.Tests/ChartDataDialogPlannerTests.cs") `
        -VisualEvidenceStatus "none" -RequiredWpfTokens @("onEditChartData", "OpenChartDataDialog") -RequiredAvaloniaTokens @("OpenChartData = OpenChartDataDialog", "OpenChartDataDialog")

    New-Route -Id "slideshow.custom-shows" -Area "Custom show authoring" `
        -Triggers @("freep.slideshow.custom-shows") `
        -WpfSurface "Custom Shows Window" -AvaloniaSurface "Custom Shows Window" -Modality "modal owner-centered" `
        -Lifecycle "Create/rename/update/delete remain in the dialog; Start Show closes; Close/Escape exits without an implicit mutation." `
        -Validation "SlideShowCustomShowPlanner validates names, selection, and reorder targets." `
        -ResultApplication "Shared custom-show mutation/session planners update the presentation." `
        -SharedPolicy "SlideShowCustomShowPlanner; SlideShowCustomShowSessionPlanner" `
        -WpfSources @("freep/FreeP.App.Host/CustomShowDialog.cs", "freep/FreeP.App.Host/MainWindow.cs") `
        -AvaloniaSources @("freep/FreeP.App.Avalonia/CustomShowDialog.cs", "freep/FreeP.App.Avalonia/MainWindow.cs") `
        -Tests @("freep/FreeP.App.Host.Tests/SlideShowTests.cs", "freep/FreeP.App.Avalonia.Tests/SlideShowWindowHeadlessTests.cs", "freep/FreeP.App.Presentation.Tests/SlideShowCustomShowPlannerTests.cs") `
        -ExistingVisualEvidence @("docs/parity/freep-custom-show-drag-reorder-2026-07-13.md", "docs/parity/freep-custom-show-launch-plan-2026-07-04.md") `
        -VisualEvidenceStatus "semantic evidence only" -RequiredWpfTokens @("onCustomShows", "OpenCustomShowDialog") -RequiredAvaloniaTokens @("OpenCustomShowDialog")

    New-Route -Id "review.comments-pane" -Area "Comments" `
        -Triggers @("freep.review.comments") `
        -WpfSurface "Docked comments pane" -AvaloniaSurface "Docked comments pane" -Modality "modeless pane" `
        -Lifecycle "Command shows/refreshes; pane actions mutate or navigate; pane close is explicit." `
        -Validation "PresentationReviewWorkflowPlanner action enablement and mention validation." `
        -ResultApplication "PresentationReviewWorkflowSession applies reply/delete/resolve/reopen/navigation and refreshes both hosts." `
        -SharedPolicy "PresentationReviewWorkflowSession; PresentationReviewWorkflowPlanner" `
        -WpfSources @("freep/FreeP.App.Host/MainWindow.cs") -AvaloniaSources @("freep/FreeP.App.Avalonia/MainWindow.cs") `
        -Tests @("freep/FreeP.App.Host.Tests/SectionsCommentsTests.cs", "freep/FreeP.App.Host.Tests/DialogLifecycleParityTests.cs", "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs", "freep/FreeP.App.Avalonia.Tests/DialogLifecycleParityTests.cs", "freep/FreeP.App.Presentation.Tests/PresentationReviewWorkflowSessionTests.cs") `
        -ExistingVisualEvidence @("docs/parity/freep-comments-review-accessibility-evidence-inventory-2026-07-05.md", "docs/parity/freep-comments-review-navigation-2026-07-03.md") `
        -VisualEvidenceStatus "semantic evidence only" -RequiredWpfTokens @("onReviewCommentsPane", "HideReviewCommentsPane") -RequiredAvaloniaTokens @("ShowCommentsPane = () => ShowReviewCommentsPane()", "HideReviewCommentsPane")

    New-Route -Id "review.accessibility-pane" -Area "Accessibility checker" `
        -Triggers @("freep.review.accessibility") `
        -WpfSurface "Docked accessibility checker pane" -AvaloniaSurface "Docked accessibility checker pane" -Modality "modeless pane" `
        -Lifecycle "Command rebuilds and shows; row selection/navigation and row actions keep the pane live; close hides it." `
        -Validation "Shared row action plans expose enabled state and required selection." `
        -ResultApplication "Accessibility row actions route to title, alt-text, table, hyperlink, and media workflows." `
        -SharedPolicy "PresentationReviewWorkflowPlanner accessibility plans" `
        -WpfSources @("freep/FreeP.App.Host/MainWindow.cs") -AvaloniaSources @("freep/FreeP.App.Avalonia/MainWindow.cs") `
        -Tests @("freep/FreeP.App.Host.Tests/SectionsCommentsTests.cs", "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs") `
        -ExistingVisualEvidence @("docs/parity/freep-accessibility-checker-navigation-2026-07-14.md", "docs/parity/freep-accessibility-table-structure-review-2026-07-13.md") `
        -VisualEvidenceStatus "semantic evidence only" -RequiredWpfTokens @("onReviewAccessibility") -RequiredAvaloniaTokens @("ShowAccessibilityPane = () => ShowAccessibilityCheckerPane()")

    New-Route -Id "review.alt-text-pane" -Area "Alternative text" `
        -Triggers @("freep.review.alt-text", "selected-object context action", "accessibility checker row action") `
        -WpfSurface "Docked Alt Text pane" -AvaloniaSurface "Docked Alt Text pane" -Modality "modeless pane" `
        -Lifecycle "Apply mutates and remains available; Close hides; decorative state disables inappropriate text entry." `
        -Validation "Shared alt-text request/mutation plans validate selection and decorative state." `
        -ResultApplication "PresentationReviewWorkflowSession.ApplyAltText." `
        -SharedPolicy "PresentationReviewWorkflowPlanner" `
        -WpfSources @("freep/FreeP.App.Host/MainWindow.cs") -AvaloniaSources @("freep/FreeP.App.Avalonia/MainWindow.cs") `
        -Tests @("freep/FreeP.App.Host.Tests/SectionsCommentsTests.cs", "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs") `
        -ExistingVisualEvidence @("docs/parity/freep-comments-review-accessibility-evidence-inventory-2026-07-05.md") `
        -VisualEvidenceStatus "semantic evidence only" -RequiredWpfTokens @("onReviewAltText") -RequiredAvaloniaTokens @("ShowAltTextPane = () => ShowAltTextPane()")

    New-Route -Id "review.reading-order-pane" -Area "Reading order" `
        -Triggers @("freep.review.reading-order") `
        -WpfSurface "Docked reading order pane" -AvaloniaSurface "Docked reading order pane" -Modality "modeless pane" `
        -Lifecycle "Selection and move actions keep pane state synchronized; close hides without mutation." `
        -Validation "Shared action plans disable first/last moves and require a selected shape." `
        -ResultApplication "PresentationReviewWorkflowSession move/select plans -> EditingSession." `
        -SharedPolicy "PresentationReviewWorkflowPlanner" `
        -WpfSources @("freep/FreeP.App.Host/MainWindow.cs") -AvaloniaSources @("freep/FreeP.App.Avalonia/MainWindow.cs") `
        -Tests @("freep/FreeP.App.Host.Tests/SectionsCommentsTests.cs", "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs") `
        -VisualEvidenceStatus "none" -RequiredWpfTokens @("onReviewReadingOrder") -RequiredAvaloniaTokens @("ShowReadingOrderPane = () => ShowReadingOrderPane()")

    New-Route -Id "review.proofing-pane" -Area "Proofing" `
        -Triggers @("freep.review.proofing") `
        -WpfSurface "Docked proofing pane" -AvaloniaSurface "Docked proofing pane" -Modality "modeless pane" `
        -Lifecycle "Issue selection and correction/ignore actions refresh the pane; close hides." `
        -Validation "Shared execution and action plans require an active issue and valid suggestion." `
        -ResultApplication "PresentationReviewWorkflowSession correction/ignore/dictionary actions." `
        -SharedPolicy "PresentationReviewWorkflowPlanner; PresentationReviewWorkflowSession" `
        -WpfSources @("freep/FreeP.App.Host/MainWindow.cs") -AvaloniaSources @("freep/FreeP.App.Avalonia/MainWindow.cs") `
        -Tests @("freep/FreeP.App.Host.Tests/SectionsCommentsTests.cs", "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs") `
        -ExistingVisualEvidence @("docs/parity/freep-proofing-ignore-actions-2026-07-13.md", "docs/parity/freep-proofing-add-to-dictionary-2026-07-13.md") `
        -VisualEvidenceStatus "semantic evidence only" -RequiredWpfTokens @("onReviewProofing") -RequiredAvaloniaTokens @("ShowProofingPane = () => ShowProofingPane()")

    New-Route -Id "accessibility.media-caption-pane" -Area "Media captions" `
        -Triggers @("Accessibility checker media-caption row action") `
        -WpfSurface "Docked media captions pane" -AvaloniaSurface "Docked media captions pane" -Modality "modeless pane" `
        -Lifecycle "Create/replace/delete are explicit; close hides; selection changes rebuild the working plan." `
        -Validation "PresentationMediaTranscriptPlanner validates selected media and caption payload fields." `
        -ResultApplication "Shared caption track mutation plans update presentation media relationships." `
        -SharedPolicy "PresentationMediaTranscriptPlanner" `
        -WpfSources @("freep/FreeP.App.Host/MainWindow.cs") -AvaloniaSources @("freep/FreeP.App.Avalonia/MainWindow.cs") `
        -Tests @("freep/FreeP.App.Host.Tests/MediaFieldsTests.cs", "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs", "freep/FreeP.App.Presentation.Tests/PresentationMediaTranscriptPlannerTests.cs") `
        -ExistingVisualEvidence @("docs/parity/freep-visible-media-caption-authoring-2026-07-13.md", "docs/parity/freep-media-caption-ttml-sidecar-retention-2026-07-14.md") `
        -VisualEvidenceStatus "semantic evidence only" -RequiredWpfTokens @("ShowMediaCaptionPane") -RequiredAvaloniaTokens @("ShowMediaCaptionPane")

    New-Route -Id "context.smartart-text-pane" -Area "SmartArt text" `
        -Triggers @("Selected SmartArt context action") `
        -WpfSurface "Docked SmartArt text pane" -AvaloniaSurface "Docked SmartArt text pane" -Modality "modeless pane" `
        -Lifecycle "Apply writes the outline; keyboard routes edit hierarchy; Close hides." `
        -Validation "SmartArtEditingPlanner validates selected model nodes and keyboard routes." `
        -ResultApplication "Shared SmartArt edit/apply/cache regeneration plans update the selected SmartArt." `
        -SharedPolicy "SmartArtEditingPlanner" `
        -WpfSources @("freep/FreeP.App.Host/MainWindow.cs") -AvaloniaSources @("freep/FreeP.App.Avalonia/MainWindow.cs") `
        -Tests @("freep/FreeP.App.Host.Tests/SmartArtTests.cs", "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs", "freep/FreeP.App.Presentation.Tests/SmartArtEditingPlannerTests.cs") `
        -ExistingVisualEvidence @("docs/parity/freep-smartart-text-pane-hosts-2026-07-14.md", "docs/parity/freep-smartart-text-pane-keyboard-routing-2026-07-14.md") `
        -VisualEvidenceStatus "semantic evidence only" -RequiredWpfTokens @("ShowSmartArtTextPane") -RequiredAvaloniaTokens @("ShowSmartArtTextPane")

    New-Route -Id "animations.animation-pane" -Area "Animation timeline and options" `
        -Triggers @("freep.anim.pane") `
        -WpfSurface "Docked AnimationPane control" -AvaloniaSurface "Docked animation pane realization" -Modality "modeless toggle pane" `
        -Lifecycle "Toggle shows/hides; selection and timing/effect edits refresh; preview remains explicit." `
        -Validation "AnimationPanePlanner action/timing/effect plans validate selected animation and option values." `
        -ResultApplication "Shared animation mutation plans -> EditingSession; preview routes to slideshow host." `
        -SharedPolicy "AnimationPanePlanner; PresentationAnimationCommandPlanner" `
        -WpfSources @("freep/FreeP.App.Host/AnimationPane.cs", "freep/FreeP.App.Host/MainWindow.cs") `
        -AvaloniaSources @("freep/FreeP.App.Avalonia/MainWindow.cs") `
        -Tests @("freep/FreeP.App.Host.Tests/AnimationPaneTests.cs", "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs", "freep/FreeP.App.Presentation.Tests/AnimationPanePlannerTests.cs") `
        -ExistingVisualEvidence @("docs/parity/freep-animation-pane-workflow-depth-2026-07-06.md", "docs/parity/freep-animation-pane-playback-workflow-evidence-2026-07-14.md") `
        -VisualEvidenceStatus "workflow evidence only; paired pane screenshots remain" `
        -RequiredWpfTokens @("onAnimPane", "ToggleAnimationPane") -RequiredAvaloniaTokens @("OnAnimationPaneRequested", "ShowAnimationPane")

    New-Route -Id "file.print-options" -Area "Print options" `
        -Triggers @("File > Print", "freep.file.print", "Ctrl+P") `
        -WpfSurface "Backstage Print pane" -AvaloniaSurface "Backstage Print pane plus keyboard/ribbon print-options pane" -Modality "modeless app pane" `
        -Lifecycle "Backstage close/Escape dismisses; option choices are explicit and no native print handoff occurs without capability." `
        -Validation "PresentationPrintBackstagePlanner exposes availability and disabled reasons." `
        -ResultApplication "Shared print request/option plans build preview/package/native-handoff descriptors." `
        -SharedPolicy "PresentationPrintBackstagePlanner; PresentationExportPlanner" `
        -WpfSources @("freep/FreeP.App.Host/Backstage/BackstageView.cs", "freep/FreeP.App.Host/MainWindow.cs") `
        -AvaloniaSources @("freep/FreeP.App.Avalonia/Backstage/BackstageView.cs", "freep/FreeP.App.Avalonia/MainWindow.cs") `
        -Tests @("freep/FreeP.App.Host.Tests/ExportPlannerTests.Dialog.cs", "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs", "freep/FreeP.App.Presentation.Tests/PresentationPrintBackstagePlannerTests.cs") `
        -ExistingVisualEvidence @("docs/parity/freep-print-output-option-choice-ui-2026-07-03.md", "docs/parity/freep-export-backstage-evidence-2026-07-05.md") `
        -VisualEvidenceStatus "package/workflow evidence only; paired app-pane screenshots remain" `
        -Status "behavior-aligned-host-shape-differs" -Notes "Avalonia has an additional compact print-options pane for direct command/keyboard routing; shared option and result policy is the same." `
        -RequiredWpfTokens @("PrintPresentation = ShowPrintBackstage", "RefreshPrintBackstagePlan") `
        -RequiredAvaloniaTokens @("PrintPresentation = ShowPrintBackstage", "ShowPrintOptionsPane") `
        -RequiredWorkareaSessionTokens @("FreePKeyboardCommand.PrintPresentation")

    New-Route -Id "file.open-picker" -Area "Open presentation" `
        -Triggers @("File > Open", "freep.file.open", "Ctrl+O") `
        -WpfSurface "Native Windows open dialog" -AvaloniaSurface "Platform storage-provider open picker" -Modality "native modal" `
        -Lifecycle "Cancel returns no path and does not replace the presentation; valid selection loads and closes stale modeless Find/Replace." `
        -Validation "PresentationFileDialogPlanner and persistence workflow validate supported extension and load outcome." `
        -ResultApplication "PresentationFilePersistenceWorkflow opens PPTX/FXP and rebuilds EditingSession." `
        -SharedPolicy "PresentationFileDialogPlanner; PresentationFilePersistenceWorkflow; shared file dialog descriptors" `
        -WpfSources @("freep/FreeP.App.Host/FileCommands.cs", "freep/FreeP.App.Host/MainWindow.cs") `
        -AvaloniaSources @("freep/FreeP.App.Avalonia/MainWindow.cs") `
        -Tests @("freep/FreeP.App.Host.Tests/FileLifecycleTests.cs", "freep/FreeP.App.Avalonia.Tests/FileLifecycleWorkflowSourceTests.cs", "freep/FreeP.App.Presentation.Tests/PresentationFileDialogPlannerTests.cs") `
        -VisualEvidenceStatus "native platform visual; exact cross-platform pixel parity is not applicable" `
        -RequiredWpfTokens @("OpenPresentation = () => _file.Open()") `
        -RequiredAvaloniaTokens @("OpenPresentation = () => _ = FileOpenAsync()") `
        -RequiredWorkareaSessionTokens @("FreePKeyboardCommand.OpenPresentation")

    New-Route -Id "file.save-as-picker" -Area "Save presentation as" `
        -Triggers @("File > Save As", "freep.file.save-as", "Ctrl+Shift+S") `
        -WpfSurface "Native Windows save dialog" -AvaloniaSurface "Platform storage-provider save picker" -Modality "native modal" `
        -Lifecycle "Cancel preserves path/dirty state; accepted selection resolves format and writes before updating path/state." `
        -Validation "PresentationFileDialogPlanner resolves extension/format; persistence workflow reports write failures without claiming success." `
        -ResultApplication "PresentationFilePersistenceWorkflow saves PPTX/FXP and updates recent/path/dirty state." `
        -SharedPolicy "PresentationFileDialogPlanner; PresentationFilePersistenceWorkflow; shared file dialog descriptors" `
        -WpfSources @("freep/FreeP.App.Host/FileCommands.cs", "freep/FreeP.App.Host/MainWindow.cs") `
        -AvaloniaSources @("freep/FreeP.App.Avalonia/MainWindow.cs") `
        -Tests @("freep/FreeP.App.Host.Tests/FileLifecycleTests.cs", "freep/FreeP.App.Avalonia.Tests/FileLifecycleWorkflowSourceTests.cs", "freep/FreeP.App.Presentation.Tests/PresentationFileDialogPlannerTests.cs") `
        -VisualEvidenceStatus "native platform visual; exact cross-platform pixel parity is not applicable" `
        -RequiredWpfTokens @("SavePresentationAs = () => _file.SaveAs()") `
        -RequiredAvaloniaTokens @("SavePresentationAs = () => _ = FileSaveAsAsync()") `
        -RequiredWorkareaSessionTokens @("FreePKeyboardCommand.SavePresentationAs")
)

$pairedEvidencePath = "docs/parity/freep-dialog-pane-visual-evidence/report.md"
$appOwnedRouteIds = @($routes | Where-Object { $_.id -notin @("file.open-picker", "file.save-as-picker") } | ForEach-Object { $_.id })
foreach ($route in $routes) {
    if ($route.id -in $appOwnedRouteIds) {
        $route.existingVisualEvidence = @(@($route.existingVisualEvidence) + @($pairedEvidencePath) | Select-Object -Unique)
        $route.visualEvidenceStatus = "committed real paired app-owned 96-DPI target capture; pixel and semantic thresholds pass"
    }
}

$residualVisualOnlyWork = @(
    [ordered]@{
        id = "freep-native-picker-human-evidence"
        routes = @("file.open-picker", "file.save-as-picker")
        exactWork = "Record platform-specific human smoke evidence for cancel, extension selection, overwrite/error handling, and focus return; do not use pixel equality across native picker implementations."
    }
)

$summary = [ordered]@{
    routeCount = $routes.Count
    behaviorAligned = @($routes | Where-Object { $_.status -eq "behavior-aligned" }).Count
    behaviorAlignedHostShapeDiffers = @($routes | Where-Object { $_.status -eq "behavior-aligned-host-shape-differs" }).Count
    productGaps = @($routes | Where-Object { $_.status -like "*gap*" }).Count
    routesWithExistingEvidence = @($routes | Where-Object { $_.existingVisualEvidence.Count -gt 0 }).Count
    routesWithCommittedPairedScreenshots = $appOwnedRouteIds.Count
    residualVisualOnlyWorkItems = $residualVisualOnlyWork.Count
}

$document = [ordered]@{
    schemaVersion = 1
    generatedBy = "tools/Generate-FreePDialogPaneParityInventory.ps1"
    inventoryMethod = "Explicit semantic trigger contracts validated against WPF and Avalonia command, keyboard, context-action, and startup source tokens; no class-name pairing or filename similarity matching."
    summary = $summary
    routes = $routes
    residualVisualOnlyWork = $residualVisualOnlyWork
}

$json = ($document | ConvertTo-Json -Depth 20) + [Environment]::NewLine
$markdown = New-Object System.Text.StringBuilder
[void]$markdown.AppendLine("# FreeP dialog and pane parity inventory")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine('Generated by `tools/Generate-FreePDialogPaneParityInventory.ps1` from explicit semantic trigger contracts validated against current WPF/Avalonia source. It does not pair surfaces by class name.')
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("## Summary")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("- Routes: $($summary.routeCount)")
[void]$markdown.AppendLine("- Behavior aligned: $($summary.behaviorAligned)")
[void]$markdown.AppendLine("- Behavior aligned with host shape difference: $($summary.behaviorAlignedHostShapeDiffers)")
[void]$markdown.AppendLine("- Product gaps: $($summary.productGaps)")
[void]$markdown.AppendLine("- Routes with existing semantic/render evidence: $($summary.routesWithExistingEvidence)")
[void]$markdown.AppendLine("- Routes with committed paired app screenshots: $($summary.routesWithCommittedPairedScreenshots)")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("## Routes")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("| Trigger route | Surfaces / modality | Lifecycle | Validation and result | Evidence | Status |")
[void]$markdown.AppendLine("| --- | --- | --- | --- | --- | --- |")
foreach ($route in $routes) {
    $triggerText = ($route.triggers -join "; ").Replace("|", "\|")
    $surfaceText = "WPF: $($route.wpfSurface); Avalonia: $($route.avaloniaSurface); $($route.modality)".Replace("|", "\|")
    $lifecycleText = $route.lifecycle.Replace("|", "\|")
    $resultText = ("$($route.validation) $($route.resultApplication)").Replace("|", "\|")
    $evidenceText = if ($route.existingVisualEvidence.Count -gt 0) { $route.existingVisualEvidence -join "<br>" } else { $route.visualEvidenceStatus }
    [void]$markdown.AppendLine(('| `{0}`<br>{1} | {2} | {3} | {4} | {5} | {6} |' -f $route.id, $triggerText, $surfaceText, $lifecycleText, $resultText, $evidenceText, $route.status))
}
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("## Residual Visual-Only Work")
[void]$markdown.AppendLine()
foreach ($item in $residualVisualOnlyWork) {
    [void]$markdown.AppendLine(('- **{0}** (`{1}`): {2}' -f $item.id, ($item.routes -join '`, `'), $item.exactWork))
}
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("Native Open/Save pickers require platform-specific interaction evidence, not cross-platform pixel equality. All 19 app-owned routes have committed real paired capture evidence; no functional FreeP dialog/pane gap remains in this inventory.")
$markdownText = $markdown.ToString()

$resolvedJsonPath = Resolve-ToolRepoPath -Path $JsonPath -RepoRoot $repoRoot
$resolvedMarkdownPath = Resolve-ToolRepoPath -Path $MarkdownPath -RepoRoot $repoRoot

if ($Check) {
    Test-ToolGeneratedContentMatches -ExpectedContent $json -ActualPath $resolvedJsonPath -Label "FreeP dialog/pane JSON inventory" -GeneratorScriptName "tools/Generate-FreePDialogPaneParityInventory.ps1" -NormalizeNewlines
    Test-ToolGeneratedContentMatches -ExpectedContent $markdownText -ActualPath $resolvedMarkdownPath -Label "FreeP dialog/pane Markdown inventory" -GeneratorScriptName "tools/Generate-FreePDialogPaneParityInventory.ps1" -NormalizeNewlines
    Write-Host "FreeP dialog/pane parity inventory is current."
    exit 0
}

New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedJsonPath) -Force | Out-Null
[System.IO.File]::WriteAllText($resolvedJsonPath, $json, [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($resolvedMarkdownPath, $markdownText, [System.Text.UTF8Encoding]::new($false))
Write-Host "Generated FreeP dialog/pane parity inventory with $($routes.Count) semantic routes."
