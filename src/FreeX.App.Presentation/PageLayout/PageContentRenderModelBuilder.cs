using System.Globalization;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.Text;
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
/// drawing objects, charts, comments, hyperlinks, header/footer pictures, and rich text
/// wrapping/trimming — only the cell grid, gridlines, headings, text boxes, and header/footer text
/// are produced.
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
    public static PageContentLayout? Build(
        Workbook workbook,
        Sheet sheet,
        PagePaginationResult pagePlan,
        int pageIndex,
        ITextMeasurer textMeasurer,
        DateTime? now = null)
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

        var measurement = PrintLayoutPlanner.MeasurePrintableGrid(
            printableW,
            printableH,
            (uint)pageRows.Count,
            (uint)pageColumns.Count,
            sheet.PrintHeadings);

        var rowHeight = measurement.RowHeight;
        var colWidth = measurement.ColumnWidth;
        var printedWidth = measurement.HeaderWidth + colWidth * pageColumns.Count;
        var printedHeight = measurement.HeaderHeight + rowHeight * pageRows.Count;
        var xOffset = sheet.CenterHorizontallyOnPage ? Math.Max(0, (printableW - printedWidth) / 2) : 0;
        var yOffset = sheet.CenterVerticallyOnPage ? Math.Max(0, (printableH - printedHeight) / 2) : 0;
        var contentLeft = marginLeft + xOffset;
        var contentTop = marginTop + yOffset;
        var gridLeft = contentLeft + measurement.HeaderWidth;
        var gridTop = contentTop + measurement.HeaderHeight;
        var gridBounds = new LayoutRect(gridLeft, gridTop, colWidth * pageColumns.Count, rowHeight * pageRows.Count);

        var pageNumber = (sheet.FirstPageNumber ?? 1) + pageIndex;
        var totalPages = pagePlan.PageCount;

        var cells = BuildCells(
            workbook,
            sheet,
            pageRows,
            pageColumns,
            gridLeft,
            gridTop,
            colWidth,
            rowHeight,
            textMeasurer);

        var gridLines = sheet.PrintGridlines
            ? BuildGridLines(gridBounds, pageRows.Count, pageColumns.Count, colWidth, rowHeight)
            : [];

        var (columnHeadings, rowHeadings) = sheet.PrintHeadings
            ? BuildHeadings(measurement, pageRows, pageColumns, contentLeft, contentTop, colWidth, rowHeight, textMeasurer)
            : ([], []);

        var textBoxes = PageTextBoxLayoutPlanner.Build(
            sheet.TextBoxes,
            workbook.Theme,
            pageRows,
            pageColumns,
            gridLeft,
            gridTop,
            colWidth,
            rowHeight);

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
        double colWidth,
        double rowHeight,
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

                var x = gridLeft + colIndex * colWidth;
                var y = gridTop + rowIndex * rowHeight;
                var width = colWidth;
                var height = rowHeight;
                if (merge is { } mergedRegion)
                {
                    (width, height) = MeasureMergedExtent(
                        mergedRegion,
                        rowIndexes,
                        columnIndexes,
                        colIndex,
                        rowIndex,
                        colWidth,
                        rowHeight);
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
        double colWidth,
        double rowHeight)
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
            (lastColIndex - anchorColIndex + 1) * colWidth,
            (lastRowIndex - anchorRowIndex + 1) * rowHeight);
    }

    private static IReadOnlyList<PageGridLine> BuildGridLines(
        LayoutRect gridBounds,
        int rowCount,
        int columnCount,
        double colWidth,
        double rowHeight)
    {
        var lines = new List<PageGridLine>(rowCount + columnCount + 2);
        for (var colIndex = 0; colIndex <= columnCount; colIndex++)
        {
            var x = gridBounds.Left + colIndex * colWidth;
            lines.Add(new PageGridLine(new LayoutPoint(x, gridBounds.Top), new LayoutPoint(x, gridBounds.Bottom)));
        }

        for (var rowIndex = 0; rowIndex <= rowCount; rowIndex++)
        {
            var y = gridBounds.Top + rowIndex * rowHeight;
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
        double colWidth,
        double rowHeight,
        ITextMeasurer textMeasurer)
    {
        var columnHeadings = new List<PageHeadingCell>(pageColumns.Count);
        for (var colIndex = 0; colIndex < pageColumns.Count; colIndex++)
        {
            var rect = new LayoutRect(
                contentLeft + measurement.HeaderWidth + colIndex * colWidth,
                contentTop,
                colWidth,
                measurement.HeaderHeight);
            columnHeadings.Add(BuildHeadingCell(rect, CellAddress.NumberToColumnName(pageColumns[colIndex]), textMeasurer));
        }

        var rowHeadings = new List<PageHeadingCell>(pageRows.Count);
        for (var rowIndex = 0; rowIndex < pageRows.Count; rowIndex++)
        {
            var rect = new LayoutRect(
                contentLeft,
                contentTop + measurement.HeaderHeight + rowIndex * rowHeight,
                measurement.HeaderWidth,
                rowHeight);
            rowHeadings.Add(BuildHeadingCell(rect, pageRows[rowIndex].ToString(CultureInfo.InvariantCulture), textMeasurer));
        }

        return (columnHeadings, rowHeadings);
    }

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
            workbookName, sheetName, pageNumber, totalPages, now, textMeasurer);
        var footerRuns = BuildBandRuns(
            footer, pageW, leftInset, rightInset, footerY, lineHeight,
            workbookName, sheetName, pageNumber, totalPages, now, textMeasurer);
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
            PageTextAlignment.Left, workbookName, sheetName, pageNumber, totalPages, now, textMeasurer);
        AddBandRun(runs, value.Center, new LayoutRect((pageW - sectionWidth) / 2, y, sectionWidth, lineHeight),
            PageTextAlignment.Center, workbookName, sheetName, pageNumber, totalPages, now, textMeasurer);
        AddBandRun(runs, value.Right, new LayoutRect(pageW - rightInset - sectionWidth, y, sectionWidth, lineHeight),
            PageTextAlignment.Right, workbookName, sheetName, pageNumber, totalPages, now, textMeasurer);
        return runs;
    }

    private static void AddBandRun(
        ICollection<PageHeaderFooterRun> runs,
        string raw,
        LayoutRect bounds,
        PageTextAlignment alignment,
        string workbookName,
        string sheetName,
        int pageNumber,
        int totalPages,
        DateTime now,
        ITextMeasurer textMeasurer)
    {
        var text = ExpandHeaderFooterText(raw, pageNumber, totalPages, workbookName, sheetName, now);
        if (string.IsNullOrEmpty(text))
            return;

        var origin = VerticallyCenteredOrigin(
            textMeasurer, text, PrintFontFamily, PrintFontSize, bold: false, italic: false,
            bounds.Left + 2, bounds.Top, bounds.Height);
        runs.Add(new PageHeaderFooterRun(bounds, text, alignment, origin));
    }

    /// <summary>
    /// Substitutes the header/footer tokens, mirroring the source print renderer's token handling:
    /// the bracketed <c>&amp;[Page]</c>/<c>&amp;[Pages]</c>/<c>&amp;[Date]</c>/<c>&amp;[Time]</c>/
    /// <c>&amp;[File]</c>/<c>&amp;[Path]</c>/<c>&amp;[Tab]</c> forms and the short
    /// <c>&amp;P</c>/<c>&amp;N</c>/<c>&amp;D</c>/<c>&amp;T</c>/<c>&amp;F</c>/<c>&amp;Z</c>/<c>&amp;A</c>
    /// forms (page number, page count, date, time, file name, path, and sheet name). Picture tokens are
    /// stripped (pictures are out of scope for this content model).
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
                workbook.Theme).Text;

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
}
