using System.Globalization;
using System.Text;

namespace Free.Shared.Pdf;

/// <summary>
/// Dependency-free WinAnsi (Helvetica) PDF writer. Serializes an app-agnostic
/// <see cref="PdfContentDocument"/> (draw-op pages) to PDF 1.7 bytes using only the two built-in
/// Type1 Helvetica faces — no font files, no native dependencies — so it runs anywhere including
/// fully headless environments.
///
/// <para>
/// This is the lossless extraction of FreeX's original <c>PortablePdfDocumentExporter</c> emitter:
/// the per-op content-stream operators, the object/xref/trailer layout, and the WinAnsi text
/// encoding are byte-for-byte identical, which is what keeps FreeX's pinned PDF tests green.
/// Text outside ASCII/WinAnsi throws (callers should preflight via
/// <see cref="PdfWinAnsiTextCapability"/>); geometry is supplied by the caller via draw ops.
/// </para>
/// </summary>
public static class PortablePdfWriter
{
    private static readonly Encoding PdfEncoding = Encoding.ASCII;
    private const string DeferredUnicodePdfPathRequirements =
        PdfWinAnsiTextCapability.DeferredUnicodePdfPathRequirements;

    /// <summary>Header comment written after the <c>%PDF-1.7</c> marker.</summary>
    public const string DefaultHeaderComment = "FreeX portable PDF";

    /// <summary>
    /// Serializes <paramref name="document"/> to <paramref name="stream"/>. Each page is rendered to
    /// a content stream from its draw ops; pages may differ in size. The writer overwrites a seekable
    /// stream from position 0.
    /// </summary>
    public static void Write(PdfContentDocument document, Stream stream, string headerComment = DefaultHeaderComment)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new ArgumentException("Portable PDF export requires a writable stream.", nameof(stream));
        if (document.Pages.Count == 0)
            throw new InvalidOperationException("Portable PDF export requires at least one rendered page.");

        if (stream.CanSeek)
        {
            stream.Position = 0;
            stream.SetLength(0);
        }

        var pages = document.Pages
            .Select(page => (Content: RenderContentStream(page.Ops), page.WidthPoints, page.HeightPoints))
            .ToArray();
        WritePdf(stream, pages, headerComment);
    }

    /// <summary>Serializes <paramref name="document"/> to an in-memory byte array.</summary>
    public static byte[] WriteToBytes(PdfContentDocument document, string headerComment = DefaultHeaderComment)
    {
        using var stream = new MemoryStream();
        Write(document, stream, headerComment);
        return stream.ToArray();
    }

    private static string RenderContentStream(IReadOnlyList<PdfDrawOp> ops)
    {
        var content = new StringBuilder();
        foreach (var op in ops)
        {
            switch (op)
            {
                case PdfFillRect fill:
                    AppendFilledRectangle(content, fill.X, fill.Y, fill.Width, fill.Height, fill.Color);
                    break;
                case PdfStrokeRect stroke:
                    AppendStrokedRectangle(content, stroke.X, stroke.Y, stroke.Width, stroke.Height, stroke.Color, stroke.LineWidth);
                    break;
                case PdfText text:
                    AppendText(content, text.X, text.Y, text.FontSize, FontResource(text.Face), text.Color, text.Text);
                    break;
                case PdfLine line:
                    AppendLine(content, line.X1, line.Y1, line.X2, line.Y2, line.Color, line.LineWidth);
                    break;
            }
        }

        return content.ToString();
    }

    private static string FontResource(PdfFontFace face) => face == PdfFontFace.Bold ? "F2" : "F1";

    private static void WritePdf(
        Stream stream,
        IReadOnlyList<(string Content, double Width, double Height)> pages,
        string headerComment)
    {
        var objects = new List<string>();
        var pageObjectIds = Enumerable.Range(0, pages.Count)
            .Select(index => 5 + (index * 2))
            .ToArray();

        objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
        objects.Add($"<< /Type /Pages /Kids [{string.Join(" ", pageObjectIds.Select(id => $"{id} 0 R"))}] /Count {pages.Count} >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

        for (var index = 0; index < pages.Count; index++)
        {
            var pageObjectId = pageObjectIds[index];
            var contentObjectId = pageObjectId + 1;
            objects.Add(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {FormatNumber(pages[index].Width)} {FormatNumber(pages[index].Height)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentObjectId} 0 R >>");

            var pageStream = pages[index].Content.EndsWith("\n", StringComparison.Ordinal)
                ? pages[index].Content
                : pages[index].Content + "\n";
            objects.Add($"<< /Length {PdfEncoding.GetByteCount(pageStream)} >>\nstream\n{pageStream}endstream");
        }

        WriteAscii(stream, $"%PDF-1.7\n% {headerComment}\n");
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
        PdfColor color)
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
        PdfColor color,
        double lineWidth)
    {
        content.AppendLine("q");
        AppendRgb(content, color, "RG");
        content.AppendLine($"{FormatNumber(lineWidth)} w");
        content.AppendLine($"{FormatNumber(x)} {FormatNumber(y)} {FormatNumber(width)} {FormatNumber(height)} re S");
        content.AppendLine("Q");
    }

    private static void AppendLine(
        StringBuilder content,
        double x1,
        double y1,
        double x2,
        double y2,
        PdfColor color,
        double lineWidth)
    {
        content.AppendLine("q");
        AppendRgb(content, color, "RG");
        content.AppendLine($"{FormatNumber(lineWidth)} w");
        content.AppendLine($"{FormatNumber(x1)} {FormatNumber(y1)} m");
        content.AppendLine($"{FormatNumber(x2)} {FormatNumber(y2)} l S");
        content.AppendLine("Q");
    }

    private static void AppendText(
        StringBuilder content,
        double x,
        double y,
        double fontSize,
        string fontResource,
        PdfColor color,
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

    private static void AppendRgb(StringBuilder content, PdfColor color, string operatorName) =>
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
        PdfWinAnsiTextCapability.NormalizePdfText(text);

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
        if (PdfWinAnsiTextCapability.TryEncodeWinAnsiByte(ch, out var value))
            return value;

        throw new InvalidOperationException(
            "Portable PDF export currently supports ASCII and WinAnsi text only; " +
            $"characters outside the built-in Helvetica/WinAnsi set require the deferred embedded-font Unicode PDF path. {DeferredUnicodePdfPathRequirements}");
    }

    private static string FormatNumber(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static void WriteAscii(Stream stream, string text) =>
        stream.Write(PdfEncoding.GetBytes(text));
}
