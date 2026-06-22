using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.TableUI;

/// <summary>
/// UI-free planning for creating structured tables. Shells own dialogs, prompts, and status text;
/// this planner owns source-range expansion and command construction so insert-table and
/// format-as-table behavior stays aligned across renderers.
/// </summary>
public static class TableCreationPlanner
{
    public static GridRange PlanSourceRange(Sheet sheet, GridRange selectedRange)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (IsValidExplicitTableRange(selectedRange))
            return selectedRange;

        return ExpandToCurrentRegion(sheet, selectedRange);
    }

    public static GridRange ExpandToCurrentRegion(Sheet sheet, GridRange seedRange)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (sheet.GetUsedRange() is not { } usedRange)
            return seedRange;

        if (!HasAnyValueInRange(sheet, seedRange) &&
            (HasBlankSeedRowBoundary(sheet, seedRange, usedRange) ||
             HasBlankSeedColumnBoundary(sheet, seedRange, usedRange)))
        {
            return seedRange;
        }

        var top = seedRange.Start.Row;
        var bottom = seedRange.End.Row;
        var left = seedRange.Start.Col;
        var right = seedRange.End.Col;

        var changed = true;
        while (changed)
        {
            changed = false;

            while (top > usedRange.Start.Row && HasAnyValueInRow(sheet, top - 1, left, right))
            {
                top--;
                changed = true;
            }

            while (bottom < usedRange.End.Row && HasAnyValueInRow(sheet, bottom + 1, left, right))
            {
                bottom++;
                changed = true;
            }

            while (left > usedRange.Start.Col && HasAnyValueInColumn(sheet, left - 1, top, bottom))
            {
                left--;
                changed = true;
            }

            while (right < usedRange.End.Col && HasAnyValueInColumn(sheet, right + 1, top, bottom))
            {
                right++;
                changed = true;
            }
        }

        return new GridRange(
            new CellAddress(seedRange.Start.Sheet, top, left),
            new CellAddress(seedRange.Start.Sheet, bottom, right));
    }

    public static bool HasCompleteHeaderRow(Sheet sheet, GridRange range)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        for (var col = range.Start.Col; col <= range.End.Col; col++)
        {
            if (IsBlank(sheet.GetValue(range.Start.Row, col)))
                return false;
        }

        return true;
    }

    public static CreateStructuredTableCommand BuildInsertCommand(
        SheetId sheetId,
        GridRange range,
        bool firstRowHasHeaders) =>
        new(sheetId, range, styleName: null, firstRowHasHeaders: firstRowHasHeaders);

    public static CreateStyledStructuredTableCommand BuildStyledCommand(
        SheetId sheetId,
        GridRange range,
        string? styleName,
        bool firstRowHasHeaders,
        StructuredTableStyleBanding banding) =>
        new(
            sheetId,
            range,
            styleName,
            firstRowHasHeaders,
            banding);

    public static bool IsBlank(ScalarValue value) =>
        value is BlankValue;

    private static bool IsValidExplicitTableRange(GridRange range) =>
        range.RowCount >= 2;

    private static bool HasAnyValueInRange(Sheet sheet, GridRange range)
    {
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            for (var col = range.Start.Col; col <= range.End.Col; col++)
            {
                if (!IsBlank(sheet.GetValue(row, col)))
                    return true;
            }
        }

        return false;
    }

    private static bool HasBlankSeedRowBoundary(Sheet sheet, GridRange seedRange, GridRange usedRange)
    {
        for (var row = seedRange.Start.Row; row <= seedRange.End.Row; row++)
        {
            if (!HasAnyValueInRow(sheet, row, usedRange.Start.Col, usedRange.End.Col))
                return true;
        }

        return false;
    }

    private static bool HasBlankSeedColumnBoundary(Sheet sheet, GridRange seedRange, GridRange usedRange)
    {
        for (var col = seedRange.Start.Col; col <= seedRange.End.Col; col++)
        {
            if (!HasAnyValueInColumn(sheet, col, usedRange.Start.Row, usedRange.End.Row))
                return true;
        }

        return false;
    }

    private static bool HasAnyValueInRow(Sheet sheet, uint row, uint startCol, uint endCol)
    {
        for (var col = startCol; col <= endCol; col++)
        {
            if (!IsBlank(sheet.GetValue(row, col)))
                return true;
        }

        return false;
    }

    private static bool HasAnyValueInColumn(Sheet sheet, uint col, uint startRow, uint endRow)
    {
        for (var row = startRow; row <= endRow; row++)
        {
            if (!IsBlank(sheet.GetValue(row, col)))
                return true;
        }

        return false;
    }
}
