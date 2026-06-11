using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public readonly record struct FlashFillCommandPlan(
    uint FillColumn,
    uint SourceColumn,
    uint StartRow,
    uint EndRow)
{
    public FlashFillCommand CreateCommand(SheetId sheetId) =>
        new FlashFillCommand(sheetId, FillColumn, SourceColumn, StartRow, EndRow);
}

public static class FlashFillRangePlanner
{
    public static FlashFillCommandPlan Plan(Sheet sheet, GridRange range)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var fillColumn = range.Start.Col;
        var sourceColumn = fillColumn > 1 ? fillColumn - 1 : fillColumn + 1;
        var selectedColumnPlan = CreatePlan(sheet, range, fillColumn, sourceColumn);

        if (HasFillTargets(sheet, selectedColumnPlan))
            return selectedColumnPlan;

        return FindAdjacentTargetPlan(sheet, range, selectedColumnPlan) ?? selectedColumnPlan;
    }

    public static bool HasFillTargets(Sheet sheet, FlashFillCommandPlan plan)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        for (var row = plan.StartRow; row <= plan.EndRow; row++)
        {
            if (IsBlankForFlashFill(sheet.GetValue(row, plan.FillColumn)) &&
                HasValue(sheet, row, plan.SourceColumn))
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasExamples(Sheet sheet, FlashFillCommandPlan plan)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        for (var row = plan.StartRow; row <= plan.EndRow; row++)
        {
            if (HasValue(sheet, row, plan.FillColumn))
                return true;
        }

        return false;
    }

    private static uint FindContiguousExampleStart(
        Sheet sheet,
        uint fillColumn,
        uint sourceColumn,
        uint selectedRow)
    {
        var startRow = selectedRow;
        while (startRow > 1)
        {
            var previousRow = startRow - 1;
            if (!HasValue(sheet, previousRow, fillColumn) || !HasValue(sheet, previousRow, sourceColumn))
                break;

            startRow = previousRow;
        }

        return startRow;
    }

    private static uint FindAdjacentDataEnd(
        Sheet sheet,
        uint fillColumn,
        uint sourceColumn,
        uint selectedRow)
    {
        var endRow = selectedRow;
        for (var row = selectedRow + 1; row <= CellAddress.MaxRow; row++)
        {
            if (!HasValue(sheet, row, fillColumn) && !HasValue(sheet, row, sourceColumn))
                break;

            endRow = row;
        }

        return endRow;
    }

    private static bool HasValue(Sheet sheet, uint row, uint column) =>
        !IsBlankForFlashFill(sheet.GetValue(row, column));

    private static bool IsBlankForFlashFill(ScalarValue value) =>
        value is BlankValue || value is TextValue { Value: "" };

    private static FlashFillCommandPlan CreatePlan(
        Sheet sheet,
        GridRange range,
        uint fillColumn,
        uint sourceColumn)
    {
        var startRow = range.Start.Row;
        var endRow = range.End.Row;

        if (startRow == endRow)
        {
            startRow = FindContiguousExampleStart(sheet, fillColumn, sourceColumn, startRow);
            endRow = FindAdjacentDataEnd(sheet, fillColumn, sourceColumn, endRow);
        }

        return new FlashFillCommandPlan(fillColumn, sourceColumn, startRow, endRow);
    }

    private static FlashFillCommandPlan? FindAdjacentTargetPlan(
        Sheet sheet,
        GridRange range,
        FlashFillCommandPlan selectedColumnPlan)
    {
        if (range.Start.Col != range.End.Col)
            return null;

        var selectedColumn = range.Start.Col;
        var candidates = GetAdjacentTargetColumns(selectedColumn)
            .Select(fillColumn => CreatePlan(sheet, range, fillColumn, selectedColumn))
            .ToList();

        var exampleBackedTarget = candidates.FirstOrDefault(plan =>
            HasExamples(sheet, plan) && HasFillTargets(sheet, plan));
        if (exampleBackedTarget != default)
            return exampleBackedTarget;

        if (HasAnySourceValue(sheet, selectedColumnPlan))
            return null;

        var blankTarget = candidates.FirstOrDefault(plan => HasFillTargets(sheet, plan));
        return blankTarget == default ? null : blankTarget;
    }

    private static IEnumerable<uint> GetAdjacentTargetColumns(uint sourceColumn)
    {
        if (sourceColumn < CellAddress.MaxCol)
            yield return sourceColumn + 1;

        if (sourceColumn > 1)
            yield return sourceColumn - 1;
    }

    private static bool HasAnySourceValue(Sheet sheet, FlashFillCommandPlan plan)
    {
        for (var row = plan.StartRow; row <= plan.EndRow; row++)
        {
            if (HasValue(sheet, row, plan.SourceColumn))
                return true;
        }

        return false;
    }
}
