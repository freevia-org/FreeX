namespace Free.Shared.AppServices;

public readonly record struct TableGridCell(int GridSpan, int RowSpan, bool HMerge, bool VMerge);

public readonly record struct TableGridHit(int Row, int Col);

public readonly record struct TableGridRect(double X, double Y, double Width, double Height);

public sealed record TableGridGeometry(
    IReadOnlyList<double> ColumnWidths,
    IReadOnlyList<double> RowHeights,
    IReadOnlyList<IReadOnlyList<TableGridCell>> Cells);

public static class TableGridGeometryPlanner
{
    public static TableGridHit? HitTest(
        TableGridGeometry geometry,
        double originX,
        double originY,
        double x,
        double y)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        var totalWidth = geometry.ColumnWidths.Sum();
        var totalHeight = geometry.RowHeights.Sum();

        if (x < originX || x > originX + totalWidth)
            return null;
        if (y < originY || y > originY + totalHeight)
            return null;

        var col = FindBand(geometry.ColumnWidths, originX, x);
        var row = FindBand(geometry.RowHeights, originY, y);
        if (row < 0 || col < 0)
            return null;

        return ResolveCell(geometry, row, col);
    }

    /// <summary>
    /// Resolves a grid coordinate to the top-left anchor of its merged cell.
    /// </summary>
    public static TableGridHit? ResolveCell(
        TableGridGeometry geometry,
        int row,
        int col)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        if (row < 0 || row >= geometry.RowHeights.Count
            || col < 0 || col >= geometry.ColumnWidths.Count
            || !TryGetCell(geometry, row, col, out _))
            return null;

        var anchor = FindAnchor(geometry, row, col);
        return TryGetCell(geometry, anchor.Row, anchor.Col, out _)
            ? anchor
            : null;
    }

    public static TableGridRect? GetCellRect(
        TableGridGeometry geometry,
        double originX,
        double originY,
        int row,
        int col)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        if (row < 0 || row >= geometry.RowHeights.Count)
            return null;
        if (col < 0 || col >= geometry.ColumnWidths.Count)
            return null;
        var anchor = ResolveCell(geometry, row, col);
        if (anchor is null
            || !TryGetCell(geometry, anchor.Value.Row, anchor.Value.Col, out var cell))
            return null;

        var x = originX + SumBefore(geometry.ColumnWidths, anchor.Value.Col);
        var y = originY + SumBefore(geometry.RowHeights, anchor.Value.Row);
        var gridSpan = Math.Max(1, cell.GridSpan);
        var rowSpan = Math.Max(1, cell.RowSpan);
        var width = SumRange(geometry.ColumnWidths, anchor.Value.Col, gridSpan);
        var height = SumRange(geometry.RowHeights, anchor.Value.Row, rowSpan);

        return new TableGridRect(x, y, width, height);
    }

    private static int FindBand(IReadOnlyList<double> sizes, double origin, double coordinate)
    {
        if (sizes.Count == 0)
            return -1;

        var running = origin;
        for (var i = 0; i < sizes.Count; i++)
        {
            running += sizes[i];
            if (coordinate <= running)
                return i;
        }

        return sizes.Count - 1;
    }

    private static TableGridHit FindAnchor(TableGridGeometry geometry, int row, int col)
    {
        var anchorRow = row;
        while (anchorRow >= 0 && TryGetCell(geometry, anchorRow, col, out var cell))
        {
            if (!cell.VMerge)
                break;
            anchorRow--;
        }

        anchorRow = Math.Max(0, anchorRow);

        var anchorCol = col;
        while (anchorCol >= 0 && TryGetCell(geometry, anchorRow, anchorCol, out var cell))
        {
            if (!cell.HMerge)
                break;
            anchorCol--;
        }

        return new TableGridHit(anchorRow, Math.Max(0, anchorCol));
    }

    private static bool TryGetCell(TableGridGeometry geometry, int row, int col, out TableGridCell cell)
    {
        if (row < 0 || row >= geometry.Cells.Count ||
            col < 0 || col >= geometry.Cells[row].Count)
        {
            cell = default;
            return false;
        }

        cell = geometry.Cells[row][col];
        return true;
    }

    private static double SumBefore(IReadOnlyList<double> values, int count) =>
        SumRange(values, 0, count);

    private static double SumRange(IReadOnlyList<double> values, int start, int count)
    {
        var end = Math.Min(values.Count, start + count);
        var sum = 0.0;
        for (var i = start; i < end; i++)
            sum += values[i];

        return sum;
    }
}
