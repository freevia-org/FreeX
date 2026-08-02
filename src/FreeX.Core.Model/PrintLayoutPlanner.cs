namespace FreeX.Core.Model;

public sealed record PrintPageRowPlan(IReadOnlyList<uint> TitleRows, IReadOnlyList<uint> BodyRows);
public sealed record PrintPageColumnPlan(IReadOnlyList<uint> TitleColumns, IReadOnlyList<uint> BodyColumns);

/// <summary>
/// Measurement of one printed page's cell grid. <see cref="ColumnWidth"/>/<see cref="RowHeight"/> are
/// the uniform fallback size (used when no per-row/per-column offsets are supplied, and as the last
/// slot's width/height when they are). <see cref="ColumnOffsets"/>/<see cref="RowOffsets"/>, when
/// present, give the cumulative pixel offset of each printed column/row from the grid's left/top edge
/// (length = printed column/row count + 1, so slot <c>i</c> spans <c>[offsets[i], offsets[i + 1])</c>),
/// derived from the sheet's actual column widths/row heights so cells, gridlines, headings, text boxes,
/// and anchored charts/text boxes all land at the same position the on-screen grid shows.
/// </summary>
public sealed record PrintGridMeasurement(
    double HeaderWidth,
    double HeaderHeight,
    double ColumnWidth,
    double RowHeight,
    IReadOnlyList<double>? ColumnOffsets = null,
    IReadOnlyList<double>? RowOffsets = null)
{
    /// <summary>Pixel offset (from the grid's left edge) of the printed column at <paramref name="columnIndex"/>.</summary>
    public double ColumnOffset(int columnIndex) =>
        ColumnOffsets is { } offsets && columnIndex >= 0 && columnIndex < offsets.Count
            ? offsets[columnIndex]
            : columnIndex * ColumnWidth;

    /// <summary>Pixel offset (from the grid's top edge) of the printed row at <paramref name="rowIndex"/>.</summary>
    public double RowOffset(int rowIndex) =>
        RowOffsets is { } offsets && rowIndex >= 0 && rowIndex < offsets.Count
            ? offsets[rowIndex]
            : rowIndex * RowHeight;

    /// <summary>Pixel width of the printed column at <paramref name="columnIndex"/> (0-based within the page).</summary>
    public double ColumnWidthAt(int columnIndex) =>
        ColumnOffsets is { } offsets && columnIndex >= 0 && columnIndex + 1 < offsets.Count
            ? offsets[columnIndex + 1] - offsets[columnIndex]
            : ColumnWidth;

    /// <summary>Pixel height of the printed row at <paramref name="rowIndex"/> (0-based within the page).</summary>
    public double RowHeightAt(int rowIndex) =>
        RowOffsets is { } offsets && rowIndex >= 0 && rowIndex + 1 < offsets.Count
            ? offsets[rowIndex + 1] - offsets[rowIndex]
            : RowHeight;

    /// <summary>Total printed width of all columns on the page.</summary>
    public double TotalColumnWidth(int columnCount) =>
        ColumnOffsets is { } offsets && offsets.Count > 0 ? offsets[^1] : columnCount * ColumnWidth;

    /// <summary>Total printed height of all rows on the page.</summary>
    public double TotalRowHeight(int rowCount) =>
        RowOffsets is { } offsets && offsets.Count > 0 ? offsets[^1] : rowCount * RowHeight;
}

public static class PrintLayoutPlanner
{
    public static IReadOnlyList<PrintPageRowPlan> BuildRowPlans(
        GridRange printRange,
        WorksheetRepeatRange? repeatRows,
        uint rowsPerPage,
        IReadOnlyCollection<uint>? manualRowBreaks = null,
        Func<uint, bool>? isRowHidden = null)
    {
        ThrowIfInvalidPageSize(rowsPerPage, nameof(rowsPerPage), "Rows");

        return BuildAxisPlans(
            printRange.Start.Row,
            printRange.End.Row,
            repeatRows,
            rowsPerPage,
            CellAddress.MaxRow,
            manualRowBreaks,
            isRowHidden,
            static (titleRows, bodyPage) => new PrintPageRowPlan(titleRows, bodyPage));
    }

    public static IReadOnlyList<PrintPageColumnPlan> BuildColumnPlans(
        GridRange printRange,
        WorksheetRepeatRange? repeatColumns,
        uint columnsPerPage,
        IReadOnlyCollection<uint>? manualColumnBreaks = null,
        Func<uint, bool>? isColumnHidden = null)
    {
        ThrowIfInvalidPageSize(columnsPerPage, nameof(columnsPerPage), "Columns");

        return BuildAxisPlans(
            printRange.Start.Col,
            printRange.End.Col,
            repeatColumns,
            columnsPerPage,
            CellAddress.MaxCol,
            manualColumnBreaks,
            isColumnHidden,
            static (titleColumns, bodyPage) => new PrintPageColumnPlan(titleColumns, bodyPage));
    }

    /// <summary>
    /// Reports whether a manual row/column break registered at <paramref name="manualBreak"/> would have
    /// any effect on the real printed/exported page layout -- i.e. whether <see cref="BuildManualBreakSet"/>
    /// (used by <see cref="BuildAxisPlans{TPlan}"/>, and therefore by every real pagination consumer:
    /// printing, PDF/XPS export, and print preview) would keep it. Excel pins the print-title rows/columns
    /// to the top/left of every printed page and never lets a manual break split them from themselves, so a
    /// break registered at or before the first body row/column after the title range is silently dropped --
    /// this mirrors that exclusion exactly so a page-break UI indicator (drawn in both the WPF and Avalonia
    /// shells) never shows a break line print/export will ignore. See R115-manual-break-title-exclusion.
    /// </summary>
    public static bool IsManualBreakEffective(
        uint manualBreak,
        uint startValue,
        uint endValue,
        WorksheetRepeatRange? repeatRange,
        uint maxIndex,
        Func<uint, bool>? isHidden)
    {
        var titleValues = BuildTitleIndexes(repeatRange, maxIndex, isHidden).ToHashSet();
        var bodyValues = BuildBodyValues(startValue, endValue, titleValues, isHidden);
        if (bodyValues.Count == 0)
            return false;

        return manualBreak > bodyValues[0] && manualBreak <= bodyValues[^1];
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

    /// <summary>
    /// Measures a printed page's cell grid using the sheet's actual per-row heights and per-column
    /// pixel widths (already resolved to pixels by the caller) for any row/column that has an explicit
    /// override recorded on the sheet, so cell/gridline/heading/text-box positions and anchored chart
    /// placement match the sheet's real, non-uniform geometry instead of always assuming a fixed 20px
    /// row / evenly-divided column. Rows/columns with no recorded override keep the original uniform
    /// fallback (fixed 20px row height; printable width divided evenly across printed columns), so an
    /// all-default sheet measures identically to <see cref="MeasurePrintableGrid(double, double, uint, uint, bool)"/>.
    /// </summary>
    /// <param name="printableWidth">Printable page width in pixels (paper width minus margins).</param>
    /// <param name="printableHeight">Printable page height in pixels (paper height minus margins).</param>
    /// <param name="pageRows">The 1-based row indexes printed on this page, in on-page order (titles then body).</param>
    /// <param name="pageColumns">The 1-based column indexes printed on this page, in on-page order (titles then body).</param>
    /// <param name="rowHeightsPixels">Explicit per-row height overrides in pixels (1-based row → pixels). Rows absent here use the uniform fallback.</param>
    /// <param name="columnWidthsPixels">Explicit per-column width overrides in pixels (1-based col → pixels). Columns absent here use the uniform fallback.</param>
    /// <param name="printHeadings">Whether row/column heading gutters are reserved.</param>
    public static PrintGridMeasurement MeasurePrintableGrid(
        double printableWidth,
        double printableHeight,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns,
        IReadOnlyDictionary<uint, double> rowHeightsPixels,
        IReadOnlyDictionary<uint, double> columnWidthsPixels,
        bool printHeadings)
    {
        const double rowHeight = 20.0;
        const double headerWidth = 40.0;
        const double headerHeight = 20.0;
        var reservedWidth = printHeadings ? headerWidth : 0.0;
        var reservedHeight = printHeadings ? headerHeight : 0.0;
        var columnWidth = Math.Max(40.0, (printableWidth - reservedWidth) / Math.Max(1, pageColumns.Count));

        var columnOffsets = BuildOffsets(pageColumns, columnWidthsPixels, columnWidth, minimumSize: 40.0);
        var rowOffsets = BuildOffsets(pageRows, rowHeightsPixels, rowHeight, minimumSize: 1.0);

        return new PrintGridMeasurement(
            reservedWidth,
            reservedHeight,
            columnWidth,
            rowHeight,
            columnOffsets,
            rowOffsets);
    }

    /// <summary>
    /// Builds cumulative pixel offsets (length <paramref name="indexes"/>.Count + 1) for a page's printed
    /// rows/columns, so slot <c>i</c> spans <c>[offsets[i], offsets[i + 1])</c>. Each item's size comes
    /// from <paramref name="sizesPixels"/> when present, otherwise <paramref name="uniformFallback"/>, so a
    /// row/column with no recorded override keeps the original uniform size.
    /// </summary>
    private static IReadOnlyList<double> BuildOffsets(
        IReadOnlyList<uint> indexes,
        IReadOnlyDictionary<uint, double> sizesPixels,
        double uniformFallback,
        double minimumSize)
    {
        var offsets = new double[indexes.Count + 1];
        var running = 0.0;
        for (var i = 0; i < indexes.Count; i++)
        {
            offsets[i] = running;
            var size = sizesPixels.TryGetValue(indexes[i], out var s) && s > 0 ? s : uniformFallback;
            running += Math.Max(minimumSize, size);
        }

        offsets[indexes.Count] = running;
        return offsets;
    }

    private static List<uint> BuildTitleIndexes(WorksheetRepeatRange? repeatRange, uint maxIndex, Func<uint, bool>? isHidden)
    {
        var titleIndexes = new List<uint>();
        if (repeatRange is not { } range)
            return titleIndexes;

        for (var row = range.Start; row <= range.End && row <= maxIndex; row++)
        {
            if (row >= 1 && isHidden?.Invoke(row) != true)
                titleIndexes.Add(row);
        }

        return titleIndexes;
    }

    private static void ThrowIfInvalidPageSize(uint valuesPerPage, string paramName, string label)
    {
        if (valuesPerPage == 0)
            throw new ArgumentOutOfRangeException(paramName, $"{label} per page must be at least 1.");
    }

    private static List<TPlan> BuildAxisPlans<TPlan>(
        uint startValue,
        uint endValue,
        WorksheetRepeatRange? repeatRange,
        uint valuesPerPage,
        uint maxTitleIndex,
        IReadOnlyCollection<uint>? manualBreaks,
        Func<uint, bool>? isHidden,
        Func<IReadOnlyList<uint>, IReadOnlyList<uint>, TPlan> createPlan)
    {
        var titleValues = BuildTitleIndexes(repeatRange, maxTitleIndex, isHidden);
        var titleSet = titleValues.ToHashSet();
        var bodyValues = BuildBodyValues(startValue, endValue, titleSet, isHidden);

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

    private static List<uint> BuildBodyValues(
        uint startValue,
        uint endValue,
        HashSet<uint> titleValues,
        Func<uint, bool>? isHidden)
    {
        var bodyValues = new List<uint>();
        for (var value = startValue; value <= endValue; value++)
        {
            if (!titleValues.Contains(value) && isHidden?.Invoke(value) != true)
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
