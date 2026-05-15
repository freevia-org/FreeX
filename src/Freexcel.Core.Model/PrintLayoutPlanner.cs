namespace Freexcel.Core.Model;

public sealed record PrintPageRowPlan(IReadOnlyList<uint> TitleRows, IReadOnlyList<uint> BodyRows);
public sealed record PrintPageColumnPlan(IReadOnlyList<uint> TitleColumns, IReadOnlyList<uint> BodyColumns);
public sealed record PrintGridMeasurement(
    double HeaderWidth,
    double HeaderHeight,
    double ColumnWidth,
    double RowHeight);

public static class PrintLayoutPlanner
{
    public static IReadOnlyList<PrintPageRowPlan> BuildRowPlans(
        GridRange printRange,
        WorksheetRepeatRange? repeatRows,
        uint rowsPerPage)
    {
        if (rowsPerPage == 0)
            throw new ArgumentOutOfRangeException(nameof(rowsPerPage), "Rows per page must be at least 1.");

        var titleRows = BuildTitleRows(repeatRows);
        var titleSet = titleRows.ToHashSet();
        var bodyRows = new List<uint>();
        for (var row = printRange.Start.Row; row <= printRange.End.Row; row++)
        {
            if (!titleSet.Contains(row))
                bodyRows.Add(row);
        }

        var titleRowsOnPage = rowsPerPage > 1
            ? Math.Min((uint)titleRows.Count, rowsPerPage - 1)
            : 0;
        var bodyRowsPerPage = Math.Max(1u, rowsPerPage - titleRowsOnPage);
        var pages = new List<PrintPageRowPlan>();
        for (var index = 0; index < bodyRows.Count; index += (int)bodyRowsPerPage)
        {
            pages.Add(new PrintPageRowPlan(
                titleRows,
                bodyRows.Skip(index).Take((int)bodyRowsPerPage).ToList()));
        }

        if (pages.Count == 0 && titleRows.Count > 0)
            pages.Add(new PrintPageRowPlan(titleRows, []));

        return pages;
    }

    public static IReadOnlyList<PrintPageColumnPlan> BuildColumnPlans(
        GridRange printRange,
        WorksheetRepeatRange? repeatColumns,
        uint columnsPerPage)
    {
        if (columnsPerPage == 0)
            throw new ArgumentOutOfRangeException(nameof(columnsPerPage), "Columns per page must be at least 1.");

        var titleColumns = BuildTitleColumns(repeatColumns);
        var titleSet = titleColumns.ToHashSet();
        var bodyColumns = new List<uint>();
        for (var column = printRange.Start.Col; column <= printRange.End.Col; column++)
        {
            if (!titleSet.Contains(column))
                bodyColumns.Add(column);
        }

        var titleColumnsOnPage = columnsPerPage > 1
            ? Math.Min((uint)titleColumns.Count, columnsPerPage - 1)
            : 0;
        var bodyColumnsPerPage = Math.Max(1u, columnsPerPage - titleColumnsOnPage);
        var pages = new List<PrintPageColumnPlan>();
        for (var index = 0; index < bodyColumns.Count; index += (int)bodyColumnsPerPage)
        {
            pages.Add(new PrintPageColumnPlan(
                titleColumns,
                bodyColumns.Skip(index).Take((int)bodyColumnsPerPage).ToList()));
        }

        if (pages.Count == 0 && titleColumns.Count > 0)
            pages.Add(new PrintPageColumnPlan(titleColumns, []));

        return pages;
    }

    public static PrintGridMeasurement MeasurePrintableGrid(
        double printableWidth,
        double printableHeight,
        uint rowCount,
        uint columnCount,
        bool printHeadings)
    {
        const double rowHeight = 20.0;
        const double headerWidth = 40.0;
        const double headerHeight = 20.0;
        var reservedWidth = printHeadings ? headerWidth : 0.0;
        var reservedHeight = printHeadings ? headerHeight : 0.0;
        var columnWidth = Math.Max(40.0, (printableWidth - reservedWidth) / Math.Max(1, columnCount));
        return new PrintGridMeasurement(
            reservedWidth,
            reservedHeight,
            columnWidth,
            rowHeight);
    }

    private static List<uint> BuildTitleRows(WorksheetRepeatRange? repeatRows)
    {
        var titleRows = new List<uint>();
        if (repeatRows is not { } rows)
            return titleRows;

        for (var row = rows.Start; row <= rows.End && row <= CellAddress.MaxRow; row++)
        {
            if (row >= 1)
                titleRows.Add(row);
        }

        return titleRows;
    }

    private static List<uint> BuildTitleColumns(WorksheetRepeatRange? repeatColumns)
    {
        var titleColumns = new List<uint>();
        if (repeatColumns is not { } columns)
            return titleColumns;

        for (var column = columns.Start; column <= columns.End && column <= CellAddress.MaxCol; column++)
        {
            if (column >= 1)
                titleColumns.Add(column);
        }

        return titleColumns;
    }
}
