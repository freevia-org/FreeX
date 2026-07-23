using System.Globalization;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Text;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Free.Shared.Pdf;

namespace FreeX.App.Services;

/// <summary>
/// FreeX's Workbook → shared <see cref="PdfContentDocument"/> adapter. This is the FreeX-specific
/// half of the PDF export: it turns a workbook + export plan into the app-agnostic draw-op page
/// model (geometry, styles, number formatting, header/footer) that any shared PDF backend — the
/// dependency-free <see cref="PortablePdfWriter"/> or the Unicode-capable Skia writer — can emit.
/// Both FreeX exporters build identical pages from this builder, so portable and Skia output share
/// one geometry.
///
/// <para>
/// <b>Page-setup-aware path</b> (<see cref="BuildWithPageSetup"/>): honors each sheet's
/// <see cref="Sheet.PaperSize"/>, <see cref="Sheet.PageOrientation"/>, <see cref="Sheet.PageMargins"/>,
/// <see cref="Sheet.HeaderMargin"/>/<see cref="Sheet.FooterMargin"/>, <see cref="Sheet.PrintGridlines"/>,
/// <see cref="Sheet.ScaleToFit"/>, and header/footer format strings. Each page emits the correct
/// MediaBox (PDF points) and renders the cell grid, optional gridlines, and header/footer text bands.
/// </para>
/// <para>
/// <b>Legacy path</b> (<see cref="Build"/>): accepts caller-supplied <see cref="PortablePdfDocumentOptions"/>
/// so existing tests and usages that supply fixed geometry are unaffected.
/// </para>
/// </summary>
public static class WorkbookPdfContentBuilder
{
    private static readonly PdfColor GridStrokeColor  = new(196, 202, 210);
    private static readonly PdfColor TitleFillColor   = new(238, 242, 247);
    private static readonly PdfColor HeaderTextColor  = new(31, 41, 55);
    private static readonly PdfColor FooterTextColor  = new(97, 106, 117);
    private static readonly PdfColor GridLineColor    = new(180, 185, 190);
    private static readonly PdfColor HeadingFillColor   = new(242, 242, 242);
    private static readonly PdfColor HeadingBorderColor = new(211, 211, 211);

    // R79-services-pagesetup-print-5-2: row/column heading gutter size in PDF points, converted from
    // the same fixed 40px (row-number gutter width) / 20px (column-letter band height) pixel constants
    // PrintLayoutPlanner.MeasurePrintableGrid uses for the WPF print/preview path -- unscaled by the
    // sheet's Scale%/Fit-to-pages ratio, matching PrintRenderer.HeaderFooter.cs (the heading gutter is
    // reserved from the printable area, not shrunk/grown with the grid content).
    private const double HeadingGutterWidthPx  = 40.0;
    private const double HeadingGutterHeightPx = 20.0;
    private const double HeadingFontSize = 9.0;

    // -----------------------------------------------------------------------
    // Page-setup-aware path (new)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a PDF document where each page's dimensions, margins, scale, gridlines, and
    /// header/footer are derived from the exporting sheet's OOXML page setup. Prefers this path
    /// over <see cref="Build(Workbook,PortablePdfExportPlan,PortablePdfDocumentOptions)"/> for
    /// the Avalonia/Skia PDF export.
    /// </summary>
    public static PdfContentDocument BuildWithPageSetup(
        Workbook workbook,
        PortablePdfExportPlan exportPlan,
        string workbookDirectory = "")
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(exportPlan);

        var pages = exportPlan.PageRequests
            .Select(request => BuildPageWithPageSetup(workbook, exportPlan, request, workbookDirectory))
            .ToArray();
        return new PdfContentDocument(pages);
    }

    /// <summary>
    /// Builds one PDF page honoring the sheet's page setup.
    /// </summary>
    /// <param name="workbookDirectory">
    /// Directory that contains the workbook file, with a trailing path separator (e.g.
    /// <c>C:\Docs\</c>). Substituted for <c>&amp;Z</c> / <c>&amp;[Path]</c>; pass an empty string
    /// when the workbook is unsaved.
    /// </param>
    public static PdfContentPage BuildPageWithPageSetup(
        Workbook workbook,
        PortablePdfExportPlan exportPlan,
        PortablePdfExportPageRequest request,
        string workbookDirectory = "")
    {
        var contentPlan = PortablePdfPageContentPlanner.CreatePlan(workbook, request);
        if (!contentPlan.IsReady)
            throw new InvalidOperationException(contentPlan.StatusText);

        // Resolve the sheet that actually owns this page's print range rather than indexing by
        // request.SheetIndex -- SheetIndex is the position of the print AREA within the export
        // plan's flattened SheetPlans list, which is not the same as the sheet's index in the
        // workbook once any earlier sheet has more than one configured print area (see N45/N46).
        var sheet = workbook.GetSheet(request.PrintRange.Start.Sheet)
            ?? workbook.GetSheetAt(request.SheetIndex);

        var (pageW, pageH, mL, mR, mT, mB, headerBandPt, footerBandPt) =
            SheetPdfPageSetupResolver.ComputePdfGeometry(sheet);

        // Effective scale for rendering (percent / 100).
        var scaleRatio = ResolveScaleRatio(sheet, exportPlan, request);

        // Content rect: page minus margins. y-origin is bottom-left in PDF space.
        var contentLeft   = mL;
        var contentBottom = mB;
        var contentRight  = pageW - mR;
        var contentTop    = pageH - mT;
        var contentWidth  = Math.Max(1.0, contentRight - contentLeft);
        var contentHeight = Math.Max(1.0, contentTop - contentBottom);

        // Header band: sits between the top of the page and the content rect.
        // In PDF y-up: header band top = pageH - headerEdge, header band bottom = pageH - mT.
        // Footer band: sits between the bottom of the content rect and the bottom of the page.
        var headerEdgePt  = sheet.HeaderMargin * SheetPdfPageSetupResolver.PdfPointsPerInch;
        var footerEdgePt  = sheet.FooterMargin * SheetPdfPageSetupResolver.PdfPointsPerInch;
        _ = headerBandPt;
        _ = footerBandPt;

        var ops = new List<PdfDrawOp>();

        // ── Cell grid ──────────────────────────────────────────────────────────
        var columnCount = Math.Max(1, contentPlan.ColumnCount);
        var rowCount    = Math.Max(1, contentPlan.RowCount);

        // R79-services-pagesetup-print-5-2: reserve a row-number/column-letter heading gutter from the
        // content rect when the sheet has "Print row and column headings" enabled, matching
        // PrintLayoutPlanner.MeasurePrintableGrid's fixed 40px/20px reservation for the WPF print path
        // -- the gutter eats into the space available for the cell grid rather than being layered on
        // top of it.
        const double ptPerPx = SheetPdfPageSetupResolver.PdfPointsPerInch / 96.0;
        var headingWidthPt  = sheet.PrintHeadings ? HeadingGutterWidthPx  * ptPerPx : 0.0;
        var headingHeightPt = sheet.PrintHeadings ? HeadingGutterHeightPx * ptPerPx : 0.0;
        var gridAvailableWidth  = Math.Max(1.0, contentWidth  - headingWidthPt);
        var gridAvailableHeight = Math.Max(1.0, contentHeight - headingHeightPt);

        // Distribute available width/height proportionally to actual column/row sizes, then apply
        // the sheet's Scale%/Fit-to-pages ratio directly to the grid geometry -- matching the WPF
        // PrintRenderer path (PrintRenderer.HeaderFooter.cs), which always applies
        // ScaleTransform(scaleRatio, scaleRatio) once scaleRatio&lt;1, regardless of whether the
        // unscaled content already fits the page. Excel shrinks/grows every printed element in
        // direct proportion to the configured scale, not merely "when it would otherwise overflow".
        var (colWidths, rowHeights, effectiveScaleRatio) = ComputeActualGridSizes(
            sheet, contentPlan, gridAvailableWidth, gridAvailableHeight, scaleRatio);

        // R79-services-pagesetup-print-5-1: Page Setup > Margins > Center on page (Horizontally /
        // Vertically) offsets the whole printed block (heading gutter + grid) within the content rect,
        // matching PageContentRenderModelBuilder.cs's xOffset/yOffset for the WPF print-preview path --
        // pre-fix, the grid was always pinned flush to the top-left content margin regardless of these
        // flags.
        var printedWidth  = headingWidthPt  + colWidths.Sum();
        var printedHeight = headingHeightPt + rowHeights.Sum();
        var centerXOffset = sheet.CenterHorizontallyOnPage ? Math.Max(0.0, (contentWidth  - printedWidth)  / 2.0) : 0.0;
        var centerYOffset = sheet.CenterVerticallyOnPage   ? Math.Max(0.0, (contentHeight - printedHeight) / 2.0) : 0.0;
        var offsetContentLeft = contentLeft + centerXOffset;
        var offsetContentTop  = contentTop  - centerYOffset;

        // Grid origin: top-left corner in PDF y-up (top = high y), shifted past the heading gutter (if
        // any) and by the center-on-page offset (if any).
        var gridLeft = offsetContentLeft + headingWidthPt;
        var gridTop  = offsetContentTop  - headingHeightPt;   // PDF y-up: top edge = high y value

        // Build a cumulative column-x lookup (left edge of each column).
        var colXs  = BuildCumulative(colWidths,  gridLeft);
        var rowYs  = BuildCumulativeDown(rowHeights, gridTop);  // row y-bottom (PDF y-up, going down)

        // ── Draw cell fills and text ───────────────────────────────────────────
        foreach (var cell in contentPlan.Cells)
        {
            var rowIndex = FindRowIndex(contentPlan.Rows, cell.Row);
            var colIndex = FindColumnIndex(contentPlan.Columns, cell.Column);
            if (rowIndex < 0 || colIndex < 0)
                continue;

            if (rowIndex >= rowHeights.Length || colIndex >= colWidths.Length)
                continue;

            var x = colXs[colIndex];
            var w = colWidths[colIndex];
            var h = rowHeights[rowIndex];
            var y = rowYs[rowIndex];  // bottom of this row in PDF y-up

            var style = workbook.GetStyle(cell.StyleId);
            // R72-render-cf-visual-4-1: a conditional-format fill (color scale or a matched
            // highlight/AboveAverage rule) overrides the cell's raw style fill, matching the WPF PDF
            // path (which reads DisplayCell.Style already merged with CF by the viewport) and the
            // Avalonia print-preview path (PageContentRenderModelBuilder).
            var fill = cell.ConditionalFillColor ?? style.ResolveFillColor(workbook.Theme);

            // B&W mode: suppress colored cell fills (treat as white / transparent).
            // The page background is already white so simply omitting the fill rect is correct.
            var bw = sheet.PrintBlackAndWhite;
            if (!bw && (fill is not null || cell.IsTitle))
                ops.Add(new PdfFillRect(x, y, w, h, fill is { } fillColor ? ToPdfColor(fillColor) : TitleFillColor));

            if (!string.IsNullOrEmpty(cell.DisplayText))
            {
                // Font size honors the sheet's Scale%/Fit-to-pages ratio in both directions (shrink AND
                // grow, matching the grid geometry above) -- Excel's Page Setup scaling shrinks/grows
                // printed text along with everything else, matching the WPF print path's single
                // ScaleTransform over the whole content area (which also rescales text overlay font
                // sizes uncapped -- see RescaleTextOverlays). Clamp is applied to the unscaled size
                // first (matching the sheet's authored font sizes) and the result is then scaled, so a
                // 50% scale on a 10pt font yields 5pt rather than re-clamping back up to the 7pt floor,
                // and a 200% scale on a 10pt font yields 20pt.
                //
                // Use effectiveScaleRatio (scaleRatio further adjusted by ComputeActualGridSizes'
                // defensive fit-to-page correction), not the raw scaleRatio -- otherwise a fit-to-N-pages
                // sheet whose already-resolved page count matches its target (so CalculateEffectiveScalePercent
                // reports ~100%) shrinks the cell grid geometry via the defensive correction but leaves
                // text rendered at the unscaled size, producing oversized text that overflows its
                // now-smaller cell.
                var textScale = effectiveScaleRatio;
                var fontSize  = Math.Clamp(style.FontSize, 7, 10) * textScale;
                var fontFace  = cell.IsTitle || style.Bold ? PdfFontFace.Bold : PdfFontFace.Regular;
                // B&W mode: force font colour to black regardless of style.
                var fontColor = bw ? PdfColor.Black : ToPdfColor(style.ResolveFontColor(workbook.Theme));
                var displayText = PortablePdfWinAnsiTextCapability.Truncate(cell.DisplayText, 64);

                // Resolve the cell's effective horizontal alignment the same way the on-screen
                // GridView viewport does (GridView.Rendering.cs + CellTextOrientationLayoutPlanner):
                // General resolves to Right for numeric/date content (left in a right-to-left
                // context) and Left otherwise; explicit Left/Center/Right/Justify/Distributed/Fill
                // are honored as authored. Without this, every cell -- including right-aligned
                // numbers and centered titles -- rendered flush-left in the exported PDF (R53
                // fix-one-path-miss-twin-sweep-4).
                var rawCell = sheet.GetCell(cell.Row, cell.Column);
                var isNumeric = rawCell?.Value is NumberValue or DateTimeValue;
                var isEffectivelyRightToLeft = CellTextOrientationLayoutPlanner.ResolveIsEffectivelyRightToLeft(
                    style.ReadingOrder, sheet.IsRightToLeft);
                var effectiveAlign = CellTextOrientationLayoutPlanner.ResolveEffectiveHorizontalAlignment(
                    style.HorizontalAlignment, isNumeric, isEffectivelyRightToLeft);

                // Format Cells > Alignment > Indent, converted from the same 8px-per-level unit the
                // GridView viewport uses (GridView.Rendering.cs) into PDF points and scaled with the
                // sheet's Scale%/Fit-to-pages ratio like everything else in this cell rect.
                var indentPt = style.IndentLevel * 8.0 * (SheetPdfPageSetupResolver.PdfPointsPerInch / 96.0) * textScale;

                var textWidth = PortablePdfTextMeasurer.Instance.Measure(
                    displayText, null, fontSize, cell.IsTitle || style.Bold, italic: false).Width;

                var textX = effectiveAlign switch
                {
                    // Right: anchor the text's right edge inside the cell (minus a matching pad and
                    // the indent). Deliberately not clamped to the cell's left edge -- a too-wide
                    // right-aligned value should overflow leftward, exactly like Excel and the
                    // viewport (see CalculateLayout's identical comment).
                    HorizontalAlignment.Right => x + w - textWidth - (2.0 * textScale) - indentPt,
                    HorizontalAlignment.Center
                        or HorizontalAlignment.Justify
                        or HorizontalAlignment.Distributed => x + ((w - textWidth) / 2.0),
                    // Fill: matches the on-screen GridView (DoesHorizontalAlignmentConsumeIndent excludes
                    // Fill) and the canonical CellTextOrientationLayoutPlanner (Fill => cellRect.Left + 2,
                    // no indent term) -- Excel's Format Cells indent stepper is disabled for Fill, so any
                    // leftover nonzero IndentLevel on a Fill-aligned cell must not shift the text.
                    HorizontalAlignment.Fill => x + (2.0 * textScale),
                    _ => x + (2.0 * textScale) + indentPt
                };

                // Text inset/baseline scale with the grid so text stays proportionally placed
                // within its (now possibly shrunk) cell rect.
                var baseline = y + (3.0 * textScale);
                ops.Add(new PdfText(
                    textX,
                    baseline,
                    fontSize,
                    fontFace,
                    fontColor,
                    displayText));
            }
        }

        // ── Gridlines ─────────────────────────────────────────────────────────
        if (sheet.PrintGridlines)
        {
            var gridBottom = rowYs.Length > 0 ? rowYs[rowCount - 1] : contentBottom;
            var gridRight  = colXs.Length > 0 ? colXs[columnCount - 1] + colWidths[columnCount - 1] : contentRight;

            // Horizontal lines (one per row boundary + bottom).
            for (var ri = 0; ri <= rowCount; ri++)
            {
                double lineY;
                if (ri == 0)
                    lineY = gridTop;
                else if (ri <= rowYs.Length)
                    lineY = rowYs[ri - 1];  // bottom of row ri-1
                else
                    break;

                ops.Add(new PdfLine(gridLeft, lineY, gridRight, lineY, GridLineColor, 0.4));
            }

            // Vertical lines (one per column boundary + right).
            for (var ci = 0; ci <= columnCount; ci++)
            {
                double lineX;
                if (ci < colXs.Length)
                    lineX = colXs[ci];
                else if (ci == columnCount && colXs.Length > 0)
                    lineX = colXs[columnCount - 1] + colWidths[columnCount - 1];
                else
                    break;

                ops.Add(new PdfLine(lineX, gridTop, lineX, gridBottom, GridLineColor, 0.4));
            }
        }

        // ── Row/column headings ──────────────────────────────────────────────
        // R79-services-pagesetup-print-5-2: draw the A/B/C.../1/2/3... heading gutter when the sheet's
        // Page Setup > Sheet > "Row and column headings" is enabled, matching PrintRenderer.Headings.cs
        // (DrawPrintHeadings) for the WPF print path.
        if (sheet.PrintHeadings)
        {
            AddPrintHeadings(ops, offsetContentLeft, offsetContentTop, headingWidthPt, headingHeightPt,
                colWidths, rowHeights, colXs, rowYs, contentPlan);
        }

        // ── Header band ────────────────────────────────────────────────────────
        // N45/N46: page.SheetPageNumber is always 1-based per print area (PrintPageGridPlanner
        // numbers every area's pages 1..N independently), so it neither honors sheet.FirstPageNumber
        // nor continues across a sheet's multiple print areas. Resolve the printed page number the
        // same way WorksheetPrintRenderPlanner.TryBuild does for WPF: a single counter, seeded from
        // FirstPageNumber, running across every page that belongs to this sheet (all its print areas,
        // in export order).
        AddVectorDrawingOps(workbook, sheet, exportPlan, request, ops, pageW, pageH);

        var pageNumber = ResolveEffectiveSheetPageNumber(exportPlan, request, sheet);
        var (header, footer) = ResolveHeaderFooterForPage(sheet, pageNumber);
        // &N (total pages) must reset per sheet, matching Excel and the WPF PrintRenderer path
        // (RenderWorksheet computes totalPages = printPlan.GridPageCount + comment pages, scoped to
        // that one sheet) -- NOT exportPlan.TotalPageCount, which sums every sheet in the export and
        // would leak whole-workbook page counts into every sheet's &N when printing/exporting more
        // than one sheet at once (O41).
        var totalPages = ResolveEffectiveSheetTotalPages(exportPlan, sheet);

        // Header text: rendered just below the header margin from the top of the page.
        var headerY = pageH - headerEdgePt - 8;   // baseline approx 8pt below header edge
        RenderHeaderFooterBand(ops, header, pageW, mL, mR, headerY, 8,
            workbook.Name, workbookDirectory, sheet.Name, pageNumber, totalPages, HeaderTextColor);

        // Footer text: rendered just above the footer edge from the bottom.
        var footerY = footerEdgePt + 2;            // baseline approx 2pt above footer edge
        RenderHeaderFooterBand(ops, footer, pageW, mL, mR, footerY, 8,
            workbook.Name, workbookDirectory, sheet.Name, pageNumber, totalPages, FooterTextColor);

        return new PdfContentPage(pageW, pageH, ops);
    }

    // -----------------------------------------------------------------------
    // Legacy path (unchanged API — options supplied by caller)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds the full draw-op document using caller-supplied <paramref name="options"/>. Assumes
    /// <paramref name="exportPlan"/> is ready (callers validate); throws if a page's content plan
    /// is not ready.
    /// </summary>
    public static PdfContentDocument Build(
        Workbook workbook,
        PortablePdfExportPlan exportPlan,
        PortablePdfDocumentOptions options)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(exportPlan);
        ArgumentNullException.ThrowIfNull(options);

        var pages = exportPlan.PageRequests
            .Select(request => BuildPage(workbook, exportPlan, request, options))
            .ToArray();
        return new PdfContentDocument(pages);
    }

    public static PdfContentPage BuildPage(
        Workbook workbook,
        PortablePdfExportPlan exportPlan,
        PortablePdfExportPageRequest request,
        PortablePdfDocumentOptions options)
    {
        var contentPlan = PortablePdfPageContentPlanner.CreatePlan(workbook, request);
        if (!contentPlan.IsReady)
            throw new InvalidOperationException(contentPlan.StatusText);

        var ops = new List<PdfDrawOp>();
        var title = string.IsNullOrWhiteSpace(workbook.Name) ? "FreeX Workbook" : workbook.Name.Trim();
        ops.Add(new PdfText(
            options.MarginPoints,
            options.PageHeightPoints - options.MarginPoints,
            14,
            PdfFontFace.Bold,
            HeaderTextColor,
            title));
        ops.Add(new PdfText(
            options.MarginPoints,
            options.PageHeightPoints - options.MarginPoints - 18,
            9,
            PdfFontFace.Regular,
            FooterTextColor,
            $"{request.SheetName} - sheet page {request.SheetPageNumber} - export page {request.ExportPageNumber} of {exportPlan.TotalPageCount}"));

        var columnCount = Math.Max(1, contentPlan.ColumnCount);
        var availableWidth = options.PageWidthPoints - (options.MarginPoints * 2);
        var columnWidth = ResolveColumnWidth(availableWidth, columnCount, options);
        var gridTop = options.PageHeightPoints - options.MarginPoints - options.HeaderHeightPoints;
        var gridLeft = options.MarginPoints;

        foreach (var cell in contentPlan.Cells)
        {
            var rowIndex = FindRowIndex(contentPlan.Rows, cell.Row);
            var columnIndex = FindColumnIndex(contentPlan.Columns, cell.Column);
            if (rowIndex < 0 || columnIndex < 0)
                continue;

            var x = gridLeft + (columnIndex * columnWidth);
            var y = gridTop - ((rowIndex + 1) * options.RowHeightPoints);
            var style = workbook.GetStyle(cell.StyleId);
            // R72-render-cf-visual-4-1: same conditional-format fill override as the page-setup-aware
            // path above.
            var fill = cell.ConditionalFillColor ?? style.ResolveFillColor(workbook.Theme);
            if (fill is not null || cell.IsTitle)
                ops.Add(new PdfFillRect(x, y, columnWidth, options.RowHeightPoints, fill is { } fillColor ? ToPdfColor(fillColor) : TitleFillColor));

            ops.Add(new PdfStrokeRect(x, y, columnWidth, options.RowHeightPoints, GridStrokeColor, 0.5));
            if (string.IsNullOrEmpty(cell.DisplayText))
                continue;

            var fontSize = Math.Clamp(style.FontSize, 7, 10);
            var fontFace = cell.IsTitle || style.Bold ? PdfFontFace.Bold : PdfFontFace.Regular;
            var fontColor = ToPdfColor(style.ResolveFontColor(workbook.Theme));
            ops.Add(new PdfText(
                x + 4,
                y + Math.Max(7, options.RowHeightPoints - 14),
                fontSize,
                fontFace,
                fontColor,
                PortablePdfWinAnsiTextCapability.Truncate(cell.DisplayText, options.MaximumCellTextLength)));
        }

        ops.Add(new PdfText(
            options.MarginPoints,
            options.MarginPoints - 12,
            8,
            PdfFontFace.Regular,
            FooterTextColor,
            $"FreeX portable PDF - {request.SheetName} page {request.SheetPageNumber}"));

        return new PdfContentPage(options.PageWidthPoints, options.PageHeightPoints, ops);
    }

    // -----------------------------------------------------------------------
    // Page-setup helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resolves the sheet's Scale%/Fit-to-pages setting to a single shrink ratio for this page, using
    /// <see cref="PagePaginationPlanner.CalculateEffectiveScalePercent"/> as the single source of truth
    /// so this PDF path can never silently disagree with the neutral pagination plan (O42) -- it feeds
    /// the same actual row/column page counts that were used to slice this sheet into pages, rather
    /// than re-deriving an independent ratio here.
    /// </summary>
    private static double ResolveScaleRatio(
        Sheet sheet,
        PortablePdfExportPlan exportPlan,
        PortablePdfExportPageRequest request)
    {
        var sheetPlan = exportPlan.ExportPrintPlan.SheetPlans[request.SheetIndex];
        var effectiveScalePercent = PagePaginationPlanner.CalculateEffectiveScalePercent(
            sheet.ScaleToFit,
            sheetPlan.RowPageCount,
            sheetPlan.ColumnPageCount);

        return Math.Max(0.1, effectiveScalePercent / 100.0);
    }

    /// <summary>
    /// Computes per-column widths and per-row heights in PDF points for the content plan.
    /// Sizes start from the sheet's actual (unscaled) column/row dimensions, are shrunk to fit
    /// <paramref name="availableWidth"/>/<paramref name="availableHeight"/> only if they would
    /// otherwise overflow the page, and are then multiplied by <paramref name="scaleRatio"/> so the
    /// sheet's Page Setup &gt; Scaling (Scale%/Fit-to-pages) always shrinks the grid geometry in
    /// direct proportion to the configured scale -- matching the WPF PrintRenderer path, which
    /// applies its ScaleTransform unconditionally once scaleRatio&lt;1, never only "on overflow".
    /// </summary>
    private static (double[] ColWidths, double[] RowHeights, double EffectiveScaleRatio) ComputeActualGridSizes(
        Sheet sheet,
        PortablePdfPageContentPlan contentPlan,
        double availableWidth,
        double availableHeight,
        double scaleRatio)
    {
        const double layoutDpi = 96.0;
        const double ptPerPx   = SheetPdfPageSetupResolver.PdfPointsPerInch / layoutDpi;

        // Convert actual column widths from character units → pixels → points.
        var colWidthsPt = new double[contentPlan.ColumnCount];
        var totalColWidthPt = 0.0;
        for (var i = 0; i < contentPlan.Columns.Count; i++)
        {
            var col = contentPlan.Columns[i].Column;
            var chars = sheet.ColumnWidths.TryGetValue(col, out var w) && w > 0
                ? w
                : sheet.DefaultColumnWidth;
            var px  = Math.Max(4.0, ColumnWidthPixelMapper.ColumnWidthToPixels(chars));
            var pt  = px * ptPerPx;
            colWidthsPt[i]   = pt;
            totalColWidthPt += pt;
        }

        // Convert actual row heights from pixels → points.
        var rowHeightsPt = new double[contentPlan.RowCount];
        var totalRowHeightPt = 0.0;
        for (var i = 0; i < contentPlan.Rows.Count; i++)
        {
            var row = contentPlan.Rows[i].Row;
            var px  = sheet.RowHeights.TryGetValue(row, out var h) && h > 0 ? h : sheet.DefaultRowHeight;
            var pt  = Math.Max(1.0, px * ptPerPx);
            rowHeightsPt[i]   = pt;
            totalRowHeightPt += pt;
        }

        // Apply the sheet's configured Scale%/Fit-to-pages ratio directly and unconditionally first,
        // for both shrink (&lt;100%) and grow (&gt;100%) -- matching the WPF PrintRenderer path, whose
        // single ScaleTransform(scaleRatio, scaleRatio) is pushed whenever scaleRatio != 1.0
        // (PrintRenderer.HeaderFooter.cs:138), not merely when it shrinks. This is what makes
        // "Adjust to 50% normal size" visibly shrink a grid whose real size already fit the page, and
        // "Adjust to 200% normal size" visibly grow one, matching Excel: Scale% is a direct multiplier
        // on every printed element, not merely a repagination hint that only matters once content
        // overflows.
        if (scaleRatio != 1.0)
        {
            for (var i = 0; i < colWidthsPt.Length; i++)
                colWidthsPt[i] *= scaleRatio;
            for (var i = 0; i < rowHeightsPt.Length; i++)
                rowHeightsPt[i] *= scaleRatio;
            totalColWidthPt  *= scaleRatio;
            totalRowHeightPt *= scaleRatio;
        }

        // Defensive fit-to-page shrink: even after applying the configured scale, guard against
        // residual overflow (e.g. a merged/oversized row that still doesn't fit) the same way the
        // legacy path always has, but relative to the now-already-scaled sizes so a page whose
        // scaled content exactly matches the available budget is never shrunk a second time.
        // R18-print-pagination-exact-2: use a SINGLE uniform scale -- the smaller of the width and
        // height overflow ratios -- applied to BOTH axes, mirroring PrintRenderer.HeaderFooter.cs's
        // uniform scaleRatio (and Excel's own fit-to-page behavior). Shrinking width/height
        // independently distorts the aspect ratio (e.g. columns squished to fit while rows keep
        // their full scaled height), which never happens on the WPF print path this PDF path mirrors.
        var widthFitScale = totalColWidthPt > 0 && totalColWidthPt > availableWidth
            ? availableWidth / totalColWidthPt
            : 1.0;
        var heightFitScale = totalRowHeightPt > 0 && totalRowHeightPt > availableHeight
            ? availableHeight / totalRowHeightPt
            : 1.0;
        var uniformFitScale = Math.Min(widthFitScale, heightFitScale);

        if (uniformFitScale < 1.0)
        {
            for (var i = 0; i < colWidthsPt.Length; i++)
                colWidthsPt[i] *= uniformFitScale;
            for (var i = 0; i < rowHeightsPt.Length; i++)
                rowHeightsPt[i] *= uniformFitScale;
        }

        // Surface the defensive correction to the caller so text rendered inside these cells can be
        // scaled by the SAME ratio the grid geometry actually ended up using, not just the raw
        // configured scaleRatio (see the R50 pagination-3-1 fix at the textScale call site).
        return (colWidthsPt, rowHeightsPt, scaleRatio * uniformFitScale);
    }

    /// <summary>
    /// Builds cumulative left-x positions for each column from a starting x.
    /// </summary>
    private static double[] BuildCumulative(double[] widths, double startX)
    {
        var xs = new double[widths.Length];
        var x = startX;
        for (var i = 0; i < widths.Length; i++)
        {
            xs[i] = x;
            x += widths[i];
        }

        return xs;
    }

    /// <summary>
    /// Builds bottom-y positions for each row going down from the top edge (PDF y-up).
    /// Row 0's bottom = topY - rowHeight[0]; row 1's bottom = topY - rowHeight[0] - rowHeight[1]; etc.
    /// </summary>
    private static double[] BuildCumulativeDown(double[] heights, double topY)
    {
        var ys = new double[heights.Length];
        var y = topY;
        for (var i = 0; i < heights.Length; i++)
        {
            y -= heights[i];
            ys[i] = y;
        }

        return ys;
    }

    /// <summary>
    /// Draws the row-number / column-letter heading gutter for one page, matching
    /// PrintRenderer.Headings.cs's <c>DrawPrintHeadings</c> for the WPF print path: a light-gray fill +
    /// border behind each heading cell (plus the top-left corner box), with the column letter / row
    /// number centered inside.
    /// </summary>
    private static void AddPrintHeadings(
        List<PdfDrawOp> ops,
        double contentLeft,
        double contentTop,
        double headingWidthPt,
        double headingHeightPt,
        double[] colWidths,
        double[] rowHeights,
        double[] colXs,
        double[] rowYs,
        PortablePdfPageContentPlan contentPlan)
    {
        var bandTop = contentTop;
        var bandBottom = contentTop - headingHeightPt;

        // Top-left corner box (blank -- no label).
        ops.Add(new PdfFillRect(contentLeft, bandBottom, headingWidthPt, headingHeightPt, HeadingFillColor));
        ops.Add(new PdfStrokeRect(contentLeft, bandBottom, headingWidthPt, headingHeightPt, HeadingBorderColor, 0.4));

        // Column letters, spanning the same column widths/offsets as the cell grid.
        for (var colIndex = 0; colIndex < colXs.Length && colIndex < colWidths.Length; colIndex++)
        {
            var x = colXs[colIndex];
            var w = colWidths[colIndex];
            ops.Add(new PdfFillRect(x, bandBottom, w, headingHeightPt, HeadingFillColor));
            ops.Add(new PdfStrokeRect(x, bandBottom, w, headingHeightPt, HeadingBorderColor, 0.4));

            var label = CellAddress.NumberToColumnName(contentPlan.Columns[colIndex].Column);
            AddCenteredHeadingText(ops, label, x, w, bandBottom, headingHeightPt);
        }

        // Row numbers, spanning the same row heights/offsets as the cell grid.
        for (var rowIndex = 0; rowIndex < rowYs.Length && rowIndex < rowHeights.Length; rowIndex++)
        {
            var y = rowYs[rowIndex];
            var h = rowHeights[rowIndex];
            ops.Add(new PdfFillRect(contentLeft, y, headingWidthPt, h, HeadingFillColor));
            ops.Add(new PdfStrokeRect(contentLeft, y, headingWidthPt, h, HeadingBorderColor, 0.4));

            var label = contentPlan.Rows[rowIndex].Row.ToString(CultureInfo.InvariantCulture);
            AddCenteredHeadingText(ops, label, contentLeft, headingWidthPt, y, h);
        }
    }

    /// <summary>Centers <paramref name="label"/> horizontally and vertically inside a heading cell rect.</summary>
    private static void AddCenteredHeadingText(
        List<PdfDrawOp> ops, string label, double cellX, double cellWidth, double cellBottomY, double cellHeight)
    {
        var textWidth = PortablePdfTextMeasurer.Instance.Measure(
            label, null, HeadingFontSize, bold: false, italic: false).Width;
        var textX = cellX + Math.Max(0.0, (cellWidth - textWidth) / 2.0);
        var baseline = cellBottomY + Math.Max(0.0, (cellHeight - HeadingFontSize) / 2.0) + (HeadingFontSize * 0.3);
        ops.Add(new PdfText(textX, baseline, HeadingFontSize, PdfFontFace.Regular, HeaderTextColor, label));
    }

    private static void AddVectorDrawingOps(
        Workbook workbook,
        Sheet sheet,
        PortablePdfExportPlan exportPlan,
        PortablePdfExportPageRequest request,
        List<PdfDrawOp> ops,
        double pageWidthPoints,
        double pageHeightPoints)
    {
        if (request.IsCommentSummaryPage)
            return;

        var layout = BuildPageContentLayout(workbook, sheet, exportPlan, request);
        if (layout is null || (layout.Charts.Count == 0 && layout.TextBoxes.Count == 0))
            return;

        var scaleX = pageWidthPoints / layout.PageBounds.Width;
        var scaleY = pageHeightPoints / layout.PageBounds.Height;

        foreach (var chart in layout.Charts)
        {
            AddFillRect(ops, chart.Bounds, chart.Fill, pageHeightPoints, scaleX, scaleY);
            AddStrokeRect(ops, chart.Bounds, chart.Outline, chart.OutlineThickness, pageHeightPoints, scaleX, scaleY);
            AddChartPlotOps(workbook, sheet, chart, ops, pageHeightPoints, scaleX, scaleY);

            foreach (var overlay in chart.TextOverlays)
                AddTextOverlay(ops, overlay, pageHeightPoints, scaleX, scaleY);
        }

        foreach (var textBox in layout.TextBoxes)
        {
            if (textBox.Fill is { } fill)
                AddFillRect(ops, textBox.Bounds, fill, pageHeightPoints, scaleX, scaleY, textBox.FillAlpha / 255d);

            AddStrokeRect(ops, textBox.Bounds, textBox.Outline, textBox.OutlineThickness, pageHeightPoints, scaleX, scaleY);

            if (!string.IsNullOrWhiteSpace(textBox.Text))
            {
                var fontSize = Math.Max(1, textBox.Font.FontSize * scaleY);
                ops.Add(new PdfText(
                    textBox.TextBounds.Left * scaleX,
                    pageHeightPoints - ((textBox.TextBounds.Top * scaleY) + fontSize),
                    fontSize,
                    ToPdfFontFace(textBox.Font.Bold, textBox.Font.Italic),
                    ToPdfColor(textBox.Font.Color),
                    PortablePdfWinAnsiTextCapability.Truncate(textBox.Text, 128)));
            }
        }
    }

    private static void AddChartPlotOps(
        Workbook workbook,
        Sheet sheet,
        PageChartBlock chartBlock,
        List<PdfDrawOp> ops,
        double pageHeightPoints,
        double scaleX,
        double scaleY)
    {
        var chart = sheet.Charts.FirstOrDefault(candidate => candidate.Id == chartBlock.Id);
        if (chart is null || !ChartLayoutEngine.IsSupported(chart.Type))
            return;

        var inset = Math.Min(28, Math.Min(chartBlock.Bounds.Width, chartBlock.Bounds.Height) / 4);
        var plotArea = new PlotRect(
            inset,
            inset,
            Math.Max(1, chartBlock.Bounds.Width - (2 * inset)),
            Math.Max(1, chartBlock.Bounds.Height - (2 * inset)));
        var request = ChartLayoutRequestBuilder.TryBuild(
            chart,
            plotArea,
            BuildChartCellAccessor(workbook, sheet),
            PortablePdfTextMeasurer.Instance);
        if (request is null)
            return;

        ChartLayout chartLayout;
        try
        {
            chartLayout = ChartLayoutEngine.Layout(request);
        }
        catch (NotSupportedException)
        {
            return;
        }

        var palette = ChartStylePlanner.BuildExcelSeriesPalette(workbook.Theme);
        var barSeriesCount = chartLayout.Series.Count(series =>
            series.Kind is SeriesGeometryKind.Columns or SeriesGeometryKind.Bars);

        foreach (var series in chartLayout.Series)
        {
            switch (series.Kind)
            {
                case SeriesGeometryKind.Columns:
                case SeriesGeometryKind.Bars:
                    AddChartBarOps(workbook, chart, chartBlock.Bounds, series, barSeriesCount, palette, ops, pageHeightPoints, scaleX, scaleY);
                    break;
                case SeriesGeometryKind.Line:
                    AddChartLineOps(workbook, chart, chartBlock.Bounds, series, palette, ops, pageHeightPoints, scaleX, scaleY);
                    break;
            }
        }
    }

    private static void AddChartBarOps(
        Workbook workbook,
        ChartModel chart,
        LayoutRect chartBounds,
        SeriesLayout series,
        int barSeriesCount,
        IReadOnlyList<CellColor> palette,
        List<PdfDrawOp> ops,
        double pageHeightPoints,
        double scaleX,
        double scaleY)
    {
        var paint = ChartStylePlanner.ResolveBarPaint(chart, series.SeriesIndex, workbook.Theme, palette);
        foreach (var bar in series.Bars)
        {
            if (bar.Rect.Width <= 0 || bar.Rect.Height <= 0)
                continue;

            var varyColorsFill = ChartStylePlanner.ResolveVaryColorsPointFill(
                chart,
                series.SeriesIndex,
                bar.PointIndex,
                barSeriesCount,
                workbook.Theme,
                palette);
            var fill = bar.FillColorOverride
                ?? varyColorsFill
                ?? paint.FillColor;

            if (fill is { } fillColor)
            {
                ops.Add(new PdfFillRect(
                    ToPdfX(chartBounds.Left + bar.Rect.Left, scaleX),
                    ToPdfY(chartBounds.Top + bar.Rect.Bottom, pageHeightPoints, scaleY),
                    bar.Rect.Width * scaleX,
                    bar.Rect.Height * scaleY,
                    ToPdfColor(fillColor)));
            }

            if (paint.StrokeColor is { } strokeColor && paint.StrokeThickness > 0)
            {
                ops.Add(new PdfStrokeRect(
                    ToPdfX(chartBounds.Left + bar.Rect.Left, scaleX),
                    ToPdfY(chartBounds.Top + bar.Rect.Bottom, pageHeightPoints, scaleY),
                    bar.Rect.Width * scaleX,
                    bar.Rect.Height * scaleY,
                    ToPdfColor(strokeColor),
                    Math.Max(0.25, paint.StrokeThickness * Math.Min(scaleX, scaleY))));
            }
        }
    }

    private static void AddChartLineOps(
        Workbook workbook,
        ChartModel chart,
        LayoutRect chartBounds,
        SeriesLayout series,
        IReadOnlyList<CellColor> palette,
        List<PdfDrawOp> ops,
        double pageHeightPoints,
        double scaleX,
        double scaleY)
    {
        var format = ChartStylePlanner.FindSeriesFormat(chart, series.SeriesIndex);
        if (format?.NoLine == true || series.Points.Count < 2)
            return;

        var paint = ChartStylePlanner.ResolveSeriesPaint(chart, series.SeriesIndex, workbook.Theme, palette);
        var strokeWidth = Math.Max(0.25, (format?.StrokeThickness ?? 2.0) * Math.Min(scaleX, scaleY));
        for (var index = 1; index < series.Points.Count; index++)
        {
            var previous = series.Points[index - 1].Position;
            var current = series.Points[index].Position;
            ops.Add(new PdfLine(
                ToPdfX(chartBounds.Left + previous.X, scaleX),
                ToPdfY(chartBounds.Top + previous.Y, pageHeightPoints, scaleY),
                ToPdfX(chartBounds.Left + current.X, scaleX),
                ToPdfY(chartBounds.Top + current.Y, pageHeightPoints, scaleY),
                ToPdfColor(paint.StrokeColor),
                strokeWidth));
        }
    }

    private static ChartLayoutRequestBuilder.ChartCellAccessor BuildChartCellAccessor(
        Workbook workbook,
        Sheet sheet) =>
        (uint row, uint col, out double value, out string displayText) =>
        {
            var cell = sheet.GetCell(row, col);
            if (cell is null)
            {
                value = 0;
                displayText = "";
                return false;
            }

            var style = workbook.GetStyle(cell.StyleId);
            displayText = NumberFormatter.FormatWithColor(
                cell.Value,
                style.NumberFormat,
                workbook.IndexedColors,
                workbook.Theme,
                workbook.Uses1904DateSystem).Text;

            return TryGetChartNumericValue(cell.Value, displayText, out value);
        };

    private static bool TryGetChartNumericValue(ScalarValue value, string displayText, out double result)
    {
        switch (value)
        {
            case NumberValue number:
                result = number.Value;
                return double.IsFinite(result);
            case DateTimeValue dateTime:
                result = dateTime.Value;
                return double.IsFinite(result);
            case BoolValue boolean:
                result = boolean.Value ? 1 : 0;
                return true;
        }

        return double.TryParse(displayText, NumberStyles.Any, CultureInfo.InvariantCulture, out result)
            && double.IsFinite(result);
    }

    private static double ToPdfX(double layoutX, double scaleX) => layoutX * scaleX;

    private static double ToPdfY(double layoutY, double pageHeightPoints, double scaleY) =>
        pageHeightPoints - (layoutY * scaleY);

    private static PageContentLayout? BuildPageContentLayout(
        Workbook workbook,
        Sheet sheet,
        PortablePdfExportPlan exportPlan,
        PortablePdfExportPageRequest request)
    {
        if (request.SheetIndex < 0 || request.SheetIndex >= exportPlan.ExportPrintPlan.SheetPlans.Count)
            return null;

        var sheetPlan = exportPlan.ExportPrintPlan.SheetPlans[request.SheetIndex];
        var pagePlan = new PagePaginationResult(
            BuildSegments(sheetPlan.RowPagePlans, static plan => plan.BodyRows, static plan => plan.TitleRows),
            BuildSegments(sheetPlan.ColumnPagePlans, static plan => plan.BodyColumns, static plan => plan.TitleColumns),
            PagePaginationPlanner.CalculateEffectiveScalePercent(
                sheet.ScaleToFit,
                sheetPlan.RowPageCount,
                sheetPlan.ColumnPageCount));
        var pageIndex = ResolvePageIndex(request, pagePlan);
        return pageIndex < 0
            ? null
            : PageContentRenderModelBuilder.Build(
                workbook,
                sheet,
                pagePlan,
                pageIndex,
                PortablePdfTextMeasurer.Instance);
    }

    private static int ResolvePageIndex(PortablePdfExportPageRequest request, PagePaginationResult pagePlan)
    {
        var pages = PrintPageGridPlanner.BuildIndexes(
            pagePlan.RowPageCount,
            pagePlan.ColumnPageCount,
            request.PageOrder);

        for (var index = 0; index < pages.Count; index++)
        {
            if (pages[index].RowPageIndex == request.RowPageIndex &&
                pages[index].ColumnPageIndex == request.ColumnPageIndex)
            {
                return index;
            }
        }

        return -1;
    }

    private static IReadOnlyList<PageAxisSegment> BuildSegments<TPlan>(
        IReadOnlyList<TPlan> plans,
        Func<TPlan, IReadOnlyList<uint>> getBodyIndexes,
        Func<TPlan, IReadOnlyList<uint>> getTitleIndexes)
    {
        var segments = new List<PageAxisSegment>(plans.Count);
        foreach (var plan in plans)
        {
            var indexes = getBodyIndexes(plan);
            if (indexes.Count == 0)
                indexes = getTitleIndexes(plan);
            if (indexes.Count == 0)
                continue;

            segments.Add(new PageAxisSegment(indexes[0], indexes[^1]));
        }

        return segments;
    }

    private static void AddFillRect(
        List<PdfDrawOp> ops,
        LayoutRect bounds,
        PresentationRgb color,
        double pageHeightPoints,
        double scaleX,
        double scaleY,
        double opacity = 1.0)
    {
        var rect = new PdfFillRect(
            bounds.Left * scaleX,
            pageHeightPoints - (bounds.Bottom * scaleY),
            bounds.Width * scaleX,
            bounds.Height * scaleY,
            ToPdfColor(color));

        ops.Add(opacity >= 0.999
            ? rect
            : new PdfOpacityGroup(Math.Clamp(opacity, 0, 1), [rect]));
    }

    private static void AddStrokeRect(
        List<PdfDrawOp> ops,
        LayoutRect bounds,
        PresentationRgb color,
        double lineWidth,
        double pageHeightPoints,
        double scaleX,
        double scaleY)
    {
        if (lineWidth <= 0)
            return;

        ops.Add(new PdfStrokeRect(
            bounds.Left * scaleX,
            pageHeightPoints - (bounds.Bottom * scaleY),
            bounds.Width * scaleX,
            bounds.Height * scaleY,
            ToPdfColor(color),
            Math.Max(0.25, lineWidth * Math.Min(scaleX, scaleY))));
    }

    private static void AddTextOverlay(
        List<PdfDrawOp> ops,
        PrintChartTextOverlayPlan overlay,
        double pageHeightPoints,
        double scaleX,
        double scaleY)
    {
        if (string.IsNullOrWhiteSpace(overlay.Text))
            return;

        var fontSize = Math.Max(1, overlay.FontSize * scaleY);
        var text = new PdfText(
            overlay.X * scaleX,
            pageHeightPoints - ((overlay.Y * scaleY) + fontSize),
            fontSize,
            PdfFontFace.Regular,
            ToPdfColor(overlay.Color),
            PortablePdfWinAnsiTextCapability.Truncate(overlay.Text, 128));

        if (Math.Abs(overlay.RotationDegrees) < 0.01)
        {
            ops.Add(text);
            return;
        }

        ops.Add(new PdfRotationGroup(
            overlay.X * scaleX,
            pageHeightPoints - (overlay.Y * scaleY),
            overlay.RotationDegrees,
            [text]));
    }

    private static PdfFontFace ToPdfFontFace(bool bold, bool italic) =>
        (bold, italic) switch
        {
            (true, true) => PdfFontFace.BoldItalic,
            (true, false) => PdfFontFace.Bold,
            (false, true) => PdfFontFace.Italic,
            _ => PdfFontFace.Regular
        };

    private static void RenderHeaderFooterBand(
        List<PdfDrawOp> ops,
        WorksheetHeaderFooter band,
        double pageW,
        double mL,
        double mR,
        double baselineY,
        double fontSize,
        string workbookName,
        string workbookDirectory,
        string sheetName,
        int pageNumber,
        int totalPages,
        PdfColor color)
    {
        var now = DateTime.Now;
        var sectionWidth = Math.Max(1, (pageW - mL - mR) / 3.0);

        // Left section.
        var leftText = ExpandHF(band.Left, pageNumber, totalPages, workbookName, workbookDirectory, sheetName, now);
        if (!string.IsNullOrEmpty(leftText))
            ops.Add(new PdfText(mL, baselineY, fontSize, PdfFontFace.Regular, color,
                PortablePdfWinAnsiTextCapability.Truncate(leftText, 128)));

        // Center section.
        var centerText = ExpandHF(band.Center, pageNumber, totalPages, workbookName, workbookDirectory, sheetName, now);
        if (!string.IsNullOrEmpty(centerText))
        {
            var centerX = mL + sectionWidth;  // approximate — no text measurement available here
            ops.Add(new PdfText(centerX, baselineY, fontSize, PdfFontFace.Regular, color,
                PortablePdfWinAnsiTextCapability.Truncate(centerText, 128)));
        }

        // Right section.
        var rightText = ExpandHF(band.Right, pageNumber, totalPages, workbookName, workbookDirectory, sheetName, now);
        if (!string.IsNullOrEmpty(rightText))
        {
            var rightX = pageW - mR - sectionWidth;
            ops.Add(new PdfText(rightX, baselineY, fontSize, PdfFontFace.Regular, color,
                PortablePdfWinAnsiTextCapability.Truncate(rightText, 128)));
        }
    }

    /// <summary>
    /// Simple header/footer token expansion without formatting codes (bold/italic/etc. are stripped;
    /// value placeholders &amp;P/&amp;N/&amp;D/&amp;T/&amp;F/&amp;Z/&amp;A are substituted).
    /// <paramref name="workbookDirectory"/> is the folder that contains the workbook file (with a
    /// trailing separator), substituted for &amp;Z / &amp;[Path]; pass an empty string when the
    /// workbook is unsaved, matching the WPF <c>PagePrintTextPlanner</c> path.
    /// </summary>
    internal static string ExpandHF(
        string raw,
        int pageNumber,
        int totalPages,
        string workbookName,
        string workbookDirectory,
        string sheetName,
        DateTime now)
    {
        if (string.IsNullOrEmpty(raw))
            return "";

        var sb = new System.Text.StringBuilder(raw.Length);
        var span = raw.AsSpan();
        var i = 0;
        while (i < span.Length)
        {
            if (span[i] != '&')
            {
                sb.Append(span[i]);
                i++;
                continue;
            }

            if (i + 1 >= span.Length) { sb.Append('&'); i++; continue; }
            var next = span[i + 1];

            if (next == '[')
            {
                var close = span[(i + 2)..].IndexOf(']');
                if (close < 0) { sb.Append('&'); i++; continue; }
                var token = span.Slice(i + 2, close).ToString().ToUpperInvariant();
                i += 3 + close;
                switch (token)
                {
                    case "PAGE":   sb.Append(pageNumber.ToString(CultureInfo.InvariantCulture)); break;
                    case "PAGES":  sb.Append(totalPages.ToString(CultureInfo.InvariantCulture)); break;
                    case "DATE":   sb.Append(now.ToString("d", CultureInfo.CurrentCulture)); break;
                    case "TIME":   sb.Append(now.ToString("t", CultureInfo.CurrentCulture)); break;
                    case "FILE":   sb.Append(workbookName); break;
                    case "PATH":   sb.Append(workbookDirectory); break;
                    case "TAB":    sb.Append(sheetName); break;
                }
                continue;
            }

            if (next == '&') { sb.Append('&'); i += 2; continue; }

            // Font/style codes — skip them.
            var code = char.ToUpperInvariant(next);
            switch (code)
            {
                case 'B': case 'I': case 'U': case 'E': case 'S': case 'G':
                case '+': case '-': case 'X': case 'Y':
                    i += 2;
                    continue;
                case '"':
                {
                    var close = span[(i + 2)..].IndexOf('"');
                    i += close >= 0 ? 3 + close : 2;
                    continue;
                }
                case 'K':
                    i += i + 7 < span.Length ? 8 : 2;
                    continue;
                case 'P':
                    sb.Append(pageNumber.ToString(CultureInfo.InvariantCulture));
                    i += 2; continue;
                case 'N':
                    sb.Append(totalPages.ToString(CultureInfo.InvariantCulture));
                    i += 2; continue;
                case 'D':
                    sb.Append(now.ToString("d", CultureInfo.CurrentCulture));
                    i += 2; continue;
                case 'T':
                    sb.Append(now.ToString("t", CultureInfo.CurrentCulture));
                    i += 2; continue;
                case 'F':
                    sb.Append(workbookName);
                    i += 2; continue;
                case 'Z':
                    sb.Append(workbookDirectory);
                    i += 2; continue;
                case 'A':
                    sb.Append(sheetName);
                    i += 2; continue;
                default:
                    if (char.IsAsciiDigit(next))
                    {
                        // font-size code — skip digits
                        var end = i + 2;
                        if (end < span.Length && char.IsAsciiDigit(span[end])) end++;
                        i = end;
                    }
                    else
                    {
                        sb.Append('&');
                        i++;
                    }
                    continue;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Computes the printed page number for <paramref name="request"/> honoring
    /// <see cref="Sheet.FirstPageNumber"/> and continuing sequentially across every print area that
    /// belongs to the same sheet, matching <c>WorksheetPrintRenderPlanner.TryBuild</c>'s single
    /// running counter (seeded from <c>sheet.FirstPageNumber ?? 1</c>) across areas -- unlike
    /// <see cref="PortablePdfExportPageRequest.SheetPageNumber"/>, which <see cref="PrintPageGridPlanner"/>
    /// always numbers 1..N independently per print area.
    /// </summary>
    private static int ResolveEffectiveSheetPageNumber(
        PortablePdfExportPlan exportPlan,
        PortablePdfExportPageRequest request,
        Sheet sheet)
    {
        var firstPageNumber = sheet.FirstPageNumber ?? 1;
        var offset = 0;
        foreach (var candidate in exportPlan.PageRequests)
        {
            if (candidate.PrintRange.Start.Sheet != sheet.Id)
                continue;

            if (candidate.ExportPageNumber == request.ExportPageNumber)
                return firstPageNumber + offset;

            offset++;
        }

        // Should not happen (request always comes from exportPlan.PageRequests), but fall back to
        // the area-local numbering rather than throwing.
        return firstPageNumber + request.SheetPageNumber - 1;
    }

    /// <summary>
    /// Computes the &amp;N total-page-count value for <paramref name="sheet"/>: the count of every
    /// actual <see cref="PortablePdfExportPageRequest"/> belonging to that sheet (a sheet can have more
    /// than one configured print area — see N45/N46), INCLUDING any "at end of sheet" comment-summary
    /// pages <c>PortablePdfExportPlanner.AddCommentSummaryPageRequests</c> appends after the grid pages
    /// -- NOT merely <see cref="WorkbookSheetExportPrintPlanSummary.PageCount"/> (grid pages only), and
    /// NOT <see cref="WorkbookExportPrintPlan.TotalPageCount"/>, which sums across every sheet in the
    /// export. Real Excel resets &amp;N per sheet in a multi-sheet print job and includes the appended
    /// comment pages in that count, and so does FreeX's own WPF path (<c>PrintRenderer.RenderWorksheet</c>
    /// computes totalPages = printPlan.GridPageCount + commentSummaryPages.Count).
    /// </summary>
    private static int ResolveEffectiveSheetTotalPages(PortablePdfExportPlan exportPlan, Sheet sheet)
    {
        var total = 0;
        foreach (var candidate in exportPlan.PageRequests)
        {
            if (candidate.PrintRange.Start.Sheet == sheet.Id)
                total++;
        }

        // Should not happen (the sheet owning this page must have at least one request), but fall
        // back to the whole-export total rather than reporting 0 pages.
        return total > 0 ? total : exportPlan.TotalPageCount;
    }

    private static (WorksheetHeaderFooter Header, WorksheetHeaderFooter Footer)
        ResolveHeaderFooterForPage(Sheet sheet, int pageNumber)
    {
        if (sheet.DifferentFirstPageHeaderFooter && pageNumber == (sheet.FirstPageNumber ?? 1))
            return (sheet.FirstPageHeader, sheet.FirstPageFooter);

        if (sheet.DifferentOddEvenHeaderFooter && pageNumber % 2 == 0)
            return (sheet.EvenPageHeader, sheet.EvenPageFooter);

        return (sheet.PageHeader, sheet.PageFooter);
    }

    // -----------------------------------------------------------------------
    // Shared helpers
    // -----------------------------------------------------------------------

    private static PdfColor? ToPdfColor(CellColor? color) =>
        color is { } c ? new PdfColor(c.R, c.G, c.B) : null;

    private static PdfColor ToPdfColor(CellColor color) =>
        new(color.R, color.G, color.B);

    private static PdfColor ToPdfColor(PresentationRgb color) =>
        new(color.R, color.G, color.B);

    private static int FindRowIndex(IReadOnlyList<PortablePdfPageRow> rows, uint row)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index].Row == row)
                return index;
        }

        return -1;
    }

    private static int FindColumnIndex(IReadOnlyList<PortablePdfPageColumn> columns, uint column)
    {
        for (var index = 0; index < columns.Count; index++)
        {
            if (columns[index].Column == column)
                return index;
        }

        return -1;
    }

    private static double ResolveColumnWidth(
        double availableWidth,
        int columnCount,
        PortablePdfDocumentOptions options)
    {
        var equalWidth = availableWidth / columnCount;
        var bounded = Math.Clamp(equalWidth, options.MinimumColumnWidthPoints, options.MaximumColumnWidthPoints);
        return bounded * columnCount > availableWidth
            ? equalWidth
            : bounded;
    }

    private sealed class PortablePdfTextMeasurer : ITextMeasurer
    {
        public static readonly PortablePdfTextMeasurer Instance = new();

        public TextSize Measure(string? text, string? fontFamily, double fontSize, bool bold, bool italic)
        {
            if (string.IsNullOrEmpty(text))
                return TextSize.Empty;

            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');
            var widthFactor = bold ? 0.58 : 0.54;
            var maxWidth = lines.Max(line => line.Length * fontSize * widthFactor);
            return new TextSize(maxWidth, lines.Length * fontSize * 1.2);
        }
    }
}
