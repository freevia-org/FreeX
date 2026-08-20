namespace FreeW.Core.Model;

/// <summary>
/// Shared table-layout mutations used by both FreeW shells. The methods are deliberately model-only:
/// callers own undo grouping, caret movement, dialogs, and rendering invalidation.
/// </summary>
public static class TableLayoutOperations
{
    public const double DefaultAutoFitWindowWidthPt = 468.0;

    public static bool UpdateFormatting(Table? table, Func<TableFormatting, TableFormatting> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (table is null)
            return false;

        table.Formatting = update(table.Formatting);
        return true;
    }

    public static bool SetCellTextDirection(
        Table? table,
        int rowIndex,
        int columnIndex,
        CellTextDirection direction)
    {
        if (!TryGetCell(table, rowIndex, columnIndex, out var cell))
            return false;

        cell.TextDirection = direction;
        return true;
    }

    public static bool SetColumnWidth(Table? table, int columnIndex, double? widthPt)
    {
        if (table is null || columnIndex < 0 || columnIndex >= table.ColumnCount)
            return false;

        if (table.ColumnWidthsPt.Count == table.ColumnCount)
            table.ColumnWidthsPt[columnIndex] = widthPt ?? table.ColumnWidthsPt[columnIndex];

        // columnIndex is a grid-column index, not a per-row cell index: a row containing a
        // horizontally merged cell (GridSpan > 1) before this column has fewer Cells entries than
        // grid columns, so Cells[columnIndex] would land on the wrong cell. Route through the same
        // grid projection TableGridProjection already uses elsewhere to find whichever cell actually
        // occupies this grid column in each row.
        foreach (var row in table.Rows)
            if (TableGridProjection.At(row, columnIndex) is { } projected)
                projected.Cell.WidthPt = widthPt;

        return true;
    }

    public static bool DistributeRows(Table? table)
    {
        if (table is null || table.Rows.Count == 0)
            return false;

        var explicitHeights = table.Rows
            .Where(row => row.HeightPt.HasValue)
            .Select(row => row.HeightPt!.Value)
            .ToList();
        var targetHeight = explicitHeights.Count > 0
            ? explicitHeights.Average()
            : (double?)null;

        foreach (var row in table.Rows)
        {
            row.HeightPt = targetHeight;
            row.HeightRule = targetHeight.HasValue ? TableRowHeightRule.Exact : TableRowHeightRule.Auto;
        }

        return true;
    }

    public static bool DistributeColumns(
        Table? table,
        double fallbackWidthPt = DefaultAutoFitWindowWidthPt)
    {
        if (table is null)
            return false;

        var columnCount = table.ColumnCount;
        if (columnCount == 0)
            return false;

        var totalWidth = table.ColumnWidthsPt.Count == columnCount
            ? table.ColumnWidthsPt.Sum()
            : table.PreferredWidthPt ?? fallbackWidthPt;
        if (totalWidth <= 0)
            totalWidth = fallbackWidthPt;

        var columnWidth = totalWidth / columnCount;
        table.ColumnWidthsPt.Clear();
        for (var i = 0; i < columnCount; i++)
            table.ColumnWidthsPt.Add(columnWidth);

        foreach (var row in table.Rows)
            foreach (var cell in row.Cells)
                cell.WidthPt = columnWidth;

        return true;
    }

    public static bool SetAutoFit(
        Table? table,
        AutoFitMode mode,
        double windowWidthPt = DefaultAutoFitWindowWidthPt)
    {
        if (table is null)
            return false;

        table.AutoFit = mode;
        if (mode == AutoFitMode.Contents)
        {
            table.ColumnWidthsPt.Clear();
            foreach (var row in table.Rows)
                foreach (var cell in row.Cells)
                    cell.WidthPt = null;
        }
        else if (mode == AutoFitMode.Window)
        {
            table.PreferredWidthPt = windowWidthPt;
        }

        return true;
    }

    public static bool TryBuildSplitReplacement(
        Table? table,
        int rowIndex,
        out IReadOnlyList<Block> replacement)
    {
        replacement = Array.Empty<Block>();
        if (table is null || rowIndex <= 0 || rowIndex >= table.Rows.Count)
            return false;

        var top = CopyTableShell(table);
        for (var i = 0; i < rowIndex; i++)
            top.Rows.Add(table.Rows[i]);

        var bottom = CopyTableShell(table);
        for (var i = rowIndex; i < table.Rows.Count; i++)
            bottom.Rows.Add(table.Rows[i]);

        replacement = [top, new Paragraph(string.Empty), bottom];
        return true;
    }

    public static Table CopyTableWithRows(Table source, IEnumerable<TableRow> rows)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rows);

        var copy = CopyTableShell(source);
        copy.Rows.AddRange(rows);
        return copy;
    }

    public static Run BuildFormulaRun(
        Table table,
        int rowIndex,
        int columnIndex,
        TableFormulaField formula)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(formula);

        var result = TableFormulaEvaluator.Evaluate(table, rowIndex, columnIndex, formula);
        return Run.TableFormulaFieldRun(formula, result);
    }

    private static bool TryGetCell(
        Table? table,
        int rowIndex,
        int columnIndex,
        out TableCell cell)
    {
        cell = null!;
        if (table is null || rowIndex < 0 || rowIndex >= table.Rows.Count)
            return false;

        var cells = table.Rows[rowIndex].Cells;
        if (columnIndex < 0 || columnIndex >= cells.Count)
            return false;

        cell = cells[columnIndex];
        return true;
    }

    private static Table CopyTableShell(Table source)
    {
        var copy = new Table
        {
            Formatting = source.Formatting,
            TableStyleId = source.TableStyleId,
            Borders = source.Borders,
            PreferredWidthPt = source.PreferredWidthPt,
            Alignment = source.Alignment,
            IndentFromLeftPt = source.IndentFromLeftPt,
            FloatingPosition = source.FloatingPosition,
            FloatingTableAllowsOverlap = source.FloatingTableAllowsOverlap,
            DefaultCellMargins = source.DefaultCellMargins,
            CellSpacingPt = source.CellSpacingPt,
            AutoFit = source.AutoFit
        };
        copy.ColumnWidthsPt.AddRange(source.ColumnWidthsPt);
        return copy;
    }
}
