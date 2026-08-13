using System.Windows;
using System.Windows.Media;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static partial class PrintRenderer
{
    private static (
        DrawingVisual Visual,
        IReadOnlyList<PdfTextOverlay> TextOverlays,
        IReadOnlyList<PdfLinkOverlay> LinkOverlays,
        IReadOnlyList<PdfCellDestinationOverlay> CellDestinationOverlays) RenderPageVisual(
        Workbook workbook,
        Sheet sheet,
        WorksheetPrintPageContentPlan plan,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        ViewportModel viewport,
        string workbookDirectory = "")
    {
        var metrics = plan.Metrics;
        var cells = plan.Cells;
        var transform = plan.Transform;
        var headerFooter = plan.HeaderFooter;
        var pageWidth = metrics.PageWidth;
        var pageHeight = metrics.PageHeight;
        var measurement = cells.Measurement;
        var pageRows = cells.Rows;
        var pageColumns = cells.Columns;
        var contentLeft = transform.Anchor.X;
        var contentTop = transform.Anchor.Y;
        var gridLeft = cells.GridBounds.Left;
        var gridTop = cells.GridBounds.Top;
        var scaleRatio = transform.ScaleRatio;

        var visual = new DrawingVisual();
        var textOverlays = new List<PdfTextOverlay>();
        var linkOverlays = new List<PdfLinkOverlay>();
        var cellDestinationOverlays = new List<PdfCellDestinationOverlay>();
        using var dc = visual.RenderOpen();
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageWidth, pageHeight));
        dc.PushClip(new RectangleGeometry(new Rect(
            transform.PageClip.Left,
            transform.PageClip.Top,
            transform.PageClip.Width,
            transform.PageClip.Height)));

        DrawHeaderFooter(
            dc,
            textOverlays,
            headerFooter,
            workbook.Name,
            sheet.Name,
            plan.DisplayedPageNumber,
            plan.TotalPageCount,
            draftQuality: !plan.Drawings.RenderCharts,
            fontScale: transform.HeaderFooterFontScale,
            workbookDirectory: workbookDirectory);

        var scaledTextOverlayStart = textOverlays.Count;
        var scaledLinkOverlayStart = linkOverlays.Count;
        var scaledCellDestinationOverlayStart = cellDestinationOverlays.Count;

        if (transform.ApplyNativeTransform)
        {
            dc.PushTransform(new TranslateTransform(contentLeft, contentTop));
            dc.PushTransform(new ScaleTransform(scaleRatio, scaleRatio));
            dc.PushTransform(new TranslateTransform(-contentLeft, -contentTop));
        }

        if (cells.PrintHeadings)
            DrawPrintHeadings(dc, contentLeft, contentTop, measurement, pageRows, pageColumns);

        dc.DrawRectangle(
            null,
            new Pen(Brushes.Black, 0.5),
            new Rect(
                gridLeft,
                gridTop,
                measurement.TotalColumnWidth(pageColumns.Count),
                measurement.TotalRowHeight(pageRows.Count)));

        DrawPrintedGridCells(
            dc,
            textOverlays,
            linkOverlays,
            cellDestinationOverlays,
            measurement,
            pageRows,
            pageColumns,
            cellLookup,
            plan.Hyperlinks,
            plan.CellDestinations,
            cells.PrintGridlines,
            cells.PrintErrorValue,
            gridLeft,
            gridTop,
            workbook,
            cells.BlackAndWhite,
            sheet);

        if (plan.Drawings.RenderCharts)
        {
            DrawPrintedCharts(
                dc,
                textOverlays,
                viewport,
                cellLookup,
                sheet,
                sheet.Charts,
                workbook.Theme,
                cells.BodyRows,
                cells.BodyColumns,
                pageRows.Count - cells.BodyRows.Count,
                pageColumns.Count - cells.BodyColumns.Count,
                gridLeft,
                gridTop,
                measurement);
            DrawPrintedPictures(dc, plan.Drawings.Pictures);
        }

        DrawPrintedTextBoxes(dc, textOverlays, plan.Drawings.TextBoxes);

        if (plan.Comments.RenderDisplayedComments)
        {
            DrawDisplayedComments(
                dc,
                textOverlays,
                plan.Comments.DisplayedComments,
                cells.BlackAndWhite);
        }

        if (transform.ApplyNativeTransform)
        {
            dc.Pop();
            dc.Pop();
            dc.Pop();

            RescaleTextOverlays(textOverlays, scaledTextOverlayStart, scaleRatio, contentLeft, contentTop);
            RescaleLinkOverlays(linkOverlays, scaledLinkOverlayStart, scaleRatio, contentLeft, contentTop);
            RescaleCellDestinationOverlays(
                cellDestinationOverlays,
                scaledCellDestinationOverlayStart,
                scaleRatio,
                contentLeft,
                contentTop);
        }

        dc.Pop();
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
