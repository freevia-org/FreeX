using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation;

public static class WorksheetStructureCommandPlanner
{
    public static IWorkbookCommand CreateGroupCommand(Sheet sheet, GridRange range)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (OutlineGroupingService.GetGroupingAxis(range) == OutlineGroupingAxis.Columns)
        {
            var level = OutlineGroupingPlanner.GetNextOutlineLevel(
                range.Start.Col,
                range.End.Col,
                sheet.ColOutlineLevels);
            return new GroupColumnsCommand(
                sheet.Id,
                range.Start.Col,
                range.End.Col,
                level,
                preserveExistingHierarchy: true);
        }

        var rowLevel = OutlineGroupingPlanner.GetNextOutlineLevel(
            range.Start.Row,
            range.End.Row,
            sheet.RowOutlineLevels);
        return new GroupRowsCommand(
            sheet.Id,
            range.Start.Row,
            range.End.Row,
            rowLevel,
            preserveExistingHierarchy: true);
    }

    public static IWorkbookCommand CreateUngroupCommand(Sheet sheet, GridRange range)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (OutlineGroupingService.GetGroupingAxis(range) == OutlineGroupingAxis.Columns)
        {
            var commands = GetContiguousSameLevelRuns(
                    range.Start.Col,
                    range.End.Col,
                    sheet.ColOutlineLevels)
                .Select(run => (IWorkbookCommand)new GroupColumnsCommand(
                    sheet.Id,
                    run.Start,
                    run.End,
                    OutlineGroupingPlanner.GetUngroupedOutlineLevel(
                        run.Start,
                        run.End,
                        sheet.ColOutlineLevels)))
                .ToList();
            return new CompositeWorkbookCommand("Ungroup", commands);
        }

        var rowCommands = GetContiguousSameLevelRuns(
                range.Start.Row,
                range.End.Row,
                sheet.RowOutlineLevels)
            .Select(run => (IWorkbookCommand)new GroupRowsCommand(
                sheet.Id,
                run.Start,
                run.End,
                OutlineGroupingPlanner.GetUngroupedOutlineLevel(
                    run.Start,
                    run.End,
                    sheet.RowOutlineLevels)))
            .ToList();
        return new CompositeWorkbookCommand("Ungroup", rowCommands);
    }

    public static IWorkbookCommand CreateSelectedOutlineVisibilityCommand(
        Sheet sheet,
        GridRange range,
        bool collapse)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (OutlineGroupingService.GetGroupingAxis(range) == OutlineGroupingAxis.Columns)
        {
            return collapse
                ? new CollapseColGroupCommand(sheet.Id, 1, range.Start.Col, range.End.Col)
                : new ExpandColGroupCommand(sheet.Id, 1, range.Start.Col, range.End.Col);
        }

        return collapse
            ? new CollapseRowGroupCommand(sheet.Id, 1, range.Start.Row, range.End.Row)
            : new ExpandRowGroupCommand(sheet.Id, 1, range.Start.Row, range.End.Row);
    }

    public static IWorkbookCommand CreateOutlineGroupToggleCommand(
        SheetId sheetId,
        OutlineGroupingAxis axis,
        uint start,
        uint end,
        int level,
        bool collapse) =>
        axis == OutlineGroupingAxis.Columns
            ? new SetColumnOutlineGroupCollapsedCommand(sheetId, start, end, level, collapse)
            : new SetRowOutlineGroupCollapsedCommand(sheetId, start, end, level, collapse);

    public static (uint? SplitRow, uint? SplitColumn) ResolveSplitTarget(
        uint activeRow,
        uint activeColumn,
        bool wasSplit,
        IReadOnlyList<RowMetric>? viewportRows = null,
        IReadOnlyList<ColMetric>? viewportColumns = null)
    {
        if (wasSplit)
            return (null, null);

        uint? splitRow = activeRow > 1 ? activeRow : null;
        uint? splitColumn = activeColumn > 1 ? activeColumn : null;
        if (splitRow is null && splitColumn is null)
        {
            if (viewportRows is { Count: > 1 })
                splitRow = viewportRows[viewportRows.Count / 2].Row;
            if (viewportColumns is { Count: > 1 })
                splitColumn = viewportColumns[viewportColumns.Count / 2].Col;
        }

        return (splitRow, splitColumn);
    }

    public static IReadOnlyList<(uint Start, uint End)> GetContiguousSameLevelRuns(
        uint start,
        uint end,
        IReadOnlyDictionary<uint, int> outlineLevels)
    {
        var runs = new List<(uint Start, uint End)>();
        uint? runStart = null;
        var runLevel = 0;
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
