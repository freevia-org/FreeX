using System.Windows;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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

    private void CollapseGroupBtn_Click(object sender, RoutedEventArgs e)
    {
        IWorkbookCommand CreateCommand()
        {
            var axis = SheetGrid.SelectedRange is { } range
                ? OutlineGroupingService.GetGroupingAxis(range)
                : OutlineGroupingAxis.Rows;
            return axis == OutlineGroupingAxis.Columns
                ? new CollapseColGroupCommand(_currentSheetId, 1)
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
            var axis = SheetGrid.SelectedRange is { } range
                ? OutlineGroupingService.GetGroupingAxis(range)
                : OutlineGroupingAxis.Rows;
            return axis == OutlineGroupingAxis.Columns
                ? new ExpandColGroupCommand(_currentSheetId, 1)
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

    private IWorkbookCommand CreateGroupCommand(GridRange range)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return new GroupRowsCommand(_currentSheetId, range.Start.Row, range.End.Row, 1);

        if (OutlineGroupingService.GetGroupingAxis(range) == OutlineGroupingAxis.Columns)
        {
            int newLevel = OutlineGroupingPlanner.GetNextOutlineLevel(range.Start.Col, range.End.Col, sheet.ColOutlineLevels);
            return new GroupColumnsCommand(_currentSheetId, range.Start.Col, range.End.Col, newLevel);
        }

        int rowLevel = OutlineGroupingPlanner.GetNextOutlineLevel(range.Start.Row, range.End.Row, sheet.RowOutlineLevels);
        return new GroupRowsCommand(_currentSheetId, range.Start.Row, range.End.Row, rowLevel);
    }
}
