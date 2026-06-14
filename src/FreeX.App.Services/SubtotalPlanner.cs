using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class SubtotalPlanner
{
    public const string NoOccupiedDataMessage = "Select a data range with headers and rows before using Subtotal.";
    public const string NotEnoughRowsMessage = "Subtotal requires a header row and at least one data row.";
    public const string NotEnoughColumnsMessage = "Subtotal requires at least one grouping column and one subtotal column.";

    public static GridRange ExpandRangeForInsertedSubtotalRows(
        GridRange sourceRange,
        IReadOnlyList<CellAddress>? affectedCells)
    {
        if (affectedCells is null || affectedCells.Count == 0)
            return sourceRange;

        var insertedRows = affectedCells
            .Where(address => address.Sheet == sourceRange.Start.Sheet)
            .Select(address => address.Row)
            .Distinct()
            .Count();
        if (insertedRows == 0)
            return sourceRange;

        var expandedEndRow = (uint)Math.Min(
            (ulong)CellAddress.MaxRow,
            (ulong)sourceRange.End.Row + (ulong)insertedRows);

        return new GridRange(
            sourceRange.Start,
            new CellAddress(
                sourceRange.End.Sheet,
                expandedEndRow,
                sourceRange.End.Col));
    }

    public static bool TryCreateSourceRange(
        Sheet sheet,
        GridRange selectedRange,
        out GridRange sourceRange,
        out string? error,
        bool requireCompleteTableShape = true)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var shouldTrimToUsedRange = ShouldTrimToUsedRange(selectedRange);
        if (shouldTrimToUsedRange)
        {
            var usedRange = sheet.GetUsedRange();
            if (usedRange is null ||
                !TryIntersect(selectedRange, usedRange.Value, out sourceRange))
            {
                error = NoOccupiedDataMessage;
                sourceRange = selectedRange;
                return false;
            }
        }
        else
        {
            sourceRange = selectedRange;
        }

        if (requireCompleteTableShape && sourceRange.RowCount < 2)
        {
            error = NotEnoughRowsMessage;
            return false;
        }

        if (requireCompleteTableShape && sourceRange.ColCount < 2)
        {
            error = NotEnoughColumnsMessage;
            return false;
        }

        error = null;
        return true;
    }

    public static GridRange NormalizeSourceRange(Sheet sheet, GridRange selectedRange)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (!ShouldTrimToUsedRange(selectedRange))
            return selectedRange;

        var usedRange = sheet.GetUsedRange();
        return usedRange is not null && TryIntersect(selectedRange, usedRange.Value, out var sourceRange)
            ? sourceRange
            : selectedRange;
    }

    private static bool ShouldTrimToUsedRange(GridRange range) =>
        SelectionRangeService.IsWholeColumnSelection(range) ||
        SelectionRangeService.IsWholeRowSelection(range) ||
        range.End.Row == CellAddress.MaxRow ||
        range.End.Col == CellAddress.MaxCol;

    private static bool TryIntersect(GridRange left, GridRange right, out GridRange intersection)
    {
        if (left.Start.Sheet != right.Start.Sheet)
        {
            intersection = default;
            return false;
        }

        var startRow = Math.Max(left.Start.Row, right.Start.Row);
        var endRow = Math.Min(left.End.Row, right.End.Row);
        var startCol = Math.Max(left.Start.Col, right.Start.Col);
        var endCol = Math.Min(left.End.Col, right.End.Col);

        if (startRow > endRow || startCol > endCol)
        {
            intersection = default;
            return false;
        }

        intersection = new GridRange(
            new CellAddress(left.Start.Sheet, startRow, startCol),
            new CellAddress(left.Start.Sheet, endRow, endCol));
        return true;
    }
}
