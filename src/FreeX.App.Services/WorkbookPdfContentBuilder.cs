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
/// </summary>
public static class WorkbookPdfContentBuilder
{
    private static readonly PdfColor GridStrokeColor = new(196, 202, 210);
    private static readonly PdfColor TitleFillColor = new(238, 242, 247);
    private static readonly PdfColor HeaderTextColor = new(31, 41, 55);
    private static readonly PdfColor FooterTextColor = new(97, 106, 117);

    /// <summary>
    /// Builds the full draw-op document. Assumes <paramref name="exportPlan"/> is ready (callers
    /// validate); throws if a page's content plan is not ready.
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
