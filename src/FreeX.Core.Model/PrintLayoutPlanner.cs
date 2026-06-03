namespace FreeX.Core.Model;

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
        uint rowsPerPage,
        IReadOnlyCollection<uint>? manualRowBreaks = null)
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
        var bodyPages = BuildBodyPages(bodyRows, bodyRowsPerPage, manualRowBreaks);
        var pages = new List<PrintPageRowPlan>(bodyPages.Count);
        foreach (var bodyPage in bodyPages)
            pages.Add(new PrintPageRowPlan(titleRows, bodyPage));

        if (pages.Count == 0 && titleRows.Count > 0)
            pages.Add(new PrintPageRowPlan(titleRows, []));

        return pages;
    }

    public static IReadOnlyList<PrintPageColumnPlan> BuildColumnPlans(
        GridRange printRange,
        WorksheetRepeatRange? repeatColumns,
        uint columnsPerPage,
        IReadOnlyCollection<uint>? manualColumnBreaks = null)
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
        var bodyPages = BuildBodyPages(bodyColumns, bodyColumnsPerPage, manualColumnBreaks);
        var pages = new List<PrintPageColumnPlan>(bodyPages.Count);
        foreach (var bodyPage in bodyPages)
            pages.Add(new PrintPageColumnPlan(titleColumns, bodyPage));

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

    private static int ToPageItemCount(uint itemsPerPage) =>
        itemsPerPage > int.MaxValue ? int.MaxValue : (int)itemsPerPage;

    private static int GetPageCount(int itemCount, int itemsPerPage) =>
        itemCount == 0 ? 0 : ((itemCount - 1) / itemsPerPage) + 1;

    private static List<List<uint>> BuildBodyPages(
        List<uint> bodyValues,
        uint valuesPerPage,
        IReadOnlyCollection<uint>? manualBreaks)
    {
        var valuesPerPageCount = ToPageItemCount(valuesPerPage);
        var pages = new List<List<uint>>(GetPageCount(bodyValues.Count, valuesPerPageCount));
        if (bodyValues.Count == 0)
            return pages;

        var manualBreakSet = BuildManualBreakSet(manualBreaks, bodyValues[0], bodyValues[^1]);
        var segmentStartIndex = 0;
        for (var index = 0; index < bodyValues.Count; index++)
        {
            if (index > segmentStartIndex && manualBreakSet.Contains(bodyValues[index]))
            {
                AddCapacityPages(bodyValues, segmentStartIndex, index, valuesPerPageCount, pages);
                segmentStartIndex = index;
            }
        }

        AddCapacityPages(bodyValues, segmentStartIndex, bodyValues.Count, valuesPerPageCount, pages);
        return pages;
    }

    private static HashSet<uint> BuildManualBreakSet(
        IReadOnlyCollection<uint>? manualBreaks,
        uint firstBodyValue,
        uint lastBodyValue)
    {
        var manualBreakSet = new HashSet<uint>();
        if (manualBreaks is null || manualBreaks.Count == 0)
            return manualBreakSet;

        foreach (var manualBreak in manualBreaks)
        {
            if (manualBreak > firstBodyValue && manualBreak <= lastBodyValue)
                manualBreakSet.Add(manualBreak);
        }

        return manualBreakSet;
    }

    private static void AddCapacityPages(
        List<uint> values,
        int segmentStartIndex,
        int segmentEndIndex,
        int valuesPerPageCount,
        List<List<uint>> pages)
    {
        for (var index = segmentStartIndex; index < segmentEndIndex; index += valuesPerPageCount)
        {
            pages.Add(CopyPageValues(
                values,
                index,
                Math.Min(valuesPerPageCount, segmentEndIndex - index)));
        }
    }

    private static List<uint> CopyPageValues(List<uint> values, int startIndex, int maxCount)
    {
        var count = Math.Min(maxCount, values.Count - startIndex);
        var pageValues = new List<uint>(count);
        for (var offset = 0; offset < count; offset++)
            pageValues.Add(values[startIndex + offset]);

        return pageValues;
    }
}
