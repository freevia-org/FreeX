using System.Globalization;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Calc;
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

        // Distribute available width/height proportionally to actual column/row sizes, then apply
        // the sheet's Scale%/Fit-to-pages ratio directly to the grid geometry -- matching the WPF
        // PrintRenderer path (PrintRenderer.HeaderFooter.cs), which always applies
        // ScaleTransform(scaleRatio, scaleRatio) once scaleRatio&lt;1, regardless of whether the
        // unscaled content already fits the page. Excel shrinks/grows every printed element in
        // direct proportion to the configured scale, not merely "when it would otherwise overflow".
        var (colWidths, rowHeights) = ComputeActualGridSizes(
            sheet, contentPlan, contentWidth, contentHeight, scaleRatio);

        // Grid origin: top-left corner in PDF y-up (top = high y).
        // We position the grid at the top of the content rect.
        var gridLeft = contentLeft;
        var gridTop  = contentTop;   // PDF y-up: top edge = high y value

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
            var fill  = style.ResolveFillColor(workbook.Theme);

            // B&W mode: suppress colored cell fills (treat as white / transparent).
            // The page background is already white so simply omitting the fill rect is correct.
            var bw = sheet.PrintBlackAndWhite;
            if (!bw && (fill is not null || cell.IsTitle))
                ops.Add(new PdfFillRect(x, y, w, h, ToPdfColor(fill) ?? TitleFillColor));

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
                var textScale = scaleRatio;
                var fontSize  = Math.Clamp(style.FontSize, 7, 10) * textScale;
                var fontFace  = cell.IsTitle || style.Bold ? PdfFontFace.Bold : PdfFontFace.Regular;
                // B&W mode: force font colour to black regardless of style.
                var fontColor = bw ? PdfColor.Black : (ToPdfColor(style.ResolveFontColor(workbook.Theme)) ?? PdfColor.Black);
                // Text inset/baseline scale with the grid so text stays proportionally placed
                // within its (now possibly shrunk) cell rect.
                var baseline = y + (3.0 * textScale);
                ops.Add(new PdfText(
                    x + (2.0 * textScale),
                    baseline,
                    fontSize,
                    fontFace,
                    fontColor,
                    PortablePdfWinAnsiTextCapability.Truncate(cell.DisplayText, 64)));
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

        // ── Header band ────────────────────────────────────────────────────────
        // N45/N46: page.SheetPageNumber is always 1-based per print area (PrintPageGridPlanner
        // numbers every area's pages 1..N independently), so it neither honors sheet.FirstPageNumber
        // nor continues across a sheet's multiple print areas. Resolve the printed page number the
        // same way WorksheetPrintRenderPlanner.TryBuild does for WPF: a single counter, seeded from
        // FirstPageNumber, running across every page that belongs to this sheet (all its print areas,
        // in export order).
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
            var fill = style.ResolveFillColor(workbook.Theme);
            if (fill is not null || cell.IsTitle)
                ops.Add(new PdfFillRect(x, y, columnWidth, options.RowHeightPoints, ToPdfColor(fill) ?? TitleFillColor));

            ops.Add(new PdfStrokeRect(x, y, columnWidth, options.RowHeightPoints, GridStrokeColor, 0.5));
            if (string.IsNullOrEmpty(cell.DisplayText))
                continue;

            var fontSize = Math.Clamp(style.FontSize, 7, 10);
            var fontFace = cell.IsTitle || style.Bold ? PdfFontFace.Bold : PdfFontFace.Regular;
            var fontColor = ToPdfColor(style.ResolveFontColor(workbook.Theme)) ?? PdfColor.Black;
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
    private static (double[] ColWidths, double[] RowHeights) ComputeActualGridSizes(
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

        return (colWidthsPt, rowHeightsPt);
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
}
