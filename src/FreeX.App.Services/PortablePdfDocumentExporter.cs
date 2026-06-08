using System.Globalization;
using System.Text;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record PortablePdfDocumentOptions(
    double PageWidthPoints = 612,
    double PageHeightPoints = 792,
    double MarginPoints = 36,
    double HeaderHeightPoints = 64,
    double RowHeightPoints = 22,
    double MinimumColumnWidthPoints = 42,
    double MaximumColumnWidthPoints = 118,
    int MaximumCellTextLength = 64);

public sealed record PortablePdfDocumentExportResult(
    int PageCount,
    string StatusText);

public static class PortablePdfDocumentExporter
{
    private static readonly Encoding PdfEncoding = Encoding.ASCII;
    // Keep the non-WinAnsi guard until Unicode PDF output is backed by real font data and external PDF validation.
    private const string DeferredUnicodePdfPathRequirements =
        PortablePdfWinAnsiTextCapability.DeferredUnicodePdfPathRequirements;
    private static readonly CellColor GridStrokeColor = new(196, 202, 210);
    private static readonly CellColor TitleFillColor = new(238, 242, 247);
    private static readonly CellColor HeaderTextColor = new(31, 41, 55);
    private static readonly CellColor FooterTextColor = new(97, 106, 117);

    public static PortablePdfDocumentExportResult Save(
        Workbook workbook,
        PortablePdfExportPlan exportPlan,
        string path,
        PortablePdfDocumentOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(exportPlan);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var (result, document) = CreateDocument(workbook, exportPlan, options);
        using (document)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            using var stream = File.Create(path);
            document.CopyTo(stream);
        }

        return result;
    }

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
            throw new ArgumentException("Portable PDF export requires a writable stream.", nameof(stream));

        var (result, document) = CreateDocument(workbook, exportPlan, options);
        using (document)
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
                stream.SetLength(0);
            }

            document.CopyTo(stream);
        }

        return result;
    }

    private static (PortablePdfDocumentExportResult Result, MemoryStream Document) CreateDocument(
        Workbook workbook,
        PortablePdfExportPlan exportPlan,
        PortablePdfDocumentOptions? options)
    {
        if (!exportPlan.IsReady)
            throw new InvalidOperationException(exportPlan.StatusText);

        options ??= new PortablePdfDocumentOptions();
        var textCapabilityPlan = PortablePdfTextCapabilityPlanner.CreatePlan(workbook, exportPlan, options);
        if (!textCapabilityPlan.IsReady)
            throw new InvalidOperationException(textCapabilityPlan.StatusText);

        var pageStreams = exportPlan.PageRequests
            .Select(request => RenderPage(workbook, exportPlan, request, options))
            .ToArray();
        if (pageStreams.Length == 0)
            throw new InvalidOperationException("Portable PDF export requires at least one rendered page.");

        var document = new MemoryStream();
        WritePdf(document, pageStreams, options);
        document.Position = 0;
        var result = new PortablePdfDocumentExportResult(
            pageStreams.Length,
            $"Exported portable PDF: {pageStreams.Length} {Pluralize(pageStreams.Length, "page")}.");
        return (result, document);
    }

    private static string RenderPage(
        Workbook workbook,
        PortablePdfExportPlan exportPlan,
        PortablePdfExportPageRequest request,
        PortablePdfDocumentOptions options)
    {
        var contentPlan = PortablePdfPageContentPlanner.CreatePlan(workbook, request);
        if (!contentPlan.IsReady)
            throw new InvalidOperationException(contentPlan.StatusText);

        var content = new StringBuilder();
        var title = string.IsNullOrWhiteSpace(workbook.Name) ? "FreeX Workbook" : workbook.Name.Trim();
        AppendText(
            content,
            options.MarginPoints,
            options.PageHeightPoints - options.MarginPoints,
            fontSize: 14,
            fontResource: "F2",
            HeaderTextColor,
            title);
        AppendText(
            content,
            options.MarginPoints,
            options.PageHeightPoints - options.MarginPoints - 18,
            fontSize: 9,
            fontResource: "F1",
            FooterTextColor,
            $"{request.SheetName} - sheet page {request.SheetPageNumber} - export page {request.ExportPageNumber} of {exportPlan.TotalPageCount}");

        var rowCount = Math.Max(1, contentPlan.RowCount);
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
                AppendFilledRectangle(content, x, y, columnWidth, options.RowHeightPoints, fill ?? TitleFillColor);

            AppendStrokedRectangle(content, x, y, columnWidth, options.RowHeightPoints, GridStrokeColor);
            if (string.IsNullOrEmpty(cell.DisplayText))
                continue;

            var fontSize = Math.Clamp(style.FontSize, 7, 10);
            var fontResource = cell.IsTitle || style.Bold ? "F2" : "F1";
            var fontColor = style.ResolveFontColor(workbook.Theme);
            AppendText(
                content,
                x + 4,
                y + Math.Max(7, options.RowHeightPoints - 14),
                fontSize,
                fontResource,
                fontColor,
                Truncate(cell.DisplayText, options.MaximumCellTextLength));
        }

        AppendText(
            content,
            options.MarginPoints,
            options.MarginPoints - 12,
            fontSize: 8,
            fontResource: "F1",
            FooterTextColor,
            $"FreeX portable PDF - {request.SheetName} page {request.SheetPageNumber}");
        return content.ToString();
    }

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

    private static void WritePdf(
        Stream stream,
        IReadOnlyList<string> pageStreams,
        PortablePdfDocumentOptions options)
    {
        var objects = new List<string>();
        var pageObjectIds = Enumerable.Range(0, pageStreams.Count)
            .Select(index => 5 + (index * 2))
            .ToArray();

        objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
        objects.Add($"<< /Type /Pages /Kids [{string.Join(" ", pageObjectIds.Select(id => $"{id} 0 R"))}] /Count {pageStreams.Count} >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

        for (var index = 0; index < pageStreams.Count; index++)
        {
            var pageObjectId = pageObjectIds[index];
            var contentObjectId = pageObjectId + 1;
            objects.Add(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {FormatNumber(options.PageWidthPoints)} {FormatNumber(options.PageHeightPoints)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentObjectId} 0 R >>");

            var pageStream = pageStreams[index].EndsWith("\n", StringComparison.Ordinal)
                ? pageStreams[index]
                : pageStreams[index] + "\n";
            objects.Add($"<< /Length {PdfEncoding.GetByteCount(pageStream)} >>\nstream\n{pageStream}endstream");
        }

        WriteAscii(stream, "%PDF-1.7\n% FreeX portable PDF\n");
        var offsets = new List<long> { 0 };
        for (var objectIndex = 0; objectIndex < objects.Count; objectIndex++)
        {
            offsets.Add(stream.Position);
            WriteAscii(stream, $"{objectIndex + 1} 0 obj\n{objects[objectIndex]}\nendobj\n");
        }

        var xrefOffset = stream.Position;
        WriteAscii(stream, $"xref\n0 {objects.Count + 1}\n");
        WriteAscii(stream, "0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            WriteAscii(stream, $"{offset.ToString("0000000000", CultureInfo.InvariantCulture)} 00000 n \n");

        WriteAscii(
            stream,
            $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
    }

    private static void AppendFilledRectangle(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        CellColor color)
    {
        content.AppendLine("q");
        AppendRgb(content, color, "rg");
        content.AppendLine($"{FormatNumber(x)} {FormatNumber(y)} {FormatNumber(width)} {FormatNumber(height)} re f");
        content.AppendLine("Q");
    }

    private static void AppendStrokedRectangle(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        CellColor color)
    {
        content.AppendLine("q");
        AppendRgb(content, color, "RG");
        content.AppendLine("0.5 w");
        content.AppendLine($"{FormatNumber(x)} {FormatNumber(y)} {FormatNumber(width)} {FormatNumber(height)} re S");
        content.AppendLine("Q");
    }

    private static void AppendText(
        StringBuilder content,
        double x,
        double y,
        double fontSize,
        string fontResource,
        CellColor color,
        string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var textOperand = EncodeTextOperand(text);
        AppendRgb(content, color, "rg");
        content.AppendLine("BT");
        content.AppendLine($"/{fontResource} {FormatNumber(fontSize)} Tf");
        content.AppendLine($"1 0 0 1 {FormatNumber(x)} {FormatNumber(y)} Tm");
        content.AppendLine($"{textOperand} Tj");
        content.AppendLine("ET");
    }

    private static void AppendRgb(StringBuilder content, CellColor color, string operatorName) =>
        content.AppendLine(
            $"{FormatNumber(color.R / 255d)} {FormatNumber(color.G / 255d)} {FormatNumber(color.B / 255d)} {operatorName}");

    private static string EncodeTextOperand(string text)
    {
        var normalized = NormalizePdfText(text);
        if (!RequiresWinAnsiHexText(normalized))
            return $"({EscapePdfLiteralText(normalized)})";

        return $"<{EncodeWinAnsiHexText(normalized)}>";
    }

    private static string NormalizePdfText(string text) =>
        PortablePdfWinAnsiTextCapability.NormalizePdfText(text);

    private static bool RequiresWinAnsiHexText(string text) => text.Any(ch => ch is < ' ' or > '~');

    private static string EscapePdfLiteralText(string text)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\':
                    escaped.Append(@"\\");
                    break;
                case '(':
                    escaped.Append(@"\(");
                    break;
                case ')':
                    escaped.Append(@"\)");
                    break;
                case >= ' ' and <= '~':
                    escaped.Append(ch);
                    break;
                default:
                    throw new InvalidOperationException("Portable PDF ASCII text path received unsupported text.");
            }
        }

        return escaped.ToString();
    }

    private static string EncodeWinAnsiHexText(string text)
    {
        var hex = new StringBuilder(text.Length * 2);
        foreach (var ch in text)
            hex.Append(EncodeWinAnsiByte(ch).ToString("X2", CultureInfo.InvariantCulture));

        return hex.ToString();
    }

    private static byte EncodeWinAnsiByte(char ch)
    {
        if (PortablePdfWinAnsiTextCapability.TryEncodeWinAnsiByte(ch, out var value))
            return value;

        throw new InvalidOperationException(
            "Portable PDF export currently supports ASCII and WinAnsi text only; " +
            $"characters outside the built-in Helvetica/WinAnsi set require the deferred embedded-font Unicode PDF path. {DeferredUnicodePdfPathRequirements}");
    }

    private static string Truncate(string text, int maximumLength) =>
        PortablePdfWinAnsiTextCapability.Truncate(text, maximumLength);

    private static string FormatNumber(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Pluralize(int count, string singular) =>
        count == 1 ? singular : $"{singular}s";

    private static void WriteAscii(Stream stream, string text) =>
        stream.Write(PdfEncoding.GetBytes(text));
}
