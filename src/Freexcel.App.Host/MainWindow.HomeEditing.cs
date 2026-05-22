using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Freexcel.Core.Calc;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    // ── Editing group (pickers) ──────────────────────────────────────────────

    private void AutoSumPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
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
                    var addr = currentRange.Start;
                    var formula = AutoSumFormulaPlanner.BuildFormula(_workbook.GetSheet(_currentSheetId), func, addr);
                    var edits = new List<(CellAddress Address, Cell NewCell)> { (addr, Cell.FromFormula(formula)) };
                    var targetSheetIds = CurrentGroupedEditSheetIds();
                    return targetSheetIds.Count > 1
                        ? new GroupedEditCellsCommand(targetSheetIds, _currentSheetId, edits)
                        : new EditCellsCommand(_currentSheetId, edits);
                },
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? [range.Start]);
        SetActiveCell(new CellAddress(_currentSheetId, range.Start.Row + 1, range.Start.Col));
        UpdateViewport();
    }

    private void AutoSumSumMenuItem_Click(object sender, RoutedEventArgs e)   => InsertAutoSumFormula("SUM");
    private void AutoSumAvgMenuItem_Click(object sender, RoutedEventArgs e)   => InsertAutoSumFormula("AVERAGE");
    private void AutoSumCountMenuItem_Click(object sender, RoutedEventArgs e) => InsertAutoSumFormula("COUNT");
    private void AutoSumMaxMenuItem_Click(object sender, RoutedEventArgs e)   => InsertAutoSumFormula("MAX");
    private void AutoSumMinMenuItem_Click(object sender, RoutedEventArgs e)   => InsertAutoSumFormula("MIN");
    private void AutoSumMoreMenuItem_Click(object sender, RoutedEventArgs e)  => InsertFunctionBtn_Click(sender, e);

    private void FillPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
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

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
    }

    private void FillSeriesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // Basic: fill a linear series starting from selected cell
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId); if (sheet is null) return;
        var startVal = sheet.GetValue(range.Start.Row, range.Start.Col) as NumberValue;
        if (startVal is null) { MessageBox.Show("Select a cell with a numeric value to start a series."); return; }
        var dialog = new FillSeriesStepDialog { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        var step = dialog.Result.Step;

        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Fill Series",
                range,
                currentRange =>
                {
                    var currentSheet = _workbook.GetSheet(_currentSheetId);
                    List<(CellAddress Address, Cell NewCell)> edits = currentSheet is null
                        ? []
                        : FillSeriesPlanner.BuildLinearSeriesEdits(currentSheet, currentRange, dialog.Result.Step);
                    var targetSheetIds = CurrentGroupedEditSheetIds();
                    return targetSheetIds.Count > 1
                        ? new GroupedEditCellsCommand(targetSheetIds, _currentSheetId, edits)
                        : new EditCellsCommand(_currentSheetId, edits);
                },
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
    }

    private void FlashFillMenuItem_Click(object sender, RoutedEventArgs e) => TryFlashFill();

    private void TryFlashFill()
    {
        var range = SheetGrid.SelectedRange;
        if (range is null) return;

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Flash Fill",
                range.Value,
                currentRange => CreateFlashFillCommand(sheet, currentRange),
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
    }

    private FlashFillCommand CreateFlashFillCommand(Sheet sheet, GridRange range)
    {
        uint fillCol = range.Start.Col;
        uint sourceCol = fillCol > 1 ? fillCol - 1 : fillCol + 1;
        uint startRow = range.Start.Row;
        uint endRow = range.End.Row;

        if (startRow == endRow)
        {
            uint maxRow = startRow;
            for (uint r = startRow + 1; r <= CellAddress.MaxRow; r++)
            {
                var fillVal = sheet.GetValue(r, fillCol);
                var srcVal = sheet.GetValue(r, sourceCol);
                if (fillVal is BlankValue && srcVal is BlankValue)
                    break;
                maxRow = r;
            }
            endRow = maxRow;
        }

        return new FlashFillCommand(_currentSheetId, fillCol, sourceCol, startRow, endRow);
    }

    private void SortFilterPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void SortAZMenuItem_Click(object sender, RoutedEventArgs e)    => SortAscButton_Click(sender, e);
    private void SortZAMenuItem_Click(object sender, RoutedEventArgs e)    => SortDescButton_Click(sender, e);
    private void SortCustomMenuItem_Click(object sender, RoutedEventArgs e) => SortCustomButton_Click(sender, e);
    private void FilterToggleMenuItem_Click(object sender, RoutedEventArgs e) => FilterButton_Click(sender, e);
    private void FilterClearMenuItem_Click(object sender, RoutedEventArgs e)  => ClearFilterButton_Click(sender, e);
    private void FilterReapplyMenuItem_Click(object sender, RoutedEventArgs e) => FilterButton_Click(sender, e);

    private void FindSelectPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void FindFindMenuItem_Click(object sender, RoutedEventArgs e)       => FindButton_Click(sender, e);
    private void FindReplaceMenuItem_Click(object sender, RoutedEventArgs e)    => ReplaceButton_Click(sender, e);
    private void FindGoToMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var defaultAddress = SheetGrid.SelectedRange?.Start.ToA1() ?? "A1";
        var dialog = new GoToDialog(_currentSheetId, defaultAddress, _workbook.NamedRanges) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        if (dialog.SelectedSpecialKind is { } specialKind)
        {
            SelectGoToSpecialMatches(specialKind, showEmptyMessage: true);
            return;
        }

        SetActiveCell(dialog.SelectedAddress);
        EnsureCellVisible(dialog.SelectedAddress);
    }
    private void FindGoToSpecialMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        var range = SheetGrid.SelectedRange ?? sheet.GetUsedRange() ??
            new GridRange(new CellAddress(_currentSheetId, 1, 1), new CellAddress(_currentSheetId, 1, 1));
        var dialog = new GoToSpecialDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;

        SelectGoToSpecialMatches(dialog.SelectedKind, showEmptyMessage: true, sheet, range);
    }

    private void SelectGoToSpecialMatches(GoToSpecialKind kind, bool showEmptyMessage)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        var range = SheetGrid.SelectedRange ?? sheet.GetUsedRange() ??
            new GridRange(new CellAddress(_currentSheetId, 1, 1), new CellAddress(_currentSheetId, 1, 1));

        SelectGoToSpecialMatches(kind, showEmptyMessage, sheet, range);
    }

    private void SelectGoToSpecialMatches(GoToSpecialKind kind, bool showEmptyMessage, Sheet sheet, GridRange range)
    {
        var matches = GoToSpecialService.Find(sheet, range, kind, range.Start);
        if (matches.Count == 0)
        {
            if (showEmptyMessage)
                MessageBox.Show("No cells found.", "Go To Special", MessageBoxButton.OK, MessageBoxImage.Information);
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
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void ClearAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Clear All",
                sheetId =>
                {
                    var currentRange = GroupedSheetRangePlanner.RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId);
                    return new CompositeWorkbookCommand(
                        "Clear All",
                        [
                            new ClearContentsCommand(sheetId, currentRange),
                            new ApplyStyleCommand(sheetId, currentRange, CellStyleDiffPlanner.ClearFormatsDiff())
                        ]);
                },
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
    }
    private void ClearFormatsMenuItem_Click(object sender, RoutedEventArgs e) => ClearFormats();
    private void ClearValuesMenuItem_Click(object sender, RoutedEventArgs e)  => ClearValues();
    private void ClearCommentsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Clear Comments",
                range,
                currentRange => new ClearCommentsCommand(_currentSheetId, currentRange)))
            return;

        UpdateViewport();
    }

    private void ClearHyperlinksMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Clear Hyperlinks",
                range,
                currentRange => new ClearHyperlinksCommand(_currentSheetId, currentRange)))
            return;
        UpdateViewport();
    }

    private void ClearValues()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Clear Contents",
                sheetId => new ClearContentsCommand(sheetId, GroupedSheetRangePlanner.RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId)),
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
    }
    private void ClearFormats()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        ApplyStyleDiff(CellStyleDiffPlanner.ClearFormatsDiff());
    }
}
