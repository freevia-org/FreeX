using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed class SpreadsheetXmlFileAdapter : IFileAdapter
{
    private static readonly XNamespace SpreadsheetNs = "urn:schemas-microsoft-com:office:spreadsheet";
    private static readonly XNamespace OfficeNs = "urn:schemas-microsoft-com:office:office";
    private static readonly XNamespace ExcelNs = "urn:schemas-microsoft-com:office:excel";
    private static readonly XName SpreadsheetIndexAttribute = SpreadsheetNs + "Index";
    private static readonly XName SpreadsheetNameAttribute = SpreadsheetNs + "Name";
    private static readonly XName SpreadsheetFormulaAttribute = SpreadsheetNs + "Formula";
    private static readonly XName SpreadsheetTypeAttribute = SpreadsheetNs + "Type";
    private static readonly XName SpreadsheetMergeAcrossAttribute = SpreadsheetNs + "MergeAcross";
    private static readonly XName SpreadsheetMergeDownAttribute = SpreadsheetNs + "MergeDown";
    private static readonly XName SpreadsheetHrefAttribute = SpreadsheetNs + "HRef";
    private static readonly XName SpreadsheetHrefScreenTipAttribute = SpreadsheetNs + "HRefScreenTip";
    private static readonly XName SpreadsheetAuthorAttribute = SpreadsheetNs + "Author";
    private static readonly XName SpreadsheetVisibleAttribute = SpreadsheetNs + "Visible";

    public string Extension => ".xml";
    public string FormatName => "XML Spreadsheet 2003";

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new FileFormatDescriptor(".xml", "XML Spreadsheet 2003", CanOpen: true, CanSave: true)
    ];

    public Workbook Load(Stream stream)
    {
        var document = LoadDocument(stream);
        if (document.Root?.Name != SpreadsheetNs + "Workbook")
            throw new InvalidDataException("The XML document is not an Excel XML Spreadsheet 2003 workbook.");

        var workbook = new Workbook("XML Spreadsheet");
        var sheetIndex = 1;
        foreach (var worksheetElement in document.Root.Elements(SpreadsheetNs + "Worksheet"))
        {
            var sheetName = UniqueSheetName(
                workbook,
                worksheetElement.Attribute(SpreadsheetNameAttribute)?.Value,
                sheetIndex++);
            var sheet = workbook.AddSheet(sheetName);
            ReadWorksheetVisibility(sheet, worksheetElement);
            ReadWorksheet(sheet, worksheetElement);
        }

        if (workbook.Sheets.Count == 0)
            workbook.AddSheet("Sheet1");

        return workbook;
    }

    public void Save(Workbook workbook, Stream stream)
    {
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XProcessingInstruction("mso-application", "progid=\"Excel.Sheet\""),
            new XElement(
                SpreadsheetNs + "Workbook",
                new XAttribute(XNamespace.Xmlns + "ss", SpreadsheetNs),
                new XAttribute(XNamespace.Xmlns + "o", OfficeNs),
                new XAttribute(XNamespace.Xmlns + "x", ExcelNs),
                workbook.Sheets.Select(ToWorksheetElement)));

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            OmitXmlDeclaration = false,
            NewLineChars = "\r\n",
            NewLineHandling = NewLineHandling.Replace
        };
        using var writer = XmlWriter.Create(stream, settings);
        document.Save(writer);
    }

    public static Workbook LoadTransformed(Stream sourceXml, Stream stylesheet)
        => LoadTransformed(sourceXml, stylesheet, XsltWorkbookTransform.DefaultMaxOutputBytes);

    public static Workbook LoadTransformed(Stream sourceXml, Stream stylesheet, long maxOutputBytes)
        => LoadTransformed(sourceXml, stylesheet, maxOutputBytes, XsltWorkbookTransform.DefaultMaxInputCharacters);

    public static Workbook LoadTransformed(
        Stream sourceXml,
        Stream stylesheet,
        long maxOutputBytes,
        long maxInputCharacters)
    {
        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(
            sourceXml,
            stylesheet,
            maxOutputBytes,
            maxInputCharacters);
        try
        {
            return new SpreadsheetXmlFileAdapter().Load(transformed);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException("The XSLT transform output could not be read as XML Spreadsheet 2003.", ex);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException("The XSLT transform output is not a valid Excel XML Spreadsheet 2003 workbook.", ex);
        }
    }

    private static XDocument LoadDocument(Stream stream)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static void ReadWorksheetVisibility(Sheet sheet, XElement worksheetElement)
    {
        var visibility = worksheetElement.Attribute(SpreadsheetVisibleAttribute)?.Value;
        sheet.IsVeryHidden = string.Equals(visibility, "SheetVeryHidden", StringComparison.OrdinalIgnoreCase);
        sheet.IsHidden = sheet.IsVeryHidden ||
                         string.Equals(visibility, "SheetHidden", StringComparison.OrdinalIgnoreCase);
    }

    private static void ReadWorksheet(Sheet sheet, XElement worksheetElement)
    {
        var tableElement = worksheetElement.Element(SpreadsheetNs + "Table");
        if (tableElement is null)
            return;

        var rowIndex = 1u;
        foreach (var rowElement in tableElement.Elements(SpreadsheetNs + "Row"))
        {
            rowIndex = ReadIndex(rowElement, rowIndex);
            if (rowIndex > CellAddress.MaxRow)
                break;

            var columnIndex = 1u;
            foreach (var cellElement in rowElement.Elements(SpreadsheetNs + "Cell"))
            {
                columnIndex = ReadIndex(cellElement, columnIndex);
                if (columnIndex > CellAddress.MaxCol)
                    break;

                var address = new CellAddress(sheet.Id, rowIndex, columnIndex);
                var cell = ReadCell(cellElement);
                var hyperlinkTarget = cellElement.Attribute(SpreadsheetHrefAttribute)?.Value;
                if (cell.Value is not BlankValue || cell.FormulaText is not null || !string.IsNullOrWhiteSpace(hyperlinkTarget))
                    sheet.SetCell(address, cell);

                if (!string.IsNullOrWhiteSpace(hyperlinkTarget))
                {
                    sheet.Hyperlinks[address] = hyperlinkTarget.Trim();
                    sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(
                        GetHyperlinkTargetKind(hyperlinkTarget),
                        cellElement.Attribute(SpreadsheetHrefScreenTipAttribute)?.Value?.Trim() ?? "",
                        GetHyperlinkBookmark(hyperlinkTarget));
                }

                if (ReadComment(cellElement) is { } comment)
                    sheet.Comments[address] = comment;

                var mergeAcross = ReadMergeExtent(cellElement, SpreadsheetMergeAcrossAttribute);
                if (TryReadMergeRange(sheet.Id, rowIndex, columnIndex, cellElement, mergeAcross, out var mergeRange))
                    sheet.AddMergedRegion(mergeRange);

                columnIndex = AdvanceColumnIndex(columnIndex, mergeAcross);
            }

            rowIndex++;
        }
    }

    private static Cell ReadCell(XElement cellElement)
    {
        var value = ReadValue(cellElement.Element(SpreadsheetNs + "Data"));
        var formula = cellElement.Attribute(SpreadsheetFormulaAttribute)?.Value;
        if (string.IsNullOrWhiteSpace(formula))
            return Cell.FromValue(value);

        return new Cell
        {
            FormulaText = formula.StartsWith("=", StringComparison.Ordinal) ? formula[1..] : formula,
            Value = value
        };
    }

    private static ScalarValue ReadValue(XElement? dataElement)
    {
        if (dataElement is null)
            return BlankValue.Instance;

        var text = dataElement.Value;
        var type = dataElement.Attribute(SpreadsheetTypeAttribute)?.Value;
        return type switch
        {
            "Number" when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) =>
                new NumberValue(number),
            "Boolean" when ReadBoolean(text, out var boolean) =>
                new BoolValue(boolean),
            "DateTime" when DateTime.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var dateTime) =>
                DateTimeValue.FromDateTime(dateTime),
            "Error" when text.Length > 0 => new ErrorValue(text),
            _ => new TextValue(text)
        };
    }

    private static string? ReadComment(XElement cellElement)
    {
        var commentElement = cellElement.Element(SpreadsheetNs + "Comment");
        var text = commentElement?.Element(SpreadsheetNs + "Data")?.Value;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static bool ReadBoolean(string text, out bool value)
    {
        if (string.Equals(text, "1", StringComparison.Ordinal) ||
            string.Equals(text, "TRUE", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (string.Equals(text, "0", StringComparison.Ordinal) ||
            string.Equals(text, "FALSE", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    private static uint ReadIndex(XElement element, uint fallback)
    {
        var indexText = element.Attribute(SpreadsheetIndexAttribute)?.Value;
        return uint.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index >= fallback
            ? index
            : fallback;
    }

    private static bool TryReadMergeRange(
        SheetId sheetId,
        uint row,
        uint column,
        XElement cellElement,
        uint mergeAcross,
        out GridRange range)
    {
        range = default;
        var mergeDown = ReadMergeExtent(cellElement, SpreadsheetMergeDownAttribute);
        if (mergeAcross == 0 && mergeDown == 0)
            return false;

        if (mergeAcross > CellAddress.MaxCol - column ||
            mergeDown > CellAddress.MaxRow - row)
        {
            return false;
        }

        range = new GridRange(
            new CellAddress(sheetId, row, column),
            new CellAddress(sheetId, row + mergeDown, column + mergeAcross));
        return true;
    }

    private static uint ReadMergeExtent(XElement cellElement, XName attributeName)
    {
        var text = cellElement.Attribute(attributeName)?.Value;
        return uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0u;
    }

    private static uint AdvanceColumnIndex(uint columnIndex, uint mergeAcross)
    {
        if (mergeAcross > CellAddress.MaxCol - columnIndex)
            return columnIndex + 1;

        return columnIndex + mergeAcross + 1;
    }

    private static XElement ToWorksheetElement(Sheet sheet) =>
        new(
            SpreadsheetNs + "Worksheet",
            new XAttribute(SpreadsheetNameAttribute, sheet.Name),
            ToWorksheetVisibilityAttribute(sheet),
            new XElement(
                SpreadsheetNs + "Table",
                EnumerateXmlCells(sheet)
                    .GroupBy(entry => entry.Row)
                    .Select(ToRowElement)));

    private static XAttribute? ToWorksheetVisibilityAttribute(Sheet sheet)
    {
        if (sheet.IsVeryHidden)
            return new XAttribute(SpreadsheetVisibleAttribute, "SheetVeryHidden");

        return sheet.IsHidden
            ? new XAttribute(SpreadsheetVisibleAttribute, "SheetHidden")
            : null;
    }

    private static IEnumerable<SpreadsheetXmlCell> EnumerateXmlCells(Sheet sheet)
    {
        var mergeStarts = new Dictionary<(uint Row, uint Col), GridRange>();
        foreach (var region in sheet.MergedRegions)
            mergeStarts.TryAdd((region.Start.Row, region.Start.Col), region);

        var emitted = new HashSet<(uint Row, uint Col)>();
        var cells = new List<SpreadsheetXmlCell>();

        foreach (var (address, cell) in sheet.EnumerateCells()
                     .OrderBy(entry => entry.Address.Row)
                     .ThenBy(entry => entry.Address.Col))
        {
            if (IsCoveredByMergeNonAnchor(sheet, address))
                continue;

            mergeStarts.TryGetValue((address.Row, address.Col), out var mergeRange);
            sheet.Hyperlinks.TryGetValue(address, out var hyperlinkTarget);
            sheet.HyperlinkMetadata.TryGetValue(address, out var hyperlinkMetadata);
            sheet.Comments.TryGetValue(address, out var comment);
            emitted.Add((address.Row, address.Col));
            cells.Add(new SpreadsheetXmlCell(address.Row, address.Col, cell, mergeRange, hyperlinkTarget, hyperlinkMetadata, comment));
        }

        foreach (var (address, hyperlinkTarget) in sheet.Hyperlinks
                     .Where(entry => !emitted.Contains((entry.Key.Row, entry.Key.Col)))
                     .OrderBy(entry => entry.Key.Row)
                     .ThenBy(entry => entry.Key.Col))
        {
            if (IsCoveredByMergeNonAnchor(sheet, address))
                continue;

            mergeStarts.TryGetValue((address.Row, address.Col), out var mergeRange);
            sheet.HyperlinkMetadata.TryGetValue(address, out var hyperlinkMetadata);
            sheet.Comments.TryGetValue(address, out var comment);
            emitted.Add((address.Row, address.Col));
            cells.Add(new SpreadsheetXmlCell(
                address.Row,
                address.Col,
                Cell.FromValue(BlankValue.Instance),
                mergeRange,
                hyperlinkTarget,
                hyperlinkMetadata,
                comment));
        }

        foreach (var (address, comment) in sheet.Comments
                     .Where(entry => !emitted.Contains((entry.Key.Row, entry.Key.Col)))
                     .OrderBy(entry => entry.Key.Row)
                     .ThenBy(entry => entry.Key.Col))
        {
            if (IsCoveredByMergeNonAnchor(sheet, address))
                continue;

            mergeStarts.TryGetValue((address.Row, address.Col), out var mergeRange);
            emitted.Add((address.Row, address.Col));
            cells.Add(new SpreadsheetXmlCell(
                address.Row,
                address.Col,
                Cell.FromValue(BlankValue.Instance),
                mergeRange,
                HyperlinkTarget: null,
                HyperlinkMetadata: null,
                Comment: comment));
        }

        foreach (var mergeRange in sheet.MergedRegions
                     .Where(region => !emitted.Contains((region.Start.Row, region.Start.Col)))
                     .OrderBy(region => region.Start.Row)
                     .ThenBy(region => region.Start.Col))
        {
            cells.Add(new SpreadsheetXmlCell(
                mergeRange.Start.Row,
                mergeRange.Start.Col,
                Cell.FromValue(BlankValue.Instance),
                mergeRange,
                HyperlinkTarget: null,
                HyperlinkMetadata: null,
                Comment: null));
        }

        foreach (var cell in cells.OrderBy(cell => cell.Row).ThenBy(cell => cell.Col))
            yield return cell;
    }

    private static bool IsCoveredByMergeNonAnchor(Sheet sheet, CellAddress address) =>
        sheet.GetMergeRegion(address) is { } mergeRange &&
        (mergeRange.Start.Row != address.Row || mergeRange.Start.Col != address.Col);

    private static XElement ToRowElement(IGrouping<uint, SpreadsheetXmlCell> row)
    {
        var rowElement = new XElement(SpreadsheetNs + "Row", new XAttribute(SpreadsheetIndexAttribute, row.Key));
        foreach (var cell in row.OrderBy(entry => entry.Col))
            rowElement.Add(ToCellElement(cell));

        return rowElement;
    }

    private static XElement ToCellElement(SpreadsheetXmlCell cell)
    {
        var element = new XElement(SpreadsheetNs + "Cell", new XAttribute(SpreadsheetIndexAttribute, cell.Col));
        if (cell.MergeRange is { } mergeRange)
        {
            if (mergeRange.ColCount > 1)
                element.SetAttributeValue(SpreadsheetMergeAcrossAttribute, mergeRange.ColCount - 1);
            if (mergeRange.RowCount > 1)
                element.SetAttributeValue(SpreadsheetMergeDownAttribute, mergeRange.RowCount - 1);
        }

        if (cell.Cell.FormulaText is { Length: > 0 } formulaText)
            element.SetAttributeValue(SpreadsheetFormulaAttribute, formulaText.StartsWith("=", StringComparison.Ordinal) ? formulaText : $"={formulaText}");

        if (!string.IsNullOrWhiteSpace(cell.HyperlinkTarget))
        {
            element.SetAttributeValue(SpreadsheetHrefAttribute, cell.HyperlinkTarget);
            if (!string.IsNullOrWhiteSpace(cell.HyperlinkMetadata?.ScreenTip))
                element.SetAttributeValue(SpreadsheetHrefScreenTipAttribute, cell.HyperlinkMetadata.ScreenTip);
        }

        if (cell.Cell.Value is not BlankValue)
            element.Add(ToDataElement(cell.Cell.Value));

        if (!string.IsNullOrWhiteSpace(cell.Comment))
        {
            element.Add(new XElement(
                SpreadsheetNs + "Comment",
                new XAttribute(SpreadsheetAuthorAttribute, "FreeX"),
                new XElement(SpreadsheetNs + "Data", cell.Comment)));
        }

        return element;
    }

    private static XElement ToDataElement(ScalarValue value)
    {
        var (type, text) = value switch
        {
            NumberValue number => ("Number", number.Value.ToString("R", CultureInfo.InvariantCulture)),
            DateTimeValue dateTime => ("DateTime", dateTime.ToDateTime().ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture)),
            BoolValue boolean => ("Boolean", boolean.Value ? "1" : "0"),
            ErrorValue error => ("Error", error.Code),
            TextValue textValue => ("String", textValue.Value),
            _ => ("String", "")
        };

        return new XElement(
            SpreadsheetNs + "Data",
            new XAttribute(SpreadsheetTypeAttribute, type),
            text);
    }

    private static string UniqueSheetName(Workbook workbook, string? rawName, int index)
    {
        var baseName = string.IsNullOrWhiteSpace(rawName) ? $"Sheet{index}" : rawName.Trim();
        baseName = SanitizeSheetName(baseName);
        var candidate = baseName;
        var suffix = 1;
        while (workbook.ValidateSheetName(candidate) is not null)
        {
            var marker = $" ({suffix++})";
            candidate = string.Concat(baseName.AsSpan(0, Math.Min(baseName.Length, 31 - marker.Length)), marker);
        }

        return candidate;
    }

    private static string SanitizeSheetName(string value)
    {
        Span<char> invalid = [':', '\\', '/', '?', '*', '[', ']'];
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
            builder.Append(invalid.Contains(ch) ? '_' : ch);

        var sanitized = builder.ToString().Trim('\'');
        if (sanitized.Length == 0)
            return "Sheet";

        return sanitized.Length <= 31 ? sanitized : sanitized[..31];
    }

    private static HyperlinkTargetKind GetHyperlinkTargetKind(string target)
    {
        if (target.StartsWith("#", StringComparison.Ordinal))
            return HyperlinkTargetKind.PlaceInThisDocument;

        return target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            ? HyperlinkTargetKind.EmailAddress
            : HyperlinkTargetKind.ExistingFileOrWebPage;
    }

    private static string GetHyperlinkBookmark(string target) =>
        target.StartsWith("#", StringComparison.Ordinal) ? target[1..] : "";

    private readonly record struct SpreadsheetXmlCell(
        uint Row,
        uint Col,
        Cell Cell,
        GridRange? MergeRange,
        string? HyperlinkTarget,
        HyperlinkMetadata? HyperlinkMetadata,
        string? Comment);
}
