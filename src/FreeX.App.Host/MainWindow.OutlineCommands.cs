using System.Windows;
using FreeX.App.UI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    // ── Group / Ungroup handlers ─────────────────────────────────────────────

    private void GroupRowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand("Group", range, CreateGroupCommand))
            return;
        UpdateViewport();
    }

    private void GroupRowsMenuItem_Click(object sender, RoutedEventArgs e) => GroupRowsBtn_Click(sender, e);

    private void UngroupRowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Ungroup",
                range,
                currentRange => OutlineGroupingService.GetGroupingAxis(currentRange) == OutlineGroupingAxis.Columns
                    ? new GroupColumnsCommand(_currentSheetId, currentRange.Start.Col, currentRange.End.Col, 0)
                    : new GroupRowsCommand(_currentSheetId, currentRange.Start.Row, currentRange.End.Row, 0)))
            return;

        UpdateViewport();
    }

    private void UngroupRowsMenuItem_Click(object sender, RoutedEventArgs e) => UngroupRowsBtn_Click(sender, e);

    private void ClearOutlineMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteGroupedSheetCommand("Clear Outline", sheetId => new ClearWorksheetOutlineCommand(sheetId)))
            return;

        UpdateViewport();
    }

    private void CollapseGroupBtn_Click(object sender, RoutedEventArgs e)
    {
        IWorkbookCommand CreateCommand()
        {
            var range = SheetGrid.SelectedRange;
            var axis = range is { } r ? OutlineGroupingService.GetGroupingAxis(r) : OutlineGroupingAxis.Rows;
            if (axis == OutlineGroupingAxis.Columns)
                return new CollapseColGroupCommand(_currentSheetId, 1);

            return range is { } rowRange
                ? new CollapseRowGroupCommand(_currentSheetId, 1, rowRange.Start.Row, rowRange.End.Row)
                : new CollapseRowGroupCommand(_currentSheetId, 1);
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Collapse Group");
            return;
        }

        _repeatPostAction = null;
        UpdateViewport();
    }

    private void ExpandGroupBtn_Click(object sender, RoutedEventArgs e)
    {
        IWorkbookCommand CreateCommand()
        {
            var range = SheetGrid.SelectedRange;
            var axis = range is { } r ? OutlineGroupingService.GetGroupingAxis(r) : OutlineGroupingAxis.Rows;
            if (axis == OutlineGroupingAxis.Columns)
                return new ExpandColGroupCommand(_currentSheetId, 1);

            return range is { } rowRange
                ? new ExpandRowGroupCommand(_currentSheetId, 1, rowRange.Start.Row, rowRange.End.Row)
                : new ExpandRowGroupCommand(_currentSheetId, 1);
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Expand Group");
            return;
        }

        _repeatPostAction = null;
        UpdateViewport();
    }

    private void OnOutlineGroupToggleRequested(GridOutlineGroupToggleRequest request)
    {
        IWorkbookCommand CreateCommand() =>
            request.Axis == GridOutlineGroupAxis.Columns
                ? new SetColumnOutlineGroupCollapsedCommand(_currentSheetId, request.Start, request.End, request.Level, request.Collapse)
                : new SetRowOutlineGroupCollapsedCommand(_currentSheetId, request.Start, request.End, request.Level, request.Collapse);

        var label = request.Collapse ? "Collapse Group" : "Expand Group";
        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, label);
            return;
        }

        _repeatPostAction = null;
        UpdateViewport();
    }

    private IWorkbookCommand CreateGroupCommand(GridRange range)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return new GroupRowsCommand(_currentSheetId, range.Start.Row, range.End.Row, 1, preserveExistingHierarchy: true);

        if (OutlineGroupingService.GetGroupingAxis(range) == OutlineGroupingAxis.Columns)
        {
            int newLevel = OutlineGroupingPlanner.GetNextOutlineLevel(range.Start.Col, range.End.Col, sheet.ColOutlineLevels);
            return new GroupColumnsCommand(_currentSheetId, range.Start.Col, range.End.Col, newLevel, preserveExistingHierarchy: true);
        }

        int rowLevel = OutlineGroupingPlanner.GetNextOutlineLevel(range.Start.Row, range.End.Row, sheet.RowOutlineLevels);
        return new GroupRowsCommand(_currentSheetId, range.Start.Row, range.End.Row, rowLevel, preserveExistingHierarchy: true);
    }
}
