using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

// GOTCHA: an unqualified HorizontalAlignment inside a MainWindow partial resolves to the
// Avalonia.Controls.Control.HorizontalAlignment property, not the layout enum. Alias the enum
// so dialog layout code can set it explicitly.
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Windows-parity Home-tab merge variants and the "Paste Special..." dialog for the FreeX
/// Avalonia shell.
///
/// These are the ribbon ids that previously resolved to no-ops:
///   - home.mergeCells   ("Merge Cells")    -> merge the selection into one region, NO re-centering.
///   - home.mergeAcross  ("Merge Across")   -> merge each selected ROW into one cell, row by row.
///   - home.unmerge      ("Unmerge Cells")  -> wired to the existing <see cref="UnmergeSelectedRange"/>.
///   - home.pasteSpecial ("Paste Special...") -> an Excel-parity dialog exposing the full content-kind
///                                               radio set, composable Skip Blanks/Transpose/Keep Source
///                                               Column Widths checkboxes, and a math Operation group,
///                                               layered on top of the existing clipboard paste model.
///
/// Everything here reuses the established shell glue: command construction via the
/// <see cref="FreeX.App.Services.CellMergePlanner"/> primitives, execution through
/// <c>WorkbookSession.ExecuteReviewCommand(IWorkbookCommand)</c>, and the same content-loss warning
/// dialog used by Merge &amp; Center. No new <see cref="FreeX.App.Services.WorkbookSession"/> method is
/// required.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// "Merge Cells" (home.mergeCells). Like Merge &amp; Center but WITHOUT re-centering the result.
    /// Merges the whole selection into a single merged region, keeping (or, on request, concatenating)
    /// the contents using the same warning dialog as Merge &amp; Center.
    ///
    /// R127-avalonia-mergepaste-multiarea-1: a Ctrl+click multi-area selection (<c>_session.SelectedRanges</c>)
    /// must merge EVERY disjoint area independently, not just the active <c>_session.SelectedRange</c> --
    /// matching Excel and the WPF host's MainWindow.HomeFormatting.cs fix for the same defect. Resolved via
    /// the same <see cref="SelectionStyleCommandPlanner.ResolveRanges"/> choke point the WPF host and
    /// <see cref="FreeX.App.Services.WorkbookSession"/>'s own multi-area fixes use.
    ///
    /// R130-avalonia-mergepaste-groupedsheet-1 (parity fix): when tabs are grouped (<c>_session.IsWorkbookGrouped</c>),
    /// Excel/WPF's <c>MergeCellsMenuItem_Click</c> (via <c>TryExecuteRepeatableCurrentSelectionRangesCommand</c> ->
    /// <see cref="SelectionStyleCommandPlanner.CreateRangeCommand"/>) fans the merge out to EVERY sheet
    /// <c>CurrentGroupedEditSheetIds()</c> returns, not just the active sheet. This handler previously scoped
    /// both the command build AND the content-loss analysis to <c>_session.ActiveSheet</c> only, so with
    /// grouped tabs, Windows merged every grouped sheet while Linux/macOS merged only the active one -- a
    /// silent functional divergence for the same user gesture (not itself data loss, since the narrow
    /// analysis matched the narrow execution). Both are now widened together: execution fans `areas` across
    /// <c>_session.GetCurrentGroupedEditSheetIds()</c> via <see cref="GroupedSheetRangePlanner.RemapRangeToSheet"/>
    /// (mirroring <see cref="SelectionStyleCommandPlanner.CreateRangeCommand"/>'s sheet x range fan-out), and
    /// the analysis uses <see cref="AnalyzeGroupedSheetMergeContent"/> -- the SAME grouped-sheet-aware helper
    /// already used by <c>MergeAndCenterSelectedRangeAsync</c> and <c>ShowFormatCellsDialogAsync</c> (R128B)
    /// -- so a non-active grouped sheet's content is warned about, not silently discarded (avoiding the
    /// r127/r128 trap of widening execution without widening the guard in the same change). When the
    /// workbook isn't grouped, <c>GetCurrentGroupedEditSheetIds()</c> returns just the active sheet, so
    /// ungrouped behaviour is unchanged.
    /// </summary>
    private async Task MergeSelectedRangeAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var range = _session.SelectedRange;
        var areas = SelectionStyleCommandPlanner.ResolveRanges(range, _session.SelectedRanges);
        if (areas.Count == 0 || areas.All(area => area.CellCount <= 1))
        {
            RefreshShell(_statusText.Text ?? UiText.Get("TableLoc_Ready"));
            ShowEditIssue(UiText.Get("TableLoc_MergeSelectTwoOrMoreCells"));
            return;
        }

        var contentResolution = MergeCellContentResolution.KeepFirstCell;
        // R127-avalonia-mergepaste-multiarea-2 (data-loss fix): analyze EVERY disjoint area the merge
        // above will actually touch, not just the active `range` -- a Ctrl+click area other than the
        // active one can hold content that is about to be silently discarded with no warning at all.
        // R130-avalonia-mergepaste-groupedsheet-1: AnalyzeGroupedSheetMergeContent (not the single-sheet
        // CellMergePlanner.AnalyzeContent overload) so a non-active grouped sheet is covered too -- see
        // the fan-out execution below and the class doc comment above.
        var contentPlan = AnalyzeGroupedSheetMergeContent(areas);
        if (contentPlan.WouldLoseContent)
        {
            var decision = CellMergePlanner.ResolveContentChoice(
                await ShowMergeCellsContentWarningDialogAsync(contentPlan));
            if (!decision.ShouldProceed)
            {
                RefreshShell(_statusText.Text ?? UiText.Get("TableLoc_Ready"));
                return;
            }

            contentResolution = decision.Resolution;
        }

        var rangeReference = FormatRangeReference(range);
        // R130-avalonia-mergepaste-groupedsheet-1: fan each disjoint area across every grouped-edit
        // sheet, remapping the area onto that sheet -- mirrors
        // SelectionStyleCommandPlanner.CreateRangeCommand's sheet x range cross product (the WPF host's
        // own execution choke point for MergeCellsMenuItem_Click). Ungrouped selections still resolve to
        // a single-sheet loop since GetCurrentGroupedEditSheetIds() returns just the active sheet.
        var targetSheetIds = _session.GetCurrentGroupedEditSheetIds();
        var areaCommands = new List<IWorkbookCommand>();
        foreach (var sheetId in targetSheetIds)
        {
            var sheet = _session.Workbook.GetSheet(sheetId);
            if (sheet is null)
                continue;

            foreach (var area in areas)
            {
                var sheetArea = GroupedSheetRangePlanner.RemapRangeToSheet(area, sheetId);
                areaCommands.Add(CellMergePlanner.CreateMergeCellsCommand(
                    sheet,
                    sheetId,
                    sheetArea,
                    contentResolution));
            }
        }

        var command = CellMergePlanner.WrapCommands("Merge Cells", areaCommands);
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? UiText.Get("TableLoc_Ready"));
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("TableLoc_MergeCellsFailed"));
            return;
        }

        RefreshShell(UiText.Format("TableLoc_Merged", rangeReference));
    }

    /// <summary>
    /// "Merge Across" (home.mergeAcross). Merges each selected ROW into a single horizontal merged
    /// region, leaving the rows independent from one another (matching Excel's Merge Across behaviour).
    /// A single-column selection has nothing to merge across.
    ///
    /// R127-avalonia-mergepaste-multiarea-1: extended to a Ctrl+click multi-area selection the same way
    /// as <see cref="MergeSelectedRangeAsync"/> above -- every disjoint area gets its own per-row merge
    /// batch, and an area that is itself single-column (but sits alongside a wider area) is skipped
    /// rather than rejecting the whole action, matching real Excel and the WPF host's
    /// MainWindow.HomeFormatting.cs fix for the same defect.
    ///
    /// R130-avalonia-mergepaste-groupedsheet-2 (parity fix): same grouped-sheet fan-out gap and fix as
    /// <see cref="MergeSelectedRangeAsync"/>'s R130 note above -- the WPF host's
    /// <c>MergeAcrossMenuItem_Click</c> fans across <c>CurrentGroupedEditSheetIds()</c> via the same
    /// <c>TryExecuteRepeatableCurrentSelectionRangesCommand</c> choke point, so this handler now builds
    /// its per-area per-row commands for every grouped-edit sheet (not just the active one), and analyzes
    /// content loss the same way via <see cref="AnalyzeGroupedSheetMergeContent"/> (widened together, not
    /// separately -- see the r127/r128 trap note there).
    /// </summary>
    private async Task MergeAcrossSelectedRangeAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var range = _session.SelectedRange;
        var areas = SelectionStyleCommandPlanner.ResolveRanges(range, _session.SelectedRanges);
        if (areas.Count == 0 || areas.All(area => area.ColCount <= 1))
        {
            RefreshShell(_statusText.Text ?? UiText.Get("TableLoc_Ready"));
            ShowEditIssue(UiText.Get("TableLoc_MergeAcrossSelectTwoOrMoreColumns"));
            return;
        }

        // Analyze the WHOLE selection once up front, matching the WPF host's
        // TryResolveMergeContentResolution and this file's own MergeSelectedRangeAsync above, so
        // multi-cell rows (e.g. A1:C1 = "Jan"/"Feb"/"Mar") get the "Merging cells only keeps the
        // upper-leftmost value" confirmation instead of the per-row merges below silently discarding
        // every non-left-most value in each row with zero warning.
        //
        // R127-avalonia-mergepaste-multiarea-2 (data-loss fix): analyze EVERY disjoint area in `areas`,
        // not just the active `range` -- a Ctrl+click area other than the active one can hold content
        // that the per-area per-row loop below is about to discard with no warning at all.
        //
        // R130-avalonia-mergepaste-groupedsheet-2: AnalyzeGroupedSheetMergeContent (perRow: true) covers
        // every grouped-edit sheet too, matching the fan-out execution below.
        var contentResolution = MergeCellContentResolution.KeepFirstCell;
        var contentPlan = AnalyzeGroupedSheetMergeContent(areas, perRow: true);
        if (contentPlan.WouldLoseContent)
        {
            var decision = CellMergePlanner.ResolveContentChoice(
                await ShowMergeCellsContentWarningDialogAsync(contentPlan));
            if (!decision.ShouldProceed)
            {
                RefreshShell(_statusText.Text ?? UiText.Get("TableLoc_Ready"));
                return;
            }

            contentResolution = decision.Resolution;
        }

        // Build one composite per (grouped sheet, disjoint area) pair, each containing one horizontal
        // per-row merge for that area's own column span remapped onto that sheet. An area that is
        // itself single-column contributes nothing. Ungrouped selections resolve to a single-sheet loop since
        // GetCurrentGroupedEditSheetIds() returns just the active sheet.
        var targetSheetIds = _session.GetCurrentGroupedEditSheetIds();
        var areaCommands = new List<IWorkbookCommand>();
        foreach (var sheetId in targetSheetIds)
        {
            var sheet = _session.Workbook.GetSheet(sheetId);
            if (sheet is null)
                continue;

            foreach (var area in areas)
            {
                if (area.ColCount <= 1)
                    continue;

                var sheetArea = GroupedSheetRangePlanner.RemapRangeToSheet(area, sheetId);

                areaCommands.Add(CellMergePlanner.CreateMergeAcrossCommand(
                    sheet,
                    sheetId,
                    sheetArea,
                    contentResolution));
            }
        }

        if (areaCommands.Count == 0)
        {
            RefreshShell(_statusText.Text ?? UiText.Get("TableLoc_Ready"));
            return;
        }

        var rangeReference = FormatRangeReference(range);
        var command = CellMergePlanner.WrapCommands("Merge Across", areaCommands);
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? UiText.Get("TableLoc_Ready"));
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("TableLoc_MergeAcrossFailed"));
            return;
        }

        RefreshShell(UiText.Format("TableLoc_MergedAcross", rangeReference));
    }

    /// <summary>
    /// "Paste Special..." (home.pasteSpecial). Opens an Excel-parity dialog: a full content-kind radio
    /// group, composable Skip Blanks / Transpose / Keep Source Column Widths checkboxes, a math Operation
    /// group (None/Add/Subtract/Multiply/Divide) and a Paste Link button -- matching WPF's
    /// <see cref="FreeX.App.Host.PasteSpecialDialog"/> and real Excel's single unified Paste Special
    /// dialog.
    ///
    /// R120: previously this ribbon-triggered dialog exposed only All / Values / Formulas / Formats; the
    /// richer content kinds (All Except Borders, All Merging Conditional Formats, Formulas/Values and
    /// Number Formats, Values and Source Formatting, Column Widths, Comments and Notes, Validation, Text,
    /// Unicode Text, Picture, Linked Picture, Paste Link, Skip Blanks, Transpose, the math Operations) were
    /// reachable only through the Paste split-button's Paste Special submenu (<see cref="CreatePasteSpecialMenuItems"/>),
    /// never from this ribbon button. Every option below dispatches through those SAME already-wired
    /// execution methods (<see cref="PasteSpecialClipboardTextAsync"/>, <see cref="PasteCommentsFromClipboardAsync"/>,
    /// <see cref="PasteDataValidationFromClipboardAsync"/>, <see cref="PasteColumnWidthsFromClipboardAsync"/>,
    /// <see cref="PasteSpecialExternalTextFromClipboardAsync"/>, <see cref="PastePictureFromClipboardAsync"/>,
    /// <see cref="PasteLinkFromClipboardAsync"/>) -- no new paste behaviour is introduced, only a single
    /// consolidated surface for what the shell already does.
    /// </summary>
    private async Task ShowPasteSpecialDialogAsync()
    {
        if (PasteSpecialWorkflowOverrideForTest is { } workflowOverride)
        {
            await workflowOverride();
            return;
        }

        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var choice = await PromptPasteSpecialModeAsync();
        if (choice is not { } selection)
        {
            RefreshShell(_statusText.Text ?? UiText.Get("TableLoc_Ready"));
            return;
        }

        var option = selection.Option;
        switch (option.Kind)
        {
            case PasteSpecialDialogActionKind.Comments:
                await PasteCommentsFromClipboardAsync(option.Label);
                return;
            case PasteSpecialDialogActionKind.Validation:
                await PasteDataValidationFromClipboardAsync(option.Label);
                return;
            case PasteSpecialDialogActionKind.ColumnWidths:
                await PasteColumnWidthsFromClipboardAsync(option.Label);
                return;
            case PasteSpecialDialogActionKind.Text:
            case PasteSpecialDialogActionKind.UnicodeText:
                await PasteSpecialExternalTextFromClipboardAsync(option.Label);
                return;
            case PasteSpecialDialogActionKind.Picture:
                await PastePictureFromClipboardAsync(option.Label, linkedPicture: false);
                return;
            case PasteSpecialDialogActionKind.LinkedPicture:
                await PastePictureFromClipboardAsync(option.Label, linkedPicture: true);
                return;
            case PasteSpecialDialogActionKind.Link:
                await PasteLinkFromClipboardAsync(option.Label);
                return;
            default:
                var pasteOptions = new PasteSpecialOptions(
                    Transpose: selection.Transpose,
                    Operation: selection.Operation,
                    SkipBlanks: selection.SkipBlanks,
                    ContentKind: option.ContentKind);
                await PasteSpecialClipboardTextAsync(option.Mode, pasteOptions, option.Label, selection.KeepSourceColumnWidths);
                return;
        }
    }

    internal Func<Task>? PasteSpecialWorkflowOverrideForTest { get; set; }

    /// <summary>
    /// Which existing execution method a content-kind radio in the ribbon's Paste Special dialog routes
    /// to. <see cref="Cells"/> is the composable family (goes through <see cref="PasteSpecialClipboardTextAsync"/>
    /// together with the Skip Blanks/Transpose/Keep Source Column Widths checkboxes and the Operation
    /// group); every other member is a fixed, non-composable action mirroring one submenu item in
    /// <see cref="CreatePasteSpecialMenuItems"/>.
    /// </summary>
    internal enum PasteSpecialDialogActionKind
    {
        Cells,
        Comments,
        Validation,
        ColumnWidths,
        Text,
        UnicodeText,
        Picture,
        LinkedPicture,
        Link,
    }

    internal sealed record PasteSpecialDialogOption(
        string Label,
        string AutomationId,
        PasteSpecialDialogActionKind Kind,
        PasteCellsMode Mode = PasteCellsMode.All,
        PasteSpecialContentKind ContentKind = PasteSpecialContentKind.Default);

    internal sealed record PasteSpecialDialogSelection(
        PasteSpecialDialogOption Option,
        bool SkipBlanks,
        bool Transpose,
        bool KeepSourceColumnWidths,
        PasteSpecialOperation Operation);

    /// <summary>
    /// Test-only hook (matching the established SmokeProbe convention used by
    /// <c>ShowFormatCellsInputDialogAsync</c>/<c>ShowFindDialogAsync</c>/etc. in MainWindow.cs) exposing
    /// the real, production content-kind radios / checkboxes / operation radios / footer buttons of the
    /// ribbon's Paste Special dialog so a headless test can drive them directly -- the OS clipboard itself
    /// (<c>IClipboard</c>) is <c>[NotClientImplementable]</c> so cannot be doubled in a headless test (see
    /// the R66/R68 rationale on <see cref="TryGetClipboardTextAsync"/>), but this probe fires from the
    /// dialog's own <c>Opened</c> event, before any clipboard access happens, so it can still exercise the
    /// real dialog end-to-end for everything up to (and including) the OK-click selection decision.
    /// </summary>
    internal sealed record PasteSpecialDialogSmokeProbe(
        Window Dialog,
        IReadOnlyList<RadioButton> ContentRadios,
        CheckBox SkipBlanksBox,
        CheckBox TransposeBox,
        CheckBox KeepColumnWidthsBox,
        IReadOnlyList<RadioButton> OperationRadios,
        Button PasteLinkButton,
        Button OkButton,
        Button CancelButton);

    /// <summary>
    /// The content-kind radio list for the ribbon's Paste Special dialog, in the same order as (and
    /// covering every entry of) the Paste split-button's Paste Special submenu built by
    /// <see cref="CreatePasteSpecialMenuItems"/>, minus the Transpose / Skip Blanks / the four math
    /// Operation entries there -- this dialog exposes those as composable checkboxes and an Operation
    /// group instead (see <see cref="PromptPasteSpecialModeAsync"/>), so they can be combined with any
    /// <see cref="PasteSpecialDialogActionKind.Cells"/> content kind, matching Excel and
    /// <see cref="FreeX.App.Host.PasteSpecialDialog"/>.
    /// </summary>
    private static readonly PasteSpecialDialogOption[] PasteSpecialDialogOptions =
    [
        new("All", "PasteSpecialAllRadio", PasteSpecialDialogActionKind.Cells, PasteCellsMode.All),
        new("Values", "PasteSpecialValuesRadio", PasteSpecialDialogActionKind.Cells, PasteCellsMode.Values),
        new("Formulas", "PasteSpecialFormulasRadio", PasteSpecialDialogActionKind.Cells, PasteCellsMode.Formulas),
        new("Formats", "PasteSpecialFormatsRadio", PasteSpecialDialogActionKind.Cells, PasteCellsMode.Formats),
        new("Comments and Notes", "PasteSpecialCommentsRadio", PasteSpecialDialogActionKind.Comments),
        new("Validation", "PasteSpecialValidationRadio", PasteSpecialDialogActionKind.Validation),
        new("All Except Borders", "PasteSpecialAllExceptBordersRadio", PasteSpecialDialogActionKind.Cells, PasteCellsMode.All, PasteSpecialContentKind.AllExceptBorders),
        new("All Merging Conditional Formats", "PasteSpecialAllMergingConditionalFormatsRadio", PasteSpecialDialogActionKind.Cells, PasteCellsMode.All, PasteSpecialContentKind.AllMergingConditionalFormats),
        new("Column Widths", "PasteSpecialColumnWidthsRadio", PasteSpecialDialogActionKind.ColumnWidths),
        new("Formulas and Number Formats", "PasteSpecialFormulasAndNumberFormatsRadio", PasteSpecialDialogActionKind.Cells, PasteCellsMode.All, PasteSpecialContentKind.FormulasAndNumberFormats),
        new("Values and Number Formats", "PasteSpecialValuesAndNumberFormatsRadio", PasteSpecialDialogActionKind.Cells, PasteCellsMode.All, PasteSpecialContentKind.ValuesAndNumberFormats),
        new("Values and Source Formatting", "PasteSpecialValuesAndSourceFormattingRadio", PasteSpecialDialogActionKind.Cells, PasteCellsMode.All, PasteSpecialContentKind.ValuesAndSourceFormatting),
        new("Text", "PasteSpecialTextRadio", PasteSpecialDialogActionKind.Text),
        new("Unicode Text", "PasteSpecialUnicodeTextRadio", PasteSpecialDialogActionKind.UnicodeText),
        new("Picture", "PasteSpecialPictureRadio", PasteSpecialDialogActionKind.Picture),
        new("Linked Picture", "PasteSpecialLinkedPictureRadio", PasteSpecialDialogActionKind.LinkedPicture),
    ];

    private static readonly PasteSpecialDialogOption PasteSpecialPasteLinkOption =
        new("Paste Link", "PasteSpecialPasteLinkButton", PasteSpecialDialogActionKind.Link);

    /// <summary>
    /// Shows the Paste Special dialog and returns the selected content kind plus checkbox/operation state,
    /// or <c>null</c> if cancelled. The "Paste Link" footer button (matching WPF/Excel) closes the dialog
    /// with a dedicated <see cref="PasteSpecialDialogActionKind.Link"/> selection regardless of which
    /// content-kind radio is checked, mirroring <see cref="CreatePasteSpecialMenuItems"/>'s standalone
    /// "Paste Link" submenu entry.
    /// </summary>
    internal async Task<PasteSpecialDialogSelection?> PromptPasteSpecialModeAsync(
        Action<PasteSpecialDialogSmokeProbe>? launchSmokeProbe = null)
    {
        PasteSpecialDialogSelection? result = null;

        var dialog = new Window
        {
            Title = UiText.Get("TableLoc_PasteSpecialTitle"),
            Width = 420,
            Height = 600,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PasteSpecialDialog");

        var root = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
        };

        root.Children.Add(new TextBlock
        {
            Text = UiText.Get("TableLoc_PasteSpecialPasteLabel"),
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        });

        var radios = new List<RadioButton>();
        var optionsPanel = new StackPanel { Spacing = 4 };
        foreach (var option in PasteSpecialDialogOptions)
        {
            var radio = new RadioButton
            {
                Content = option.Label,
                GroupName = "PasteSpecialMode",
                IsChecked = option == PasteSpecialDialogOptions[0],
                Tag = option,
            };
            ApplyDataOpsRadioButtonChrome(radio);
            AutomationProperties.SetAutomationId(radio, option.AutomationId);
            radios.Add(radio);
            optionsPanel.Children.Add(radio);
        }

        root.Children.Add(new ScrollViewer
        {
            MaxHeight = 230,
            Content = optionsPanel,
        });

        var skipBlanksBox = new CheckBox { Content = "Skip Blanks" };
        ApplyDataOpsCheckBoxChrome(skipBlanksBox);
        AutomationProperties.SetAutomationId(skipBlanksBox, "PasteSpecialSkipBlanksBox");

        var transposeBox = new CheckBox { Content = "Transpose" };
        ApplyDataOpsCheckBoxChrome(transposeBox);
        AutomationProperties.SetAutomationId(transposeBox, "PasteSpecialTransposeBox");

        var keepColumnWidthsBox = new CheckBox { Content = "Keep Source Column Widths" };
        ApplyDataOpsCheckBoxChrome(keepColumnWidthsBox);
        AutomationProperties.SetAutomationId(keepColumnWidthsBox, "PasteSpecialKeepColumnWidthsBox");

        var checkboxPanel = new StackPanel { Spacing = 4 };
        checkboxPanel.Children.Add(skipBlanksBox);
        checkboxPanel.Children.Add(transposeBox);
        checkboxPanel.Children.Add(keepColumnWidthsBox);
        root.Children.Add(checkboxPanel);

        root.Children.Add(new TextBlock
        {
            Text = "Operation",
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        });

        var operationChoices = new (PasteSpecialOperation Operation, string Label, string AutomationId)[]
        {
            (PasteSpecialOperation.None, "None", "PasteSpecialOperationNoneRadio"),
            (PasteSpecialOperation.Add, "Add", "PasteSpecialOperationAddRadio"),
            (PasteSpecialOperation.Subtract, "Subtract", "PasteSpecialOperationSubtractRadio"),
            (PasteSpecialOperation.Multiply, "Multiply", "PasteSpecialOperationMultiplyRadio"),
            (PasteSpecialOperation.Divide, "Divide", "PasteSpecialOperationDivideRadio"),
        };

        var operationRadios = new List<RadioButton>();
        var operationGrid = new Grid();
        operationGrid.ColumnDefinitions.Add(new ColumnDefinition());
        operationGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 3; i++)
            operationGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var i = 0; i < operationChoices.Length; i++)
        {
            var choice = operationChoices[i];
            var radio = new RadioButton
            {
                Content = choice.Label,
                GroupName = "PasteSpecialOperation",
                IsChecked = choice.Operation == PasteSpecialOperation.None,
                Tag = choice.Operation,
            };
            ApplyDataOpsRadioButtonChrome(radio);
            AutomationProperties.SetAutomationId(radio, choice.AutomationId);
            Grid.SetRow(radio, i / 2);
            Grid.SetColumn(radio, i % 2);
            operationRadios.Add(radio);
            operationGrid.Children.Add(radio);
        }

        root.Children.Add(operationGrid);

        var pasteLinkButton = new Button
        {
            Content = "Paste Link",
            MinWidth = 82,
        };
        ApplyDataOpsButtonChrome(pasteLinkButton);
        AutomationProperties.SetAutomationId(pasteLinkButton, "PasteSpecialPasteLinkButton");
        pasteLinkButton.Click += (_, _) =>
        {
            result = new PasteSpecialDialogSelection(
                PasteSpecialPasteLinkOption, SkipBlanks: false, Transpose: false, KeepSourceColumnWidths: false, Operation: PasteSpecialOperation.None);
            dialog.Close();
        };

        var okButton = new Button
        {
            Content = UiText.Get("TableLoc_OK"),
            MinWidth = 82,
            IsDefault = true,
        };
        ApplyDataOpsButtonChrome(okButton, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "PasteSpecialOkButton");
        okButton.Click += (_, _) =>
        {
            var selected = radios.FirstOrDefault(r => r.IsChecked == true);
            if (selected?.Tag is PasteSpecialDialogOption option)
            {
                var operation = PasteSpecialOperation.None;
                if (operationRadios.FirstOrDefault(r => r.IsChecked == true) is { Tag: PasteSpecialOperation selectedOperation })
                    operation = selectedOperation;

                result = new PasteSpecialDialogSelection(
                    option,
                    SkipBlanks: skipBlanksBox.IsChecked == true,
                    Transpose: transposeBox.IsChecked == true,
                    KeepSourceColumnWidths: keepColumnWidthsBox.IsChecked == true,
                    Operation: operation);
            }

            dialog.Close();
        };

        var cancelButton = new Button
        {
            Content = UiText.Get("TableLoc_Cancel"),
            MinWidth = 82,
            IsCancel = true,
        };
        ApplyDataOpsButtonChrome(cancelButton);
        AutomationProperties.SetAutomationId(cancelButton, "PasteSpecialCancelButton");
        cancelButton.Click += (_, _) =>
        {
            result = null;
            dialog.Close();
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
        };
        buttonRow.Children.Add(pasteLinkButton);
        buttonRow.Children.Add(okButton);
        buttonRow.Children.Add(cancelButton);
        root.Children.Add(buttonRow);

        dialog.Content = root;
        dialog.Opened += (_, _) => okButton.Focus();
        if (launchSmokeProbe is not null)
        {
            dialog.Opened += (_, _) =>
            {
                RunLaunchSmokeDialogProbe(
                    dialog,
                    () => launchSmokeProbe(new PasteSpecialDialogSmokeProbe(
                        dialog, radios, skipBlanksBox, transposeBox, keepColumnWidthsBox, operationRadios, pasteLinkButton, okButton, cancelButton)));
            };
        }

        await dialog.ShowDialog(this);
        return result;
    }
}
