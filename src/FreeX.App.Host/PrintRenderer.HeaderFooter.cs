using System.Windows;
using System.Windows.Media;
using FreeX.App.Presentation.Comments;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static partial class PrintRenderer
{
    private static readonly IReadOnlyDictionary<CellAddress, ThreadedComment> EmptyThreadedComments =
        new Dictionary<CellAddress, ThreadedComment>();

    private static (WorksheetHeaderFooter Header, WorksheetHeaderFooter Footer, WorksheetHeaderFooterPictureSet HeaderPictures, WorksheetHeaderFooterPictureSet FooterPictures) ResolveHeaderFooterForPage(
        Sheet sheet,
        int pageNumber)
    {
        if (sheet.DifferentFirstPageHeaderFooter && pageNumber == (sheet.FirstPageNumber ?? 1))
            return (sheet.FirstPageHeader, sheet.FirstPageFooter, sheet.FirstPageHeaderPictures, sheet.FirstPageFooterPictures);

        if (sheet.DifferentOddEvenHeaderFooter && pageNumber % 2 == 0)
            return (sheet.EvenPageHeader, sheet.EvenPageFooter, sheet.EvenPageHeaderPictures, sheet.EvenPageFooterPictures);

        return (sheet.PageHeader, sheet.PageFooter, sheet.PageHeaderPictures, sheet.PageFooterPictures);
    }

    private static (DrawingVisual Visual, IReadOnlyList<PdfTextOverlay> TextOverlays, IReadOnlyList<PdfLinkOverlay> LinkOverlays, IReadOnlyList<PdfCellDestinationOverlay> CellDestinationOverlays) RenderPageVisual(
        Workbook workbook,
        Sheet sheet,
        double pageW,
        double pageH,
        double marginLeft,
        double marginRight,
        double marginTop,
        double marginBottom,
        double headerMargin,
        double footerMargin,
        PrintGridMeasurement measurement,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        IReadOnlyDictionary<(uint Row, uint Col), PdfLinkTarget> hyperlinkLookup,
        IReadOnlyDictionary<(uint Row, uint Col), CellAddress> cellDestinationLookup,
        bool printGridlines,
        bool printHeadings,
        WorksheetHeaderFooter pageHeader,
        WorksheetHeaderFooter pageFooter,
        WorksheetHeaderFooterPictureSet pageHeaderPictures,
        WorksheetHeaderFooterPictureSet pageFooterPictures,
        string workbookName,
        string sheetName,
        WorkbookTheme workbookTheme,
        IReadOnlyList<TextBoxModel> textBoxes,
        IReadOnlyList<ChartModel> charts,
        ViewportModel viewport,
        IReadOnlyList<uint> bodyRows,
        IReadOnlyList<uint> bodyColumns,
        bool alignHeaderFooterWithMargins,
        bool centerHorizontally,
        bool centerVertically,
        WorksheetPrintErrorValue printErrorValue,
        WorksheetPrintComments printComments,
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments,
        double printableW,
        double printableH,
        int pageNumber,
        int totalPages,
        bool draftQuality,
        bool blackAndWhite,
        double configuredScalePercent,
        string workbookDirectory = "")
    {
        var visual = new DrawingVisual();
        var textOverlays = new List<PdfTextOverlay>();
        var linkOverlays = new List<PdfLinkOverlay>();
        var cellDestinationOverlays = new List<PdfCellDestinationOverlay>();
        using var dc = visual.RenderOpen();
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageW, pageH));

        var printedWidth = measurement.HeaderWidth + measurement.TotalColumnWidth(pageColumns.Count);
        var printedHeight = measurement.HeaderHeight + measurement.TotalRowHeight(pageRows.Count);

        // Excel's Page Setup > Scaling ('Adjust to N%' or 'Fit to N pages') shrinks/grows every printed
        // element (gridlines, cell text, headings, charts, text boxes, comments) in direct proportion to
        // the configured scale -- it is never merely a repagination hint that only kicks in once content
        // would otherwise overflow. configuredScalePercent is PagePaginationPlanner's single source of
        // truth (the same EffectiveScalePercent that decided this area's page capacity/count), so apply
        // it here unconditionally first, exactly like the portable/Skia PDF export path
        // (FreeX.App.Services.WorkbookPdfContentBuilder.ResolveScaleRatio/ComputeActualGridSizes) --
        // this is what makes
        // "Adjust to 50% normal size" visibly shrink a grid whose real (unscaled) size already fits one
        // page (P97), and keeps every page of a multi-page printout at the same scale instead of each
        // page deriving its own shrink from its own possibly-still-overflowing content extent.
        var scaleRatio = Math.Max(0.001, configuredScalePercent / 100.0);

        // Defensive fit-to-page shrink: even after applying the configured scale, guard against residual
        // overflow on this page's own content (e.g. an oversized merged row) the same way the portable
        // PDF path does, relative to the already-scaled size -- never scale up here, only shrink further.
        //
        // R101-app-host-uniform-residual-scale-1: both overflow ratios must be derived from the SAME
        // pre-clamp scaleRatio and combined with PageGeometryRules.ResolveUniformScale (Math.Min), not
        // applied sequentially -- applying the width clamp first and then multiplying the height clamp
        // on top of the ALREADY-width-shrunk ratio (the previous code here) computes
        // scaleRatio*widthFitScale*heightFitScale (a PRODUCT of both shrinks) whenever both axes
        // overflow at once, over-shrinking relative to the uniform-scale rule every other tier in this
        // codebase applies (PageContentRenderModelBuilder.ResolveScaleRatio,
        // WorkbookPdfContentBuilder.ComputeActualGridSizes) -- e.g. a 10% width overflow and a 20%
        // height overflow used to compound to a ~28% shrink here instead of the correct, uniform 20%.
        var scaledPrintedWidth = printedWidth * scaleRatio;
        var scaledPrintedHeight = printedHeight * scaleRatio;
        var widthFitScale = scaledPrintedWidth > printableW && scaledPrintedWidth > 0
            ? printableW / scaledPrintedWidth
            : 1.0;
        var heightFitScale = scaledPrintedHeight > printableH && scaledPrintedHeight > 0
            ? printableH / scaledPrintedHeight
            : 1.0;
        scaleRatio *= PageGeometryRules.ResolveUniformScale(widthFitScale, heightFitScale);
        if (!double.IsFinite(scaleRatio) || scaleRatio <= 0)
            scaleRatio = 1.0;

        // R111-app-host-headerfooter-scale-with-document-1: Sheet.HeaderFooterScaleWithDocument
        // (Excel's Page Setup > Header/Footer > "Scale with document" checkbox, default checked) governs
        // ONLY whether the header/footer TEXT's own font size follows the page's print scale -- it has
        // no effect on the grid/content scale computed above, which always applies regardless of this
        // flag. When checked (the default), header/footer text shrinks/grows by the exact same
        // scaleRatio as the grid; when unchecked, Excel keeps header/footer text at its authored size
        // no matter how the page content is scaled. Resolved once here (the grid's own scaleRatio is
        // fully known at this point) and threaded into DrawHeaderFooter below as a single value instead
        // of re-deriving it deeper in the call tree, so no future header/footer draw path can forget to
        // consult the flag.
        var headerFooterFontScale = PageGeometryRules.ResolveHeaderFooterFontScale(sheet.HeaderFooterScaleWithDocument, scaleRatio);

        DrawHeaderFooter(
            dc,
            textOverlays,
            pageW,
            pageH,
            marginLeft,
            marginRight,
            marginBottom,
            headerMargin,
            footerMargin,
            pageHeader,
            pageFooter,
            pageHeaderPictures,
            pageFooterPictures,
            workbookName,
            sheetName,
            alignHeaderFooterWithMargins,
            pageNumber,
            totalPages,
            draftQuality,
            headerFooterFontScale,
            workbookDirectory);

        var scaledWidth = printedWidth * scaleRatio;
        var scaledHeight = printedHeight * scaleRatio;
        var xOffset = centerHorizontally ? Math.Max(0, (printableW - scaledWidth) / 2) : 0;
        var yOffset = centerVertically ? Math.Max(0, (printableH - scaledHeight) / 2) : 0;
        // R99-app-host-header-footer-margin-overlap-1: mirrors PagePaginationPlanner.
        // CalculatePageCapacityDetail's bodyTopInches = Math.Max(margins.Top, headerMarginInches) --
        // the header/footer margin is the distance from the page edge to the header/footer band, which
        // sits WITHIN the top margin band as long as it doesn't exceed it, but Excel pushes the grid's
        // top edge down to the header margin (not the plain top margin) once the header margin is the
        // larger of the two, so the printed grid never starts above the header text's own band. Using
        // the plain top margin here (as before) disagreed with the row capacity the pagination planner
        // already computed for this same page, causing the header text to visually collide with the
        // first printed row whenever Header margin &gt; Top margin.
        var contentLeft = marginLeft + xOffset;
        var contentTop = PageGeometryRules.ResolveBodyEdge(marginTop, headerMargin) + yOffset;
        var gridLeft = contentLeft + measurement.HeaderWidth * scaleRatio;
        var gridTop = contentTop + measurement.HeaderHeight * scaleRatio;

        var scaledTextOverlayStart = textOverlays.Count;
        var scaledLinkOverlayStart = linkOverlays.Count;
        var scaledCellDestinationOverlayStart = cellDestinationOverlays.Count;

        if (scaleRatio != 1.0)
        {
            // Scale everything printed inside the content area (headings, gridlines, cells, charts,
            // text boxes, comments) about the content's own top-left corner so it shrinks (Scale% &lt;
            // 100) or grows (Scale% &gt; 100, e.g. "Adjust to 200% normal size") in place without
            // shifting off the already-applied centering offset. Header/footer bands are drawn outside
            // this transform -- they were already drawn above (before this transform is pushed) using
            // headerFooterFontScale, which independently governs their own text scale per
            // Sheet.HeaderFooterScaleWithDocument (R111-app-host-headerfooter-scale-with-document-1).
            dc.PushTransform(new TranslateTransform(contentLeft, contentTop));
            dc.PushTransform(new ScaleTransform(scaleRatio, scaleRatio));
            dc.PushTransform(new TranslateTransform(-contentLeft, -contentTop));
        }

        if (printHeadings)
            DrawPrintHeadings(dc, contentLeft, contentTop, measurement, pageRows, pageColumns);

        dc.DrawRectangle(null, new Pen(Brushes.Black, 0.5),
            new Rect(gridLeft, gridTop, measurement.TotalColumnWidth(pageColumns.Count), measurement.TotalRowHeight(pageRows.Count)));

        DrawPrintedGridCells(
            dc,
            textOverlays,
            linkOverlays,
            cellDestinationOverlays,
            measurement,
            pageRows,
            pageColumns,
            cellLookup,
            hyperlinkLookup,
            cellDestinationLookup,
            printGridlines,
            printErrorValue,
            gridLeft,
            gridTop,
            workbook,
            blackAndWhite,
            sheet);

        if (!draftQuality)
        {
            DrawPrintedCharts(
                dc,
                textOverlays,
                viewport,
                cellLookup,
                sheet,
                charts,
                workbookTheme,
                bodyRows,
                bodyColumns,
                pageRows.Count - bodyRows.Count,
                pageColumns.Count - bodyColumns.Count,
                gridLeft,
                gridTop,
                measurement);

            // R92-consumer-wiring-sweep-1: gated on !draftQuality like charts above -- Excel's Draft
            // Quality print option skips "most graphics" (raster pictures included), unlike text boxes
            // (vector text content), which this renderer already draws unconditionally below.
            var pictureBlocks = PagePictureLayoutPlanner.Build(
                sheet.Pictures,
                pageRows,
                pageColumns,
                gridLeft,
                gridTop,
                measurement);
            DrawPrintedPictures(dc, pictureBlocks);
        }

        var textBoxBlocks = PageTextBoxLayoutPlanner.Build(
            textBoxes,
            workbookTheme,
            pageRows,
            pageColumns,
            gridLeft,
            gridTop,
            measurement);
        DrawPrintedTextBoxes(dc, textOverlays, textBoxBlocks);

        if (!draftQuality && printComments == WorksheetPrintComments.AsDisplayed)
        {
            // WorksheetPageLayout.GetDisplayedCommentOverlays only merges a threaded comment's ROOT
            // text (pair.Value.Text) for addresses not already covered by a plain note — it has no
            // way to include replies or the resolved marker. Pre-format every threaded comment with
            // CommentNavigationPlanner.FormatThreadedComment (the same formatter the "Comments: At
            // end of sheet" mode already uses via PrintCommentSummaryPlanner) and fold the result
            // into the plain-notes dictionary here, so "As displayed on sheet" shows the identical,
            // complete text (all replies + resolved state) instead of silently dropping them (M28).
            // A threaded comment always wins over any legacy/compat placeholder at the same address
            // (the placeholder is only a stand-in for the richer threaded content), so this overwrites
            // rather than skips existing keys. Passing an empty threaded-comments map onward avoids
            // double-handling downstream.
            var displayedComments = comments;
            if (threadedComments.Count > 0)
            {
                var merged = new Dictionary<CellAddress, string>(comments);
                foreach (var pair in threadedComments)
                {
                    merged[pair.Key] = CommentNavigationPlanner.FormatThreadedComment(pair.Value);
                }
                displayedComments = merged;
            }

            DrawDisplayedComments(
                dc,
                textOverlays,
                displayedComments,
                EmptyThreadedComments,
                pageRows,
                pageColumns,
                gridLeft,
                gridTop,
                measurement,
                pageW,
                pageH,
                blackAndWhite,
                sheet.ShownComments);
        }

        if (scaleRatio != 1.0)
        {
            dc.Pop();
            dc.Pop();
            dc.Pop();

            // The overlay lists above are plain coordinate records, not visual-tree elements, so they
            // don't inherit dc's pushed transforms the way the drawn content does. Rescale the entries
            // added while the transform was active (grid text, hyperlinks, cell destinations) about the
            // same anchor so PDF export's selectable-text/link layer lines up with the shrunk raster.
            RescaleTextOverlays(textOverlays, scaledTextOverlayStart, scaleRatio, contentLeft, contentTop);
            RescaleLinkOverlays(linkOverlays, scaledLinkOverlayStart, scaleRatio, contentLeft, contentTop);
            RescaleCellDestinationOverlays(cellDestinationOverlays, scaledCellDestinationOverlayStart, scaleRatio, contentLeft, contentTop);
        }

        return (visual, textOverlays, linkOverlays, cellDestinationOverlays);
    }

    private static void RescaleTextOverlays(
        List<PdfTextOverlay> overlays,
        int startIndex,
        double scaleRatio,
        double anchorX,
        double anchorY)
    {
        for (var i = startIndex; i < overlays.Count; i++)
        {
            var overlay = overlays[i];
            overlays[i] = overlay with
            {
                X = anchorX + (overlay.X - anchorX) * scaleRatio,
                Y = anchorY + (overlay.Y - anchorY) * scaleRatio,
                FontSize = overlay.FontSize * scaleRatio
            };
        }
    }

    private static void RescaleLinkOverlays(
        List<PdfLinkOverlay> overlays,
        int startIndex,
        double scaleRatio,
        double anchorX,
        double anchorY)
    {
        for (var i = startIndex; i < overlays.Count; i++)
        {
            var overlay = overlays[i];
            overlays[i] = overlay with
            {
                X = anchorX + (overlay.X - anchorX) * scaleRatio,
                Y = anchorY + (overlay.Y - anchorY) * scaleRatio,
                Width = overlay.Width * scaleRatio,
                Height = overlay.Height * scaleRatio
            };
        }
    }

    private static void RescaleCellDestinationOverlays(
        List<PdfCellDestinationOverlay> overlays,
        int startIndex,
        double scaleRatio,
        double anchorX,
        double anchorY)
    {
        for (var i = startIndex; i < overlays.Count; i++)
        {
            var overlay = overlays[i];
            overlays[i] = overlay with
            {
                X = anchorX + (overlay.X - anchorX) * scaleRatio,
                Y = anchorY + (overlay.Y - anchorY) * scaleRatio,
                Width = overlay.Width * scaleRatio,
                Height = overlay.Height * scaleRatio
            };
        }
    }
}
