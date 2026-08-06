using System.Linq;
using System.Windows;
using FreeX.App.UI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    // ── Group / Ungroup handlers ─────────────────────────────────────────────

    // R124-outlinecmds-multiarea-group-1: a Ctrl+click multi-area row/column header selection
    // must group/ungroup EVERY disjoint area, not just the active (last-clicked) one -- Excel
    // groups all selected areas in a single Group/Ungroup action. Route through
    // TryExecuteRepeatableCurrentRangesCommand (built for this fix), which iterates
    // SheetGrid.SelectedRanges the same way GetCurrentSelectionRanges already does for the
    // AutoFit Row Height/Column Width multi-area fix, instead of reading only the single active
    // SheetGrid.SelectedRange.
    private void GroupRowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangesCommand("Group", range, CreateGroupCommand))
            return;
        UpdateViewport();
    }

    private void GroupRowsMenuItem_Click(object sender, RoutedEventArgs e) => GroupRowsBtn_Click(sender, e);

    private void UngroupRowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangesCommand("Ungroup", range, CreateUngroupCommand))
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
            if (range is { } columnRange && axis == OutlineGroupingAxis.Columns)
                return new CollapseColGroupCommand(
                    _currentSheetId,
                    1,
                    columnRange.Start.Col,
                    columnRange.End.Col);

            return range is { } rowRange
                ? new CollapseRowGroupCommand(_currentSheetId, 1, rowRange.Start.Row, rowRange.End.Row)
                : new CollapseRowGroupCommand(_currentSheetId, 1);
        }

        if (!TryExecuteRepeatableCommand(CreateCommand, "Collapse Group", out _))
            return;

        UpdateViewport();
    }

    private void ExpandGroupBtn_Click(object sender, RoutedEventArgs e)
    {
        IWorkbookCommand CreateCommand()
        {
            var range = SheetGrid.SelectedRange;
            var axis = range is { } r ? OutlineGroupingService.GetGroupingAxis(r) : OutlineGroupingAxis.Rows;
            if (range is { } columnRange && axis == OutlineGroupingAxis.Columns)
                return new ExpandColGroupCommand(
                    _currentSheetId,
                    1,
                    columnRange.Start.Col,
                    columnRange.End.Col);

            return range is { } rowRange
                ? new ExpandRowGroupCommand(_currentSheetId, 1, rowRange.Start.Row, rowRange.End.Row)
                : new ExpandRowGroupCommand(_currentSheetId, 1);
        }

        if (!TryExecuteRepeatableCommand(CreateCommand, "Expand Group", out _))
            return;

        UpdateViewport();
    }

    private void OnOutlineGroupToggleRequested(GridOutlineGroupToggleRequest request)
    {
        IWorkbookCommand CreateCommand() =>
            request.Axis == GridOutlineGroupAxis.Columns
                ? new SetColumnOutlineGroupCollapsedCommand(_currentSheetId, request.Start, request.End, request.Level, request.Collapse)
                : new SetRowOutlineGroupCollapsedCommand(_currentSheetId, request.Start, request.End, request.Level, request.Collapse);

        var label = request.Collapse ? "Collapse Group" : "Expand Group";
        if (!TryExecuteRepeatableCommand(CreateCommand, label, out _))
            return;

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

    // Excel's Ungroup decrements EACH row/column's OWN outline level by exactly one (clamped at 0)
    // -- it never force-sets the whole selection to one uniform level. A selection that straddles
    // several distinct nesting depths (e.g. some rows only ever grouped at level 1, others nested
    // to level 2 or 3) must have each row's own level dropped by one; shallower rows must never be
    // bumped UP to match the deepest row found in the selection (R49-commands-outline-group-3-2).
    // We split the selection into contiguous runs that currently share the same existing level and
    // call the shared OutlineGroupingPlanner.GetUngroupedOutlineLevel once PER RUN (every row in a
    // run has the identical source level, so that call's own "max existing level in this sub-range,
    // minus one" computation is exactly that run's decremented target -- this keeps FreeX.App.Host
    // and FreeX.App.Avalonia sharing the same GetUngroupedOutlineLevel arithmetic instead of each
    // reimplementing it, matching FreeXBehaviorDedupSourceBoundaryTests.
    // OutlineAndDiagnosticsPolicies_AreSharedAcrossShells), combined into one undo step.
    private IWorkbookCommand CreateUngroupCommand(GridRange range)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return new GroupRowsCommand(_currentSheetId, range.Start.Row, range.End.Row, 0);

        if (OutlineGroupingService.GetGroupingAxis(range) == OutlineGroupingAxis.Columns)
        {
            var colRuns = GetContiguousSameLevelRuns(range.Start.Col, range.End.Col, sheet.ColOutlineLevels);
            var colCommands = colRuns
                .Select(run => (IWorkbookCommand)new GroupColumnsCommand(
                    _currentSheetId,
                    run.Start,
                    run.End,
                    OutlineGroupingPlanner.GetUngroupedOutlineLevel(run.Start, run.End, sheet.ColOutlineLevels)))
                .ToList();
            return new CompositeWorkbookCommand("Ungroup", colCommands);
        }

        var rowRuns = GetContiguousSameLevelRuns(range.Start.Row, range.End.Row, sheet.RowOutlineLevels);
        var rowCommands = rowRuns
            .Select(run => (IWorkbookCommand)new GroupRowsCommand(
                _currentSheetId,
                run.Start,
                run.End,
                OutlineGroupingPlanner.GetUngroupedOutlineLevel(run.Start, run.End, sheet.RowOutlineLevels)))
            .ToList();
        return new CompositeWorkbookCommand("Ungroup", rowCommands);
    }

    // Splits [start, end] into contiguous runs of indices that currently share the same outline
    // level. Indices with no level / level 0 are excluded entirely (they have nothing to decrement
    // and must stay untouched).
    private static List<(uint Start, uint End)> GetContiguousSameLevelRuns(
        uint start, uint end, IReadOnlyDictionary<uint, int> outlineLevels)
    {
        var runs = new List<(uint Start, uint End)>();
        uint? runStart = null;
        int runLevel = 0;
        for (var index = start; index <= end; index++)
        {
            outlineLevels.TryGetValue(index, out var level);
            if (level <= 0)
            {
                if (runStart is { } pendingStart)
                {
                    runs.Add((pendingStart, index - 1));
                    runStart = null;
                }
                continue;
            }

            if (runStart is null)
            {
                runStart = index;
                runLevel = level;
            }
            else if (level != runLevel)
            {
                runs.Add((runStart.Value, index - 1));
                runStart = index;
                runLevel = level;
            }
        }

        if (runStart is { } finalStart)
            runs.Add((finalStart, end));

        return runs;
    }
}
