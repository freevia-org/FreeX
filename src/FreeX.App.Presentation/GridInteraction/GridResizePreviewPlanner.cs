using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.GridInteraction;

public enum GridResizeAxis
{
    Column,
    Row
}

public sealed record GridResizePreviewSnapshot(
    SheetId SheetId,
    GridResizeAxis Axis,
    uint StartIndex,
    uint EndIndex,
    IReadOnlyDictionary<uint, double> OriginalSizes,
    IReadOnlySet<uint> OriginalHiddenIndexes);

/// <summary>
/// Platform-neutral resize-preview policy for worksheet row and column header drags.
/// Hosts decide when to refresh/render; this planner owns range choice, preview mutation,
/// and restoration of the original sheet state before the committed command runs.
/// </summary>
public static class GridResizePreviewPlanner
{
    public static (uint Start, uint End) GetColumnResizeRange(Sheet sheet, GridRange? selectedRange, uint column) =>
        sheet.HiddenCols.Contains(column)
            ? GetContiguousHiddenRange(sheet.HiddenCols, column, CellAddress.MaxCol)
            : GetSelectedColumnResizeRange(selectedRange, column);

    public static (uint Start, uint End) GetRowResizeRange(Sheet sheet, GridRange? selectedRange, uint row) =>
        sheet.HiddenRows.Contains(row)
            ? GetContiguousHiddenRange(sheet.HiddenRows, row, CellAddress.MaxRow)
            : GetSelectedRowResizeRange(selectedRange, row);

    public static (uint Start, uint End) GetSelectedColumnResizeRange(GridRange? selectedRange, uint column)
    {
        if (selectedRange is { } range &&
            range.RowCount == CellAddress.MaxRow &&
            column >= range.Start.Col &&
            column <= range.End.Col &&
            range.Start.Col != range.End.Col)
        {
            return (range.Start.Col, range.End.Col);
        }

        return (column, column);
    }

    public static (uint Start, uint End) GetSelectedRowResizeRange(GridRange? selectedRange, uint row)
    {
        if (selectedRange is { } range &&
            range.ColCount == CellAddress.MaxCol &&
            row >= range.Start.Row &&
            row <= range.End.Row &&
            range.Start.Row != range.End.Row)
        {
            return (range.Start.Row, range.End.Row);
        }

        return (row, row);
    }

    public static bool SnapshotMatches(
        GridResizePreviewSnapshot? snapshot,
        Sheet sheet,
        GridResizeAxis axis,
        uint start,
        uint end) =>
        snapshot is not null &&
        snapshot.SheetId == sheet.Id &&
        snapshot.Axis == axis &&
        snapshot.StartIndex == start &&
        snapshot.EndIndex == end;

    public static GridResizePreviewSnapshot CaptureColumnSnapshot(Sheet sheet, uint startColumn, uint endColumn) =>
        new(
            sheet.Id,
            GridResizeAxis.Column,
            startColumn,
            endColumn,
            CaptureDimensionSnapshot(sheet.ColumnWidths, startColumn, endColumn),
            CaptureIndexSnapshot(sheet.HiddenCols, startColumn, endColumn));

    public static GridResizePreviewSnapshot CaptureRowSnapshot(Sheet sheet, uint startRow, uint endRow) =>
        new(
            sheet.Id,
            GridResizeAxis.Row,
            startRow,
            endRow,
            CaptureDimensionSnapshot(sheet.RowHeights, startRow, endRow),
            CaptureIndexSnapshot(sheet.HiddenRows, startRow, endRow));

    public static void ApplyColumnResizePreview(Sheet sheet, uint startColumn, uint endColumn, double widthPixels) =>
        ApplyDimensionResizePreview(
            sheet.ColumnWidths,
            sheet.HiddenCols,
            startColumn,
            endColumn,
            ColumnWidthPixelMapper.PixelsToColumnWidth(widthPixels));

    public static void ApplyRowResizePreview(Sheet sheet, uint startRow, uint endRow, double heightPixels) =>
        ApplyDimensionResizePreview(sheet.RowHeights, sheet.HiddenRows, startRow, endRow, heightPixels);

    public static bool RestoreColumnResizePreview(Sheet sheet, GridResizePreviewSnapshot? snapshot)
    {
        if (snapshot is not { Axis: GridResizeAxis.Column } || snapshot.SheetId != sheet.Id)
            return false;

        RestoreDimensionSnapshot(sheet.ColumnWidths, snapshot.StartIndex, snapshot.EndIndex, snapshot.OriginalSizes);
        RestoreIndexSnapshot(sheet.HiddenCols, snapshot.StartIndex, snapshot.EndIndex, snapshot.OriginalHiddenIndexes);
        return true;
    }

    public static bool RestoreRowResizePreview(Sheet sheet, GridResizePreviewSnapshot? snapshot)
    {
        if (snapshot is not { Axis: GridResizeAxis.Row } || snapshot.SheetId != sheet.Id)
            return false;

        RestoreDimensionSnapshot(sheet.RowHeights, snapshot.StartIndex, snapshot.EndIndex, snapshot.OriginalSizes);
        RestoreIndexSnapshot(sheet.HiddenRows, snapshot.StartIndex, snapshot.EndIndex, snapshot.OriginalHiddenIndexes);
        return true;
    }

    private static (uint Start, uint End) GetContiguousHiddenRange(IReadOnlySet<uint> hiddenIndexes, uint index, uint maxIndex)
    {
        var start = index;
        while (start > 1 && hiddenIndexes.Contains(start - 1))
            start--;

        var end = index;
        while (end < maxIndex && hiddenIndexes.Contains(end + 1))
            end++;

        return (start, end);
    }

    private static Dictionary<uint, double> CaptureDimensionSnapshot(
        IReadOnlyDictionary<uint, double> dimensions,
        uint start,
        uint end)
    {
        var snapshot = new Dictionary<uint, double>();
        for (var index = start; index <= end; index++)
        {
            if (dimensions.TryGetValue(index, out var size))
                snapshot[index] = size;
        }

        return snapshot;
    }

    private static HashSet<uint> CaptureIndexSnapshot(IReadOnlySet<uint> indexes, uint start, uint end)
    {
        var snapshot = new HashSet<uint>();
        for (var index = start; index <= end; index++)
        {
            if (indexes.Contains(index))
                snapshot.Add(index);
        }

        return snapshot;
    }

    private static void RestoreDimensionSnapshot(
        IDictionary<uint, double> dimensions,
        uint start,
        uint end,
        IReadOnlyDictionary<uint, double> snapshot)
    {
        for (var index = start; index <= end; index++)
            dimensions.Remove(index);

        foreach (var (index, size) in snapshot)
            dimensions[index] = size;
    }

    private static void RestoreIndexSnapshot(ISet<uint> indexes, uint start, uint end, IReadOnlySet<uint> snapshot)
    {
        for (var index = start; index <= end; index++)
            indexes.Remove(index);

        foreach (var index in snapshot)
            indexes.Add(index);
    }

    private static void ApplyDimensionResizePreview(
        IDictionary<uint, double> dimensions,
        ISet<uint> hiddenIndexes,
        uint start,
        uint end,
        double size)
    {
        for (var index = start; index <= end; index++)
        {
            if (size == 0)
            {
                dimensions.Remove(index);
                hiddenIndexes.Add(index);
            }
            else
            {
                dimensions[index] = size;
                hiddenIndexes.Remove(index);
            }
        }
    }
}
