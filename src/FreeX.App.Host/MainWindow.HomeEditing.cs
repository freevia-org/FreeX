using System.Collections.Generic;
using System.Linq;
using System.Windows;
using FreeX.App.Presentation.FillSeries;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    // ── Editing group (pickers) ──────────────────────────────────────────────

    private void AutoSumPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        InsertAutoSumFormula("SUM");
    }
    private void FormulasAutoSumPickerBtn_Click(object sender, RoutedEventArgs e) { AutoSumPickerBtn_Click(sender, e); }

    private void InsertAutoSumFormula(string func)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "AutoSum",
                range,
                currentRange =>
                {
                    if (!AutoSumFormulaPlanner.TryCreatePlan(_workbook.GetSheet(_currentSheetId), func, currentRange, out var plan))
                        return new FailedWorkbookCommand(UiText.Get("MainWindowMessage_AutoSumTargetOutOfBounds"));

                    var edits = new List<(CellAddress Address, Cell NewCell)> { (plan.Target, Cell.FromFormula(plan.Formula)) };
                    var targetSheetIds = CurrentGroupedEditSheetIds();
                    return targetSheetIds.Count > 1
                        ? new GroupedEditCellsCommand(targetSheetIds, _currentSheetId, edits)
                        : new EditCellsCommand(_currentSheetId, edits);
                },
                out var outcome))
            return;

        SetActiveCell(outcome.AffectedCells is { Count: > 0 }
            ? outcome.AffectedCells[0]
            : range.Start);
        UpdateViewport();
    }

    private void AutoSumSumMenuItem_Click(object sender, RoutedEventArgs e)   => InsertAutoSumFormula("SUM");
    private void AutoSumAvgMenuItem_Click(object sender, RoutedEventArgs e)   => InsertAutoSumFormula("AVERAGE");
    private void AutoSumCountMenuItem_Click(object sender, RoutedEventArgs e) => InsertAutoSumFormula("COUNT");
    private void AutoSumCountAllMenuItem_Click(object sender, RoutedEventArgs e) => InsertAutoSumFormula("COUNTA");
    private void AutoSumMaxMenuItem_Click(object sender, RoutedEventArgs e)   => InsertAutoSumFormula("MAX");
    private void AutoSumMinMenuItem_Click(object sender, RoutedEventArgs e)   => InsertAutoSumFormula("MIN");
    private void AutoSumMoreMenuItem_Click(object sender, RoutedEventArgs e)  => InsertFunctionBtn_Click(sender, e);

    private void FillPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }
    private void FillDownMenuItem_Click(object sender, RoutedEventArgs e)
        => ExecuteFillCells(FillCellsDirection.Down);

    private void FillRightMenuItem_Click(object sender, RoutedEventArgs e)
        => ExecuteFillCells(FillCellsDirection.Right);

    private void FillUpMenuItem_Click(object sender, RoutedEventArgs e)
        => ExecuteFillCells(FillCellsDirection.Up);

    private void FillLeftMenuItem_Click(object sender, RoutedEventArgs e)
        => ExecuteFillCells(FillCellsDirection.Left);

    private void ExecuteFillCells(FillCellsDirection direction)
    {
        if (SheetGrid.SelectedRange is not { } range || !FillSeriesPlanner.CanFill(range, direction))
            return;

        var title = direction switch
        {
            FillCellsDirection.Down => "Fill Down",
            FillCellsDirection.Right => "Fill Right",
            FillCellsDirection.Up => "Fill Up",
            FillCellsDirection.Left => "Fill Left",
            _ => "Fill"
        };

        if (!TryExecuteRepeatableGroupedSheetCommand(
                title,
                sheetId => new FillCellsCommand(sheetId, GroupedSheetRangePlanner.RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId), direction),
                out var outcome))
            return;

        UpdateViewport();
    }

    /// <summary>
    /// Whether the Fill ▸ Series dialog is worth opening for the selection's leading cell.
    /// Linear/Growth/Date only ever operate on a Number/Date seed (FillSeriesPlanner's
    /// BuildLinearSeriesEdits/BuildGrowthSeriesEdits/BuildDateSeriesEdits each require one), but
    /// AutoFill also supports a text seed -- it replays a fill-handle-style text-list/pattern
    /// detection (e.g. "Item 1" -&gt; "Item 2") via BuildAutoFillSeriesEdits /
    /// AutofillCommand.TryCreateAutoFillTextSeries. The type picker only appears inside the
    /// dialog itself, so the entry gate must admit every seed type any series type can act on --
    /// Number, Date, or Text -- rather than requiring Number/Date before the user has even had a
    /// chance to choose AutoFill.
    /// </summary>
    private static bool CanStartFillSeries(ScalarValue? startValue) =>
        startValue is NumberValue or DateTimeValue or TextValue;

    private void FillSeriesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId); if (sheet is null) return;
        var startValue = sheet.GetValue(range.Start.Row, range.Start.Col);
        if (!CanStartFillSeries(startValue))
        {
            _messageService.ShowWarning(
                UiText.Get("FillSeriesStep_SelectNumericOrDateStartMessage"),
                UiText.Get("FillSeriesStep_Title"));
            return;
        }


        var dialog = new FillSeriesStepDialog { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Fill Series",
                range,
                currentRange =>
                {
                    var currentSheet = _workbook.GetSheet(_currentSheetId);
                    List<(CellAddress Address, Cell NewCell)> edits = currentSheet is null
                        ? []
                        : FillSeriesPlanner.BuildSeriesEdits(
                            currentSheet,
                            currentRange,
                            dialog.Result);
                    var targetSheetIds = CurrentGroupedEditSheetIds();
                    return targetSheetIds.Count > 1
                        ? new GroupedEditCellsCommand(targetSheetIds, _currentSheetId, edits)
                        : new EditCellsCommand(_currentSheetId, edits);
                },
                out var outcome))
            return;

        UpdateViewport();
    }

    private void FlashFillMenuItem_Click(object sender, RoutedEventArgs e) => TryFlashFill();

    private void TryFlashFill()
    {
        var range = SheetGrid.SelectedRange;
        if (range is null) return;

        var command = CreateFlashFillCommand(range.Value, out var hasExamples, out var hasFillTargets);
        if (command is null)
        {
            if (!hasExamples)
            {
                _messageService.ShowWarning(
                    UiText.Get("MainWindowMessage_FlashFillNoExamples"),
                    UiText.Get("MainWindowMessage_FlashFillTitle"));
            }
            else if (!hasFillTargets)
            {
                _messageService.ShowInfo(
                    UiText.Get("MainWindowMessage_FlashFillNoBlankAdjacentCells"),
                    UiText.Get("MainWindowMessage_FlashFillTitle"));
            }

            return;
        }

        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Flash Fill",
                range.Value,
                currentRange => CreateFlashFillCommand(currentRange, out _, out _) ?? new FailedWorkbookCommand(UiText.Get("MainWindowMessage_FlashFillNoBlankAdjacentCells")),
                out var outcome))
            return;

        UpdateViewport();
    }

    private IWorkbookCommand? CreateFlashFillCommand(
        GridRange range,
        out bool hasExamples,
        out bool hasFillTargets)
    {
        hasExamples = false;
        hasFillTargets = false;

        var commands = new List<IWorkbookCommand>();
        foreach (var sheetId in CurrentGroupedEditSheetIds())
        {
            var sheet = _workbook.GetSheet(sheetId);
            if (sheet is null)
                continue;

            var sheetRange = GroupedSheetRangePlanner.RemapRangeToSheet(range, sheetId);
            var plan = FlashFillRangePlanner.Plan(sheet, sheetRange);
            hasExamples |= FlashFillRangePlanner.HasExamples(sheet, plan);
            if (!FlashFillRangePlanner.HasFillTargets(sheet, plan))
                continue;

            hasFillTargets = true;
            commands.Add(plan.CreateCommand(sheetId));
        }

        return commands.Count switch
        {
            0 => null,
            1 => commands[0],
            _ => new CompositeWorkbookCommand("Flash Fill", commands)
        };
    }

    private void SortFilterPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }
    private void SortAZMenuItem_Click(object sender, RoutedEventArgs e)    => SortAscButton_Click(sender, e);
    private void SortZAMenuItem_Click(object sender, RoutedEventArgs e)    => SortDescButton_Click(sender, e);
    private void SortCustomMenuItem_Click(object sender, RoutedEventArgs e) => SortCustomButton_Click(sender, e);
    private void FilterToggleMenuItem_Click(object sender, RoutedEventArgs e) => FilterButton_Click(sender, e);
    private void FilterClearMenuItem_Click(object sender, RoutedEventArgs e)  => ClearFilterButton_Click(sender, e);
    private void FilterReapplyMenuItem_Click(object sender, RoutedEventArgs e) => ReapplyAutoFilter();

    private void FindSelectPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }
    private void FindFindMenuItem_Click(object sender, RoutedEventArgs e)       => FindButton_Click(sender, e);
    private void FindReplaceMenuItem_Click(object sender, RoutedEventArgs e)    => ReplaceButton_Click(sender, e);
    private void FindGoToMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var defaultAddress = SheetGrid.SelectedRange is { } selectionForDefault
            ? FormatCellReference(selectionForDefault.Start)
            : FormatCellReference(new CellAddress(_currentSheetId, 1, 1));
        var dialog = new GoToDialog(_currentSheetId, defaultAddress, _workbook.NamedRanges) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        if (dialog.SelectedSpecialKind is { } specialKind)
        {
            SelectGoToSpecialMatches(specialKind, dialog.SelectedSpecialOptions, showEmptyMessage: true);
            return;
        }

        if (dialog.SelectedRange is { } selectedRange)
        {
            SheetGrid.SelectedRange = selectedRange;
            SheetGrid.SelectedRanges = null;
            _selectionAnchor = selectedRange.Start;
            _selectionCursor = selectedRange.End;
            CellAddressBox.Text = FormatNameBoxSelectionText(selectedRange);
            FormulaBar.Text = FormatFormulaBarText(_workbook.GetSheet(_currentSheetId)?.GetCell(selectedRange.Start), selectedRange.Start);
            EnsureCellVisible(selectedRange.Start);
            FocusSheetGridIfNeeded();
            RefreshToolbar();
            RefreshStatusBar();
            RefreshValidationDropdown();
            RefreshDvInputMessage();
            return;
        }

        SetActiveCell(dialog.SelectedAddress);
        EnsureCellVisible(dialog.SelectedAddress);
    }
    private void FindGoToSpecialMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        var range = ResolveGoToSpecialSearchRange(sheet);
        var dialog = new GoToSpecialDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;

        SelectGoToSpecialMatches(dialog.SelectedKind, dialog.SelectedOptions, showEmptyMessage: true, sheet, range);
    }

    private void FindFormulasMenuItem_Click(object sender, RoutedEventArgs e) =>
        SelectGoToSpecialMatches(GoToSpecialKind.Formulas, showEmptyMessage: true);

    private void FindNotesMenuItem_Click(object sender, RoutedEventArgs e) =>
        SelectGoToSpecialMatches(GoToSpecialKind.Comments, showEmptyMessage: true);

    private void FindConditionalFormattingMenuItem_Click(object sender, RoutedEventArgs e) =>
        SelectGoToSpecialMatches(GoToSpecialKind.ConditionalFormats, showEmptyMessage: true);

    private void FindConstantsMenuItem_Click(object sender, RoutedEventArgs e) =>
        SelectGoToSpecialMatches(GoToSpecialKind.Constants, showEmptyMessage: true);

    private void FindDataValidationMenuItem_Click(object sender, RoutedEventArgs e) =>
        SelectGoToSpecialMatches(GoToSpecialKind.DataValidation, showEmptyMessage: true);

    private void FindSelectObjectsMenuItem_Click(object sender, RoutedEventArgs e) =>
        SelectGoToSpecialMatches(GoToSpecialKind.Objects, showEmptyMessage: true);

    private void FindSelectionPaneMenuItem_Click(object sender, RoutedEventArgs e) =>
        SelectionPaneBtn_Click(sender, e);

    private void SelectGoToSpecialMatches(GoToSpecialKind kind, bool showEmptyMessage)
        => SelectGoToSpecialMatches(kind, null, showEmptyMessage);

    private void SelectGoToSpecialMatches(GoToSpecialKind kind, GoToSpecialOptions? options, bool showEmptyMessage)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        var range = ResolveGoToSpecialSearchRange(sheet);

        SelectGoToSpecialMatches(kind, options, showEmptyMessage, sheet, range);
    }

    /// <summary>
    /// Determines the range that Go To Special / the Find-family shortcuts (Formulas, Notes,
    /// Conditional Formatting, Constants, Data Validation, Objects) should search. Matching Excel,
    /// a single active cell (the ordinary result of clicking one cell) searches the whole used
    /// range of the sheet; an explicit multi-cell selection is honored as-is.
    /// </summary>
    private GridRange ResolveGoToSpecialSearchRange(Sheet sheet)
    {
        var selected = SheetGrid.SelectedRange;
        if (selected is { } range && range.Start != range.End)
            return range;

        return sheet.GetUsedRange() ??
            new GridRange(new CellAddress(_currentSheetId, 1, 1), new CellAddress(_currentSheetId, 1, 1));
    }

    private void SelectGoToSpecialMatches(GoToSpecialKind kind, bool showEmptyMessage, Sheet sheet, GridRange range)
        => SelectGoToSpecialMatches(kind, null, showEmptyMessage, sheet, range);

    private void SelectGoToSpecialMatches(GoToSpecialKind kind, GoToSpecialOptions? options, bool showEmptyMessage, Sheet sheet, GridRange range)
    {
        // CurrentRegion/Precedents/Dependents trace relationships from the user's true active
        // cell/selection, not the (possibly auto-expanded-to-used-range) content search range;
        // otherwise a single-cell selection whose used-range corner is blank falsely reports
        // "No cells found", and Precedents/Dependents would trace the whole used range instead
        // of the cell the user actually selected.
        var trueSelection = SheetGrid.SelectedRange ?? new GridRange(range.Start, range.Start);
        var activeCell = trueSelection.Start;
        var searchRange = kind is GoToSpecialKind.CurrentRegion or GoToSpecialKind.Precedents or GoToSpecialKind.Dependents
            ? trueSelection
            : range;
        var matches = GoToSpecialService.Find(_workbook, sheet, searchRange, kind, activeCell, options);
        if (matches.Count == 0)
        {
            if (showEmptyMessage)
            {
                _messageService.ShowInfo(
                    UiText.Get("GoToSpecial_NoCellsFoundMessage"),
                    UiText.Get("GoToSpecial_GoToSpecial"));
            }

            return;
        }

        var compressedRanges = SelectionRangeService.CompressAddresses(matches);
        _selectionAnchor = matches[0];
        _selectionCursor = matches[0];
        SheetGrid.SelectedRange = new GridRange(matches[0], matches[0]);
        SheetGrid.SelectedRanges = compressedRanges;
        CellAddressBox.Text = compressedRanges.Count == 1
            ? FormatRangeReference(compressedRanges[0].Start, compressedRanges[0].End)
            : $"{matches.Count} cells";
        EnsureCellVisible(matches[0]);
        UpdateViewport();
        RefreshStatusBar();
    }

    private void ClearPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }
    private void ClearAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentSelectionRangesCommand(
                "Clear All",
                range,
                (sheetId, currentRange) =>
                {
                    return new CompositeWorkbookCommand(
                        "Clear All",
                        [
                            new ClearContentsCommand(sheetId, currentRange),
                            new ApplyStyleCommand(sheetId, currentRange, CellStyleDiffPlanner.ClearFormatsDiff()),
                            new ClearConditionalFormatsCommand(sheetId, currentRange),
                            new ClearDataValidationCommand(sheetId, currentRange),
                            new ClearCommentsCommand(sheetId, currentRange),
                            new ClearHyperlinksCommand(sheetId, currentRange)
                        ]);
                },
                out var outcome))
            return;

        UpdateViewport();
    }
    private void ClearFormatsMenuItem_Click(object sender, RoutedEventArgs e) => ClearFormats();
    private void ClearValuesMenuItem_Click(object sender, RoutedEventArgs e)  => ClearValues();
    private void ClearCommentsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentSelectionRangesCommand(
                "Clear Comments and Notes",
                range,
                (sheetId, currentRange) => new ClearCommentsCommand(sheetId, currentRange)))
            return;

        UpdateViewport();
    }

    /// <summary>
    /// Home&gt;Clear&gt;Clear Hyperlinks (ribbon command id "Clear Hyperlinks", see
    /// Ribbon/FreeXRibbonHandlerMap.g.cs) and the worksheet right-click Clear submenu's own
    /// "Clear Hyperlinks" entry (WorksheetContextMenuAction.ClearHyperlinks) both route here.
    /// Matching Excel, this strips the hyperlink's visible formatting (blue/underline) via
    /// RemoveHyperlinksCommand -- despite this method's historical "Clear" name, it is the
    /// format-STRIPPING handler. It keeps this exact name because the ribbon handler map is
    /// generated (from tools/ribgen.py + a pre-cutover XAML snapshot) and renaming it would
    /// require regenerating that map. The distinct format-PRESERVING behavior used by the
    /// right-click top-level "Remove Hyperlink" item lives in RemoveHyperlinkMenuItem_Click below.
    /// </summary>
    private void ClearHyperlinksMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentSelectionRangesCommand(
                "Clear Hyperlinks",
                range,
                (sheetId, currentRange) => new RemoveHyperlinksCommand(sheetId, currentRange)))
            return;
        UpdateViewport();
    }

    /// <summary>
    /// The worksheet right-click "Remove Hyperlink" top-level item
    /// (WorksheetContextMenuAction.RemoveHyperlinks) routes here. Matching Excel, this removes
    /// only the hyperlink target and preserves the cell's visible formatting (blue/underline) via
    /// ClearHyperlinksCommand; it does not strip formatting the way Home&gt;Clear&gt;Clear
    /// Hyperlinks (ClearHyperlinksMenuItem_Click above) does.
    /// </summary>
    private void RemoveHyperlinkMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentSelectionRangesCommand(
                "Remove Hyperlink",
                range,
                (sheetId, currentRange) => new ClearHyperlinksCommand(sheetId, currentRange)))
            return;
        UpdateViewport();
    }

    private void ClearValues()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentSelectionRangesCommand(
                "Clear Contents",
                range,
                (sheetId, currentRange) => new ClearContentsCommand(sheetId, currentRange),
                out var outcome))
            return;

        UpdateViewport();
    }
    /// <summary>
    /// Home&gt;Clear&gt;Clear Formats. Matching Excel (and this app's own Clear All), clearing formats
    /// also removes any conditional-formatting rules on the selection -- CF is itself a form of
    /// formatting, so a plain <see cref="CellStyleDiffPlanner.ClearFormatsDiff"/> style-only apply
    /// left stale CF rules behind (R66-commands-clear-delete-6-1). Composed the same way
    /// <see cref="ClearAllMenuItem_Click"/> combines <c>ApplyStyleCommand</c> with
    /// <c>ClearConditionalFormatsCommand</c>, minus the contents/validation/comments/hyperlinks
    /// clears that Clear All (but not Clear Formats) also performs.
    /// </summary>
    private void ClearFormats()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentSelectionRangesCommand(
                "Clear Formats",
                range,
                (sheetId, currentRange) => new CompositeWorkbookCommand(
                    "Clear Formats",
                    [
                        new ApplyStyleCommand(sheetId, currentRange, CellStyleDiffPlanner.ClearFormatsDiff()),
                        new ClearConditionalFormatsCommand(sheetId, currentRange)
                    ])))
            return;

        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }
}
