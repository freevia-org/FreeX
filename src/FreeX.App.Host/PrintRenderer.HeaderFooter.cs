using System.Windows;
using System.Windows.Media;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static partial class PrintRenderer
{
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
        Sheet sheet,
        double pageW,
        double pageH,
        double marginLeft,
        double marginRight,
        double marginTop,
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
        bool blackAndWhite)
    {
        var visual = new DrawingVisual();
        var textOverlays = new List<PdfTextOverlay>();
        var linkOverlays = new List<PdfLinkOverlay>();
        var cellDestinationOverlays = new List<PdfCellDestinationOverlay>();
        using var dc = visual.RenderOpen();
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageW, pageH));
        DrawHeaderFooter(
            dc,
            textOverlays,
            pageW,
            pageH,
            marginLeft,
            marginRight,
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
            draftQuality);

        var printedWidth = measurement.HeaderWidth + measurement.TotalColumnWidth(pageColumns.Count);
        var printedHeight = measurement.HeaderHeight + measurement.TotalRowHeight(pageRows.Count);

        // Excel's Page Setup > Scaling ('Adjust to N%' or 'Fit to N pages') shrinks every printed
        // element (gridlines, cell text, headings, charts, text boxes, comments) so the page's real
        // content fits the printable area -- it never just changes how many rows/columns are packed
        // onto a page and then draws them at full size. PagePaginationPlanner already inflated the
        // rows/columns-per-page capacity to reach the correct page count (so pageRows/pageColumns can
        // be larger than what fits the printable area at 100%); the ratio between the printable area
        // and this page's actual (unscaled) drawn size is exactly the visual shrink factor to apply
        // here, capped at 1 so content that already fits is never scaled up.
        var scaleRatio = 1.0;
        if (printedWidth > 0)
            scaleRatio = Math.Min(scaleRatio, printableW / printedWidth);
        if (printedHeight > 0)
            scaleRatio = Math.Min(scaleRatio, printableH / printedHeight);
        if (!double.IsFinite(scaleRatio) || scaleRatio <= 0)
            scaleRatio = 1.0;

        var scaledWidth = printedWidth * scaleRatio;
        var scaledHeight = printedHeight * scaleRatio;
        var xOffset = centerHorizontally ? Math.Max(0, (printableW - scaledWidth) / 2) : 0;
        var yOffset = centerVertically ? Math.Max(0, (printableH - scaledHeight) / 2) : 0;
        var contentLeft = marginLeft + xOffset;
        var contentTop = marginTop + yOffset;
        var gridLeft = contentLeft + measurement.HeaderWidth * scaleRatio;
        var gridTop = contentTop + measurement.HeaderHeight * scaleRatio;

        var scaledTextOverlayStart = textOverlays.Count;
        var scaledLinkOverlayStart = linkOverlays.Count;
        var scaledCellDestinationOverlayStart = cellDestinationOverlays.Count;

        if (scaleRatio < 1.0)
        {
            // Scale everything printed inside the content area (headings, gridlines, cells, charts,
            // text boxes, comments) about the content's own top-left corner so it shrinks in place
            // without shifting off the already-applied centering offset. Header/footer bands are
            // drawn outside this transform (matching Excel, which never scales header/footer text).
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
            gridTop);

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
            DrawDisplayedComments(
                dc,
                textOverlays,
                comments,
                threadedComments,
                pageRows,
                pageColumns,
                gridLeft,
                gridTop,
                measurement,
                pageW,
                pageH,
                blackAndWhite);
        }

        if (scaleRatio < 1.0)
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
