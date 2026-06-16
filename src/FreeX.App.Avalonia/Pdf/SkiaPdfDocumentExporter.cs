using System.IO;
using FreeX.App.Services;
using FreeX.Core.Model;
using SkiaSharp;

namespace FreeX.App.Avalonia.Pdf;

/// <summary>
/// PDF exporter for the Avalonia shell that renders workbook text with SkiaSharp's PDF backend.
/// Unlike the dependency-free portable WinAnsi exporter, Skia shapes text (HarfBuzz) and
/// <b>automatically embeds/subsets</b> the fonts it draws, so non-WinAnsi text (Cyrillic, Greek,
/// accented Latin, etc.) renders without us bundling or hand-embedding a font. It reuses the
/// shared <see cref="PortablePdfExportPlanner"/>/<see cref="PortablePdfPageContentPlanner"/>
/// for page/row/column/cell layout so geometry matches the portable exporter.
/// </summary>
public static class SkiaPdfDocumentExporter
{
    public static PortablePdfDocumentExportResult Save(
        Workbook workbook,
        PortablePdfExportPlan exportPlan,
        Stream stream,
        PortablePdfDocumentOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(exportPlan);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new ArgumentException("PDF export requires a writable stream.", nameof(stream));
        if (!exportPlan.IsReady)
            throw new InvalidOperationException(exportPlan.StatusText);

        options ??= new PortablePdfDocumentOptions();

        if (stream.CanSeek)
        {
            stream.Position = 0;
            stream.SetLength(0);
        }

        using var regular = SKTypeface.FromFamilyName(
            null, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? SKTypeface.Default;
        using var bold = SKTypeface.FromFamilyName(
            null, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? regular;

        var pageCount = 0;
        using (var document = SKDocument.CreatePdf(stream))
        {
            foreach (var request in exportPlan.PageRequests)
            {
                var contentPlan = PortablePdfPageContentPlanner.CreatePlan(workbook, request);
                if (!contentPlan.IsReady)
                    throw new InvalidOperationException(contentPlan.StatusText);

                var canvas = document.BeginPage(
                    (float)options.PageWidthPoints,
                    (float)options.PageHeightPoints);
                canvas.Clear(SKColors.White);
                RenderPage(canvas, workbook, exportPlan, request, contentPlan, options, regular, bold);
                document.EndPage();
                pageCount++;
            }

            document.Close();
        }

        if (pageCount == 0)
            throw new InvalidOperationException("PDF export requires at least one rendered page.");

        return new PortablePdfDocumentExportResult(
            pageCount,
            $"Exported PDF (Skia, embedded fonts): {pageCount} {(pageCount == 1 ? "page" : "pages")}.");
    }

    private static void RenderPage(
        SKCanvas canvas,
        Workbook workbook,
        PortablePdfExportPlan exportPlan,
        PortablePdfExportPageRequest request,
        PortablePdfPageContentPlan contentPlan,
        PortablePdfDocumentOptions options,
        SKTypeface regular,
        SKTypeface bold)
    {
        var margin = (float)options.MarginPoints;
        using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var strokePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 0.5f,
            Color = new SKColor(0xCC, 0xCC, 0xCC)
        };
        using var textPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        // Header (title) and footer use a Skia (top-left, y-down) coordinate system.
        var title = string.IsNullOrWhiteSpace(workbook.Name) ? "FreeX Workbook" : workbook.Name.Trim();
        using (var titleFont = new SKFont(bold, 14f))
        {
            textPaint.Color = SKColors.Black;
            canvas.DrawText(title, margin, margin + 12f, titleFont, textPaint);
        }

        using (var footerFont = new SKFont(regular, 9f))
        {
            textPaint.Color = new SKColor(0x66, 0x66, 0x66);
            var footer = $"{request.SheetName} - sheet page {request.SheetPageNumber} - export page {request.ExportPageNumber} of {exportPlan.TotalPageCount}";
            canvas.DrawText(footer, margin, margin + 30f, footerFont, textPaint);
        }

        var columnCount = Math.Max(1, contentPlan.ColumnCount);
        var availableWidth = options.PageWidthPoints - (options.MarginPoints * 2);
        var columnWidth = (float)ResolveColumnWidth(availableWidth, columnCount, options);
        var rowHeight = (float)options.RowHeightPoints;
        var gridTop = margin + (float)options.HeaderHeightPoints;
        var gridLeft = margin;

        foreach (var cell in contentPlan.Cells)
        {
            var rowIndex = IndexOfRow(contentPlan.Rows, cell.Row);
            var columnIndex = IndexOfColumn(contentPlan.Columns, cell.Column);
            if (rowIndex < 0 || columnIndex < 0)
                continue;

            var x = gridLeft + (columnIndex * columnWidth);
            var yTop = gridTop + (rowIndex * rowHeight);
            var rect = new SKRect(x, yTop, x + columnWidth, yTop + rowHeight);

            var style = workbook.GetStyle(cell.StyleId);
            var fill = style.ResolveFillColor(workbook.Theme);
            if (fill is not null || cell.IsTitle)
            {
                fillPaint.Color = fill is { } f
                    ? new SKColor(f.R, f.G, f.B)
                    : new SKColor(0xF2, 0xF2, 0xF2);
                canvas.DrawRect(rect, fillPaint);
            }

            canvas.DrawRect(rect, strokePaint);

            if (string.IsNullOrEmpty(cell.DisplayText))
                continue;

            var fontSize = (float)Math.Clamp(style.FontSize, 7, 10);
            using var cellFont = new SKFont(cell.IsTitle || style.Bold ? bold : regular, fontSize);
            var fontColor = style.ResolveFontColor(workbook.Theme);
            textPaint.Color = fontColor is { } fc ? new SKColor(fc.R, fc.G, fc.B) : SKColors.Black;
            var baseline = yTop + rowHeight - 6f;
            canvas.Save();
            canvas.ClipRect(rect);
            canvas.DrawText(cell.DisplayText, x + 4f, baseline, cellFont, textPaint);
            canvas.Restore();
        }
    }

    private static double ResolveColumnWidth(
        double availableWidth,
        int columnCount,
        PortablePdfDocumentOptions options)
    {
        var equalWidth = availableWidth / columnCount;
        return Math.Clamp(equalWidth, options.MinimumColumnWidthPoints, options.MaximumColumnWidthPoints);
    }

    private static int IndexOfRow(IReadOnlyList<PortablePdfPageRow> rows, uint row)
    {
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].Row == row)
                return i;
        return -1;
    }

    private static int IndexOfColumn(IReadOnlyList<PortablePdfPageColumn> columns, uint column)
    {
        for (var i = 0; i < columns.Count; i++)
            if (columns[i].Column == column)
                return i;
        return -1;
    }
}
