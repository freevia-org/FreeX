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
/// drawing shapes, comments, hyperlinks, header/footer pictures, and rich text wrapping/trimming.
/// The cell grid, gridlines, headings, text boxes, chart object blocks, raster picture blocks (see
/// <see cref="PagePictureLayoutPlanner"/> for that block's exact scope), and header/footer text are
/// produced.
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
    /// <param name="overridePageNumber">
    /// When set, used as the page's &amp;P header/footer number instead of the default
    /// <c>sheet.FirstPageNumber + pageIndex</c>. Callers that paginate a sheet's multiple print
    /// areas as separate <paramref name="pagePlan"/>s (each restarting <paramref name="pageIndex"/>
    /// at 0) pass a running index across all of that sheet's areas here, matching
    /// <c>WorkbookPdfContentBuilder.ResolveEffectiveSheetPageNumber</c>'s continuous per-sheet
    /// counter for the real print/PDF export.
    /// </param>
    /// <param name="overrideTotalPages">
    /// When set, used as the page's &amp;N total-page count instead of the default
    /// <c>pagePlan.PageCount</c> (that single area's own page count). Pass the aggregate page
    /// count across all of a sheet's print areas so multi-area previews agree with
    /// <c>WorkbookPdfContentBuilder.ResolveEffectiveSheetTotalPages</c>.
    /// </param>
    public static PageContentLayout? Build(
        Workbook workbook,
        Sheet sheet,
        PagePaginationResult pagePlan,
        int pageIndex,
        ITextMeasurer textMeasurer,
        DateTime? now = null,
        string workbookDirectory = "",
        int? overridePageNumber = null,
        int? overrideTotalPages = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(pagePlan);
        ArgumentNullException.ThrowIfNull(textMeasurer);

        if (pageIndex < 0 || pageIndex >= pagePlan.PageCount)
            return null;

        var (rowSegment, colSegment) = ResolvePageSegments(sheet.PageOrder, pagePlan, pageIndex);
        var pageRows = BuildAxisIndexes(sheet.PrintTitleRows, rowSegment, sheet.IsRowEffectivelyHidden);
        var pageColumns = BuildAxisIndexes(sheet.PrintTitleColumns, colSegment, sheet.IsColEffectivelyHidden);
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

        // R104-presentation-vertical-center-body-height-1: the defensive residual-overflow shrink
        // (ResolveScaleRatio below) and the vertical 'Center on page' offset (yOffset below) must both
        // measure against the sheet's actual printable BODY height -- pageH minus whichever is larger
        // of the top margin / header margin, and whichever is larger of the bottom margin / footer
        // margin (PageGeometryRules.ResolveBodyEdge) -- not the plain margin box `printableH` above.
        // contentTop already anchors the grid's top edge at this body-adjusted position (bodyTop, the
        // R99 fix below); before this fix, yOffset was derived from the plain-margin printableH, so a
        // Header (or Footer) margin larger than the Top (or Bottom) margin overshot the centered
        // position by (headerMargin - topMargin)/2 pixels -- agreeing with neither Excel nor this app's
        // own PDF export path (WorkbookPdfContentBuilder.cs's contentHeight is this exact body-adjusted
        // height and drives its centerYOffset identically).
        var headerMarginPx = sheet.HeaderMargin * Dpi;
        var footerMarginPx = sheet.FooterMargin * Dpi;
        var bodyTop = PageGeometryRules.ResolveBodyEdge(marginTop, headerMarginPx);
        var bodyBottom = PageGeometryRules.ResolveBodyEdge(marginBottom, footerMarginPx);
        var bodyHeight = Math.Max(0.0, pageH - bodyTop - bodyBottom);

        var columnWidthsPixels = WorksheetPrintPageContentPlanner.BuildColumnWidthsPixels(sheet);
        var measurement = PrintLayoutPlanner.MeasurePrintableGrid(
            printableW,
            bodyHeight,
            pageRows,
            pageColumns,
            sheet.RowHeights,
            columnWidthsPixels,
            sheet.PrintHeadings);

        // Excel's Page Setup > Scaling ('Adjust to N% normal size' or 'Fit to W pages wide by H
        // tall') shrinks/grows every printed element -- gridlines, cell text, headings, charts, text
        // boxes, pictures -- in direct proportion to the resolved scale; Print Preview always shows
        // exactly what will print. pagePlan.EffectiveScalePercent is PagePaginationPlanner's single
        // source of truth (the same value that decided this page's row/column capacity), so resolve
        // it into a scaleRatio here and bake it into the grid measurement BEFORE any geometry is
        // derived from it -- every cell/gridline/heading position and size below reads only from
        // `measurement`, so scaling it once is the single choke point every renderer that consumes
        // this portable model (interactive print preview, and any future renderer) automatically
        // inherits, instead of requiring each call site to remember to apply a ratio. Mirrors the
        // source desktop print renderer's own scaleRatio and the page-setup-aware PDF export path's
        // ResolveScaleRatio/ComputeActualGridSizes.
        var unscaledPrintedWidth = measurement.HeaderWidth + measurement.TotalColumnWidth(pageColumns.Count);
        var unscaledPrintedHeight = measurement.HeaderHeight + measurement.TotalRowHeight(pageRows.Count);
        var scaleRatio = WorksheetPrintPageContentPlanner.ResolveScaleRatio(
            pagePlan.EffectiveScalePercent, unscaledPrintedWidth, unscaledPrintedHeight, printableW, bodyHeight);
        measurement = WorksheetPrintPageContentPlanner.ScaleMeasurement(measurement, scaleRatio);

        var printedWidth = measurement.HeaderWidth + measurement.TotalColumnWidth(pageColumns.Count);
        var printedHeight = measurement.HeaderHeight + measurement.TotalRowHeight(pageRows.Count);
        var xOffset = sheet.CenterHorizontallyOnPage ? Math.Max(0, (printableW - printedWidth) / 2) : 0;
        var yOffset = sheet.CenterVerticallyOnPage ? Math.Max(0, (bodyHeight - printedHeight) / 2) : 0;
        var contentLeft = marginLeft + xOffset;
        // R99-presentation-header-band-preview-1: mirrors PagePaginationPlanner.
        // CalculatePageCapacityDetail's bodyTopInches = Math.Max(margins.Top, headerMarginInches) --
        // the header/footer margin is the distance from the page edge to the header/footer band, which
        // sits WITHIN the top margin band as long as it doesn't exceed it, but Excel pushes the grid's
        // top edge down to the header margin (not the plain top margin) once the header margin is the
        // larger of the two, so the printed grid never starts above the header text's own band. This
        // print-PREVIEW content model (PageContentRenderModelBuilder.Build, consumed by
        // PrintPreviewInstructionBuilder to paint the actual preview canvas on every shell) used the
        // plain top margin here, so it disagreed with the row capacity the pagination planner (and the
        // desktop print-renderer's rendering geometry, R99-app-host-header-footer-margin-overlap-1)
        // already computed for this same page -- the header text visually collided with the first
        // printed row in print preview whenever Header margin &gt; Top margin, even though the actual
        // desktop print/PDF-export output (once separately fixed) did not.
        var contentTop = bodyTop + yOffset;
        var gridLeft = contentLeft + measurement.HeaderWidth;
        var gridTop = contentTop + measurement.HeaderHeight;
        var gridBounds = new LayoutRect(
            gridLeft,
            gridTop,
            measurement.TotalColumnWidth(pageColumns.Count),
            measurement.TotalRowHeight(pageRows.Count));

        var pageNumber = overridePageNumber ?? (sheet.FirstPageNumber ?? 1) + pageIndex;
        var totalPages = overrideTotalPages ?? pagePlan.PageCount;

        var cells = BuildCells(
            workbook,
            sheet,
            pageRows,
            pageColumns,
            gridLeft,
            gridTop,
            measurement,
            scaleRatio,
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
            measurement,
            scaleRatio);

        // R127-presentation-draft-quality-preview-1: Sheet.PrintDraftQuality ("Draft quality" in Page
        // Setup > Sheet) suppresses charts and raster pictures on the WPF native print/PDF path
        // (PrintRenderer.HeaderFooter.cs's `!draftQuality` guard), but this portable content model --
        // consumed by the Avalonia interactive print-preview canvas (PrintPreviewInstructionBuilder) as
        // well as the portable PDF-export path (WorkbookPdfContentBuilder) -- built both lists
        // unconditionally, so the on-screen "Print Preview" a Linux/macOS user sees before exporting
        // never reflected the Draft Quality checkbox at all. Gated once here at this single choke point
        // so every consumer of the returned PageContentLayout automatically inherits it, instead of
        // requiring each renderer to remember its own guard (text boxes stay unconditional, matching
        // the WPF path -- vector text content, not "graphics").
        var charts = sheet.PrintDraftQuality
            ? []
            : BuildCharts(
                workbook,
                sheet,
                rowSegment,
                colSegment,
                pageRows,
                pageColumns,
                gridLeft,
                gridTop,
                measurement,
                scaleRatio,
                textMeasurer);

        var pictures = sheet.PrintDraftQuality
            ? []
            : PagePictureLayoutPlanner.Build(
                sheet.Pictures,
                pageRows,
                pageColumns,
                gridLeft,
                gridTop,
                measurement,
                scaleRatio);

        var headerFooter = WorksheetPrintPageContentPlanner.ResolveHeaderFooterVariant(sheet, pageNumber);
        var resolvedNow = now ?? DateTime.Now;
        var (headerRuns, footerRuns) = BuildHeaderFooterRuns(
            sheet,
            headerFooter.Header,
            headerFooter.Footer,
            pageW,
            pageH,
            marginLeft,
            marginRight,
            marginBottom,
            headerMarginPx,
            footerMarginPx,
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
            footerRuns,
            pictures,
            []);
    }

    /// <summary>Scales a resolved cell font's size by the page's Scale%/Fit-to-pages ratio.</summary>
    private static PageTextFont ScaleFont(PageTextFont font, double scaleRatio) =>
        scaleRatio == 1.0 ? font : font with { FontSize = font.FontSize * scaleRatio };

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

    private static IReadOnlyList<uint> BuildAxisIndexes(
        WorksheetRepeatRange? repeat,
        PageAxisSegment segment,
        Func<uint, bool> isHidden)
    {
        // Title (repeat) rows/columns are reprinted ahead of the page body. The pagination segment
        // already spans the page's whole printed extent; reprint only the repeat indexes that fall
        // before the segment so they are not duplicated when the segment itself includes them. A
        // hidden/filtered/group-collapsed row or column inside the repeat range is excluded, matching
        // PrintLayoutPlanner.BuildTitleIndexes (the source of truth the WPF print path reads from).
        var indexes = new List<uint>();
        if (repeat is { } range && range.Start >= 1 && range.End >= range.Start)
        {
            for (var index = range.Start; index <= range.End; index++)
            {
                if (index < segment.Start && !isHidden(index))
                    indexes.Add(index);
            }
        }

        // segment.Indexes is the page's explicit, gap-aware body index list -- already hidden/
        // filtered/group-collapsed-excluded by PrintLayoutPlanner.BuildRowPlans/BuildColumnPlans.
        // Do NOT reconstruct this by iterating segment.Start..segment.End: that range assumes
        // contiguity and would silently reinstate any hidden row/column sitting in the interior of
        // the page (see PageAxisSegment's doc comment).
        indexes.AddRange(segment.Indexes);

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
        double scaleRatio,
        ITextMeasurer textMeasurer)
    {
        var theme = workbook.Theme;
        var cells = new List<PageCellBlock>();

        var conditionalFormats = new ConditionalFormatRenderEvaluator(sheet, workbook);
        var validationCircleCells = sheet.ValidationCircleCells is { Count: > 0 } circled
            ? circled.Where(address => address.Sheet == sheet.Id).ToHashSet()
            : new HashSet<CellAddress>();

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
                    width = WorksheetPrintCellGeometryPlanner.MeasureMergedColumnSpan(
                        measurement,
                        pageColumns,
                        colIndex,
                        mergedRegion.End.Col);
                    height = WorksheetPrintCellGeometryPlanner.MeasureMergedRowSpan(
                        measurement,
                        pageRows,
                        rowIndex,
                        mergedRegion.End.Row);
                }

                var cfResult = conditionalFormats.HasRules
                    ? conditionalFormats.Evaluate(address, cell?.Value ?? BlankValue.Instance)
                    : default;

                var fill = cfResult.Style?.FillColor is { } cfFillColor
                    ? PresentationRgb.FromCellColor(cfFillColor)
                    : ResolveFill(style, theme);
                var font = ApplyConditionalFontDelta(ScaleFont(ResolveFont(style, theme), scaleRatio), cfResult.Style);

                // Match the target-width overflow indicator ('####') the interactive grid and WPF
                // print path apply for numbers/dates too narrow for their printed column -- otherwise
                // an over-wide value renders unclipped, overlapping the neighbor cell. `width` is
                // already scaled (it comes from the scaled `measurement`), but the character-width
                // estimate below is calibrated against the unscaled print font's fixed pixels/char
                // ratio, so divide the scale back out first -- otherwise a shrunk page (e.g. Scale%
                // 50, whose font is shrunk by the same ratio) would falsely show MORE '####' overflow
                // than an unscaled page, when Excel's proportional scaling never changes how much text
                // visually fits.
                var targetWidthCharacters = EstimateCharacterWidth(scaleRatio > 0 ? width / scaleRatio : width);
                var text = cell is not null
                    ? FormatCellText(workbook, sheet, cell, style, targetWidthCharacters, cfResult.Style)
                    : "";
                var borders = ApplyConditionalBorderDelta(ResolveBorders(style), cfResult.Style);
                var hasValidationCircle = validationCircleCells.Contains(address);
                if (string.IsNullOrEmpty(text) && fill is null && !borders.HasAny &&
                    cfResult.DataBar is null && cfResult.IconSet is null && !hasValidationCircle)
                {
                    continue;
                }

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
                    font,
                    ResolveAlignment(style, cell),
                    borders,
                    textOrigin,
                    cfResult.DataBar,
                    cfResult.IconSet,
                    hasValidationCircle));
            }
        }

        return cells;
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
        double scaleRatio,
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

        // Charts anchor at chart.Left/chart.Top/chart.Width/chart.Height, which are absolute pixel
        // offsets/extents from the sheet's real (non-uniform, hidden-row/column-skipping) origin in
        // XlsxDrawingAnchorApplier's width-in-chars*8 convention — see ChartAnchorGeometry. That is a
        // DIFFERENT pixel-per-character convention than the print grid's own column/row measurement
        // (measurement.ColumnOffset, built from ColumnWidthPixelMapper's width*7+5 convention), so
        // chart.Left/pageGridLeft (both *8-space) must never be summed directly with
        // bodyGridLeft/measurement (7x+5-space), and chart.Width/chart.Height must never be used
        // unconverted alongside a grid-space position either. ShouldPrintChart's intersection test stays
        // in the anchor's own *8 space (pageGridRect below), but the chart's final on-page bounds are
        // computed by first converting its anchor position AND extent into the grid's own pixel space via
        // ChartAnchorGeometry.ConvertColumnOffsetToGridSpace/ConvertRowOffsetToGridSpace and
        // ConvertColumnExtentToGridSpace/ConvertRowExtentToGridSpace, then translating within that single,
        // consistent space.
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
            var chartGridWidth = ChartAnchorGeometry.ConvertColumnExtentToGridSpace(sheet, chart.Left, chart.Width);
            var chartGridHeight = ChartAnchorGeometry.ConvertRowExtentToGridSpace(sheet, chart.Top, chart.Height);

            // chartGridLeft/Top/Width/Height above are resolved in the grid's real, UNSCALED pixel
            // convention (ChartAnchorGeometry converts from the sheet's actual anchor geometry, not
            // from `measurement`), while bodyGridLeft/bodyGridTop already carry the page's scaleRatio
            // (they are derived from the pre-scaled `measurement`). Scale only the chart's own offset
            // from the body origin and its own extent -- not bodyGridLeft/bodyGridTop themselves,
            // which are already in the scaled coordinate space -- so a chart's position and size shrink
            // or grow in the same proportion as the surrounding grid under Scale%/Fit-to-pages.
            var bounds = new LayoutRect(
                bodyGridLeft + (chartGridLeft - pageGridLeftInGridSpace) * scaleRatio,
                bodyGridTop + (chartGridTop - pageGridTopInGridSpace) * scaleRatio,
                chartGridWidth * scaleRatio,
                chartGridHeight * scaleRatio);
            var overlays = Contains(bodyGridRect, bounds)
                ? PrintChartTextOverlayPlanner.Build(
                    chart,
                    workbook.Theme,
                    bounds,
                    chartDataCells: null,
                    cellLookup,
                    (text, fontSize) => MeasureChartOverlayText(textMeasurer, text, fontSize),
                    sheet)
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

    // segment.Indexes is already the page's explicit, gap-aware body index list (hidden/filtered/
    // group-collapsed rows or columns already excluded) -- see BuildAxisIndexes above for why this
    // must never be reconstructed by iterating segment.Start..segment.End.
    private static IReadOnlyList<uint> BuildSegmentIndexes(PageAxisSegment segment) => segment.Indexes;

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

            // A chart with "Show data in hidden rows and columns" off must not read hidden cells at
            // all -- matching ViewportService.BuildChartDataCells, which omits them from the on-screen
            // chart's data cells. Skipping them here keeps this shared, per-sheet lookup a UNION over
            // the sheet's charts (a sibling chart that DOES show hidden data still contributes them),
            // and PrintChartTextOverlayPlanner.BuildCellLookup then re-applies the per-chart filter so
            // a permissive sibling can never widen a strict chart's printed data labels.
            for (var row = range.Start.Row; row <= range.End.Row; row++)
            {
                if (!chart.ShowDataInHiddenRowsAndColumns && sheet.IsRowEffectivelyHidden(row))
                    continue;

                for (var column = range.Start.Col; column <= range.End.Col; column++)
                {
                    if (!chart.ShowDataInHiddenRowsAndColumns && sheet.IsColEffectivelyHidden(column))
                        continue;

                    AddDisplayCell(lookup, workbook, sheet, row, column);
                }
            }
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

    // R44-meta-1: "No Line" is an explicit user choice -- returning 0 here makes the PDF/print
    // stroke a no-op (WorkbookPdfContentBuilder.AddStrokeRect skips drawing when lineWidth <= 0),
    // fully suppressing the chart-area border instead of falling back to the default outline.
    //
    // NOTE: the matching "No Fill" case (ChartAreaNoFill) cannot be fixed from this file alone --
    // PageChartBlock.Fill (PageContentRenderModel.cs) is a non-nullable PresentationRgb with no
    // alpha channel, and its only consumer (WorkbookPdfContentBuilder.AddFillRect at the chart call
    // site) always paints it opaquely. Representing "no fill" would require adding a nullable/alpha
    // field to PageChartBlock and updating that unconditional call site, both outside this bucket's
    // owned files (see PageContentRenderModel.cs / WorkbookPdfContentBuilder.cs). Deferred.
    private static double ResolveChartOutlineThickness(ChartModel chart) =>
        chart.IsChartAreaLineSuppressed
            ? 0
            : chart.ChartAreaBorderThickness is { } thickness && double.IsFinite(thickness) && thickness > 0
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
        double marginBottom,
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
        const double lineHeight = 16.0;
        var headerBand = WorksheetPrintHeaderFooterGeometryPlanner.BuildBand(
            header,
            WorksheetHeaderFooterPictureSet.Empty,
            pageW,
            pageH,
            marginLeft,
            marginRight,
            marginBottom,
            headerMargin,
            sheet.HeaderFooterAlignWithMargins,
            isFooter: false,
            draftQuality: false,
            fontScale: 1.0,
            baseLineHeight: lineHeight,
            sizeToContent: false);
        var footerBand = WorksheetPrintHeaderFooterGeometryPlanner.BuildBand(
            footer,
            WorksheetHeaderFooterPictureSet.Empty,
            pageW,
            pageH,
            marginLeft,
            marginRight,
            marginBottom,
            footerMargin,
            sheet.HeaderFooterAlignWithMargins,
            isFooter: true,
            draftQuality: false,
            fontScale: 1.0,
            baseLineHeight: lineHeight,
            sizeToContent: false);

        var headerRuns = BuildBandRuns(
            header, headerBand,
            workbookName, workbookDirectory, sheetName, pageNumber, totalPages, now, textMeasurer);
        var footerRuns = BuildBandRuns(
            footer, footerBand,
            workbookName, workbookDirectory, sheetName, pageNumber, totalPages, now, textMeasurer);
        return (headerRuns, footerRuns);
    }

    private static IReadOnlyList<PageHeaderFooterRun> BuildBandRuns(
        WorksheetHeaderFooter value,
        WorksheetPrintHeaderFooterBandGeometry geometry,
        string workbookName,
        string workbookDirectory,
        string sheetName,
        int pageNumber,
        int totalPages,
        DateTime now,
        ITextMeasurer textMeasurer)
    {
        var runs = new List<PageHeaderFooterRun>(3);

        AddBandRun(runs, value.Left, geometry.Left,
            PageTextAlignment.Left, workbookName, workbookDirectory, sheetName, pageNumber, totalPages, now, textMeasurer);
        AddBandRun(runs, value.Center, geometry.Center,
            PageTextAlignment.Center, workbookName, workbookDirectory, sheetName, pageNumber, totalPages, now, textMeasurer);
        AddBandRun(runs, value.Right, geometry.Right,
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

    private static string FormatCellText(
        Workbook workbook,
        Sheet sheet,
        Cell cell,
        CellStyle style,
        int? targetWidthCharacters = null,
        ConditionalFormatStylePlan? cfStyle = null)
    {
        // A matched conditional-format rule's own number format (e.g. Excel's "Format Cells > Number"
        // panel inside New Formatting Rule) overrides the cell's raw/unconditional number format --
        // mirrors ViewportConditionalFormatEvaluator.MergeStyles, which the on-screen grid consults via
        // ViewportService, so print/PDF renders the same accounting/percentage/date format the grid
        // shows instead of the cell's plain format.
        var numberFormat = cfStyle?.NumberFormat ?? style.NumberFormat;

        // Excel's "Show a zero in cells that have zero value" sheet option (sheetView showZeros):
        // when off, a cell whose value is numeric zero prints/exports as blank, mirroring the
        // interactive grid's expected behavior. Formula-text display mode (ShowFormulas) is
        // unaffected since it shows the literal formula, not the computed value. UNLESS the
        // effective number format (the CF rule's, when one is matched, else the cell's own) defines
        // an explicit third (zero) section (e.g. "0;-0;\"zero\""), in which case that section's own
        // rendering governs and the sheet-level preference is not consulted -- mirrors
        // ViewportService.GetDisplayText's NumberFormatHasExplicitZeroSection guard (same r51 fix,
        // screen/print parity).
        if (!sheet.ShowFormulas && !sheet.ShowZeros && cell.Value is NumberValue { Value: 0 } &&
            !NumberFormatHasExplicitZeroSection(numberFormat))
            return string.Empty;

        var raw = sheet.ShowFormulas && cell.FormulaText is not null
            ? "=" + cell.FormulaText
            : targetWidthCharacters is { } width
                ? NumberFormatter.FormatWithColor(
                    cell.Value,
                    numberFormat,
                    width,
                    workbook.IndexedColors,
                    workbook.Theme,
                    workbook.Uses1904DateSystem,
                    suppressWidthOverflowIndicator: style.ShrinkToFit).Text
                : NumberFormatter.FormatWithColor(
                    cell.Value,
                    numberFormat,
                    workbook.IndexedColors,
                    workbook.Theme,
                    workbook.Uses1904DateSystem).Text;

        return FormatPrintedCellText(raw, sheet.PrintErrorValue);
    }

    private static string FormatPrintedCellText(string displayText, WorksheetPrintErrorValue printErrorValue)
        => PagePrintTextPlanner.FormatPrintedCellText(displayText, printErrorValue);

    /// <summary>
    /// True when <paramref name="numberFormat"/> defines a third (zero-specific) section --
    /// e.g. "#,##0;(#,##0);\"-\"" -- meaning the format itself dictates how a zero value
    /// renders and the sheet's ShowZeros preference must not override it. Mirrors
    /// <c>ViewportService.NumberFormatHasExplicitZeroSection</c> (the interactive grid's sibling
    /// r51 fix) so print/PDF and the on-screen grid agree on zero-valued cells with a custom
    /// zero section. Sections are separated by top-level ';' characters (not inside a quoted
    /// literal or a [bracketed] directive); an empty/General format has a single (implicit)
    /// section.
    /// </summary>
    private static bool NumberFormatHasExplicitZeroSection(string? numberFormat)
    {
        if (string.IsNullOrEmpty(numberFormat))
            return false;

        return NumberFormatSectionTokenizer.Count(numberFormat) >= 3;
    }

    /// <summary>
    /// Converts a printed column's pixel width to an approximate character-width budget, matching
    /// <c>ViewportService.EstimateCharacterWidth</c> (the interactive grid's '####' overflow-detection
    /// width estimate: ~7 pixels/character above 12px, else pixels/12) so a value too wide for its
    /// printed column renders Excel's '#' overflow indicator here too instead of the unclipped text
    /// overlapping the neighbor cell. <paramref name="pixelWidth"/> is already merge-aware (the
    /// caller passes the merged cell's combined column width), matching Excel sizing a merged cell's
    /// displayed value against the merged range's combined width.
    /// </summary>
    private static int EstimateCharacterWidth(double pixelWidth)
    {
        if (!double.IsFinite(pixelWidth) || pixelWidth <= 0)
            return 1;

        var width = pixelWidth <= 12 ? pixelWidth / 12.0 : (pixelWidth - 5.0) / 7.0;
        return Math.Max(1, (int)Math.Round(width, MidpointRounding.AwayFromZero));
    }

    private static PageTextFont ApplyConditionalFontDelta(PageTextFont baseFont, ConditionalFormatStylePlan? delta) =>
        delta is { } d && (d.FontColor.HasValue || d.Bold || d.Italic)
            ? baseFont with
            {
                Bold = baseFont.Bold || d.Bold,
                Italic = baseFont.Italic || d.Italic,
                Color = d.FontColor is { } color ? PresentationRgb.FromCellColor(color) : baseFont.Color
            }
            : baseFont;

    /// <summary>
    /// Applies a matched conditional-format rule's per-edge border override onto the cell's raw
    /// resolved borders -- mirrors ViewportConditionalFormatEvaluator.MergeStyles's border handling
    /// (each edge from the CF wins only when the CF actually specifies a visible border on that
    /// edge), so print/PDF draws the same CF border the on-screen grid does instead of silently
    /// falling back to the cell's raw/unconditional borders.
    /// </summary>
    private static PageCellBorders ApplyConditionalBorderDelta(PageCellBorders baseBorders, ConditionalFormatStylePlan? delta)
    {
        if (delta is not { } d)
            return baseBorders;

        return new PageCellBorders(
            d.BorderTop.Style != BorderStyle.None ? ResolveEdge(d.BorderTop) : baseBorders.Top,
            d.BorderRight.Style != BorderStyle.None ? ResolveEdge(d.BorderRight) : baseBorders.Right,
            d.BorderBottom.Style != BorderStyle.None ? ResolveEdge(d.BorderBottom) : baseBorders.Bottom,
            d.BorderLeft.Style != BorderStyle.None ? ResolveEdge(d.BorderLeft) : baseBorders.Left);
    }

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

}
