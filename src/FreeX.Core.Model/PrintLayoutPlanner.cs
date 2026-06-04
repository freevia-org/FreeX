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

        return BuildAxisPlans(
            printRange.Start.Row,
            printRange.End.Row,
            repeatRows,
            rowsPerPage,
            CellAddress.MaxRow,
            manualRowBreaks,
            static (titleRows, bodyPage) => new PrintPageRowPlan(titleRows, bodyPage));
    }

    public static IReadOnlyList<PrintPageColumnPlan> BuildColumnPlans(
        GridRange printRange,
        WorksheetRepeatRange? repeatColumns,
        uint columnsPerPage,
        IReadOnlyCollection<uint>? manualColumnBreaks = null)
    {
        if (columnsPerPage == 0)
            throw new ArgumentOutOfRangeException(nameof(columnsPerPage), "Columns per page must be at least 1.");

        return BuildAxisPlans(
            printRange.Start.Col,
            printRange.End.Col,
            repeatColumns,
            columnsPerPage,
            CellAddress.MaxCol,
            manualColumnBreaks,
            static (titleColumns, bodyPage) => new PrintPageColumnPlan(titleColumns, bodyPage));
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

    private static List<uint> BuildTitleIndexes(WorksheetRepeatRange? repeatRange, uint maxIndex)
    {
        var titleIndexes = new List<uint>();
        if (repeatRange is not { } range)
            return titleIndexes;

        for (var row = range.Start; row <= range.End && row <= maxIndex; row++)
        {
            if (row >= 1)
                titleIndexes.Add(row);
        }

        return titleIndexes;
    }

    private static List<TPlan> BuildAxisPlans<TPlan>(
        uint startValue,
        uint endValue,
        WorksheetRepeatRange? repeatRange,
        uint valuesPerPage,
        uint maxTitleIndex,
        IReadOnlyCollection<uint>? manualBreaks,
        Func<IReadOnlyList<uint>, IReadOnlyList<uint>, TPlan> createPlan)
    {
        var titleValues = BuildTitleIndexes(repeatRange, maxTitleIndex);
        var titleSet = titleValues.ToHashSet();
        var bodyValues = BuildBodyValues(startValue, endValue, titleSet);

        var titleValuesOnPage = valuesPerPage > 1
            ? Math.Min((uint)titleValues.Count, valuesPerPage - 1)
            : 0;
        var bodyValuesPerPage = Math.Max(1u, valuesPerPage - titleValuesOnPage);
        var bodyPages = BuildBodyPages(bodyValues, bodyValuesPerPage, manualBreaks);
        var pages = new List<TPlan>(bodyPages.Count);
        foreach (var bodyPage in bodyPages)
            pages.Add(createPlan(titleValues, bodyPage));

        if (pages.Count == 0 && titleValues.Count > 0)
            pages.Add(createPlan(titleValues, []));

        return pages;
    }

    private static List<uint> BuildBodyValues(uint startValue, uint endValue, HashSet<uint> titleValues)
    {
        var bodyValues = new List<uint>();
        for (var value = startValue; value <= endValue; value++)
        {
            if (!titleValues.Contains(value))
                bodyValues.Add(value);
        }

        return bodyValues;
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
