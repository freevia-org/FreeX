using System.Globalization;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Sparklines;
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

    // R96-render-cf-databar-iconset-1 / R96-render-sparkline-pdf-1: fixed 96dpi(px)->72dpi(pt)
    // conversion for the "device pixels at 100% zoom" constants the portable conditional-format
    // (ConditionalDataBarLayoutPlanner/ConditionalIconCellLayoutPlanner) and sparkline
    // (GridView.Overlays.Sparklines.cs's 3px cell inset) layout planners use -- independent of any
    // sheet Scale%/Fit-to-pages ratio, matching the identical ptPerPx conversion already used for the
    // heading gutter and indent above.
    private const double PixelToPointRatio = SheetPdfPageSetupResolver.PdfPointsPerInch / 96.0;
    private static readonly PdfColor CfIconOutlineColor = new(96, 96, 96);
    private static readonly PdfColor SparklineDefaultPositiveColor = new(33, 115, 70);
    private static readonly PdfColor SparklineDefaultNegativeColor = new(192, 0, 0);

    // -----------------------------------------------------------------------
    // Page-setup-aware path (new)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a PDF document where each page's dimensions, margins, scale, gridlines, and
    /// header/footer are derived from the exporting sheet's OOXML page setup. Prefers this path
    /// over <see cref="Build(Workbook,PortablePdfExportPlan,PortablePdfDocumentOptions)"/> for
    /// the Avalonia/Skia PDF export.
    /// </summary>
    /// <param name="textMeasurer">
    /// font-text-measurement-F1: the text measurer used to position aligned cell/heading/chart/
    /// header-footer text (see <see cref="BuildPageWithPageSetup"/>). Defaults to the dependency-free
    /// <see cref="PortablePdfTextMeasurer"/> character-count heuristic when null, preserving every
    /// existing caller's behavior unchanged. A caller whose PDF backend draws with real font glyphs
    /// (e.g. the Skia writer, which measures every run's actual advance via SKFont.MeasureText) should
    /// pass a matching real measurer here so the precomputed text position agrees with what that
    /// backend actually draws -- otherwise the heuristic's up-to-~135% per-string width error (flat
    /// per-character estimate vs. real glyph widths) leaves right/center/justify/distribute-aligned
    /// text visibly offset from, or overflowing past, the cell/page edge Print Preview showed.
    /// </param>
    public static PdfContentDocument BuildWithPageSetup(
        Workbook workbook,
        PortablePdfExportPlan exportPlan,
        string workbookDirectory = "",
        ITextMeasurer? textMeasurer = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(exportPlan);

        var pages = exportPlan.PageRequests
            .Select(request => BuildPageWithPageSetup(workbook, exportPlan, request, workbookDirectory, textMeasurer))
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
    /// <param name="textMeasurer">
    /// font-text-measurement-F1: see <see cref="BuildWithPageSetup"/>'s parameter of the same name.
    /// Null (the default) keeps the existing <see cref="PortablePdfTextMeasurer"/> heuristic.
    /// </param>
    public static PdfContentPage BuildPageWithPageSetup(
        Workbook workbook,
        PortablePdfExportPlan exportPlan,
        PortablePdfExportPageRequest request,
        string workbookDirectory = "",
        ITextMeasurer? textMeasurer = null)
    {
        // font-text-measurement-F1: resolve once and thread through every downstream measurement call
        // on this page (cell text, row/column headings, chart axis/label layout, header/footer runs)
        // so they all agree with each other and with whichever backend actually draws the glyphs.
        var measurer = textMeasurer ?? PortablePdfTextMeasurer.Instance;

        var contentPlan = PortablePdfPageContentPlanner.CreatePlan(workbook, request);
        if (!contentPlan.IsReady)
            throw new InvalidOperationException(contentPlan.StatusText);

        // Resolve the sheet that actually owns this page's print range rather than indexing by
        // request.SheetIndex -- SheetIndex is the position of the print AREA within the export
        // plan's flattened SheetPlans list, which is not the same as the sheet's index in the
        // workbook once any earlier sheet has more than one configured print area (see N45/N46).
        var sheet = workbook.GetSheet(request.PrintRange.Start.Sheet)
            ?? workbook.GetSheetAt(request.SheetIndex);

        var (pageW, pageH, mL, mR, mT, mB, _, _) =
            SheetPdfPageSetupResolver.ComputePdfGeometry(sheet);

        // Effective scale for rendering (percent / 100).
        var scaleRatio = ResolveScaleRatio(sheet, exportPlan, request);

        // Header band: sits between the top of the page and the content rect.
        // In PDF y-up: header band top = pageH - headerEdge, header band bottom = pageH - mT.
        // Footer band: sits between the bottom of the content rect and the bottom of the page.
        var headerEdgePt  = sheet.HeaderMargin * SheetPdfPageSetupResolver.PdfPointsPerInch;
        var footerEdgePt  = sheet.FooterMargin * SheetPdfPageSetupResolver.PdfPointsPerInch;
        // (ComputePdfGeometry's HeaderBandPt/FooterBandPt tuple members are these same raw
        // header/footer margin-edge distances; discarded above via `_` rather than recomputed twice
        // under two names.)

        // R99-services-pagesetup-header-band-2: Excel's model -- the header/footer margin is the
        // distance from the page edge to the header/footer TEXT band, which sits WITHIN the top/bottom
        // margin band as long as it doesn't exceed it. The cell grid's own top/bottom edge is pushed
        // out to max(margin, header/footer edge), NOT the plain margin, once the header/footer margin
        // is the larger of the two -- exactly like SheetPdfPageSetupResolver.ResolveCapacityDetail's
        // bodyTopPx/bodyBottomPx (R96-services-pagesetup-header-band-1, which already computed the
        // capacity/row-count this way) and PrintRenderer.HeaderFooter.cs's contentTop
        // (R99-app-host-header-footer-margin-overlap-1, the WPF rendering-geometry twin of this bug).
        // Previously this method used the plain mT/mB unconditionally, so the PDF content renderer's
        // actual drawn grid disagreed with both Excel and its own sibling pagination-capacity method
        // whenever a Header/Footer margin exceeded the Top/Bottom margin -- the header text (drawn
        // independently below at headerY = pageH - headerEdgePt - 8) visually overlapped the first
        // printed row.
        var bodyTopEdgePt    = PageGeometryRules.ResolveBodyEdge(mT, headerEdgePt);
        var bodyBottomEdgePt = PageGeometryRules.ResolveBodyEdge(mB, footerEdgePt);

        // Content rect: page minus margins. y-origin is bottom-left in PDF space.
        var contentLeft   = mL;
        var contentBottom = bodyBottomEdgePt;
        var contentRight  = pageW - mR;
        var contentTop    = pageH - bodyTopEdgePt;
        var contentWidth  = Math.Max(1.0, contentRight - contentLeft);
        var contentHeight = Math.Max(1.0, contentTop - contentBottom);

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

        // R96-render-sparkline-pdf-1: precompute this page's sparkline lookup (by anchor cell) and the
        // group axis-scaling bounds once, mirroring GridView.RenderSparklines'/PrintRenderer.GridCells.cs's
        // DrawPrintedSparklines' own pre-compute step, so the Avalonia/portable PDF export draws the
        // exact same sparklines the interactive grid and the WPF print path already show instead of
        // silently omitting them.
        var sparklinesByCell = BuildSparklinesByCell(sheet);
        var sparklineValues = sparklinesByCell.Count > 0
            ? SparklineSeriesReader.BuildValues(workbook, sheet)
            : EmptySparklineValues;
        var axisScalePlan = SparklineAxisScalePlanner.Build(sheet.Sparklines, sparklineValues);

        // ── Draw cell fills and text ───────────────────────────────────────────
        var validationCircleCellBounds = new List<LayoutRect>();
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
            if (cell.HasValidationCircle)
                validationCircleCellBounds.Add(new LayoutRect(x, y, w, h));

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

            // R127-services-pdf-cell-borders-1: Format Cells > Border (style.BorderTop/Right/Bottom/
            // Left) was never drawn on this page-setup-aware PDF export path -- fills, gridlines
            // (Sheet.PrintGridlines), text, CF overlays and sparklines all printed, but an explicit
            // user-authored border (a boxed header, a totals-row underline) silently vanished, even
            // though it always renders in the on-screen print preview (PageContentRenderModelBuilder.
            // ResolveBorders/AddCellBorders) and in the WPF native print/PDF path (PrintRenderer.
            // GridCells.cs's DrawPrintedBorderEdge). This is independent of PrintGridlines (which
            // defaults off, like Excel) -- an explicit border must always print, matching Excel.
            // Mirrors the print-preview path's own edge resolution (each cell paints its own
            // top/right/bottom/left border, no neighbor-precedence winner and no merge-bounds
            // widening -- neither of which this per-grid-cell PDF loop implements for fills either)
            // rather than the WPF path's more elaborate shared-edge-winner/merge-suppression model, so
            // the exported PDF matches what the user already sees in Print Preview.
            //
            // freex-conditional-format-F1: a matched CF rule's per-edge border (cell.ConditionalStyle)
            // overrides the raw style's border on that edge, matching the CF fill override immediately
            // above -- previously an Excel-authored dxf border (e.g. a CF-highlighted totals row) never
            // drew here even though the raw R127 fix above already drew a plain, non-CF border.
            DrawCellBorders(ops, style, cell.ConditionalStyle, x, y, w, h, bw);

            var isEffectivelyRightToLeft = CellTextOrientationLayoutPlanner.ResolveIsEffectivelyRightToLeft(
                style.ReadingOrder, sheet.IsRightToLeft);

            // R96-render-cf-databar-iconset-1: a data-bar or icon-set conditional format is a separate
            // per-cell overlay from the style-merged fill above (PortablePdfPageCell.DataBar/IconSet are
            // populated independently of ConditionalFillColor, matching DisplayCell's own separate
            // ConditionalDataBar/ConditionalIcon fields) -- without these calls the bar/glyph a user sees
            // on screen (ConditionalDataBarPanel.cs/ConditionalFormatIconGlyphFactory.cs) silently never
            // appeared in the exported PDF or Avalonia print preview, even though the plain fill/text CF
            // gap was already fixed (R72).
            var iconGutterPt = 0.0;
            if (!bw && cell.DataBar is { } dataBar)
                DrawConditionalDataBar(ops, dataBar, x, y, w, h);
            if (!bw && cell.IconSet is { } iconSet)
                iconGutterPt = DrawConditionalIconSet(ops, iconSet, x, y, w, h, isEffectivelyRightToLeft);

            // R96-render-sparkline-pdf-1: a sparkline anchored on this cell is a screen-only overlay
            // above the grid on every shell -- draw it into the cell rect the same 3px-inset way
            // PrintRenderer.GridCells.cs's DrawPrintedSparklines does for the WPF print path.
            if (sparklinesByCell.TryGetValue((cell.Row, cell.Column), out var sparkline) &&
                sparklineValues.TryGetValue(sparkline.Id, out var sparklineSeries) &&
                sparklineSeries.Count > 0)
            {
                DrawSparklineIntoCell(
                    ops, sparkline, sparklineSeries, x, y, w, h, axisScalePlan);
            }

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
                // freex-conditional-format-F1: a matched CF rule's Bold/Italic/FontColor override the
                // cell's raw style the same way the on-screen grid does (ViewportConditionalFormatEvaluator.
                // MergeStyles) -- Bold/Italic OR-combine with the base style (a CF rule only ever turns
                // these on here, matching this evaluator's existing print-preview consumer,
                // PageContentRenderModelBuilder.ApplyConditionalFontDelta) and FontColor replaces the
                // resolved base color outright when the CF rule set one.
                var cfStyle = cell.ConditionalStyle;
                var effectiveBold = cell.IsTitle || style.Bold || (cfStyle?.Bold ?? false);
                var effectiveItalic = cfStyle?.Italic ?? false;
                var textScale = effectiveScaleRatio;
                var fontSize  = Math.Clamp(style.FontSize, 7, 10) * textScale;
                var fontFace  = ToPdfFontFace(effectiveBold, effectiveItalic);
                // B&W mode: force font colour to black regardless of style.
                var fontColor = bw
                    ? PdfColor.Black
                    : ToPdfColor(cfStyle?.FontColor ?? style.ResolveFontColor(workbook.Theme));
                var displayText = PdfWinAnsiTextCapability.Truncate(cell.DisplayText, 64);

                // Resolve the cell's effective horizontal alignment the same way the on-screen
                // GridView viewport does (GridView.Rendering.cs + CellTextOrientationLayoutPlanner):
                // General resolves to Right for numeric/date content (left in a right-to-left
                // context) and Left otherwise; explicit Left/Center/Right/Justify/Distributed/Fill
                // are honored as authored. Without this, every cell -- including right-aligned
                // numbers and centered titles -- rendered flush-left in the exported PDF (R53
                // fix-one-path-miss-twin-sweep-4).
                var rawCell = sheet.GetCell(cell.Row, cell.Column);
                var isNumeric = rawCell?.Value is NumberValue or DateTimeValue;
                var effectiveAlign = CellTextOrientationLayoutPlanner.ResolveEffectiveHorizontalAlignment(
                    style.HorizontalAlignment, isNumeric, isEffectivelyRightToLeft);

                // Format Cells > Alignment > Indent, converted from the same 8px-per-level unit the
                // GridView viewport uses (GridView.Rendering.cs) into PDF points and scaled with the
                // sheet's Scale%/Fit-to-pages ratio like everything else in this cell rect.
                var indentPt = style.IndentLevel * 8.0 * (SheetPdfPageSetupResolver.PdfPointsPerInch / 96.0) * textScale;

                var textWidth = measurer.Measure(
                    displayText, null, fontSize, effectiveBold, effectiveItalic).Width;

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
                    // R96-render-cf-databar-iconset-1: an icon-set glyph with ShowValue reserves a
                    // left gutter the same way the on-screen grid does (ConditionalIconCellLayoutPlanner),
                    // so left-anchored text doesn't overlap the glyph. Scoped to the common default/Left
                    // case rather than every alignment -- Center/Right/Justify/Distributed text sharing a
                    // cell with an icon set is a rarer combination left for a follow-up.
                    _ => x + (2.0 * textScale) + indentPt + iconGutterPt
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
                    displayText,
                    style.ResolveEffectiveFontName(workbook.Theme)));
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
        foreach (var cellBounds in validationCircleCellBounds)
            DrawValidationCircle(ops, cellBounds);

        if (sheet.PrintHeadings)
        {
            AddPrintHeadings(ops, offsetContentLeft, offsetContentTop, headingWidthPt, headingHeightPt,
                colWidths, rowHeights, colXs, rowYs, contentPlan, measurer);
        }

        // ── Header band ────────────────────────────────────────────────────────
        // N45/N46: page.SheetPageNumber is always 1-based per print area (PrintPageGridPlanner
        // numbers every area's pages 1..N independently), so it neither honors sheet.FirstPageNumber
        // nor continues across a sheet's multiple print areas. Resolve the printed page number the
        // same way WorksheetPrintRenderPlanner.TryBuild does for WPF: a single counter, seeded from
        // FirstPageNumber, running across every page that belongs to this sheet (all its print areas,
        // in export order).
        AddVectorDrawingOps(workbook, sheet, exportPlan, request, ops, pageW, pageH, measurer);

        var pageNumber = ResolveEffectiveSheetPageNumber(exportPlan, request, sheet);
        var (header, footer) = ResolveHeaderFooterForPage(sheet, pageNumber);
        // R127-services-draft-quality-vector-drawings-1: header/footer (&G) pictures are raster
        // graphics too -- Excel's Draft Quality suppresses them exactly like it suppresses charts and
        // sheet pictures, matching PrintRenderer.HeaderFooterDrawing.cs's `!draftQuality` guard on
        // leftPicture/centerPicture/rightPicture (the WPF path). Resolved to Empty (both sections'
        // pictures null) here at the single choke point RenderHeaderFooterBand reads from, rather than
        // threading draftQuality through every downstream picture-drawing call.
        var (headerPictures, footerPictures) = sheet.PrintDraftQuality
            ? (WorksheetHeaderFooterPictureSet.Empty, WorksheetHeaderFooterPictureSet.Empty)
            : ResolveHeaderFooterPicturesForPage(sheet, pageNumber);
        // &N (total pages) must reset per sheet, matching Excel and the WPF PrintRenderer path
        // (RenderWorksheet computes totalPages = printPlan.GridPageCount + comment pages, scoped to
        // that one sheet) -- NOT exportPlan.TotalPageCount, which sums every sheet in the export and
        // would leak whole-workbook page counts into every sheet's &N when printing/exporting more
        // than one sheet at once (O41).
        var totalPages = ResolveEffectiveSheetTotalPages(exportPlan, sheet);

        // Header text: rendered just below the header margin from the top of the page. The band
        // height (mT - headerEdgePt, the gap between the header edge and the content's top margin)
        // bounds how large an &G header picture may draw before it starts to overlap the grid.
        //
        // R99-services-header-band-2: when the Header margin is larger than the Top margin,
        // bodyTopEdgePt (above) already pushed the grid's own top edge down to headerEdgePt -- but
        // the natural "8pt below the header edge" baseline still landed 8pt further down than that
        // same line, i.e. INSIDE the now-lowered grid, because this line never accounted for where
        // contentTop actually ended up. Clamp the baseline to never sit below contentTop (never a
        // smaller y in this y-up space) so the header text can, at most, touch the grid's own top
        // edge -- matching PrintRenderer.HeaderFooterDrawing.cs's headerY (WPF path, R99-app-host-
        // header-footer-margin-overlap-1), whose Math.Max(marginTop, headerMargin)-derived body top
        // the header band is anchored against by construction, never drawing past it.
        var headerY = Math.Max(pageH - headerEdgePt - 8, contentTop);   // baseline approx 8pt below header edge, never past the grid's own top
        // R111-services-multiline-header-footer-1: a section may contain a literal Alt+Enter line
        // break (preserved verbatim by TokenizeSectionText as an embedded '\n'). Grow the band height
        // to fit the tallest section's line count -- mirroring PrintRenderer.HeaderFooterPictures.cs's
        // CalculateHeaderFooterLineHeight fix for the WPF path -- and lay every extra line out at its
        // own baseline instead of the previous single fixed baselineY, which silently overwrote/hid
        // every line after the first (a single PdfText op per section, one fixed Y).
        //
        // headerY above is already clamped to never sit below contentTop (the grid's own top edge),
        // so it is the SAFE anchor for whichever line sits closest to the grid. For a header that is
        // the LAST line (reading top-to-bottom the last-typed line ends up nearest the grid below it);
        // earlier lines extend upward (larger Y, away from the grid, toward the page's top edge) --
        // never toward the already-validated-safe grid boundary.
        //
        // R112-services-headerfooter-scale-with-document-1: Sheet.HeaderFooterScaleWithDocument
        // ("Scale with document") was round-tripped and user-editable but never consulted by this PDF
        // export tier -- only the WPF native print/print-preview renderer honored it (R111-app-host-
        // headerfooter-scale-with-document-1). Resolve the same multiplier here via the shared
        // PageGeometryRules.ResolveHeaderFooterFontScale rule (using effectiveScaleRatio, this tier's
        // own fully-resolved grid/content scale, as the WPF path's scaleRatio twin) and apply it to
        // both the per-line row step/band height (mirroring CalculateHeaderFooterLineHeight's `height =
        // HeaderFooterSingleLineHeight * fontScale * maxLines`) and each run's font size (mirroring
        // DrawHeaderFooterFormattedRunsLine's `fontSize = (run.FontSize ?? PrintFontSize) * fontScale`)
        // so Save-As-PDF on both Windows and Linux/macOS reflects the setting exactly like WPF print
        // does, instead of always rendering header/footer text at its authored size regardless of the
        // page's print scale.
        var headerFooterFontScale = PageGeometryRules.ResolveHeaderFooterFontScale(
            sheet.HeaderFooterScaleWithDocument, effectiveScaleRatio);
        var headerFooterLineHeightPt = HeaderFooterLineHeightPt * headerFooterFontScale;

        var headerMaxLines = ResolveMaxSectionLines(header);
        var headerBandHeightPt = Math.Max(headerFooterLineHeightPt * headerMaxLines, mT - headerEdgePt);
        RenderHeaderFooterBand(ops, header, headerPictures, pageW, mL, mR, headerY, headerBandHeightPt, 8,
            headerFooterFontScale, workbook.Name, workbookDirectory, sheet.Name, pageNumber, totalPages, HeaderTextColor,
            lineIndex => headerY + ((headerMaxLines - 1 - lineIndex) * headerFooterLineHeightPt), measurer);

        // Footer text: rendered just above the footer edge from the bottom. R99-services-header-band-2:
        // symmetric clamp -- never draw above (a larger y-up value than) contentBottom, the grid's own
        // bottom edge, once Footer margin exceeds Bottom margin and bodyBottomEdgePt has already
        // raised the grid's bottom edge to footerEdgePt.
        var footerY = Math.Min(footerEdgePt + 2, contentBottom);            // baseline approx 2pt above footer edge, never past the grid's own bottom
        // R111-services-multiline-header-footer-1 (footer half): footerY is already clamped to never
        // sit above contentBottom (the grid's own bottom edge), so it is the safe anchor for whichever
        // line sits closest to the grid -- for a footer that is the FIRST line (it reads immediately
        // below the grid); later lines extend downward (smaller Y, away from the grid, toward the
        // page's bottom edge).
        var footerMaxLines = ResolveMaxSectionLines(footer);
        var footerBandHeightPt = Math.Max(headerFooterLineHeightPt * footerMaxLines, mB - footerEdgePt);
        RenderHeaderFooterBand(ops, footer, footerPictures, pageW, mL, mR, footerY, footerBandHeightPt, 8,
            headerFooterFontScale, workbook.Name, workbookDirectory, sheet.Name, pageNumber, totalPages, FooterTextColor,
            lineIndex => footerY - (lineIndex * headerFooterLineHeightPt), measurer);

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

        // shared-localization-rtl-F1: resolve the same sheet the page-setup-aware path resolves
        // (BuildPageWithPageSetup above) -- by the print AREA that owns this page's PrintRange
        // rather than blindly indexing by request.SheetIndex, which is the print area's position
        // within the export plan's flattened SheetPlans list, not the sheet's index in the
        // workbook (see N45/N46) -- so this legacy fixed-geometry path can resolve Sheet.IsRightToLeft
        // and Format Cells > Alignment the same way the page-setup-aware path already does.
        var sheet = workbook.GetSheet(request.PrintRange.Start.Sheet)
            ?? workbook.GetSheetAt(request.SheetIndex);

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

        var validationCircleCellBounds = new List<LayoutRect>();
        foreach (var cell in contentPlan.Cells)
        {
            var rowIndex = FindRowIndex(contentPlan.Rows, cell.Row);
            var columnIndex = FindColumnIndex(contentPlan.Columns, cell.Column);
            if (rowIndex < 0 || columnIndex < 0)
                continue;

            var x = gridLeft + (columnIndex * columnWidth);
            var y = gridTop - ((rowIndex + 1) * options.RowHeightPoints);
            if (cell.HasValidationCircle)
            {
                validationCircleCellBounds.Add(new LayoutRect(
                    x,
                    y,
                    columnWidth,
                    options.RowHeightPoints));
            }
            var style = workbook.GetStyle(cell.StyleId);
            // R72-render-cf-visual-4-1: same conditional-format fill override as the page-setup-aware
            // path above.
            var fill = cell.ConditionalFillColor ?? style.ResolveFillColor(workbook.Theme);
            if (fill is not null || cell.IsTitle)
                ops.Add(new PdfFillRect(x, y, columnWidth, options.RowHeightPoints, fill is { } fillColor ? ToPdfColor(fillColor) : TitleFillColor));

            ops.Add(new PdfStrokeRect(x, y, columnWidth, options.RowHeightPoints, GridStrokeColor, 0.5));

            // R127B-services-pdf-cell-borders-legacy-1: same Format Cells > Border gap as the
            // page-setup-aware path had before R127-services-pdf-cell-borders-1 -- this legacy
            // fixed-geometry path shares the same PortablePdfPageCell/CellStyle border fields, and is
            // unconditionally reachable from PortablePdfDocumentExporter.CreateDocument (which never
            // calls BuildWithPageSetup) and, through it, from AvaloniaPdfDocumentExporter's
            // Skia-unavailable fallback -- so it must not silently drop explicit borders either.
            // PortablePdfDocumentOptions has no Black-and-White flag (this legacy path never modeled
            // Page Setup > Sheet > "Black and white" for fills either), so pass false to match this
            // path's existing behavior.
            //
            // freex-conditional-format-F1: same CF border override as the page-setup-aware path above.
            DrawCellBorders(ops, style, cell.ConditionalStyle, x, y, columnWidth, options.RowHeightPoints, blackAndWhite: false);

            // shared-localization-rtl-F1: resolve the cell's effective reading order the same way the
            // page-setup-aware path does above (CellTextOrientationLayoutPlanner.
            // ResolveIsEffectivelyRightToLeft) -- this legacy fixed-geometry path shares the same
            // Sheet.IsRightToLeft/CellStyle.ReadingOrder fields, so an RTL sheet or cell must not
            // silently render as LTR here either.
            var isEffectivelyRightToLeft = CellTextOrientationLayoutPlanner.ResolveIsEffectivelyRightToLeft(
                style.ReadingOrder, sheet.IsRightToLeft);

            // R96-render-cf-databar-iconset-1: same data-bar/icon-set overlay as the page-setup-aware
            // path above -- this legacy fixed-geometry path shares the same PortablePdfPageCell fields,
            // so it must not silently drop them either.
            var iconGutterPt = 0.0;
            if (cell.DataBar is { } dataBar)
                DrawConditionalDataBar(ops, dataBar, x, y, columnWidth, options.RowHeightPoints);
            if (cell.IconSet is { } iconSet)
                iconGutterPt = DrawConditionalIconSet(ops, iconSet, x, y, columnWidth, options.RowHeightPoints, isEffectivelyRightToLeft);

            if (string.IsNullOrEmpty(cell.DisplayText))
                continue;

            // freex-conditional-format-F1: same CF Bold/Italic/FontColor override as the page-setup-aware
            // path above.
            var legacyCfStyle = cell.ConditionalStyle;
            var fontSize = Math.Clamp(style.FontSize, 7, 10);
            var effectiveBold = cell.IsTitle || style.Bold || (legacyCfStyle?.Bold ?? false);
            var effectiveItalic = legacyCfStyle?.Italic ?? false;
            var fontFace = ToPdfFontFace(effectiveBold, effectiveItalic);
            var fontColor = ToPdfColor(legacyCfStyle?.FontColor ?? style.ResolveFontColor(workbook.Theme));
            var displayText = PdfWinAnsiTextCapability.Truncate(cell.DisplayText, options.MaximumCellTextLength);

            // shared-localization-rtl-F1: resolve the cell's effective horizontal alignment the same
            // way the page-setup-aware path does above (General resolves to Right for numeric/date
            // content -- left in an RTL context -- and Left otherwise; explicit Left/Center/Right/
            // Justify/Distributed/Fill are honored as authored). Without this every cell -- including
            // right-aligned numbers and centered titles -- rendered flush-left on this legacy path.
            var rawCell = sheet.GetCell(cell.Row, cell.Column);
            var isNumeric = rawCell?.Value is NumberValue or DateTimeValue;
            var effectiveAlign = CellTextOrientationLayoutPlanner.ResolveEffectiveHorizontalAlignment(
                style.HorizontalAlignment, isNumeric, isEffectivelyRightToLeft);
            var textWidth = PortablePdfTextMeasurer.Instance.Measure(
                displayText, null, fontSize, effectiveBold, effectiveItalic).Width;
            var textX = effectiveAlign switch
            {
                // Right: anchor the text's right edge inside the cell, matching the page-setup-aware
                // path's identical Right branch (deliberately not clamped -- a too-wide right-aligned
                // value overflows leftward, exactly like Excel and the on-screen viewport).
                HorizontalAlignment.Right => x + columnWidth - textWidth - 4,
                HorizontalAlignment.Center
                    or HorizontalAlignment.Justify
                    or HorizontalAlignment.Distributed => x + ((columnWidth - textWidth) / 2.0),
                HorizontalAlignment.Fill => x + 4,
                _ => x + 4 + iconGutterPt
            };
            ops.Add(new PdfText(
                textX,
                y + Math.Max(7, options.RowHeightPoints - 14),
                fontSize,
                fontFace,
                fontColor,
                displayText,
                style.ResolveEffectiveFontName(workbook.Theme)));
        }

        foreach (var cellBounds in validationCircleCellBounds)
            DrawValidationCircle(ops, cellBounds);

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

    private static void DrawValidationCircle(List<PdfDrawOp> ops, LayoutRect cellBounds)
    {
        var ellipse = ValidationCircleLayoutPlanner.CalculateEllipseBounds(cellBounds);
        ops.Add(new PdfStrokeEllipse(
            ellipse.Left,
            ellipse.Top,
            ellipse.Width,
            ellipse.Height,
            ToPdfColor(ValidationCircleLayoutPlanner.StrokeColor),
            ValidationCircleLayoutPlanner.StrokeThickness));
    }

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
        var uniformFitScale = PageGeometryRules.ResolveUniformScale(widthFitScale, heightFitScale);

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
        PortablePdfPageContentPlan contentPlan,
        ITextMeasurer textMeasurer)
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
            AddCenteredHeadingText(ops, label, x, w, bandBottom, headingHeightPt, textMeasurer);
        }

        // Row numbers, spanning the same row heights/offsets as the cell grid.
        for (var rowIndex = 0; rowIndex < rowYs.Length && rowIndex < rowHeights.Length; rowIndex++)
        {
            var y = rowYs[rowIndex];
            var h = rowHeights[rowIndex];
            ops.Add(new PdfFillRect(contentLeft, y, headingWidthPt, h, HeadingFillColor));
            ops.Add(new PdfStrokeRect(contentLeft, y, headingWidthPt, h, HeadingBorderColor, 0.4));

            var label = contentPlan.Rows[rowIndex].Row.ToString(CultureInfo.InvariantCulture);
            AddCenteredHeadingText(ops, label, contentLeft, headingWidthPt, y, h, textMeasurer);
        }
    }

    /// <summary>Centers <paramref name="label"/> horizontally and vertically inside a heading cell rect.</summary>
    private static void AddCenteredHeadingText(
        List<PdfDrawOp> ops, string label, double cellX, double cellWidth, double cellBottomY, double cellHeight,
        ITextMeasurer textMeasurer)
    {
        var textWidth = textMeasurer.Measure(
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
        double pageHeightPoints,
        ITextMeasurer textMeasurer)
    {
        if (request.IsCommentSummaryPage)
            return;

        var layout = BuildPageContentLayout(workbook, sheet, exportPlan, request, textMeasurer);
        if (layout is null ||
            (layout.Charts.Count == 0 && layout.TextBoxes.Count == 0 && layout.Pictures.Count == 0))
        {
            return;
        }

        var scaleX = pageWidthPoints / layout.PageBounds.Width;
        var scaleY = pageHeightPoints / layout.PageBounds.Height;

        // R127-services-draft-quality-vector-drawings-1: layout.Charts/layout.Pictures are already
        // empty here when Sheet.PrintDraftQuality is set -- PageContentRenderModelBuilder.Build (the
        // single upstream choke point BOTH this PDF-export path and the Avalonia interactive
        // print-preview canvas read from) omits them at their source, matching the WPF native
        // print/PDF path's own `!draftQuality` guard (PrintRenderer.HeaderFooter.cs) around charts and
        // raster pictures. No local guard needed here: text boxes stay unconditional below (vector
        // text content, not "graphics" -- Excel's Draft Quality does not suppress them).
        foreach (var chart in layout.Charts)
        {
            AddFillRect(ops, chart.Bounds, chart.Fill, pageHeightPoints, scaleX, scaleY);
            AddStrokeRect(ops, chart.Bounds, chart.Outline, chart.OutlineThickness, pageHeightPoints, scaleX, scaleY);
            AddChartPlotOps(workbook, sheet, chart, ops, pageHeightPoints, scaleX, scaleY, textMeasurer);

            foreach (var overlay in chart.TextOverlays)
                AddTextOverlay(ops, overlay, pageHeightPoints, scaleX, scaleY);
        }

        // R92-consumer-wiring-sweep-1: sheet pictures (Insert > Pictures, or a raster non-linked
        // Paste Special > Picture) were never emitted here at all, so an inserted picture silently
        // never appeared in print/PDF on either platform even though it always rendered on screen
        // (GridView.DrawingObjects.Pictures.cs). Paint order matches the on-screen z-order fallback
        // this codebase already uses for charts vs. text boxes: pictures sit above the chart layer,
        // below text-box annotations.
        foreach (var picture in layout.Pictures)
            AddPictureImage(ops, picture, pageHeightPoints, scaleX, scaleY);

        foreach (var textBox in layout.TextBoxes)
        {
            if (textBox.Fill is { } fill)
                AddFillRect(ops, textBox.Bounds, fill, pageHeightPoints, scaleX, scaleY, textBox.FillAlpha / 255d);

            // R91-commands-insert-object-5-1: Outline is null when the text box's line is
            // explicitly suppressed (TextBoxModel.OutlineHasNoFill) -- emit no stroke rather than
            // always forcing one.
            if (textBox.Outline is { } outline)
                AddStrokeRect(ops, textBox.Bounds, outline, textBox.OutlineThickness, pageHeightPoints, scaleX, scaleY);

            if (!string.IsNullOrWhiteSpace(textBox.Text))
            {
                var fontSize = Math.Max(1, textBox.Font.FontSize * scaleY);
                ops.Add(new PdfText(
                    textBox.TextBounds.Left * scaleX,
                    pageHeightPoints - ((textBox.TextBounds.Top * scaleY) + fontSize),
                    fontSize,
                    ToPdfFontFace(textBox.Font.Bold, textBox.Font.Italic),
                    ToPdfColor(textBox.Font.Color),
                    PdfWinAnsiTextCapability.Truncate(textBox.Text, 128),
                    textBox.Font.FontFamily));
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
        double scaleY,
        ITextMeasurer textMeasurer)
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
            textMeasurer);
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
                // R128-services-pdf-chart-plot-kinds-4: the remaining SeriesGeometryKind values the
                // layout engine emits for Area/StackedArea, Scatter, Pie/3DPie/Doughnut, Bubble,
                // Radar, Stock, BoxAndWhisker, Treemap/Sunburst, and Surface/3DSurface charts --
                // previously unhandled here, so those chart types printed/exported as an empty
                // bordered box (only the chart-area fill/outline drawn above, never the plotted
                // data). Mirrors AvaloniaChartRenderer.RenderSeries, the on-screen renderer that
                // already covers every one of these kinds.
                case SeriesGeometryKind.Area:
                    AddChartAreaOps(workbook, chart, chartBlock.Bounds, series, palette, ops, pageHeightPoints, scaleX, scaleY);
                    break;
                case SeriesGeometryKind.ScatterPoints:
                    AddChartScatterOps(workbook, chart, chartBlock.Bounds, series, palette, ops, pageHeightPoints, scaleX, scaleY);
                    break;
                case SeriesGeometryKind.PieSlices:
                    AddChartPieOps(workbook, chart, chartBlock.Bounds, series, palette, ops, pageHeightPoints, scaleX, scaleY);
                    break;
                case SeriesGeometryKind.Bubbles:
                    AddChartBubbleOps(workbook, chart, chartBlock.Bounds, series, palette, ops, pageHeightPoints, scaleX, scaleY);
                    break;
                case SeriesGeometryKind.RadarPolyline:
                    AddChartRadarOps(workbook, chart, chartBlock.Bounds, series, palette, ops, pageHeightPoints, scaleX, scaleY);
                    break;
                case SeriesGeometryKind.StockBars:
                    AddChartStockOps(workbook, chart, chartBlock.Bounds, series, palette, ops, pageHeightPoints, scaleX, scaleY);
                    break;
                case SeriesGeometryKind.BoxWhiskers:
                    AddChartBoxWhiskerOps(chartBlock.Bounds, series, ops, pageHeightPoints, scaleX, scaleY);
                    break;
                case SeriesGeometryKind.TreemapTiles:
                    AddChartTreemapOps(chartBlock.Bounds, series, palette, ops, pageHeightPoints, scaleX, scaleY);
                    break;
                case SeriesGeometryKind.SurfaceCells:
                    AddChartSurfaceOps(chartBlock.Bounds, series, ops, pageHeightPoints, scaleX, scaleY);
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

    // R128-services-pdf-chart-plot-kinds-4: fill/stroke opacities for translucent series fills
    // (area band, radar polygon, bubble marker), matching AvaloniaChartRenderer's hardcoded alpha
    // bytes (0xA0, 0x40, 0x99 respectively) converted to a [0,1] PdfOpacityGroup fraction.
    private const double ChartAreaFillOpacity = 0xA0 / 255.0;
    private const double ChartRadarFillOpacity = 0x40 / 255.0;
    private const double ChartBubbleFillOpacity = 0x99 / 255.0;
    private const double ChartMarkerRadius = 3.5;
    private static readonly PdfColor ChartWhite = new(0xFF, 0xFF, 0xFF);

    private static void AddChartAreaOps(
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
        if (series.Points.Count == 0)
            return;

        var paint = ChartStylePlanner.ResolveSeriesPaint(chart, series.SeriesIndex, workbook.Theme, palette);
        var format = ChartStylePlanner.FindSeriesFormat(chart, series.SeriesIndex);
        var strokeWidth = Math.Max(0.25, (format?.StrokeThickness ?? 2.0) * Math.Min(scaleX, scaleY));

        var points = new List<LayoutPoint>(series.Points.Count + series.BaselinePoints.Count + 2);
        foreach (var p in series.Points)
            points.Add(p.Position);

        if (series.BaselinePoints.Count > 0)
        {
            // Stacked-area band: close the ring back along the per-category bottom baseline.
            for (var i = series.BaselinePoints.Count - 1; i >= 0; i--)
                points.Add(series.BaselinePoints[i].Position);
        }
        else
        {
            // Plain area: close the polygon down to the flat scalar baseline (zero line).
            var last = series.Points[^1].Position;
            var first = series.Points[0].Position;
            points.Add(new LayoutPoint(last.X, series.AreaBaseline));
            points.Add(new LayoutPoint(first.X, series.AreaBaseline));
        }

        var contour = BuildSeriesContour(chartBounds, points, scaleX, scaleY, pageHeightPoints);

        // Fill and stroke are emitted as separate ops (fill wrapped in an opacity group, stroke at
        // full opacity) so the outline stays crisp -- matching Avalonia's Polygon, whose Fill and
        // Stroke brushes carry independent alpha.
        ops.Add(new PdfOpacityGroup(ChartAreaFillOpacity, [new PdfPath([contour], ToPdfColor(paint.FillColor), null, 0)]));
        ops.Add(new PdfPath([contour], null, ToPdfColor(paint.StrokeColor), strokeWidth));
    }

    private static void AddChartScatterOps(
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
        var paint = ChartStylePlanner.ResolveSeriesPaint(chart, series.SeriesIndex, workbook.Theme, palette);
        var fillColor = ToPdfColor(paint.FillColor);
        var strokeColor = ToPdfColor(paint.StrokeColor);

        foreach (var point in series.Points)
        {
            AddSeriesFillEllipse(ops, chartBounds, point.Position, ChartMarkerRadius, fillColor, pageHeightPoints, scaleX, scaleY);
            AddSeriesStrokeEllipse(ops, chartBounds, point.Position, ChartMarkerRadius, strokeColor, 1.0, pageHeightPoints, scaleX, scaleY);
        }
    }

    private static void AddChartPieOps(
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
        foreach (var slice in series.Slices)
        {
            if (slice.Arc.SweepAngleDegrees <= 0 || slice.Arc.OuterRadius <= 0)
                continue;

            // Per-slice fill override (Format Data Point) takes priority over the theme-palette-by-
            // point-index color, matching Avalonia's RenderPie / WPF's ChartRenderer.
            var fillColor = ChartStylePlanner.ResolvePointFillColor(chart, series.SeriesIndex, slice.PointIndex, workbook.Theme)
                ?? ChartStylePlanner.GetPaletteColor(palette, slice.PointIndex);

            var contour = BuildPieSliceContour(chartBounds, slice.Arc, scaleX, scaleY, pageHeightPoints);
            ops.Add(new PdfPath([contour], ToPdfColor(fillColor), ChartWhite, 1.0));
        }
    }

    private static void AddChartBubbleOps(
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
        var paint = ChartStylePlanner.ResolveSeriesPaint(chart, series.SeriesIndex, workbook.Theme, palette);
        var fillColor = ToPdfColor(paint.FillColor);
        var strokeColor = ToPdfColor(paint.StrokeColor);

        foreach (var bubble in series.Bubbles)
        {
            if (bubble.Radius <= 0)
                continue;

            AddSeriesFillEllipse(ops, chartBounds, bubble.Center, bubble.Radius, fillColor, pageHeightPoints, scaleX, scaleY, ChartBubbleFillOpacity);
            AddSeriesStrokeEllipse(ops, chartBounds, bubble.Center, bubble.Radius, strokeColor, 1.0, pageHeightPoints, scaleX, scaleY);
        }
    }

    private static void AddChartRadarOps(
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
        if (series.Points.Count == 0)
            return;

        var paint = ChartStylePlanner.ResolveSeriesPaint(chart, series.SeriesIndex, workbook.Theme, palette);
        var format = ChartStylePlanner.FindSeriesFormat(chart, series.SeriesIndex);
        var strokeWidth = Math.Max(0.25, (format?.StrokeThickness ?? 2.0) * Math.Min(scaleX, scaleY));

        var contour = BuildSeriesContour(
            chartBounds, series.Points.Select(p => p.Position).ToList(), scaleX, scaleY, pageHeightPoints);

        ops.Add(new PdfOpacityGroup(ChartRadarFillOpacity, [new PdfPath([contour], ToPdfColor(paint.FillColor), null, 0)]));
        ops.Add(new PdfPath([contour], null, ToPdfColor(paint.StrokeColor), strokeWidth));

        var markerFill = ToPdfColor(paint.FillColor);
        var markerStroke = ToPdfColor(paint.StrokeColor);
        foreach (var point in series.Points)
        {
            AddSeriesFillEllipse(ops, chartBounds, point.Position, ChartMarkerRadius, markerFill, pageHeightPoints, scaleX, scaleY);
            AddSeriesStrokeEllipse(ops, chartBounds, point.Position, ChartMarkerRadius, markerStroke, 1.0, pageHeightPoints, scaleX, scaleY);
        }
    }

    private static void AddChartStockOps(
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
        var paint = ChartStylePlanner.ResolveSeriesPaint(chart, series.SeriesIndex, workbook.Theme, palette);
        var strokeColor = ToPdfColor(paint.StrokeColor);
        const double tickLength = 4;

        foreach (var element in series.StockElements)
        {
            AddSeriesLine(
                ops, chartBounds, new LayoutPoint(element.X, element.HighY), new LayoutPoint(element.X, element.LowY),
                strokeColor, 1.0, pageHeightPoints, scaleX, scaleY);

            if (element.HasOpen)
            {
                // Candlestick: a box spanning open..close, white when up and filled when down.
                var top = Math.Min(element.OpenY, element.CloseY);
                var bottom = Math.Max(element.OpenY, element.CloseY);
                var box = new LayoutRect(element.X - tickLength, top, tickLength * 2, Math.Max(1, bottom - top));
                var boxFill = element.IsUp ? ChartWhite : strokeColor;
                AddSeriesFillRect(ops, chartBounds, box, boxFill, pageHeightPoints, scaleX, scaleY);
                AddSeriesStrokeRect(ops, chartBounds, box, strokeColor, 1.0, pageHeightPoints, scaleX, scaleY);
            }
            else
            {
                // High-low-close: a left open tick and a right close tick on the vertical line.
                AddSeriesLine(
                    ops, chartBounds, new LayoutPoint(element.X - tickLength, element.OpenY), new LayoutPoint(element.X, element.OpenY),
                    strokeColor, 1.0, pageHeightPoints, scaleX, scaleY);
                AddSeriesLine(
                    ops, chartBounds, new LayoutPoint(element.X, element.CloseY), new LayoutPoint(element.X + tickLength, element.CloseY),
                    strokeColor, 1.0, pageHeightPoints, scaleX, scaleY);
            }
        }
    }

    // Box-and-whisker overlay -- paired SeriesPoints encode whisker/median segments, mirroring
    // AvaloniaChartRenderer.RenderBoxWhiskers. Points arrive in groups of 6 per box:
    // [medL, medR, lowW, Q1, Q3, upW].
    private static void AddChartBoxWhiskerOps(
        LayoutRect chartBounds,
        SeriesLayout series,
        List<PdfDrawOp> ops,
        double pageHeightPoints,
        double scaleX,
        double scaleY)
    {
        var pts = series.Points;
        if (pts.Count == 0)
            return;

        var stroke = new PdfColor(0x1F, 0x49, 0x7D); // dark blue, matches Avalonia/WPF
        const double thickness = 1.5;

        var i = 0;
        while (i + 5 < pts.Count)
        {
            var medL = pts[i + 0].Position;
            var medR = pts[i + 1].Position;
            var lowW = pts[i + 2].Position;
            var q1Pt = pts[i + 3].Position;
            var q3Pt = pts[i + 4].Position;
            var upW = pts[i + 5].Position;

            AddSeriesLine(ops, chartBounds, medL, medR, stroke, thickness + 0.5, pageHeightPoints, scaleX, scaleY);
            AddSeriesLine(ops, chartBounds, lowW, q1Pt, stroke, thickness, pageHeightPoints, scaleX, scaleY);
            AddSeriesLine(ops, chartBounds, q3Pt, upW, stroke, thickness, pageHeightPoints, scaleX, scaleY);

            var cx = (medL.X + medR.X) / 2.0;
            var capHalf = (medR.X - medL.X) * 0.25;
            AddSeriesLine(
                ops, chartBounds, new LayoutPoint(cx - capHalf, lowW.Y), new LayoutPoint(cx + capHalf, lowW.Y),
                stroke, thickness, pageHeightPoints, scaleX, scaleY);
            AddSeriesLine(
                ops, chartBounds, new LayoutPoint(cx - capHalf, upW.Y), new LayoutPoint(cx + capHalf, upW.Y),
                stroke, thickness, pageHeightPoints, scaleX, scaleY);

            i += 6;
        }
    }

    // Treemap tiles -- SeriesBars carry per-bar FillColorOverride (palette color); white stroke
    // between tiles, matching AvaloniaChartRenderer.RenderTreemapTiles. (Tile labels are drawn
    // separately, via chart.TextOverlays -- not part of the series geometry this method plots.)
    private static void AddChartTreemapOps(
        LayoutRect chartBounds,
        SeriesLayout series,
        IReadOnlyList<CellColor> palette,
        List<PdfDrawOp> ops,
        double pageHeightPoints,
        double scaleX,
        double scaleY)
    {
        foreach (var bar in series.Bars)
        {
            if (bar.Rect.Width <= 0 || bar.Rect.Height <= 0)
                continue;

            var fillColor = bar.FillColorOverride ?? ChartStylePlanner.GetPaletteColor(palette, bar.PointIndex);
            AddSeriesFillRect(ops, chartBounds, bar.Rect, ToPdfColor(fillColor), pageHeightPoints, scaleX, scaleY);
            AddSeriesStrokeRect(ops, chartBounds, bar.Rect, ChartWhite, 2.0, pageHeightPoints, scaleX, scaleY);
        }
    }

    // Surface/heatmap cells -- pre-colored grid, no stroke, matching AvaloniaChartRenderer.RenderSurfaceCells.
    private static void AddChartSurfaceOps(
        LayoutRect chartBounds,
        SeriesLayout series,
        List<PdfDrawOp> ops,
        double pageHeightPoints,
        double scaleX,
        double scaleY)
    {
        foreach (var cell in series.SurfaceCells)
        {
            if (cell.Rect.Width <= 0 || cell.Rect.Height <= 0)
                continue;

            AddSeriesFillRect(ops, chartBounds, cell.Rect, ToPdfColor(cell.FillColor), pageHeightPoints, scaleX, scaleY);
        }
    }

    // ── Series geometry helpers ──────────────────────────────────────────────
    // Shared choke point every Add-Chart*Ops method above routes through: converts chart-local
    // layout-space geometry (relative to chartBlock.Bounds) into PDF user-space draw ops, the same
    // transform AddChartBarOps/AddChartLineOps already apply inline.

    private static PdfPathPoint ToSeriesPathPoint(
        LayoutRect chartBounds, LayoutPoint local, double scaleX, double scaleY, double pageHeightPoints) =>
        new(
            ToPdfX(chartBounds.Left + local.X, scaleX),
            ToPdfY(chartBounds.Top + local.Y, pageHeightPoints, scaleY));

    private static PdfPathContour BuildSeriesContour(
        LayoutRect chartBounds, IReadOnlyList<LayoutPoint> points, double scaleX, double scaleY, double pageHeightPoints)
    {
        var pathPoints = points.Select(p => ToSeriesPathPoint(chartBounds, p, scaleX, scaleY, pageHeightPoints)).ToList();
        return new PdfPathContour(pathPoints[0], pathPoints.Skip(1).Select(PdfPathSegment.LineTo).ToList(), Closed: true);
    }

    private static void AddSeriesLine(
        List<PdfDrawOp> ops, LayoutRect chartBounds, LayoutPoint from, LayoutPoint to,
        PdfColor color, double lineWidth, double pageHeightPoints, double scaleX, double scaleY)
    {
        ops.Add(new PdfLine(
            ToPdfX(chartBounds.Left + from.X, scaleX),
            ToPdfY(chartBounds.Top + from.Y, pageHeightPoints, scaleY),
            ToPdfX(chartBounds.Left + to.X, scaleX),
            ToPdfY(chartBounds.Top + to.Y, pageHeightPoints, scaleY),
            color,
            Math.Max(0.25, lineWidth * Math.Min(scaleX, scaleY))));
    }

    private static void AddSeriesFillRect(
        List<PdfDrawOp> ops, LayoutRect chartBounds, LayoutRect rect, PdfColor color,
        double pageHeightPoints, double scaleX, double scaleY)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        ops.Add(new PdfFillRect(
            ToPdfX(chartBounds.Left + rect.Left, scaleX),
            ToPdfY(chartBounds.Top + rect.Bottom, pageHeightPoints, scaleY),
            rect.Width * scaleX,
            rect.Height * scaleY,
            color));
    }

    private static void AddSeriesStrokeRect(
        List<PdfDrawOp> ops, LayoutRect chartBounds, LayoutRect rect, PdfColor color, double lineWidth,
        double pageHeightPoints, double scaleX, double scaleY)
    {
        if (rect.Width <= 0 || rect.Height <= 0 || lineWidth <= 0)
            return;

        ops.Add(new PdfStrokeRect(
            ToPdfX(chartBounds.Left + rect.Left, scaleX),
            ToPdfY(chartBounds.Top + rect.Bottom, pageHeightPoints, scaleY),
            rect.Width * scaleX,
            rect.Height * scaleY,
            color,
            Math.Max(0.25, lineWidth * Math.Min(scaleX, scaleY))));
    }

    private static void AddSeriesFillEllipse(
        List<PdfDrawOp> ops, LayoutRect chartBounds, LayoutPoint center, double radius, PdfColor color,
        double pageHeightPoints, double scaleX, double scaleY, double opacity = 1.0)
    {
        if (radius <= 0)
            return;

        var ellipse = new PdfFillEllipse(
            ToPdfX(chartBounds.Left + center.X - radius, scaleX),
            ToPdfY(chartBounds.Top + center.Y + radius, pageHeightPoints, scaleY),
            radius * 2 * scaleX,
            radius * 2 * scaleY,
            color);
        ops.Add(opacity >= 0.999 ? ellipse : new PdfOpacityGroup(Math.Clamp(opacity, 0, 1), [ellipse]));
    }

    private static void AddSeriesStrokeEllipse(
        List<PdfDrawOp> ops, LayoutRect chartBounds, LayoutPoint center, double radius, PdfColor color, double lineWidth,
        double pageHeightPoints, double scaleX, double scaleY)
    {
        if (radius <= 0 || lineWidth <= 0)
            return;

        ops.Add(new PdfStrokeEllipse(
            ToPdfX(chartBounds.Left + center.X - radius, scaleX),
            ToPdfY(chartBounds.Top + center.Y + radius, pageHeightPoints, scaleY),
            radius * 2 * scaleX,
            radius * 2 * scaleY,
            color,
            Math.Max(0.25, lineWidth * Math.Min(scaleX, scaleY))));
    }

    private static PdfPathPoint PolarSeriesPoint(
        LayoutRect chartBounds, LayoutPoint center, double angleDegrees, double radius,
        double scaleX, double scaleY, double pageHeightPoints)
    {
        // Mirrors ChartLayoutEngine's/AvaloniaChartRenderer's pie convention: angle is clockwise
        // from 12 o'clock.
        var radians = Math.PI / 180.0 * angleDegrees;
        var local = new LayoutPoint(center.X + (radius * Math.Sin(radians)), center.Y - (radius * Math.Cos(radians)));
        return ToSeriesPathPoint(chartBounds, local, scaleX, scaleY, pageHeightPoints);
    }

    /// <summary>
    /// Approximates a pie/doughnut wedge as a closed polygon (a line segment every ~4 degrees along
    /// the outer -- and, for a doughnut, inner -- arc). Visually indistinguishable from a true
    /// elliptical-arc path at chart sizes, and every portable PDF backend already supports
    /// <see cref="PdfPathSegmentKind.Line"/> contours without needing an arc primitive.
    /// </summary>
    private static PdfPathContour BuildPieSliceContour(
        LayoutRect chartBounds, LayoutArc arc, double scaleX, double scaleY, double pageHeightPoints)
    {
        const double maxStepDegrees = 4.0;
        var steps = Math.Max(1, (int)Math.Ceiling(arc.SweepAngleDegrees / maxStepDegrees));
        var segments = new List<PdfPathSegment>();

        if (arc.InnerRadius > 0)
        {
            // Doughnut: outer arc, then straight across to the inner arc, then back to the start.
            var start = PolarSeriesPoint(chartBounds, arc.Center, arc.StartAngleDegrees, arc.OuterRadius, scaleX, scaleY, pageHeightPoints);
            for (var i = 1; i <= steps; i++)
            {
                var angle = arc.StartAngleDegrees + (arc.SweepAngleDegrees * i / steps);
                segments.Add(PdfPathSegment.LineTo(PolarSeriesPoint(chartBounds, arc.Center, angle, arc.OuterRadius, scaleX, scaleY, pageHeightPoints)));
            }
            segments.Add(PdfPathSegment.LineTo(PolarSeriesPoint(chartBounds, arc.Center, arc.EndAngleDegrees, arc.InnerRadius, scaleX, scaleY, pageHeightPoints)));
            for (var i = steps - 1; i >= 0; i--)
            {
                var angle = arc.StartAngleDegrees + (arc.SweepAngleDegrees * i / steps);
                segments.Add(PdfPathSegment.LineTo(PolarSeriesPoint(chartBounds, arc.Center, angle, arc.InnerRadius, scaleX, scaleY, pageHeightPoints)));
            }
            return new PdfPathContour(start, segments, Closed: true);
        }

        // Pie wedge: center -> outer start -> arc -> back to center (closed).
        var wedgeStart = ToSeriesPathPoint(chartBounds, arc.Center, scaleX, scaleY, pageHeightPoints);
        segments.Add(PdfPathSegment.LineTo(PolarSeriesPoint(chartBounds, arc.Center, arc.StartAngleDegrees, arc.OuterRadius, scaleX, scaleY, pageHeightPoints)));
        for (var i = 1; i <= steps; i++)
        {
            var angle = arc.StartAngleDegrees + (arc.SweepAngleDegrees * i / steps);
            segments.Add(PdfPathSegment.LineTo(PolarSeriesPoint(chartBounds, arc.Center, angle, arc.OuterRadius, scaleX, scaleY, pageHeightPoints)));
        }
        return new PdfPathContour(wedgeStart, segments, Closed: true);
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
        PortablePdfExportPageRequest request,
        ITextMeasurer textMeasurer)
    {
        if (request.SheetIndex < 0 || request.SheetIndex >= exportPlan.ExportPrintPlan.SheetPlans.Count)
            return null;

        var sheetPlan = exportPlan.ExportPrintPlan.SheetPlans[request.SheetIndex];
        var pagePlan = new PagePaginationResult(
            PagePaginationPlanner.BuildSegments(sheetPlan.RowPagePlans),
            PagePaginationPlanner.BuildSegments(sheetPlan.ColumnPagePlans),
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
                textMeasurer);
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

    /// <summary>
    /// Emits one <see cref="PdfImage"/> op for a resolved picture block, converting its layout-space
    /// bounds to PDF points/bottom-up Y the same way <see cref="AddFillRect"/> does for a chart/text-box
    /// rectangle, and forwarding the picture's crop fractions as-is -- <see cref="PdfImageSourceCrop"/>
    /// uses the identical 0.0-1.0-cut-from-each-edge convention as <c>PictureCropRatios</c>
    /// (see <see cref="Free.Shared.Pdf.PdfRenderGeometry.TryGetImageSourceRect"/>), so no re-derivation
    /// is needed. Rotation is intentionally not forwarded, matching the printed text-box block's own
    /// established scope (see <see cref="FreeX.App.Presentation.PageLayout.PagePictureLayoutPlanner"/>).
    /// An unsupported <see cref="PagePictureBlock.ContentType"/> is safely skipped by the shared PDF
    /// writer rather than emitting a corrupt image stream, so no gate is needed here.
    /// </summary>
    private static void AddPictureImage(
        List<PdfDrawOp> ops,
        PagePictureBlock picture,
        double pageHeightPoints,
        double scaleX,
        double scaleY)
    {
        ops.Add(new PdfImage(
            picture.Bounds.Left * scaleX,
            pageHeightPoints - (picture.Bounds.Bottom * scaleY),
            picture.Bounds.Width * scaleX,
            picture.Bounds.Height * scaleY,
            picture.ImageBytes,
            picture.ContentType,
            SourceCrop: new PdfImageSourceCrop(
                picture.Crop.Left,
                picture.Crop.Top,
                picture.Crop.Right,
                picture.Crop.Bottom)));
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
            PdfWinAnsiTextCapability.Truncate(overlay.Text, 128));

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

    // R111-services-multiline-header-footer-1: per-line row height for a header/footer text band, in
    // PDF points -- mirrors PrintRenderer.HeaderFooterPictures.cs's HeaderFooterSingleLineHeight (the
    // WPF path's analogous constant) but scaled down for this tier's fixed 8pt header/footer font
    // instead of WPF's ~11pt default cell font.
    private const double HeaderFooterLineHeightPt = 10.0;

    /// <summary>
    /// The largest number of printed lines any of a header/footer's Left/Center/Right sections
    /// produces (see <see cref="PagePrintTextPlanner.CountSectionLines"/>) -- the band must be tall
    /// enough, and every section's lines aligned row-for-row, to fit whichever section has the most
    /// embedded line breaks.
    /// </summary>
    private static int ResolveMaxSectionLines(WorksheetHeaderFooter section) =>
        Math.Max(1, Math.Max(
            PagePrintTextPlanner.CountSectionLines(section.Left),
            Math.Max(PagePrintTextPlanner.CountSectionLines(section.Center), PagePrintTextPlanner.CountSectionLines(section.Right))));

    private static void RenderHeaderFooterBand(
        List<PdfDrawOp> ops,
        WorksheetHeaderFooter band,
        WorksheetHeaderFooterPictureSet pictures,
        double pageW,
        double mL,
        double mR,
        double baselineY,
        double bandHeightPt,
        double fontSize,
        double headerFooterFontScale,
        string workbookName,
        string workbookDirectory,
        string sheetName,
        int pageNumber,
        int totalPages,
        PdfColor color,
        Func<int, double> lineBaselineY,
        ITextMeasurer textMeasurer)
    {
        var now = DateTime.Now;
        var sectionWidth = Math.Max(1, (pageW - mL - mR) / 3.0);

        RenderHeaderFooterSection(
            ops, band.Left, pictures.Left, HeaderFooterSectionAlign.Left,
            mL, mL + sectionWidth, baselineY, bandHeightPt, fontSize, headerFooterFontScale,
            workbookName, workbookDirectory, sheetName, pageNumber, totalPages, now, color, lineBaselineY, textMeasurer);

        RenderHeaderFooterSection(
            ops, band.Center, pictures.Center, HeaderFooterSectionAlign.Center,
            mL + sectionWidth, mL + (2 * sectionWidth), baselineY, bandHeightPt, fontSize, headerFooterFontScale,
            workbookName, workbookDirectory, sheetName, pageNumber, totalPages, now, color, lineBaselineY, textMeasurer);

        RenderHeaderFooterSection(
            ops, band.Right, pictures.Right, HeaderFooterSectionAlign.Right,
            pageW - mR - sectionWidth, pageW - mR, baselineY, bandHeightPt, fontSize, headerFooterFontScale,
            workbookName, workbookDirectory, sheetName, pageNumber, totalPages, now, color, lineBaselineY, textMeasurer);
    }

    private enum HeaderFooterSectionAlign { Left, Center, Right }

    private const int HeaderFooterMaxChars = 128;

    /// <summary>
    /// Renders one left/center/right header-or-footer section: tokenizes its Excel format-code
    /// string via the shared portable <see cref="PagePrintTextPlanner.TokenizeSectionText"/> (the
    /// same tokenizer the WPF <c>PrintRenderer.HeaderFooterDrawing</c> path uses), splits the tokenized
    /// runs on any embedded line break (<see cref="PagePrintTextPlanner.SplitRunsIntoLines"/> --
    /// R111-services-multiline-header-footer-1) and draws each resulting line at its own
    /// <paramref name="lineBaselineY"/>-resolved baseline, draws each run with its own bold/italic/
    /// size/color (plus underline/strikethrough rules), measures every run with <see
    /// cref="PortablePdfTextMeasurer"/> so center/right text is actually centered/right-aligned within
    /// its section instead of flush-left, and draws the section's <c>&amp;G</c> header/footer picture
    /// (if configured) via a <see cref="PdfImage"/> op.
    /// <paramref name="headerFooterFontScale"/> is Sheet.HeaderFooterScaleWithDocument's resolved
    /// multiplier (R112-services-headerfooter-scale-with-document-1, via
    /// PageGeometryRules.ResolveHeaderFooterFontScale) -- 1.0 when the flag is off or the page's own
    /// scale is 100%. It scales the header/footer picture's own vertical anchor together with the
    /// text baseline it centers against, but never the picture's own width/height (matching the WPF
    /// path, where "Scale with document" only ever affects header/footer TEXT).
    /// </summary>
    private static void RenderHeaderFooterSection(
        List<PdfDrawOp> ops,
        string raw,
        WorksheetHeaderFooterPicture? picture,
        HeaderFooterSectionAlign align,
        double sectionLeft,
        double sectionRight,
        double baselineY,
        double bandHeightPt,
        double fontSize,
        double headerFooterFontScale,
        string workbookName,
        string workbookDirectory,
        string sheetName,
        int pageNumber,
        int totalPages,
        DateTime now,
        PdfColor color,
        Func<int, double> lineBaselineY,
        ITextMeasurer textMeasurer)
    {
        if (string.IsNullOrEmpty(raw))
            return;

        var sectionWidth = Math.Max(1, sectionRight - sectionLeft);
        var textLeft = sectionLeft;
        var textRight = sectionRight;

        // &G / &[Picture]: draw the section's configured header/footer picture and, for left/right
        // sections, reserve space so the text doesn't draw underneath it -- matching
        // PrintRenderer.HeaderFooterPictures.cs's CalculateHeaderFooterPictureRect/TextRect (center
        // sections there also leave the text rect unshifted, so we mirror that too).
        if (picture is not null && HasHeaderFooterPictureToken(raw))
        {
            const double ptPerPx = 72.0 / 96.0;
            var imageWidth = Math.Min(Math.Max(1.0, picture.Width * ptPerPx), sectionWidth);
            var imageHeight = Math.Min(Math.Max(1.0, picture.Height * ptPerPx), Math.Max(bandHeightPt, imageWidth));
            var imageX = align switch
            {
                HeaderFooterSectionAlign.Center => sectionLeft + ((sectionWidth - imageWidth) / 2.0),
                HeaderFooterSectionAlign.Right => sectionRight - imageWidth,
                _ => sectionLeft
            };
            // Vertically center the picture on the same line the text baseline sits on. Uses the
            // scaled font size (matching the text drawn at this baseline below) purely to anchor the
            // picture's own position -- the picture's imageWidth/imageHeight above are never scaled by
            // headerFooterFontScale, only their vertical placement follows the text baseline.
            var imageY = baselineY - (imageHeight / 2.0) + ((fontSize * headerFooterFontScale) / 2.0);
            ops.Add(new PdfImage(imageX, imageY, imageWidth, imageHeight, picture.ImageBytes, picture.ContentType));

            const double gap = 4.0;
            if (align == HeaderFooterSectionAlign.Left)
                textLeft = Math.Min(sectionRight, sectionLeft + imageWidth + gap);
            else if (align == HeaderFooterSectionAlign.Right)
                textRight = Math.Max(sectionLeft, sectionRight - imageWidth - gap);
        }

        var runs = ClampRunsToTotalLength(
            PagePrintTextPlanner.TokenizeSectionText(
                raw, pageNumber, totalPages, workbookName, workbookDirectory, sheetName, now),
            HeaderFooterMaxChars);
        if (runs.Count == 0)
            return;

        // R111-services-multiline-header-footer-1: split on any embedded line break (a literal
        // Alt+Enter the user typed into the Header/Footer editor, preserved verbatim by
        // TokenizeSectionText) and draw each resulting line at its own lineBaselineY(i) -- previously
        // every run was drawn at the single fixed baselineY regardless of embedded newlines, so a
        // multi-line section's later lines were silently overdrawn on top of (or invisible behind)
        // its first line instead of appearing on their own row.
        var lines = PagePrintTextPlanner.SplitRunsIntoLines(runs);
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var lineRuns = lines[lineIndex];
            if (lineRuns.Count == 0)
                continue;

            RenderHeaderFooterSectionLine(
                ops, lineRuns, align, textLeft, textRight, lineBaselineY(lineIndex), fontSize, headerFooterFontScale, color,
                textMeasurer);
        }
    }

    /// <summary>
    /// Renders one already-split line's worth of runs for a header/footer section: measures every run
    /// with <see cref="PortablePdfTextMeasurer"/> so center/right text is actually centered/right-
    /// aligned within its section, then draws each run's <see cref="PdfText"/> op (plus underline/
    /// strikethrough rules) at the given baseline. This is the single-line body previously inlined
    /// directly into <see cref="RenderHeaderFooterSection"/> before the R111 multi-line split was
    /// added there.
    /// <paramref name="headerFooterFontScale"/> multiplies every run's declared/default font size
    /// (R112-services-headerfooter-scale-with-document-1) -- 1.0 when Sheet.
    /// HeaderFooterScaleWithDocument is false or the page's own print scale is 100%, mirroring the
    /// WPF path's <c>fontSize = (run.FontSize ?? PrintFontSize) * fontScale</c>
    /// (PrintRenderer.HeaderFooterDrawing.DrawHeaderFooterFormattedRunsLine).
    /// </summary>
    private static void RenderHeaderFooterSectionLine(
        List<PdfDrawOp> ops,
        IReadOnlyList<HeaderFooterFormattedRun> runs,
        HeaderFooterSectionAlign align,
        double textLeft,
        double textRight,
        double baselineY,
        double fontSize,
        double headerFooterFontScale,
        PdfColor color,
        ITextMeasurer textMeasurer)
    {
        var runWidths = new double[runs.Count];
        var totalWidth = 0.0;
        for (var i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            var width = textMeasurer
                .Measure(run.Text, run.FontName, (run.FontSize ?? fontSize) * headerFooterFontScale, run.Bold, run.Italic).Width;
            runWidths[i] = width;
            totalWidth += width;
        }

        var availableWidth = Math.Max(1.0, textRight - textLeft);
        var cursorX = align switch
        {
            HeaderFooterSectionAlign.Center => textLeft + Math.Max(0.0, (availableWidth - totalWidth) / 2.0),
            HeaderFooterSectionAlign.Right => Math.Max(textLeft, textRight - totalWidth),
            _ => textLeft
        };

        for (var i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            if (run.Text.Length == 0)
                continue;

            var runFontSize = (run.FontSize ?? fontSize) * headerFooterFontScale;
            var runColor = run.Color is { } rgb ? new PdfColor(rgb.R, rgb.G, rgb.B) : color;
            var face = ToPdfFontFace(run.Bold, run.Italic);
            ops.Add(new PdfText(cursorX, baselineY, runFontSize, face, runColor, run.Text, run.FontName));

            if (run.Underline || run.DoubleUnderline)
            {
                var lineY = baselineY - Math.Max(1.0, runFontSize * 0.12);
                ops.Add(new PdfLine(cursorX, lineY, cursorX + runWidths[i], lineY, runColor, 0.6));
                if (run.DoubleUnderline)
                    ops.Add(new PdfLine(cursorX, lineY - 2, cursorX + runWidths[i], lineY - 2, runColor, 0.6));
            }

            if (run.Strikethrough)
            {
                var lineY = baselineY + (runFontSize * 0.3);
                ops.Add(new PdfLine(cursorX, lineY, cursorX + runWidths[i], lineY, runColor, 0.6));
            }

            cursorX += runWidths[i];
        }
    }

    /// <summary>
    /// Truncates a tokenized run sequence to a combined total of <paramref name="maxTotalChars"/>
    /// characters (matching the flat-string 128-char cap the previous single-PdfText-per-section
    /// path applied), dropping/shortening trailing runs rather than the whole section.
    /// </summary>
    private static List<HeaderFooterFormattedRun> ClampRunsToTotalLength(
        IReadOnlyList<HeaderFooterFormattedRun> runs, int maxTotalChars)
    {
        var result = new List<HeaderFooterFormattedRun>(runs.Count);
        var remaining = maxTotalChars;
        foreach (var run in runs)
        {
            if (remaining <= 0)
                break;

            var text = PdfWinAnsiTextCapability.Truncate(run.Text, remaining);
            if (text.Length == 0)
                continue;

            result.Add(run with { Text = text });
            remaining -= text.Length;
            if (text.EndsWith("...", StringComparison.Ordinal))
                break;
        }

        return result;
    }

    private static bool HasHeaderFooterPictureToken(string text) =>
        text.Contains("&[Picture]", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("&G", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Expands placeholder tokens (&amp;P/&amp;N/&amp;D/&amp;T/&amp;F/&amp;Z/&amp;A and their
    /// bracketed forms) in a header/footer section string, stripping all font/style format codes.
    /// Delegates to the shared portable tokenizer (<see cref="PagePrintTextPlanner"/>) so this and
    /// the per-run rendering path in <see cref="RenderHeaderFooterSection"/> stay in sync; kept as a
    /// flat-string convenience for callers (and tests) that only need the plain expanded text.
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
        DateTime now) =>
        PagePrintTextPlanner.ExpandHeaderFooterText(
            raw, pageNumber, totalPages, workbookName, workbookDirectory, sheetName, now);

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

    /// <summary>
    /// Resolves the header/footer picture sets (<c>&amp;G</c> images) for the page the same way
    /// <see cref="ResolveHeaderFooterForPage"/> resolves the header/footer text, so a first-page or
    /// odd/even override's picture set is honored consistently with its text.
    /// </summary>
    private static (WorksheetHeaderFooterPictureSet HeaderPictures, WorksheetHeaderFooterPictureSet FooterPictures)
        ResolveHeaderFooterPicturesForPage(Sheet sheet, int pageNumber)
    {
        if (sheet.DifferentFirstPageHeaderFooter && pageNumber == (sheet.FirstPageNumber ?? 1))
            return (sheet.FirstPageHeaderPictures, sheet.FirstPageFooterPictures);

        if (sheet.DifferentOddEvenHeaderFooter && pageNumber % 2 == 0)
            return (sheet.EvenPageHeaderPictures, sheet.EvenPageFooterPictures);

        return (sheet.PageHeaderPictures, sheet.PageFooterPictures);
    }

    // -----------------------------------------------------------------------
    // Conditional-format data bar / icon set drawing — R96-render-cf-databar-iconset-1
    // -----------------------------------------------------------------------
    //
    // Reuses the exact portable geometry the Avalonia grid itself draws with
    // (ConditionalDataBarLayoutPlanner / ConditionalIconCellLayoutPlanner / ConditionalIconGlyphGeometry
    // / ConditionalIconGlyphResolver, all framework-free types in FreeX.App.Presentation), converting
    // their neutral primitives into PDF draw ops instead of reimplementing the layout math a second
    // time. Two icon-set glyph kinds (Quarter's pie wedge, Star's partial-fill clip) fall back to a
    // full-icon-color fill rather than reproducing the exact clipped/arc geometry -- the Star fallback
    // is the one the geometry emitter's own doc comment explicitly sanctions; see the inline comments
    // at each call site.

    /// <summary>Draws one cell's data bar into its cell rect, or does nothing if it would be empty.</summary>
    private static void DrawConditionalDataBar(List<PdfDrawOp> ops, DataBarLayout dataBar, double x, double y, double w, double h)
    {
        if (ConditionalDataBarLayoutPlanner.Plan(dataBar.StartFraction, dataBar.EndFraction) is not { } bar)
            return;

        var hInsetPt = bar.HorizontalInset * PixelToPointRatio;
        var vInsetPt = bar.VerticalInset * PixelToPointRatio;
        var innerWidth = Math.Max(0.0, w - (2 * hInsetPt));
        var innerHeight = Math.Max(0.0, h - (2 * vInsetPt));
        var barWidth = bar.FractionWidth * innerWidth;
        if (innerWidth <= 0 || innerHeight <= 0 || barWidth <= 0)
            return;

        var barX = x + hInsetPt + (bar.Start * innerWidth);
        var barY = y + vInsetPt;
        ops.Add(new PdfFillRect(barX, barY, barWidth, innerHeight, ToPdfColor(dataBar.FillColor)));
    }

    /// <summary>
    /// Draws one cell's icon-set glyph into its cell rect, returning the point-space text gutter width
    /// the caller should reserve before the cell's own text (0 when the rule hides the value).
    /// </summary>
    private static double DrawConditionalIconSet(
        List<PdfDrawOp> ops, IconSetResult iconSet, double x, double y, double w, double h, bool isRightToLeft)
    {
        var layout = ConditionalIconCellLayoutPlanner.CalculateCellLayout(
            0, 0, w / PixelToPointRatio, h / PixelToPointRatio, iconSet.ShowValue, isRightToLeft);
        if (layout.IconSize <= 0)
            return 0.0;

        var iconSizePt = layout.IconSize * PixelToPointRatio;
        var iconLeftPt = x + (layout.IconLeft * PixelToPointRatio);
        // layout.IconTop is measured down from the cell's own top edge in pixel space; the cell's PDF
        // top edge (y-up) is y + h, so subtracting that pixel offset (converted to points) lands on
        // the glyph's PDF-space top edge.
        var iconTopPt = y + h - (layout.IconTop * PixelToPointRatio);

        var iconColor = ParseHexColor(ConditionalIconGlyphResolver.ResolveIconColor(iconSet.Style, iconSet.BucketIndex, iconSet.IconCount));
        var glyphKind = ConditionalIconGlyphResolver.ResolveGlyphKind(iconSet.Style);
        var isAlternateVariant = ConditionalIconGlyphResolver.IsAlternateGlyphVariant(iconSet.Style);
        var glyphOps = ConditionalIconGlyphGeometry.Build(
            glyphKind, iconSet.BucketIndex, iconSet.IconCount, 0, 0, iconSizePt, iconSizePt, isAlternateVariant);

        AddIconGlyphOps(ops, glyphOps, iconColor, iconLeftPt, iconTopPt);

        return iconSet.ShowValue ? ConditionalIconCellLayoutPlanner.GutterWidth * PixelToPointRatio : 0.0;
    }

    /// <summary>
    /// Converts one glyph's neutral <see cref="CfGlyphOp"/> primitives (local space: origin top-left,
    /// y grows downward, matching <see cref="LayoutPoint"/>'s convention) into PDF draw ops anchored at
    /// (<paramref name="originLeftPt"/>, <paramref name="originTopPt"/>) in PDF's bottom-left/y-up space.
    /// </summary>
    private static void AddIconGlyphOps(
        List<PdfDrawOp> ops, IReadOnlyList<CfGlyphOp> glyphOps, PdfColor iconColor, double originLeftPt, double originTopPt)
    {
        const double outlineWidth = 0.5;
        const double whiteThinWidth = 0.75;
        const double whiteMediumWidth = 0.9;
        var whiteColor = new PdfColor(255, 255, 255);

        PdfPathPoint ToPdfPoint(LayoutPoint p) => new(originLeftPt + p.X, originTopPt - p.Y);

        PdfColor? ResolveFill(CfGlyphFill fill) => fill switch
        {
            CfGlyphFill.Icon => iconColor,
            CfGlyphFill.White => whiteColor,
            _ => null,
        };

        (PdfColor? Color, double Width) ResolveStroke(CfGlyphStroke stroke) => stroke switch
        {
            CfGlyphStroke.Outline => (CfIconOutlineColor, outlineWidth),
            CfGlyphStroke.WhiteThin => (whiteColor, whiteThinWidth),
            CfGlyphStroke.WhiteMedium => (whiteColor, whiteMediumWidth),
            _ => (null, 0.0),
        };

        foreach (var op in glyphOps)
        {
            switch (op.Kind)
            {
                case CfGlyphPrimitiveKind.Ellipse:
                {
                    var fillColor = ResolveFill(op.Fill);
                    var (strokeColor, strokeWidth) = ResolveStroke(op.Stroke);
                    var boundsX = originLeftPt + op.Center.X - op.RadiusX;
                    var boundsY = originTopPt - op.Center.Y - op.RadiusY;
                    var width = op.RadiusX * 2;
                    var height = op.RadiusY * 2;
                    if (fillColor is { } fc)
                        ops.Add(new PdfFillEllipse(boundsX, boundsY, width, height, fc));
                    if (strokeColor is { } sc)
                        ops.Add(new PdfStrokeEllipse(boundsX, boundsY, width, height, sc, strokeWidth));
                    break;
                }
                case CfGlyphPrimitiveKind.Line:
                {
                    var (strokeColor, strokeWidth) = ResolveStroke(op.Stroke);
                    if (strokeColor is { } sc && op.Points.Count >= 2)
                    {
                        var a = ToPdfPoint(op.Points[0]);
                        var b = ToPdfPoint(op.Points[1]);
                        ops.Add(new PdfLine(a.X, a.Y, b.X, b.Y, sc, strokeWidth));
                    }
                    break;
                }
                case CfGlyphPrimitiveKind.Box:
                {
                    var fillColor = ResolveFill(op.Fill);
                    var (strokeColor, strokeWidth) = ResolveStroke(op.Stroke);
                    var boundsX = originLeftPt + op.Rect.Left;
                    var boundsY = originTopPt - op.Rect.Bottom;
                    if (fillColor is { } fc)
                        ops.Add(new PdfFillRect(boundsX, boundsY, op.Rect.Width, op.Rect.Height, fc));
                    if (strokeColor is { } sc)
                        ops.Add(new PdfStrokeRect(boundsX, boundsY, op.Rect.Width, op.Rect.Height, sc, strokeWidth));
                    break;
                }
                case CfGlyphPrimitiveKind.Polygon:
                case CfGlyphPrimitiveKind.Polyline:
                {
                    if (op.Points.Count < 2)
                        break;
                    var fillColor = op.Kind == CfGlyphPrimitiveKind.Polygon ? ResolveFill(op.Fill) : null;
                    var (strokeColor, strokeWidth) = ResolveStroke(op.Stroke);
                    AddPolyPath(ops, op.Points, fillColor, strokeColor, strokeWidth, closed: op.Kind == CfGlyphPrimitiveKind.Polygon, ToPdfPoint);
                    break;
                }
                case CfGlyphPrimitiveKind.Pie:
                {
                    // R96 fallback: draw the pie wedge as a full filled circle rather than reproducing
                    // its exact clockwise sweep-arc geometry -- PdfPath has no native arc segment, and
                    // the Quarter icon-set style (the only user of this primitive) is a low-frequency
                    // Excel gallery choice, so a full disc in the bucket's resolved color is an
                    // acceptable approximation. Every other icon-set family (arrows, traffic lights,
                    // signs, symbols, flags, rating bars, boxes) draws its exact shape above.
                    var boundsX = originLeftPt + op.Center.X - op.RadiusX;
                    var boundsY = originTopPt - op.Center.Y - op.RadiusY;
                    ops.Add(new PdfFillEllipse(boundsX, boundsY, op.RadiusX * 2, op.RadiusY * 2, iconColor));
                    break;
                }
                case CfGlyphPrimitiveKind.StarFillFraction:
                {
                    // R96 fallback: fill the whole star with the icon color instead of clipping to
                    // RadiusX's fill fraction -- explicitly sanctioned by
                    // ConditionalIconGlyphGeometry.Build's own doc comment ("Renderers that do not
                    // support the clip may fall back to a full (icon-colored) fill").
                    AddPolyPath(ops, op.Points, iconColor, CfIconOutlineColor, outlineWidth, closed: true, ToPdfPoint);
                    break;
                }
            }
        }
    }

    private static void AddPolyPath(
        List<PdfDrawOp> ops,
        IReadOnlyList<LayoutPoint> points,
        PdfColor? fillColor,
        PdfColor? strokeColor,
        double strokeWidth,
        bool closed,
        Func<LayoutPoint, PdfPathPoint> toPdfPoint)
    {
        if (points.Count < 2)
            return;

        var start = toPdfPoint(points[0]);
        var segments = new List<PdfPathSegment>(points.Count - 1);
        for (var i = 1; i < points.Count; i++)
            segments.Add(PdfPathSegment.LineTo(toPdfPoint(points[i])));

        var contour = new PdfPathContour(start, segments, closed);
        ops.Add(new PdfPath([contour], fillColor, strokeColor, strokeColor is null ? 0.0 : strokeWidth));
    }

    private static PdfColor ParseHexColor(string hex)
    {
        var span = hex.AsSpan();
        if (span.Length > 0 && span[0] == '#')
            span = span[1..];

        if (span.Length < 6 ||
            !byte.TryParse(span[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(span.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(span.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return CfIconOutlineColor;
        }

        return new PdfColor(r, g, b);
    }

    // -----------------------------------------------------------------------
    // Sparkline drawing — R96-render-sparkline-pdf-1
    // -----------------------------------------------------------------------
    //
    // Reuses the portable FreeX.App.Presentation.Sparklines.SparklineLayoutEngine/SparklineSeriesReader
    // (the same framework-free math the Avalonia grid's SparklineCellPanel.cs draws with) so this path
    // never re-derives axis/scaling math independently. SparklineAxisScalePlanner supplies the same
    // group/custom bounds to WPF, Avalonia, and this portable PDF adapter. Markers, axis lines, and
    // date-axis spacing are not drawn -- the line/column/win-loss body is the primary visual signal.

    private static readonly IReadOnlyDictionary<Guid, IReadOnlyList<double>> EmptySparklineValues =
        new Dictionary<Guid, IReadOnlyList<double>>();

    private static Dictionary<(uint Row, uint Col), SparklineModel> BuildSparklinesByCell(Sheet sheet)
    {
        var lookup = new Dictionary<(uint, uint), SparklineModel>();
        foreach (var sparkline in sheet.Sparklines)
            lookup[(sparkline.Location.Row, sparkline.Location.Col)] = sparkline;
        return lookup;
    }

    /// <summary>
    /// Draws one sparkline's line/column/win-loss body into its cell rect, using the same 3px inset
    /// the interactive grid and WPF print path use.
    /// </summary>
    private static void DrawSparklineIntoCell(
        List<PdfDrawOp> ops,
        SparklineModel sparkline,
        IReadOnlyList<double> values,
        double x,
        double y,
        double w,
        double h,
        SparklineAxisScalePlan axisScalePlan)
    {
        var insetPt = 3.0 * PixelToPointRatio;
        var innerWidth = Math.Max(1.0, w - (2 * insetPt));
        var innerHeight = Math.Max(1.0, h - (2 * insetPt));
        var originLeftPt = x + insetPt;
        var originTopPt = y + h - insetPt;

        var rect = new LayoutRect(0, 0, innerWidth, innerHeight);
        var axisScale = axisScalePlan.Resolve(sparkline);
        var seriesColor = sparkline.SeriesColor is { } sc ? ToPdfColor(sc) : SparklineDefaultPositiveColor;
        var negativeColor = sparkline.ShowNegativePoints
            ? (sparkline.NegativeColor is { } nc ? ToPdfColor(nc) : SparklineDefaultNegativeColor)
            : seriesColor;

        if (sparkline.Kind == SparklineKind.Line)
        {
            var layout = SparklineLayoutEngine.CalculateLineLayout(
                sparkline,
                values,
                rect,
                axisScale.Minimum,
                axisScale.Maximum);

            foreach (var segment in layout.Segments)
            {
                ops.Add(new PdfLine(
                    originLeftPt + segment.Start.X, originTopPt - segment.Start.Y,
                    originLeftPt + segment.End.X, originTopPt - segment.End.Y,
                    seriesColor, 0.75));
            }

            if (layout.SinglePoint is { } single)
            {
                const double dotRadius = 1.2;
                ops.Add(new PdfFillEllipse(
                    originLeftPt + single.X - dotRadius, originTopPt - single.Y - dotRadius,
                    dotRadius * 2, dotRadius * 2, seriesColor));
            }
        }
        else
        {
            var layout = SparklineLayoutEngine.CalculateColumnLayout(
                sparkline,
                values,
                rect,
                axisScale.MaximumAbsolute);

            foreach (var bar in layout.Bars)
            {
                if (bar.Rect.Width <= 0 || bar.Rect.Height <= 0)
                    continue;

                var color = bar.IsNegative ? negativeColor : seriesColor;
                ops.Add(new PdfFillRect(
                    originLeftPt + bar.Rect.Left, originTopPt - bar.Rect.Bottom,
                    bar.Rect.Width, bar.Rect.Height, color));
            }
        }
    }

    // -----------------------------------------------------------------------
    // Cell borders (R127-services-pdf-cell-borders-1)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Draws a cell's four explicit Format Cells > Border edges (diagonal borders are a screen-only
    /// concept not yet modeled by the print-preview <c>PageCellBorders</c> this mirrors, so they are
    /// intentionally out of scope here too). <paramref name="x"/>/<paramref name="y"/> is the cell's
    /// bottom-left corner in PDF y-up space, matching every other per-cell draw call in this loop.
    /// </summary>
    private static void DrawCellBorders(
        List<PdfDrawOp> ops,
        CellStyle style,
        ConditionalFormatStylePlan? cfStyle,
        double x, double y, double w, double h, bool blackAndWhite)
    {
        var top = y + h;
        var bottom = y;
        var left = x;
        var right = x + w;

        DrawBorderEdge(ops, ResolveConditionalBorder(style.BorderTop, cfStyle?.BorderTop), left, top, right, top, blackAndWhite);
        DrawBorderEdge(ops, ResolveConditionalBorder(style.BorderBottom, cfStyle?.BorderBottom), left, bottom, right, bottom, blackAndWhite);
        DrawBorderEdge(ops, ResolveConditionalBorder(style.BorderLeft, cfStyle?.BorderLeft), left, bottom, left, top, blackAndWhite);
        DrawBorderEdge(ops, ResolveConditionalBorder(style.BorderRight, cfStyle?.BorderRight), right, bottom, right, top, blackAndWhite);
    }

    /// <summary>
    /// freex-conditional-format-F1: a matched CF rule's border on this edge overrides the raw style's
    /// border, matching <c>ViewportConditionalFormatEvaluator.MergeStyles</c>'s "dxf borders: apply
    /// each edge from the CF when the CF dxf has a visible border on that edge" rule for the on-screen
    /// grid. <paramref name="conditionalBorder"/> is only ever non-null-and-visible here --
    /// <c>ConditionalFormatRenderEvaluator.ExtractStyle/StackStyle</c> already resolve "no border on
    /// this edge" down to <see cref="BorderStyle.None"/>/left untouched -- so a plain <c>??</c>-style
    /// fallback would be wrong whenever the CF struct carries a present-but-None edge.
    /// </summary>
    private static CellBorder ResolveConditionalBorder(CellBorder rawBorder, CellBorder? conditionalBorder) =>
        conditionalBorder is { Style: not BorderStyle.None } cfBorder ? cfBorder : rawBorder;

    /// <summary>
    /// Draws one border edge as a line (or, for <see cref="BorderStyle.Double"/>, two parallel lines),
    /// matching <c>PrintRenderer.GridCells.cs</c>'s <c>DrawPrintedBorderEdge</c>/
    /// <c>DrawPrintedDoubleBorderLines</c> thickness table and Black-and-White-mode override (Page
    /// Setup &gt; Sheet &gt; "Black and white" forces every border to solid black, matching Excel's
    /// grayscale print). Dash/dot patterns are not reproduced -- <see cref="PdfLine"/> has no dash
    /// support and every other line this builder already emits (gridlines, heading-gutter borders) is
    /// solid too -- so Dashed/Dotted/DashDot/etc. styles draw as a solid line of the same weight.
    /// </summary>
    private static void DrawBorderEdge(
        List<PdfDrawOp> ops, CellBorder border, double x1, double y1, double x2, double y2, bool blackAndWhite)
    {
        if (border.Style == BorderStyle.None)
            return;

        var thickness = BorderStyleThicknessPt(border.Style);
        var color = blackAndWhite ? PdfColor.Black : ToPdfColor(border.Color);

        if (border.Style == BorderStyle.Double)
        {
            DrawDoubleBorderLines(ops, color, thickness, x1, y1, x2, y2);
            return;
        }

        ops.Add(new PdfLine(x1, y1, x2, y2, color, thickness));
    }

    /// <summary>Matches <c>PrintRenderer.GridCells.cs</c>'s <c>DrawPrintedBorderEdge</c> thickness table (points).</summary>
    private static double BorderStyleThicknessPt(BorderStyle style) =>
        style switch
        {
            BorderStyle.Hair => 0.25,
            BorderStyle.Thin => 0.5,
            BorderStyle.Medium or BorderStyle.MediumDashed or BorderStyle.MediumDashDot
                or BorderStyle.MediumDashDotDot or BorderStyle.SlantDashDot => 1.5,
            BorderStyle.Thick => 2.5,
            _ => 0.5,
        };

    /// <summary>
    /// Draws a Double border as two parallel lines offset perpendicular to the edge by a fixed 1pt
    /// gap, matching <c>PrintRenderer.GridCells.cs</c>'s <c>DrawPrintedDoubleBorderLines</c>.
    /// </summary>
    private static void DrawDoubleBorderLines(
        List<PdfDrawOp> ops, PdfColor color, double thickness, double x1, double y1, double x2, double y2)
    {
        const double gap = 1.0;

        var dx = x2 - x1;
        var dy = y2 - y1;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 1e-6)
        {
            ops.Add(new PdfLine(x1, y1, x2, y2, color, thickness));
            return;
        }

        var offsetX = -dy / length * (gap / 2.0);
        var offsetY = dx / length * (gap / 2.0);

        ops.Add(new PdfLine(x1 + offsetX, y1 + offsetY, x2 + offsetX, y2 + offsetY, color, thickness));
        ops.Add(new PdfLine(x1 - offsetX, y1 - offsetY, x2 - offsetX, y2 - offsetY, color, thickness));
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
