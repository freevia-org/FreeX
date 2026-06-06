using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class SelectionStyleCommandPlanner
{
    public static IReadOnlyList<GridRange> ResolveRanges(
        GridRange? selectedRange,
        IReadOnlyList<GridRange>? selectedRanges)
    {
        if (selectedRanges is { Count: > 0 })
            return MergeCompleteRectangularBands(selectedRanges);

        return selectedRange is { } range ? [range] : [];
    }

    public static IWorkbookCommand CreateApplyStyleCommand(
        IReadOnlyList<SheetId> targetSheetIds,
        IReadOnlyList<GridRange> ranges,
        StyleDiff diff,
        string title)
    {
        if (targetSheetIds.Count == 0 || ranges.Count == 0)
            return ToCommand(title, []);

        var commands = new List<IWorkbookCommand>(ranges.Count);
        foreach (var range in ranges)
        {
            commands.Add(targetSheetIds.Count > 1
                ? new GroupedApplyStyleCommand(targetSheetIds, range, diff)
                : new ApplyStyleCommand(
                    targetSheetIds[0],
                    GroupedSheetRangePlanner.RemapRangeToSheet(range, targetSheetIds[0]),
                    diff));
        }

        return ToCommand(title, commands);
    }

    public static IWorkbookCommand CreatePerCellStyleCommand(
        IReadOnlyList<SheetId> targetSheetIds,
        IReadOnlyList<GridRange> ranges,
        Func<GridRange, CellAddress, StyleDiff> createDiff,
        string title)
    {
        if (targetSheetIds.Count == 0 || ranges.Count == 0)
            return ToCommand(title, []);

        var commands = new List<IWorkbookCommand>();
        foreach (var range in ranges)
            commands.AddRange(CreatePerCellStyleCommands(targetSheetIds, range, createDiff));

        return ToCommand(title, commands);
    }

    public static IWorkbookCommand CreateRangeCommand(
        IReadOnlyList<SheetId> targetSheetIds,
        IReadOnlyList<GridRange> ranges,
        Func<SheetId, GridRange, IWorkbookCommand> createCommand,
        string title)
    {
        if (targetSheetIds.Count == 0 || ranges.Count == 0)
            return ToCommand(title, []);

        var commands = new List<IWorkbookCommand>(targetSheetIds.Count * ranges.Count);
        foreach (var sheetId in targetSheetIds)
        {
            foreach (var range in ranges)
                commands.Add(createCommand(sheetId, GroupedSheetRangePlanner.RemapRangeToSheet(range, sheetId)));
        }

        return ToCommand(title, commands);
    }

    private static IReadOnlyList<IWorkbookCommand> CreatePerCellStyleCommands(
        IReadOnlyList<SheetId> targetSheetIds,
        GridRange range,
        Func<GridRange, CellAddress, StyleDiff> createDiff)
    {
        return range
            .AllCells()
            .Select(address => (Address: address, Diff: createDiff(range, address)))
            .Where(plan => BorderShortcutService.HasBorderChanges(plan.Diff))
            .Select(plan => CreateCellStyleCommand(targetSheetIds, plan.Address, plan.Diff))
            .ToList();
    }

    private static IWorkbookCommand CreateCellStyleCommand(
        IReadOnlyList<SheetId> targetSheetIds,
        CellAddress sourceAddress,
        StyleDiff diff)
    {
        var sourceRange = new GridRange(sourceAddress, sourceAddress);
        if (targetSheetIds.Count > 1)
            return new GroupedApplyStyleCommand(targetSheetIds, sourceRange, diff);

        var sheetId = targetSheetIds[0];
        var targetAddress = new CellAddress(sheetId, sourceAddress.Row, sourceAddress.Col);
        return new ApplyStyleCommand(sheetId, new GridRange(targetAddress, targetAddress), diff);
    }

    private static IWorkbookCommand ToCommand(string title, IReadOnlyList<IWorkbookCommand> commands) =>
        commands.Count == 1
            ? commands[0]
            : new CompositeWorkbookCommand(title, commands);

    private static IReadOnlyList<GridRange> MergeCompleteRectangularBands(IReadOnlyList<GridRange> ranges)
    {
        if (ranges.Count <= 1)
            return ranges;

        var firstSheet = ranges[0].Start.Sheet;
        if (ranges.Any(range => range.Start.Sheet != firstSheet))
            return ranges;

        var boundingRange = new GridRange(
            new CellAddress(firstSheet, ranges.Min(range => range.Start.Row), ranges.Min(range => range.Start.Col)),
            new CellAddress(firstSheet, ranges.Max(range => range.End.Row), ranges.Max(range => range.End.Col)));

        if (CanMergeRowBands(ranges, boundingRange) ||
            CanMergeColumnBands(ranges, boundingRange))
        {
            return [boundingRange];
        }

        return ranges;
    }

    private static bool CanMergeRowBands(IReadOnlyList<GridRange> ranges, GridRange boundingRange)
    {
        if (ranges.Any(range =>
                range.Start.Col != boundingRange.Start.Col ||
                range.End.Col != boundingRange.End.Col))
        {
            return false;
        }

        var sorted = ranges.OrderBy(range => range.Start.Row).ToList();
        var expectedStartRow = boundingRange.Start.Row;
        for (var i = 0; i < sorted.Count; i++)
        {
            var range = sorted[i];
            if (range.Start.Row != expectedStartRow)
                return false;

            if (range.End.Row == boundingRange.End.Row)
                return i == sorted.Count - 1;

            expectedStartRow = range.End.Row + 1;
        }

        return false;
    }

    private static bool CanMergeColumnBands(IReadOnlyList<GridRange> ranges, GridRange boundingRange)
    {
        if (ranges.Any(range =>
                range.Start.Row != boundingRange.Start.Row ||
                range.End.Row != boundingRange.End.Row))
        {
            return false;
        }

        var sorted = ranges.OrderBy(range => range.Start.Col).ToList();
        var expectedStartCol = boundingRange.Start.Col;
        for (var i = 0; i < sorted.Count; i++)
        {
            var range = sorted[i];
            if (range.Start.Col != expectedStartCol)
                return false;

            if (range.End.Col == boundingRange.End.Col)
                return i == sorted.Count - 1;

            expectedStartCol = range.End.Col + 1;
        }

        return false;
    }
}
