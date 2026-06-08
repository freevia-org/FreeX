using System.Buffers.Binary;
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
    private const string UnicodeFontResource = "FU";
    private const string UnicodeFontName = "FreeXUnicodeSubset-Regular";
    private const string UnicodeFontEncodingName = "Identity-H";
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
        var unicodeFont = new PortablePdfUnicodeFont();
        var pageStreams = exportPlan.PageRequests
            .Select(request => RenderPage(workbook, exportPlan, request, options, unicodeFont))
            .ToArray();
        if (pageStreams.Length == 0)
            throw new InvalidOperationException("Portable PDF export requires at least one rendered page.");

        var document = new MemoryStream();
        WritePdf(document, pageStreams, options, unicodeFont);
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
        PortablePdfDocumentOptions options,
        PortablePdfUnicodeFont unicodeFont)
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
            title,
            unicodeFont);
        AppendText(
            content,
            options.MarginPoints,
            options.PageHeightPoints - options.MarginPoints - 18,
            fontSize: 9,
            fontResource: "F1",
            FooterTextColor,
            $"{request.SheetName} - sheet page {request.SheetPageNumber} - export page {request.ExportPageNumber} of {exportPlan.TotalPageCount}",
            unicodeFont);

        var rowCount = Math.Max(1, contentPlan.RowCount);
        var columnCount = Math.Max(1, contentPlan.ColumnCount);
        var availableWidth = options.PageWidthPoints - (options.MarginPoints * 2);
        var columnWidth = ResolveColumnWidth(availableWidth, columnCount, options);
        var gridTop = options.PageHeightPoints - options.MarginPoints - options.HeaderHeightPoints;
        var gridLeft = options.MarginPoints;

        foreach (var cell in contentPlan.Cells)
        {
            var rowIndex = contentPlan.Rows.ToList().FindIndex(row => row.Row == cell.Row);
            var columnIndex = contentPlan.Columns.ToList().FindIndex(column => column.Column == cell.Column);
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
                Truncate(cell.DisplayText, options.MaximumCellTextLength),
                unicodeFont);
        }

        AppendText(
            content,
            options.MarginPoints,
            options.MarginPoints - 12,
            fontSize: 8,
            fontResource: "F1",
            FooterTextColor,
            $"FreeX portable PDF - {request.SheetName} page {request.SheetPageNumber}",
            unicodeFont);
        return content.ToString();
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
        PortablePdfDocumentOptions options,
        PortablePdfUnicodeFont unicodeFont)
    {
        const int pagesObjectId = 2;
        const int regularFontObjectId = 3;
        const int boldFontObjectId = 4;

        var objects = new List<byte[]>();
        var unicodeType0ObjectId = unicodeFont.HasGlyphs ? 5 : (int?)null;
        var unicodeCidFontObjectId = unicodeFont.HasGlyphs ? 6 : (int?)null;
        var unicodeFontDescriptorObjectId = unicodeFont.HasGlyphs ? 7 : (int?)null;
        var unicodeFontFileObjectId = unicodeFont.HasGlyphs ? 8 : (int?)null;
        var unicodeToUnicodeObjectId = unicodeFont.HasGlyphs ? 9 : (int?)null;
        var firstPageObjectId = unicodeFont.HasGlyphs ? 10 : 5;
        var pageObjectIds = Enumerable.Range(0, pageStreams.Count)
            .Select(index => firstPageObjectId + (index * 2))
            .ToArray();

        AddAsciiObject(objects, $"<< /Type /Catalog /Pages {pagesObjectId} 0 R >>");
        AddAsciiObject(objects, $"<< /Type /Pages /Kids [{string.Join(" ", pageObjectIds.Select(id => $"{id} 0 R"))}] /Count {pageStreams.Count} >>");
        AddAsciiObject(objects, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        AddAsciiObject(objects, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

        if (unicodeFont.HasGlyphs)
        {
            AddAsciiObject(
                objects,
                $"<< /Type /Font /Subtype /Type0 /BaseFont /{UnicodeFontName} /Encoding /{UnicodeFontEncodingName} /DescendantFonts [{unicodeCidFontObjectId} 0 R] /ToUnicode {unicodeToUnicodeObjectId} 0 R >>");
            AddAsciiObject(
                objects,
                $"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{UnicodeFontName} /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor {unicodeFontDescriptorObjectId} 0 R /DW 600 /W {BuildUnicodeWidths(unicodeFont)} /CIDToGIDMap /Identity >>");
            AddAsciiObject(
                objects,
                $"<< /Type /FontDescriptor /FontName /{UnicodeFontName} /Flags 4 /FontBBox [0 -200 600 800] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile2 {unicodeFontFileObjectId} 0 R >>");

            var fontBytes = PortablePdfTrueTypeSubsetBuilder.Build(unicodeFont.Glyphs);
            objects.Add(CreateBinaryStreamObject(
                $"<< /Length {fontBytes.Length} /Length1 {fontBytes.Length} >>",
                fontBytes));
            objects.Add(CreateAsciiStreamObject(BuildToUnicodeCMap(unicodeFont)));
        }

        var fontResources = $"/F1 {regularFontObjectId} 0 R /F2 {boldFontObjectId} 0 R";
        if (unicodeType0ObjectId is not null)
            fontResources += $" /{UnicodeFontResource} {unicodeType0ObjectId.Value} 0 R";

        for (var index = 0; index < pageStreams.Count; index++)
        {
            var pageObjectId = pageObjectIds[index];
            var contentObjectId = pageObjectId + 1;
            AddAsciiObject(
                objects,
                $"<< /Type /Page /Parent {pagesObjectId} 0 R /MediaBox [0 0 {FormatNumber(options.PageWidthPoints)} {FormatNumber(options.PageHeightPoints)}] /Resources << /Font << {fontResources} >> >> /Contents {contentObjectId} 0 R >>");

            var pageStream = pageStreams[index].EndsWith("\n", StringComparison.Ordinal)
                ? pageStreams[index]
                : pageStreams[index] + "\n";
            objects.Add(CreateAsciiStreamObject(pageStream));
        }

        WriteAscii(stream, "%PDF-1.7\n% FreeX portable PDF\n");
        var offsets = new List<long> { 0 };
        for (var objectIndex = 0; objectIndex < objects.Count; objectIndex++)
        {
            offsets.Add(stream.Position);
            WriteAscii(stream, $"{objectIndex + 1} 0 obj\n");
            stream.Write(objects[objectIndex]);
            WriteAscii(stream, "\nendobj\n");
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
        string text,
        PortablePdfUnicodeFont unicodeFont)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var normalized = NormalizePdfText(text);
        if (normalized.Length == 0)
            return;

        var effectiveFontResource = fontResource;
        var textOperand = EncodeTextOperand(normalized, unicodeFont, ref effectiveFontResource);
        AppendRgb(content, color, "rg");
        content.AppendLine("BT");
        content.AppendLine($"/{effectiveFontResource} {FormatNumber(fontSize)} Tf");
        content.AppendLine($"1 0 0 1 {FormatNumber(x)} {FormatNumber(y)} Tm");
        content.AppendLine($"{textOperand} Tj");
        content.AppendLine("ET");
    }

    private static void AppendRgb(StringBuilder content, CellColor color, string operatorName) =>
        content.AppendLine(
            $"{FormatNumber(color.R / 255d)} {FormatNumber(color.G / 255d)} {FormatNumber(color.B / 255d)} {operatorName}");

    private static string EncodeTextOperand(
        string normalized,
        PortablePdfUnicodeFont unicodeFont,
        ref string fontResource)
    {
        if (RequiresUnicodeFont(normalized))
        {
            fontResource = UnicodeFontResource;
            return $"<{unicodeFont.EncodeText(normalized)}>";
        }

        if (!RequiresWinAnsiHexText(normalized))
            return $"({EscapePdfLiteralText(normalized)})";

        return $"<{EncodeWinAnsiHexText(normalized)}>";
    }

    private static string NormalizePdfText(string text)
    {
        var normalized = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            normalized.Append(ch is '\r' or '\n' or '\t' ? ' ' : ch);
        }

        return normalized.ToString();
    }

    private static bool RequiresWinAnsiHexText(string text) => text.Any(ch => ch is < ' ' or > '~');

    private static bool RequiresUnicodeFont(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value < ' ')
                continue;

            if (!CanEncodeWinAnsiRune(rune))
                return true;
        }

        return false;
    }

    private static bool CanEncodeWinAnsiRune(Rune rune) =>
        rune.Value <= char.MaxValue && TryEncodeWinAnsiByte((char)rune.Value, out _);

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
        if (TryEncodeWinAnsiByte(ch, out var value))
            return value;

        throw new InvalidOperationException(
            "Portable PDF export currently supports ASCII and WinAnsi text only; characters outside the built-in Helvetica/WinAnsi set require the embedded-font Unicode PDF path.");
    }

    private static bool TryEncodeWinAnsiByte(char ch, out byte value)
    {
        if (ch is >= ' ' and <= '~')
        {
            value = (byte)ch;
            return true;
        }

        if (ch is >= '\u00a0' and <= '\u00ff')
        {
            value = (byte)ch;
            return true;
        }

        value = ch switch
        {
            '\u20ac' => 0x80,
            '\u201a' => 0x82,
            '\u0192' => 0x83,
            '\u201e' => 0x84,
            '\u2026' => 0x85,
            '\u2020' => 0x86,
            '\u2021' => 0x87,
            '\u02c6' => 0x88,
            '\u2030' => 0x89,
            '\u0160' => 0x8A,
            '\u2039' => 0x8B,
            '\u0152' => 0x8C,
            '\u017D' => 0x8E,
            '\u2018' => 0x91,
            '\u2019' => 0x92,
            '\u201C' => 0x93,
            '\u201D' => 0x94,
            '\u2022' => 0x95,
            '\u2013' => 0x96,
            '\u2014' => 0x97,
            '\u02dc' => 0x98,
            '\u2122' => 0x99,
            '\u0161' => 0x9A,
            '\u203A' => 0x9B,
            '\u0153' => 0x9C,
            '\u017E' => 0x9E,
            '\u0178' => 0x9F,
            _ => 0
        };

        return value != 0;
    }

    private static string Truncate(string text, int maximumLength)
    {
        if (maximumLength <= 3 || text.Length <= maximumLength)
            return text;

        var truncatedLength = maximumLength - 3;
        if (char.IsHighSurrogate(text[truncatedLength - 1]))
            truncatedLength--;

        return text[..truncatedLength] + "...";
    }

    private static string FormatNumber(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Pluralize(int count, string singular) =>
        count == 1 ? singular : $"{singular}s";

    private static void WriteAscii(Stream stream, string text) =>
        stream.Write(PdfEncoding.GetBytes(text));

    private static void AddAsciiObject(List<byte[]> objects, string body) =>
        objects.Add(PdfEncoding.GetBytes(body));

    private static byte[] CreateAsciiStreamObject(string streamText)
    {
        var length = PdfEncoding.GetByteCount(streamText);
        return PdfEncoding.GetBytes($"<< /Length {length} >>\nstream\n{streamText}endstream");
    }

    private static byte[] CreateBinaryStreamObject(string dictionary, byte[] streamBytes)
    {
        var prefix = PdfEncoding.GetBytes($"{dictionary}\nstream\n");
        var suffix = PdfEncoding.GetBytes("\nendstream");
        var body = new byte[prefix.Length + streamBytes.Length + suffix.Length];
        Buffer.BlockCopy(prefix, 0, body, 0, prefix.Length);
        Buffer.BlockCopy(streamBytes, 0, body, prefix.Length, streamBytes.Length);
        Buffer.BlockCopy(suffix, 0, body, prefix.Length + streamBytes.Length, suffix.Length);
        return body;
    }

    private static string BuildUnicodeWidths(PortablePdfUnicodeFont unicodeFont)
    {
        var widths = string.Join(" ", unicodeFont.Glyphs.Select(_ => "600"));
        return $"[1 [{widths}]]";
    }

    private static string BuildToUnicodeCMap(PortablePdfUnicodeFont unicodeFont)
    {
        var builder = new StringBuilder();
        builder.AppendLine("/CIDInit /ProcSet findresource begin");
        builder.AppendLine("12 dict begin");
        builder.AppendLine("begincmap");
        builder.AppendLine("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def");
        builder.AppendLine($"/CMapName /{UnicodeFontName}-ToUnicode def");
        builder.AppendLine("/CMapType 2 def");
        builder.AppendLine("1 begincodespacerange");
        builder.AppendLine("<0000> <FFFF>");
        builder.AppendLine("endcodespacerange");

        foreach (var chunk in unicodeFont.Glyphs.Chunk(100))
        {
            builder.AppendLine($"{chunk.Length} beginbfchar");
            foreach (var glyph in chunk)
                builder.AppendLine($"<{glyph.GlyphId.ToString("X4", CultureInfo.InvariantCulture)}> <{EncodeUtf16BigEndianHex(glyph.Scalar)}>");
            builder.AppendLine("endbfchar");
        }

        builder.AppendLine("endcmap");
        builder.AppendLine("CMapName currentdict /CMap defineresource pop");
        builder.AppendLine("end");
        builder.AppendLine("end");
        return builder.ToString();
    }

    private static string EncodeUtf16BigEndianHex(int scalar)
    {
        if (scalar <= char.MaxValue)
            return scalar.ToString("X4", CultureInfo.InvariantCulture);

        var value = scalar - 0x10000;
        var highSurrogate = 0xD800 + (value >> 10);
        var lowSurrogate = 0xDC00 + (value & 0x3FF);
        return highSurrogate.ToString("X4", CultureInfo.InvariantCulture) +
            lowSurrogate.ToString("X4", CultureInfo.InvariantCulture);
    }

    private sealed class PortablePdfUnicodeFont
    {
        private readonly Dictionary<int, int> _glyphIdsByScalar = [];
        private readonly List<PortablePdfUnicodeGlyph> _glyphs = [];

        public bool HasGlyphs => _glyphs.Count > 0;

        public IReadOnlyList<PortablePdfUnicodeGlyph> Glyphs => _glyphs;

        public string EncodeText(string text)
        {
            var hex = new StringBuilder(text.Length * 4);
            foreach (var rune in text.EnumerateRunes())
            {
                var glyphId = GetOrAddGlyphId(rune.Value);
                hex.Append(glyphId.ToString("X4", CultureInfo.InvariantCulture));
            }

            return hex.ToString();
        }

        private int GetOrAddGlyphId(int scalar)
        {
            if (_glyphIdsByScalar.TryGetValue(scalar, out var glyphId))
                return glyphId;

            if (_glyphs.Count >= ushort.MaxValue)
                throw new InvalidOperationException("Portable PDF Unicode export supports up to 65,535 unique glyphs per document.");

            glyphId = _glyphs.Count + 1;
            _glyphIdsByScalar.Add(scalar, glyphId);
            _glyphs.Add(new PortablePdfUnicodeGlyph(glyphId, scalar));
            return glyphId;
        }
    }

    private sealed record PortablePdfUnicodeGlyph(int GlyphId, int Scalar);

    private static class PortablePdfTrueTypeSubsetBuilder
    {
        private const ushort UnitsPerEm = 1000;
        private const ushort AdvanceWidth = 600;

        public static byte[] Build(IReadOnlyList<PortablePdfUnicodeGlyph> glyphs)
        {
            var glyphCount = checked((ushort)(glyphs.Count + 1));
            var glyf = BuildGlyf(glyphs, glyphCount, out var glyphOffsets);
            var tables = new List<TrueTypeTable>
            {
                new("OS/2", BuildOs2(glyphs)),
                new("cmap", BuildCmap(glyphs)),
                new("glyf", glyf),
                new("head", BuildHead()),
                new("hhea", BuildHhea(glyphCount)),
                new("hmtx", BuildHmtx(glyphCount)),
                new("loca", BuildLoca(glyphOffsets)),
                new("maxp", BuildMaxp(glyphCount)),
                new("name", BuildName()),
                new("post", BuildPost())
            };
            tables.Sort((left, right) => string.CompareOrdinal(left.Tag, right.Tag));

            var writer = new BigEndianWriter();
            writer.WriteUInt32(0x00010000);
            writer.WriteUInt16((ushort)tables.Count);
            var maxPowerOfTwo = 1;
            var entrySelector = 0;
            while (maxPowerOfTwo * 2 <= tables.Count)
            {
                maxPowerOfTwo *= 2;
                entrySelector++;
            }

            var searchRange = (ushort)(maxPowerOfTwo * 16);
            writer.WriteUInt16(searchRange);
            writer.WriteUInt16((ushort)entrySelector);
            writer.WriteUInt16((ushort)((tables.Count * 16) - searchRange));

            var tableOffset = 12 + (tables.Count * 16);
            foreach (var table in tables)
            {
                tableOffset = Align4(tableOffset);
                table.Offset = tableOffset;
                table.Checksum = CalculateChecksum(table.Data);
                tableOffset += table.Data.Length;
            }

            foreach (var table in tables)
            {
                writer.WriteTag(table.Tag);
                writer.WriteUInt32(table.Checksum);
                writer.WriteUInt32((uint)table.Offset);
                writer.WriteUInt32((uint)table.Data.Length);
            }

            foreach (var table in tables)
            {
                writer.PadTo(table.Offset);
                writer.WriteBytes(table.Data);
                writer.Pad4();
            }

            var fontBytes = writer.ToArray();
            var checksum = CalculateChecksum(fontBytes);
            var checksumAdjustment = unchecked(0xB1B0AFBAu - checksum);
            var headTable = tables.Single(table => table.Tag == "head");
            BinaryPrimitives.WriteUInt32BigEndian(
                fontBytes.AsSpan(headTable.Offset + 8, sizeof(uint)),
                checksumAdjustment);
            return fontBytes;
        }

        private static byte[] BuildCmap(IReadOnlyList<PortablePdfUnicodeGlyph> glyphs)
        {
            var mappings = glyphs
                .OrderBy(glyph => glyph.Scalar)
                .ThenBy(glyph => glyph.GlyphId)
                .ToArray();
            var writer = new BigEndianWriter();
            writer.WriteUInt16(0);
            writer.WriteUInt16(1);
            writer.WriteUInt16(3);
            writer.WriteUInt16(10);
            writer.WriteUInt32(12);
            writer.WriteUInt16(12);
            writer.WriteUInt16(0);
            writer.WriteUInt32((uint)(16 + (mappings.Length * 12)));
            writer.WriteUInt32(0);
            writer.WriteUInt32((uint)mappings.Length);
            foreach (var mapping in mappings)
            {
                writer.WriteUInt32((uint)mapping.Scalar);
                writer.WriteUInt32((uint)mapping.Scalar);
                writer.WriteUInt32((uint)mapping.GlyphId);
            }

            return writer.ToArray();
        }

        private static byte[] BuildHead()
        {
            var writer = new BigEndianWriter();
            writer.WriteUInt16(1);
            writer.WriteUInt16(0);
            writer.WriteUInt32(0x00010000);
            writer.WriteUInt32(0);
            writer.WriteUInt32(0x5F0F3CF5);
            writer.WriteUInt16(0x000B);
            writer.WriteUInt16(UnitsPerEm);
            writer.WriteUInt64(0);
            writer.WriteUInt64(0);
            writer.WriteInt16(1);
            writer.WriteInt16(-200);
            writer.WriteInt16(600);
            writer.WriteInt16(800);
            writer.WriteUInt16(0);
            writer.WriteUInt16(8);
            writer.WriteInt16(2);
            writer.WriteInt16(0);
            writer.WriteInt16(0);
            return writer.ToArray();
        }

        private static byte[] BuildHhea(ushort glyphCount)
        {
            var writer = new BigEndianWriter();
            writer.WriteUInt16(1);
            writer.WriteUInt16(0);
            writer.WriteInt16(800);
            writer.WriteInt16(-200);
            writer.WriteInt16(200);
            writer.WriteUInt16(AdvanceWidth);
            writer.WriteInt16(0);
            writer.WriteInt16(0);
            writer.WriteInt16(600);
            writer.WriteInt16(1);
            writer.WriteInt16(0);
            writer.WriteInt16(0);
            writer.WriteInt16(0);
            writer.WriteInt16(0);
            writer.WriteInt16(0);
            writer.WriteInt16(0);
            writer.WriteInt16(0);
            writer.WriteInt16(0);
            writer.WriteUInt16(glyphCount);
            return writer.ToArray();
        }

        private static byte[] BuildHmtx(ushort glyphCount)
        {
            var writer = new BigEndianWriter();
            for (var index = 0; index < glyphCount; index++)
            {
                writer.WriteUInt16(AdvanceWidth);
                writer.WriteInt16(0);
            }

            return writer.ToArray();
        }

        private static byte[] BuildLoca(IReadOnlyList<int> glyphOffsets)
        {
            var writer = new BigEndianWriter();
            foreach (var offset in glyphOffsets)
                writer.WriteUInt32((uint)offset);

            return writer.ToArray();
        }

        private static byte[] BuildMaxp(ushort glyphCount)
        {
            var writer = new BigEndianWriter();
            writer.WriteUInt32(0x00010000);
            writer.WriteUInt16(glyphCount);
            writer.WriteUInt16(16);
            writer.WriteUInt16(4);
            writer.WriteUInt16(0);
            writer.WriteUInt16(0);
            writer.WriteUInt16(2);
            writer.WriteUInt16(0);
            writer.WriteUInt16(0);
            writer.WriteUInt16(0);
            writer.WriteUInt16(0);
            writer.WriteUInt16(0);
            writer.WriteUInt16(0);
            writer.WriteUInt16(0);
            writer.WriteUInt16(0);
            return writer.ToArray();
        }

        private static byte[] BuildName()
        {
            var names = new[]
            {
                (NameId: (ushort)1, Value: "FreeX Unicode Subset"),
                (NameId: (ushort)2, Value: "Regular"),
                (NameId: (ushort)4, Value: "FreeX Unicode Subset Regular"),
                (NameId: (ushort)6, Value: UnicodeFontName)
            };
            var stringStorage = new BigEndianWriter();
            var records = new List<(ushort NameId, ushort Length, ushort Offset)>();
            foreach (var name in names)
            {
                var offset = checked((ushort)stringStorage.Position);
                foreach (var ch in name.Value)
                    stringStorage.WriteUInt16(ch);
                records.Add((name.NameId, checked((ushort)(stringStorage.Position - offset)), offset));
            }

            var writer = new BigEndianWriter();
            writer.WriteUInt16(0);
            writer.WriteUInt16((ushort)records.Count);
            writer.WriteUInt16((ushort)(6 + (records.Count * 12)));
            foreach (var record in records)
            {
                writer.WriteUInt16(3);
                writer.WriteUInt16(1);
                writer.WriteUInt16(0x0409);
                writer.WriteUInt16(record.NameId);
                writer.WriteUInt16(record.Length);
                writer.WriteUInt16(record.Offset);
            }

            writer.WriteBytes(stringStorage.ToArray());
            return writer.ToArray();
        }

        private static byte[] BuildOs2(IReadOnlyList<PortablePdfUnicodeGlyph> glyphs)
        {
            var firstBmpScalar = glyphs
                .Select(glyph => glyph.Scalar)
                .Where(scalar => scalar <= char.MaxValue)
                .DefaultIfEmpty(0)
                .Min();
            var lastBmpScalar = glyphs
                .Select(glyph => glyph.Scalar)
                .Where(scalar => scalar <= char.MaxValue)
                .DefaultIfEmpty(char.MaxValue)
                .Max();

            var writer = new BigEndianWriter();
            writer.WriteUInt16(0);
            writer.WriteInt16(AdvanceWidth);
            writer.WriteUInt16(400);
            writer.WriteUInt16(5);
            writer.WriteUInt16(0);
            writer.WriteInt16(650);
            writer.WriteInt16(700);
            writer.WriteInt16(0);
            writer.WriteInt16(140);
            writer.WriteInt16(650);
            writer.WriteInt16(700);
            writer.WriteInt16(0);
            writer.WriteInt16(480);
            writer.WriteInt16(50);
            writer.WriteInt16(250);
            writer.WriteInt16(0);
            writer.WriteBytes(new byte[10]);
            writer.WriteUInt32(0);
            writer.WriteUInt32(0);
            writer.WriteUInt32(0);
            writer.WriteUInt32(0);
            writer.WriteTag("FREX");
            writer.WriteUInt16(0x0040);
            writer.WriteUInt16((ushort)firstBmpScalar);
            writer.WriteUInt16((ushort)lastBmpScalar);
            writer.WriteInt16(800);
            writer.WriteInt16(-200);
            writer.WriteInt16(200);
            writer.WriteUInt16(1000);
            writer.WriteUInt16(200);
            return writer.ToArray();
        }

        private static byte[] BuildPost()
        {
            var writer = new BigEndianWriter();
            writer.WriteUInt32(0x00030000);
            writer.WriteUInt32(0);
            writer.WriteInt16(-75);
            writer.WriteInt16(50);
            writer.WriteUInt32(0);
            writer.WriteUInt32(0);
            writer.WriteUInt32(0);
            writer.WriteUInt32(0);
            writer.WriteUInt32(0);
            return writer.ToArray();
        }

        private static byte[] BuildGlyf(
            IReadOnlyList<PortablePdfUnicodeGlyph> glyphs,
            ushort glyphCount,
            out IReadOnlyList<int> locaOffsets)
        {
            var glyphData = new BigEndianWriter();
            var offsets = new List<int>(glyphCount + 1);
            for (var glyphId = 0; glyphId < glyphCount; glyphId++)
            {
                offsets.Add(glyphData.Position);
                var isSpaceGlyph = glyphId > 0 && glyphs[glyphId - 1].Scalar == ' ';
                glyphData.WriteBytes(isSpaceGlyph ? CreateEmptyGlyph() : CreateBoxGlyph());
                if (glyphData.Position % 2 != 0)
                    glyphData.WriteByte(0);
            }

            offsets.Add(glyphData.Position);
            locaOffsets = offsets;
            return glyphData.ToArray();
        }

        private static byte[] CreateEmptyGlyph()
        {
            var writer = new BigEndianWriter();
            writer.WriteInt16(0);
            writer.WriteInt16(0);
            writer.WriteInt16(0);
            writer.WriteInt16(0);
            writer.WriteInt16(0);
            return writer.ToArray();
        }

        private static byte[] CreateBoxGlyph()
        {
            var contours = new[]
            {
                new[] { (80, 0), (130, 0), (130, 700), (80, 700) },
                new[] { (470, 0), (520, 0), (520, 700), (470, 700) },
                new[] { (80, 0), (520, 0), (520, 50), (80, 50) },
                new[] { (80, 650), (520, 650), (520, 700), (80, 700) }
            };

            var points = contours.SelectMany(contour => contour).ToArray();
            var writer = new BigEndianWriter();
            writer.WriteInt16((short)contours.Length);
            writer.WriteInt16(80);
            writer.WriteInt16(0);
            writer.WriteInt16(520);
            writer.WriteInt16(700);

            var endPoint = -1;
            foreach (var contour in contours)
            {
                endPoint += contour.Length;
                writer.WriteUInt16((ushort)endPoint);
            }

            writer.WriteUInt16(0);
            foreach (var _ in points)
                writer.WriteByte(0x01);

            var previousX = 0;
            foreach (var point in points)
            {
                writer.WriteInt16((short)(point.Item1 - previousX));
                previousX = point.Item1;
            }

            var previousY = 0;
            foreach (var point in points)
            {
                writer.WriteInt16((short)(point.Item2 - previousY));
                previousY = point.Item2;
            }

            return writer.ToArray();
        }

        private static uint CalculateChecksum(byte[] data)
        {
            var paddedLength = Align4(data.Length);
            var checksum = 0u;
            for (var index = 0; index < paddedLength; index += sizeof(uint))
            {
                var b0 = index < data.Length ? data[index] : 0;
                var b1 = index + 1 < data.Length ? data[index + 1] : 0;
                var b2 = index + 2 < data.Length ? data[index + 2] : 0;
                var b3 = index + 3 < data.Length ? data[index + 3] : 0;
                checksum = unchecked(checksum + (uint)((b0 << 24) | (b1 << 16) | (b2 << 8) | b3));
            }

            return checksum;
        }

        private static int Align4(int value) => (value + 3) & ~3;

        private sealed class TrueTypeTable(string tag, byte[] data)
        {
            public string Tag { get; } = tag;

            public byte[] Data { get; } = data;

            public int Offset { get; set; }

            public uint Checksum { get; set; }
        }

        private sealed class BigEndianWriter
        {
            private readonly MemoryStream _stream = new();

            public int Position => checked((int)_stream.Position);

            public byte[] ToArray() => _stream.ToArray();

            public void WriteByte(byte value) => _stream.WriteByte(value);

            public void WriteBytes(byte[] value) => _stream.Write(value);

            public void WriteTag(string tag) => WriteBytes(PdfEncoding.GetBytes(tag));

            public void WriteInt16(int value) => WriteUInt16(unchecked((ushort)value));

            public void WriteUInt16(int value)
            {
                Span<byte> buffer = stackalloc byte[sizeof(ushort)];
                BinaryPrimitives.WriteUInt16BigEndian(buffer, checked((ushort)value));
                _stream.Write(buffer);
            }

            public void WriteUInt32(uint value)
            {
                Span<byte> buffer = stackalloc byte[sizeof(uint)];
                BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
                _stream.Write(buffer);
            }

            public void WriteUInt64(ulong value)
            {
                Span<byte> buffer = stackalloc byte[sizeof(ulong)];
                BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
                _stream.Write(buffer);
            }

            public void Pad4()
            {
                while (_stream.Position % 4 != 0)
                    _stream.WriteByte(0);
            }

            public void PadTo(int offset)
            {
                while (_stream.Position < offset)
                    _stream.WriteByte(0);
            }
        }
    }
}
