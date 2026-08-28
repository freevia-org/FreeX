namespace FreeW.Core.Model;

public readonly record struct TableGridCellProjection(
    TableCell Cell,
    int CellIndex,
    int StartColumn,
    int Span)
{
    public int EndColumnExclusive => StartColumn + Span;

    public bool Contains(int gridColumn) =>
        gridColumn >= StartColumn && gridColumn < EndColumnExclusive;
}

/// <summary>Canonical logical-grid projection for model tables, including malformed span normalization.</summary>
public static class TableGridProjection
{
    /// <summary>
    /// Ceiling on a single cell's horizontal span. Word tops out at 63 table columns and LibreOffice
    /// Writer at 64, so this is roughly 16x the widest table either can produce.
    /// </summary>
    /// <remarks>
    /// r164 remediation, unbounded declared quantity: a span is a COUNT the file declares, not a
    /// measure of anything it contains. Both readers accept any value above 1 -- DocxReader from
    /// <c>w:gridSpan</c>, HtmlFileAdapter from <c>colspan</c> -- so a single cell could declare a
    /// two-billion-column grid. <see cref="TableWidth"/> then reported 2,000,000,000 and the layout
    /// pass allocated a <c>double[]</c> for every column: measured at 15.3 GB and still running after
    /// 15s. Normalizing here bounds every consumer at once, which is what this canonical projection
    /// exists for; the cell keeps its original GridSpan, so a round-trip still writes back what it read.
    /// </remarks>
    public const int MaximumGridSpan = 1024;

    public static int NormalizeSpan(int span) => Math.Clamp(span, 1, MaximumGridSpan);

    public static IReadOnlyList<TableGridCellProjection> ProjectRow(TableRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var cells = new List<TableGridCellProjection>(row.Cells.Count);
        var startColumn = 0;
        for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
        {
            var cell = row.Cells[cellIndex];
            var span = NormalizeSpan(cell.GridSpan);
            cells.Add(new TableGridCellProjection(cell, cellIndex, startColumn, span));
            startColumn += span;
        }

        return cells;
    }

    public static int RowWidth(TableRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.Cells.Sum(cell => NormalizeSpan(cell.GridSpan));
    }

    public static int TableWidth(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return table.Rows.Count == 0 ? 0 : table.Rows.Max(RowWidth);
    }

    public static int StartColumn(TableRow row, int cellIndex)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (cellIndex < 0 || cellIndex >= row.Cells.Count)
            return -1;
        return ProjectRow(row)[cellIndex].StartColumn;
    }

    public static TableGridCellProjection? At(TableRow row, int gridColumn)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (gridColumn < 0)
            return null;
        foreach (var projected in ProjectRow(row))
        {
            if (projected.Contains(gridColumn))
                return projected;
        }

        return null;
    }

    public static TableGridCellProjection? At(Table table, int rowIndex, int gridColumn)
    {
        ArgumentNullException.ThrowIfNull(table);
        return rowIndex < 0 || rowIndex >= table.Rows.Count
            ? null
            : At(table.Rows[rowIndex], gridColumn);
    }

    public static TableGridCellProjection? StartingAt(TableRow row, int gridColumn)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (gridColumn < 0)
            return null;
        foreach (var projected in ProjectRow(row))
        {
            if (projected.StartColumn == gridColumn)
                return projected;
        }

        return null;
    }

    public static int SpanWithinWidth(TableGridCellProjection projected, int gridWidth) =>
        projected.StartColumn >= gridWidth
            ? 0
            : Math.Min(projected.Span, Math.Max(0, gridWidth - projected.StartColumn));

    public static int InsertionIndex(TableRow row, int gridColumn)
    {
        ArgumentNullException.ThrowIfNull(row);
        foreach (var projected in ProjectRow(row))
        {
            if (projected.StartColumn >= gridColumn)
                return projected.CellIndex;
        }

        return row.Cells.Count;
    }
}
