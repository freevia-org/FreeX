using System.Globalization;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.Text;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// Builds a portable <see cref="PageContentLayout"/> for a single printed page from the sheet cells,
/// the page grid produced by <see cref="PagePaginationPlanner"/>, and the worksheet page setup. The
/// result is backend-agnostic render instructions (cell blocks, gridlines, headings, header/footer
/// bands) that any of the desktop hosts' renderers can paint.
///
/// The cell/heading/gridline/header-footer math mirrors the source desktop print layout faithfully:
/// the page rectangle is the paper size (inches) at 96 dpi, swapped for landscape; the printable area
/// is inset by the page margins; the grid is measured by <see cref="PrintLayoutPlanner.MeasurePrintableGrid"/>
/// (20px rows, 40px heading gutter, columns filling the remaining width); cells are centered on the
/// page when requested; and header/footer text is split into three bands with token substitution.
///
/// Out of scope for this single-page content model (deferred to the renderers / later extraction):
/// drawing shapes, pictures, comments, hyperlinks, header/footer pictures, and rich text
/// wrapping/trimming. The cell grid, gridlines, headings, text boxes, chart object blocks, and
/// header/footer text are produced.
/// </summary>
public static class PageContentRenderModelBuilder
{
    /// <summary>Drawing-surface resolution the source layout assumes, in dots per inch.</summary>
    public const double Dpi = 96.0;

    /// <summary>Printed cell text size in points, matching the source print renderer.</summary>
    public const double PrintFontSize = 9.0;

    /// <summary>Font family the source print renderer uses for cell, heading, and header/footer text.</summary>
    public const string PrintFontFamily = "Segoe UI";

    /// <summary>
    /// Light-gray fill the source print renderer paints behind row/column headings. Exposed so a
    /// renderer can match the heading band fill without re-deriving it.
    /// </summary>
    public static readonly PresentationRgb HeadingFill = new(242, 242, 242);

    /// <summary>
    /// Builds the content layout for the page at <paramref name="pageIndex"/> (0-based into the page
    /// grid). The page grid is the cross product of the pagination result's row and column segments,
    /// visited in the sheet's configured <see cref="WorksheetPageOrder"/>. Returns <c>null</c> when the
    /// index is out of range or the page has no rows/columns.
    /// </summary>
    /// <param name="workbook">Workbook that owns the styles, theme, and indexed colors.</param>
    /// <param name="sheet">The worksheet being printed.</param>
    /// <param name="pagePlan">The page grid from <see cref="PagePaginationPlanner.Paginate"/>.</param>
    /// <param name="pageIndex">0-based index of the page within the page grid.</param>
    /// <param name="textMeasurer">Text measurer used to vertically center cell/heading/band text.</param>
    /// <param name="now">Snapshot timestamp for date/time tokens; defaults to <see cref="DateTime.Now"/>.</param>
    /// <param name="workbookDirectory">
    /// Directory that contains the workbook file, with a trailing path separator (e.g. <c>C:\Docs\</c>).
    /// Substituted for <c>&amp;Z</c> / <c>&amp;[Path]</c>. Pass an empty string when the workbook is unsaved.
    /// </param>
    public static PageContentLayout? Build(
        Workbook workbook,
        Sheet sheet,
        PagePaginationResult pagePlan,
        int pageIndex,
        ITextMeasurer textMeasurer,
        DateTime? now = null,
        string workbookDirectory = "")
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(pagePlan);
        ArgumentNullException.ThrowIfNull(textMeasurer);

        if (pageIndex < 0 || pageIndex >= pagePlan.PageCount)
            return null;

        var (rowSegment, colSegment) = ResolvePageSegments(sheet.PageOrder, pagePlan, pageIndex);
        var pageRows = BuildAxisIndexes(sheet.PrintTitleRows, rowSegment);
        var pageColumns = BuildAxisIndexes(sheet.PrintTitleColumns, colSegment);
        if (pageRows.Count == 0 || pageColumns.Count == 0)
            return null;

        var pageSize = WorksheetPageLayout.GetPageSizeInches(sheet.PaperSize, sheet.PageOrientation);
        var pageW = pageSize.Width * Dpi;
        var pageH = pageSize.Height * Dpi;
        var margins = sheet.PageMargins;
        var marginLeft = margins.Left * Dpi;
        var marginRight = margins.Right * Dpi;
        var marginTop = margins.Top * Dpi;
        var marginBottom = margins.Bottom * Dpi;
        var printableW = pageW - marginLeft - marginRight;
        var printableH = pageH - marginTop - marginBottom;

        var columnWidthsPixels = BuildColumnWidthsPixels(sheet);
        var measurement = PrintLayoutPlanner.MeasurePrintableGrid(
            printableW,
            printableH,
            pageRows,
            pageColumns,
            sheet.RowHeights,
            columnWidthsPixels,
            sheet.PrintHeadings);

        var printedWidth = measurement.HeaderWidth + measurement.TotalColumnWidth(pageColumns.Count);
        var printedHeight = measurement.HeaderHeight + measurement.TotalRowHeight(pageRows.Count);
        var xOffset = sheet.CenterHorizontallyOnPage ? Math.Max(0, (printableW - printedWidth) / 2) : 0;
        var yOffset = sheet.CenterVerticallyOnPage ? Math.Max(0, (printableH - printedHeight) / 2) : 0;
        var contentLeft = marginLeft + xOffset;
        var contentTop = marginTop + yOffset;
        var gridLeft = contentLeft + measurement.HeaderWidth;
        var gridTop = contentTop + measurement.HeaderHeight;
        var gridBounds = new LayoutRect(
            gridLeft,
            gridTop,
            measurement.TotalColumnWidth(pageColumns.Count),
            measurement.TotalRowHeight(pageRows.Count));

        var pageNumber = (sheet.FirstPageNumber ?? 1) + pageIndex;
        var totalPages = pagePlan.PageCount;

        var cells = BuildCells(
            workbook,
            sheet,
            pageRows,
            pageColumns,
            gridLeft,
            gridTop,
            measurement,
            textMeasurer);

        var gridLines = sheet.PrintGridlines
            ? BuildGridLines(gridBounds, pageRows.Count, pageColumns.Count, measurement)
            : [];

        var (columnHeadings, rowHeadings) = sheet.PrintHeadings
            ? BuildHeadings(measurement, pageRows, pageColumns, contentLeft, contentTop, textMeasurer)
            : ([], []);

        var textBoxes = PageTextBoxLayoutPlanner.Build(
            sheet.TextBoxes,
            workbook.Theme,
            pageRows,
            pageColumns,
            gridLeft,
            gridTop,
            measurement);

        var charts = BuildCharts(
            workbook,
            sheet,
            rowSegment,
            colSegment,
            pageRows,
            pageColumns,
            gridLeft,
            gridTop,
            measurement,
            textMeasurer);

        var (header, footer) = ResolveHeaderFooterForPage(sheet, pageNumber);
        var resolvedNow = now ?? DateTime.Now;
        var (headerRuns, footerRuns) = BuildHeaderFooterRuns(
            sheet,
            header,
            footer,
            pageW,
            pageH,
            marginLeft,
            marginRight,
            sheet.HeaderMargin * Dpi,
            sheet.FooterMargin * Dpi,
            workbook.Name,
            workbookDirectory,
            sheet.Name,
            pageNumber,
            totalPages,
            resolvedNow,
            textMeasurer);

        return new PageContentLayout(
            pageNumber,
            new LayoutRect(0, 0, pageW, pageH),
            LayoutRect.FromCorners(marginLeft, marginTop, pageW - marginRight, pageH - marginBottom),
            gridBounds,
            cells,
            gridLines,
            columnHeadings,
            rowHeadings,
            charts,
            textBoxes,
            headerRuns,
            footerRuns);
    }

    private static (PageAxisSegment Row, PageAxisSegment Column) ResolvePageSegments(
        WorksheetPageOrder pageOrder,
        PagePaginationResult pagePlan,
        int pageIndex)
    {
        var page = PrintPageGridPlanner.BuildIndexes(
            pagePlan.RowPageCount,
            pagePlan.ColumnPageCount,
            pageOrder)[pageIndex];
        return (pagePlan.RowSegments[page.RowPageIndex], pagePlan.ColumnSegments[page.ColumnPageIndex]);
    }

    private static IReadOnlyList<uint> BuildAxisIndexes(WorksheetRepeatRange? repeat, PageAxisSegment segment)
    {
        // Title (repeat) rows/columns are reprinted ahead of the page body. The pagination segment
        // already spans the page's whole printed extent; reprint only the repeat indexes that fall
        // before the segment so they are not duplicated when the segment itself includes them.
        var indexes = new List<uint>();
        if (repeat is { } range && range.Start >= 1 && range.End >= range.Start)
        {
            for (var index = range.Start; index <= range.End; index++)
            {
                if (index < segment.Start)
                    indexes.Add(index);
            }
        }

        for (var index = segment.Start; index <= segment.End; index++)
            indexes.Add(index);

        return indexes;
    }

    private static IReadOnlyList<PageCellBlock> BuildCells(
        Workbook workbook,
        Sheet sheet,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns,
        double gridLeft,
        double gridTop,
        PrintGridMeasurement measurement,
        ITextMeasurer textMeasurer)
    {
        var rowIndexes = BuildPositionLookup(pageRows);
        var columnIndexes = BuildPositionLookup(pageColumns);
        var theme = workbook.Theme;
        var cells = new List<PageCellBlock>();

        for (var rowIndex = 0; rowIndex < pageRows.Count; rowIndex++)
        {
            var row = pageRows[rowIndex];
            for (var colIndex = 0; colIndex < pageColumns.Count; colIndex++)
            {
                var col = pageColumns[colIndex];
                var address = new CellAddress(sheet.Id, row, col);

                // Honor merges: emit one block per merge anchor (top-left), sized to the merged region
                // clipped to this page; skip the covered interior cells.
                var merge = sheet.GetMergeRegion(address);
                if (merge is { } region && (region.Start.Row != row || region.Start.Col != col))
                    continue;

                var cell = sheet.GetCell(address);
                var styleId = cell?.StyleId ?? sheet.GetStyleOnly(row, col) ?? StyleId.Default;
                var style = workbook.GetStyle(styleId);

                var x = gridLeft + measurement.ColumnOffset(colIndex);
                var y = gridTop + measurement.RowOffset(rowIndex);
                var width = measurement.ColumnWidthAt(colIndex);
                var height = measurement.RowHeightAt(rowIndex);
                if (merge is { } mergedRegion)
                {
                    (width, height) = MeasureMergedExtent(
                        mergedRegion,
                        rowIndexes,
                        columnIndexes,
                        colIndex,
                        rowIndex,
                        measurement);
                }

                var fill = ResolveFill(style, theme);
                var text = cell is not null ? FormatCellText(workbook, sheet, cell, style) : "";
                var borders = ResolveBorders(style);
                if (string.IsNullOrEmpty(text) && fill is null && !borders.HasAny)
                    continue;

                var bounds = new LayoutRect(x, y, width, height);
                var textOrigin = VerticallyCenteredOrigin(
                    textMeasurer, text, PrintFontFamily, PrintFontSize, bold: false, italic: false,
                    x + 2, y, height);

                cells.Add(new PageCellBlock(
                    bounds,
                    row,
                    col,
                    fill,
                    text,
                    ResolveFont(style, theme),
                    ResolveAlignment(style, cell),
                    borders,
                    textOrigin));
            }
        }

        return cells;
    }

    private static (double Width, double Height) MeasureMergedExtent(
        GridRange region,
        IReadOnlyDictionary<uint, int> rowIndexes,
        IReadOnlyDictionary<uint, int> columnIndexes,
        int anchorColIndex,
        int anchorRowIndex,
        PrintGridMeasurement measurement)
    {
        var lastColIndex = anchorColIndex;
        for (var col = region.Start.Col; col <= region.End.Col; col++)
        {
            if (columnIndexes.TryGetValue(col, out var index) && index > lastColIndex)
                lastColIndex = index;
        }

        var lastRowIndex = anchorRowIndex;
        for (var row = region.Start.Row; row <= region.End.Row; row++)
        {
            if (rowIndexes.TryGetValue(row, out var index) && index > lastRowIndex)
                lastRowIndex = index;
        }

        return (
            measurement.ColumnOffset(lastColIndex + 1) - measurement.ColumnOffset(anchorColIndex),
            measurement.RowOffset(lastRowIndex + 1) - measurement.RowOffset(anchorRowIndex));
    }

    private static IReadOnlyList<PageGridLine> BuildGridLines(
        LayoutRect gridBounds,
        int rowCount,
        int columnCount,
        PrintGridMeasurement measurement)
    {
        var lines = new List<PageGridLine>(rowCount + columnCount + 2);
        for (var colIndex = 0; colIndex <= columnCount; colIndex++)
        {
            var x = gridBounds.Left + measurement.ColumnOffset(colIndex);
            lines.Add(new PageGridLine(new LayoutPoint(x, gridBounds.Top), new LayoutPoint(x, gridBounds.Bottom)));
        }

        for (var rowIndex = 0; rowIndex <= rowCount; rowIndex++)
        {
            var y = gridBounds.Top + measurement.RowOffset(rowIndex);
            lines.Add(new PageGridLine(new LayoutPoint(gridBounds.Left, y), new LayoutPoint(gridBounds.Right, y)));
        }

        return lines;
    }

    private static (IReadOnlyList<PageHeadingCell> Columns, IReadOnlyList<PageHeadingCell> Rows) BuildHeadings(
        PrintGridMeasurement measurement,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns,
        double contentLeft,
        double contentTop,
        ITextMeasurer textMeasurer)
    {
        var columnHeadings = new List<PageHeadingCell>(pageColumns.Count);
        for (var colIndex = 0; colIndex < pageColumns.Count; colIndex++)
        {
            var rect = new LayoutRect(
                contentLeft + measurement.HeaderWidth + measurement.ColumnOffset(colIndex),
                contentTop,
                measurement.ColumnWidthAt(colIndex),
                measurement.HeaderHeight);
            columnHeadings.Add(BuildHeadingCell(rect, CellAddress.NumberToColumnName(pageColumns[colIndex]), textMeasurer));
        }

        var rowHeadings = new List<PageHeadingCell>(pageRows.Count);
        for (var rowIndex = 0; rowIndex < pageRows.Count; rowIndex++)
        {
            var rect = new LayoutRect(
                contentLeft,
                contentTop + measurement.HeaderHeight + measurement.RowOffset(rowIndex),
                measurement.HeaderWidth,
                measurement.RowHeightAt(rowIndex));
            rowHeadings.Add(BuildHeadingCell(rect, pageRows[rowIndex].ToString(CultureInfo.InvariantCulture), textMeasurer));
        }

        return (columnHeadings, rowHeadings);
    }

    private static IReadOnlyList<PageChartBlock> BuildCharts(
        Workbook workbook,
        Sheet sheet,
        PageAxisSegment rowSegment,
        PageAxisSegment colSegment,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns,
        double gridLeft,
        double gridTop,
        PrintGridMeasurement measurement,
        ITextMeasurer textMeasurer)
    {
        if (sheet.Charts.Count == 0 || rowSegment.End < rowSegment.Start || colSegment.End < colSegment.Start)
            return [];

        var bodyRows = BuildSegmentIndexes(rowSegment);
        var bodyColumns = BuildSegmentIndexes(colSegment);
        if (bodyRows.Count == 0 || bodyColumns.Count == 0)
            return [];

        var titleRowCount = Math.Max(0, pageRows.Count - bodyRows.Count);
        var titleColumnCount = Math.Max(0, pageColumns.Count - bodyColumns.Count);
        var bodyGridLeft = gridLeft + measurement.ColumnOffset(titleColumnCount);
        var bodyGridTop = gridTop + measurement.RowOffset(titleRowCount);
        var bodyGridRect = new LayoutRect(
            bodyGridLeft,
            bodyGridTop,
            measurement.ColumnOffset(pageColumns.Count) - measurement.ColumnOffset(titleColumnCount),
            measurement.RowOffset(pageRows.Count) - measurement.RowOffset(titleRowCount));

        // Charts anchor at chart.Left/chart.Top, which are absolute pixel offsets from the sheet's
        // real (non-uniform, hidden-row/column-skipping) origin in XlsxDrawingAnchorApplier's
        // width-in-chars*8 convention — see ChartAnchorGeometry. That is a DIFFERENT pixel-per-character
        // convention than the print grid's own column/row measurement (measurement.ColumnOffset, built
        // from ColumnWidthPixelMapper's width*7+5 convention), so chart.Left/pageGridLeft (both *8-space)
        // must never be summed directly with bodyGridLeft/measurement (7x+5-space). ShouldPrintChart's
        // intersection test stays in the anchor's own *8 space (pageGridRect below), but the chart's
        // final on-page bounds are computed by first converting its anchor position into the grid's own
        // pixel space via ChartAnchorGeometry.ConvertColumnOffsetToGridSpace/ConvertRowOffsetToGridSpace,
        // then translating within that single, consistent space.
        var pageGridLeft = ChartAnchorGeometry.SumColumnPixels(sheet, 1, bodyColumns[0] - 1);
        var pageGridTop = ChartAnchorGeometry.SumRowPixels(sheet, 1, bodyRows[0] - 1);
        var pageGridRect = new LayoutRect(
            pageGridLeft,
            pageGridTop,
            bodyGridRect.Width,
            bodyGridRect.Height);
        var pageGridLeftInGridSpace = ChartAnchorGeometry.ConvertColumnOffsetToGridSpace(sheet, pageGridLeft);
        var pageGridTopInGridSpace = ChartAnchorGeometry.ConvertRowOffsetToGridSpace(sheet, pageGridTop);

        var cellLookup = BuildChartCellLookup(workbook, sheet, pageRows, pageColumns);
        var blocks = new List<PageChartBlock>();
        foreach (var chart in sheet.Charts)
        {
            if (!ShouldPrintChart(chart, pageGridRect))
                continue;

            var chartGridLeft = ChartAnchorGeometry.ConvertColumnOffsetToGridSpace(sheet, chart.Left);
            var chartGridTop = ChartAnchorGeometry.ConvertRowOffsetToGridSpace(sheet, chart.Top);
            var bounds = new LayoutRect(
                bodyGridLeft + chartGridLeft - pageGridLeftInGridSpace,
                bodyGridTop + chartGridTop - pageGridTopInGridSpace,
                chart.Width,
                chart.Height);
            var overlays = Contains(bodyGridRect, bounds)
                ? PrintChartTextOverlayPlanner.Build(
                    chart,
                    workbook.Theme,
                    bounds,
                    chartDataCells: null,
                    cellLookup,
                    (text, fontSize) => MeasureChartOverlayText(textMeasurer, text, fontSize))
                : [];

            blocks.Add(new PageChartBlock(
                chart.Id,
                bounds,
                ResolveChartFill(chart, workbook.Theme),
                ResolveChartOutline(chart, workbook.Theme),
                ResolveChartOutlineThickness(chart),
                overlays));
        }

        return blocks;
    }

    private static IReadOnlyList<uint> BuildSegmentIndexes(PageAxisSegment segment)
    {
        var indexes = new List<uint>((int)Math.Min(segment.End - segment.Start + 1, int.MaxValue));
        for (var index = segment.Start; index <= segment.End; index++)
            indexes.Add(index);
        return indexes;
    }

    private static bool ShouldPrintChart(ChartModel chart, LayoutRect pageGridRect)
    {
        if (!chart.IsVisible ||
            !double.IsFinite(chart.Left) ||
            !double.IsFinite(chart.Top) ||
            !double.IsFinite(chart.Width) ||
            !double.IsFinite(chart.Height) ||
            chart.Width <= 0 ||
            chart.Height <= 0)
        {
            return false;
        }

        return Intersects(new LayoutRect(chart.Left, chart.Top, chart.Width, chart.Height), pageGridRect);
    }

    private static bool Intersects(LayoutRect a, LayoutRect b) =>
        a.Left < b.Right &&
        a.Right > b.Left &&
        a.Top < b.Bottom &&
        a.Bottom > b.Top;

    private static bool Contains(LayoutRect outer, LayoutRect inner) =>
        inner.Left >= outer.Left &&
        inner.Top >= outer.Top &&
        inner.Right <= outer.Right &&
        inner.Bottom <= outer.Bottom;

    private static Dictionary<(uint Row, uint Col), DisplayCell> BuildChartCellLookup(
        Workbook workbook,
        Sheet sheet,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns)
    {
        var lookup = new Dictionary<(uint Row, uint Col), DisplayCell>();
        foreach (var row in pageRows)
            foreach (var column in pageColumns)
                AddDisplayCell(lookup, workbook, sheet, row, column);

        foreach (var chart in sheet.Charts)
        {
            var range = chart.DataRange;
            if (range.Start.Sheet != sheet.Id || range.End.Sheet != sheet.Id)
                continue;

            for (var row = range.Start.Row; row <= range.End.Row; row++)
                for (var column = range.Start.Col; column <= range.End.Col; column++)
                    AddDisplayCell(lookup, workbook, sheet, row, column);
        }

        return lookup;
    }

    private static void AddDisplayCell(
        IDictionary<(uint Row, uint Col), DisplayCell> lookup,
        Workbook workbook,
        Sheet sheet,
        uint row,
        uint column)
    {
        var address = new CellAddress(sheet.Id, row, column);
        var cell = sheet.GetCell(address);
        var styleId = cell?.StyleId ?? sheet.GetStyleOnly(row, column) ?? StyleId.Default;
        lookup[(row, column)] = new DisplayCell(
            row,
            column,
            cell?.Value,
            cell is null ? "" : FormatCellText(workbook, sheet, cell, workbook.GetStyle(styleId)),
            cell?.FormulaText,
            styleId,
            Error: null,
            workbook.GetStyle(styleId));
    }

    private static PrintChartOverlayTextMetrics MeasureChartOverlayText(
        ITextMeasurer textMeasurer,
        string text,
        double fontSize)
    {
        var size = textMeasurer.Measure(text, PrintChartTextOverlayPlanner.FontFamily, fontSize, bold: false, italic: false);
        return new PrintChartOverlayTextMetrics(size.Width, size.Width);
    }

    private static PresentationRgb ResolveChartFill(ChartModel chart, WorkbookTheme theme) =>
        PresentationRgb.FromCellColor(chart.ResolveChartAreaFillColor(theme) ?? CellColor.White);

    private static PresentationRgb ResolveChartOutline(ChartModel chart, WorkbookTheme theme) =>
        PresentationRgb.FromCellColor(chart.ResolveChartAreaBorderColor(theme) ?? new CellColor(217, 217, 217));

    private static double ResolveChartOutlineThickness(ChartModel chart) =>
        chart.ChartAreaBorderThickness is { } thickness && double.IsFinite(thickness) && thickness > 0
            ? thickness
            : 1.0;

    private static PageHeadingCell BuildHeadingCell(LayoutRect rect, string label, ITextMeasurer textMeasurer)
    {
        var origin = VerticallyCenteredOrigin(
            textMeasurer, label, PrintFontFamily, PrintFontSize, bold: false, italic: false,
            rect.Left + 2, rect.Top, rect.Height);
        return new PageHeadingCell(rect, label, origin);
    }

    private static (IReadOnlyList<PageHeaderFooterRun> Header, IReadOnlyList<PageHeaderFooterRun> Footer) BuildHeaderFooterRuns(
        Sheet sheet,
        WorksheetHeaderFooter header,
        WorksheetHeaderFooter footer,
        double pageW,
        double pageH,
        double marginLeft,
        double marginRight,
        double headerMargin,
        double footerMargin,
        string workbookName,
        string workbookDirectory,
        string sheetName,
        int pageNumber,
        int totalPages,
        DateTime now,
        ITextMeasurer textMeasurer)
    {
        // Band layout mirrors the source header/footer renderer: the header line sits at headerMargin
        // (minus a nominal line height) and the footer at the bottom inset by footerMargin; each line is
        // split into left/center/right thirds, with the inset following the align-with-margins flag.
        const double lineHeight = 16.0;
        var headerY = Math.Max(4, headerMargin - lineHeight);
        var footerY = Math.Max(4, pageH - footerMargin - lineHeight);
        var leftInset = sheet.HeaderFooterAlignWithMargins ? marginLeft : 0.3 * Dpi;
        var rightInset = sheet.HeaderFooterAlignWithMargins ? marginRight : 0.3 * Dpi;

        var headerRuns = BuildBandRuns(
            header, pageW, leftInset, rightInset, headerY, lineHeight,
            workbookName, workbookDirectory, sheetName, pageNumber, totalPages, now, textMeasurer);
        var footerRuns = BuildBandRuns(
            footer, pageW, leftInset, rightInset, footerY, lineHeight,
            workbookName, workbookDirectory, sheetName, pageNumber, totalPages, now, textMeasurer);
        return (headerRuns, footerRuns);
    }

    private static IReadOnlyList<PageHeaderFooterRun> BuildBandRuns(
        WorksheetHeaderFooter value,
        double pageW,
        double leftInset,
        double rightInset,
        double y,
        double lineHeight,
        string workbookName,
        string workbookDirectory,
        string sheetName,
        int pageNumber,
        int totalPages,
        DateTime now,
        ITextMeasurer textMeasurer)
    {
        var availableWidth = Math.Max(1, pageW - leftInset - rightInset);
        var sectionWidth = Math.Max(1, availableWidth / 3);
        var runs = new List<PageHeaderFooterRun>(3);

        AddBandRun(runs, value.Left, new LayoutRect(leftInset, y, sectionWidth, lineHeight),
            PageTextAlignment.Left, workbookName, workbookDirectory, sheetName, pageNumber, totalPages, now, textMeasurer);
        AddBandRun(runs, value.Center, new LayoutRect((pageW - sectionWidth) / 2, y, sectionWidth, lineHeight),
            PageTextAlignment.Center, workbookName, workbookDirectory, sheetName, pageNumber, totalPages, now, textMeasurer);
        AddBandRun(runs, value.Right, new LayoutRect(pageW - rightInset - sectionWidth, y, sectionWidth, lineHeight),
            PageTextAlignment.Right, workbookName, workbookDirectory, sheetName, pageNumber, totalPages, now, textMeasurer);
        return runs;
    }

    private static void AddBandRun(
        ICollection<PageHeaderFooterRun> runs,
        string raw,
        LayoutRect bounds,
        PageTextAlignment alignment,
        string workbookName,
        string workbookDirectory,
        string sheetName,
        int pageNumber,
        int totalPages,
        DateTime now,
        ITextMeasurer textMeasurer)
    {
        var formattedRuns = PagePrintTextPlanner.TokenizeSectionText(
            raw, pageNumber, totalPages, workbookName, workbookDirectory, sheetName, now);
        if (formattedRuns.Count == 0)
            return;

        // Flatten the text from all runs for sizing and PDF overlay purposes.
        var text = formattedRuns.Count == 1
            ? formattedRuns[0].Text
            : string.Concat(formattedRuns.Select(r => r.Text));
        if (string.IsNullOrEmpty(text))
            return;

        // Use the first run's style for the baseline vertical-centering measurement.
        var firstRun = formattedRuns[0];
        var origin = VerticallyCenteredOrigin(
            textMeasurer,
            text,
            firstRun.FontName ?? PrintFontFamily,
            firstRun.FontSize ?? PrintFontSize,
            firstRun.Bold,
            firstRun.Italic,
            bounds.Left + 2,
            bounds.Top,
            bounds.Height);
        runs.Add(new PageHeaderFooterRun(bounds, text, formattedRuns, alignment, origin));
    }

    /// <summary>
    /// Substitutes the header/footer tokens, mirroring the source print renderer's token handling:
    /// the bracketed <c>&amp;[Page]</c>/<c>&amp;[Pages]</c>/<c>&amp;[Date]</c>/<c>&amp;[Time]</c>/
    /// <c>&amp;[File]</c>/<c>&amp;[Path]</c>/<c>&amp;[Tab]</c> forms and the short
    /// <c>&amp;P</c>/<c>&amp;N</c>/<c>&amp;D</c>/<c>&amp;T</c>/<c>&amp;F</c>/<c>&amp;Z</c>/<c>&amp;A</c>
    /// forms (page number, page count, date, time, file name, path, and sheet name). Format-style codes
    /// are stripped and picture tokens are removed (pictures are out of scope for this content model).
    /// </summary>
    public static string ExpandHeaderFooterText(
        string text,
        int pageNumber,
        int totalPages,
        string workbookName,
        string sheetName,
        DateTime now) =>
        PagePrintTextPlanner.ExpandHeaderFooterText(
            text,
            pageNumber,
            totalPages,
            workbookName,
            workbookDirectory: "",
            sheetName,
            now);

    private static (WorksheetHeaderFooter Header, WorksheetHeaderFooter Footer) ResolveHeaderFooterForPage(
        Sheet sheet,
        int pageNumber)
    {
        if (sheet.DifferentFirstPageHeaderFooter && pageNumber == (sheet.FirstPageNumber ?? 1))
            return (sheet.FirstPageHeader, sheet.FirstPageFooter);

        if (sheet.DifferentOddEvenHeaderFooter && pageNumber % 2 == 0)
            return (sheet.EvenPageHeader, sheet.EvenPageFooter);

        return (sheet.PageHeader, sheet.PageFooter);
    }

    private static string FormatCellText(Workbook workbook, Sheet sheet, Cell cell, CellStyle style)
    {
        var raw = sheet.ShowFormulas && cell.FormulaText is not null
            ? "=" + cell.FormulaText
            : NumberFormatter.FormatWithColor(
                cell.Value,
                style.NumberFormat,
                workbook.IndexedColors,
                workbook.Theme,
                workbook.Uses1904DateSystem).Text;

        return FormatPrintedCellText(raw, sheet.PrintErrorValue);
    }

    private static string FormatPrintedCellText(string displayText, WorksheetPrintErrorValue printErrorValue)
        => PagePrintTextPlanner.FormatPrintedCellText(displayText, printErrorValue);

    private static PresentationRgb? ResolveFill(CellStyle style, WorkbookTheme theme)
    {
        var fill = style.ResolveFillColor(theme);
        return fill is { } color ? PresentationRgb.FromCellColor(color) : null;
    }

    private static PageTextFont ResolveFont(CellStyle style, WorkbookTheme theme) =>
        new(
            style.ResolveEffectiveFontName(theme),
            style.FontSize,
            style.Bold,
            style.Italic,
            PresentationRgb.FromCellColor(style.ResolveFontColor(theme)));

    private static PageTextAlignment ResolveAlignment(CellStyle style, Cell? cell) =>
        style.HorizontalAlignment switch
        {
            HorizontalAlignment.Center => PageTextAlignment.Center,
            HorizontalAlignment.Right => PageTextAlignment.Right,
            HorizontalAlignment.Left => PageTextAlignment.Left,
            // General alignment: numbers/dates align right, everything else left (the spreadsheet default).
            _ => IsRightAlignedByValue(cell) ? PageTextAlignment.Right : PageTextAlignment.Left
        };

    private static bool IsRightAlignedByValue(Cell? cell) =>
        cell?.Value is NumberValue or DateTimeValue;

    private static LayoutPoint VerticallyCenteredOrigin(
        ITextMeasurer textMeasurer,
        string text,
        string fontFamily,
        double fontSize,
        bool bold,
        bool italic,
        double left,
        double top,
        double height)
    {
        var size = textMeasurer.Measure(text, fontFamily, fontSize, bold, italic);
        return new LayoutPoint(left, top + Math.Max(0, (height - size.Height) / 2));
    }

    private static PageCellBorders ResolveBorders(CellStyle style) =>
        new(
            ResolveEdge(style.BorderTop),
            ResolveEdge(style.BorderRight),
            ResolveEdge(style.BorderBottom),
            ResolveEdge(style.BorderLeft));

    private static PageBorderEdge ResolveEdge(CellBorder border) =>
        border.Style == BorderStyle.None
            ? PageBorderEdge.None
            : new PageBorderEdge(border.Style, PresentationRgb.FromCellColor(border.Color));

    private static IReadOnlyDictionary<uint, int> BuildPositionLookup(IReadOnlyList<uint> indexes)
    {
        var lookup = new Dictionary<uint, int>(indexes.Count);
        for (var i = 0; i < indexes.Count; i++)
            lookup[indexes[i]] = i;

        return lookup;
    }

    /// <summary>
    /// Converts the sheet's character-unit column widths to pixels (matching
    /// <see cref="PagePaginationPlanner.AverageColumnWidthPixels"/>'s per-column conversion), so
    /// <see cref="PrintLayoutPlanner.MeasurePrintableGrid(double, double, IReadOnlyList{uint}, IReadOnlyList{uint}, IReadOnlyDictionary{uint, double}, IReadOnlyDictionary{uint, double}, bool)"/>
    /// can measure the page grid from real per-column pixel sizes.
    /// </summary>
    private static IReadOnlyDictionary<uint, double> BuildColumnWidthsPixels(Sheet sheet)
    {
        var pixels = new Dictionary<uint, double>(sheet.ColumnWidths.Count);
        foreach (var (col, width) in sheet.ColumnWidths)
            pixels[col] = ColumnWidthPixelMapper.ColumnWidthToPixels(width);

        return pixels;
    }
}
