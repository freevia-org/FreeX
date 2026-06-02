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
    private static readonly XName SpreadsheetSpanAttribute = SpreadsheetNs + "Span";
    private static readonly XName SpreadsheetNameAttribute = SpreadsheetNs + "Name";
    private static readonly XName SpreadsheetFormulaAttribute = SpreadsheetNs + "Formula";
    private static readonly XName SpreadsheetTypeAttribute = SpreadsheetNs + "Type";
    private static readonly XName SpreadsheetMergeAcrossAttribute = SpreadsheetNs + "MergeAcross";
    private static readonly XName SpreadsheetMergeDownAttribute = SpreadsheetNs + "MergeDown";
    private static readonly XName SpreadsheetIdAttribute = SpreadsheetNs + "ID";
    private static readonly XName SpreadsheetParentAttribute = SpreadsheetNs + "Parent";
    private static readonly XName SpreadsheetStyleIdAttribute = SpreadsheetNs + "StyleID";
    private static readonly XName SpreadsheetFormatAttribute = SpreadsheetNs + "Format";
    private static readonly XName SpreadsheetHrefAttribute = SpreadsheetNs + "HRef";
    private static readonly XName SpreadsheetHrefScreenTipAttribute = SpreadsheetNs + "HRefScreenTip";
    private static readonly XName SpreadsheetAuthorAttribute = SpreadsheetNs + "Author";
    private static readonly XName SpreadsheetVisibleAttribute = SpreadsheetNs + "Visible";
    private static readonly XName SpreadsheetHeightAttribute = SpreadsheetNs + "Height";
    private static readonly XName SpreadsheetWidthAttribute = SpreadsheetNs + "Width";
    private static readonly XName SpreadsheetHiddenAttribute = SpreadsheetNs + "Hidden";
    private static readonly XName SpreadsheetRefersToAttribute = SpreadsheetNs + "RefersTo";

    public string Extension => ".xml";
    public string FormatName => "XML Spreadsheet 2003";

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new FileFormatDescriptor(".xml", "XML Spreadsheet 2003", CanOpen: true, CanSave: true)
    ];

    public Workbook Load(Stream stream) =>
        Load(stream, SecureXmlReaderSettings.DefaultMaxCharactersInDocument);

    internal Workbook Load(Stream stream, long maxCharactersInDocument)
    {
        var document = LoadDocument(stream, maxCharactersInDocument);
        if (document.Root?.Name != SpreadsheetNs + "Workbook")
            throw new InvalidDataException("The XML document is not an Excel XML Spreadsheet 2003 workbook.");

        var workbook = new Workbook("XML Spreadsheet");
        var styles = ReadStyles(workbook, document.Root);
        var sheetIndex = 1;
        foreach (var worksheetElement in document.Root.Elements(SpreadsheetNs + "Worksheet"))
        {
            var sheetName = UniqueSheetName(
                workbook,
                worksheetElement.Attribute(SpreadsheetNameAttribute)?.Value,
                sheetIndex++);
            var sheet = workbook.AddSheet(sheetName);
            ReadWorksheetVisibility(sheet, worksheetElement);
            ReadWorksheetOptions(sheet, worksheetElement);
            ReadWorksheet(sheet, worksheetElement, styles);
        }

        if (workbook.Sheets.Count == 0)
            workbook.AddSheet("Sheet1");

        ReadNamedRanges(workbook, document.Root);

        return workbook;
    }

    public void Save(Workbook workbook, Stream stream)
    {
        SaveStreamPreparer.TruncateFromCurrentPosition(stream);

        var styleIds = CreateNumberFormatStyleIds(workbook);
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            OmitXmlDeclaration = false,
            NewLineChars = "\r\n",
            NewLineHandling = NewLineHandling.Replace
        };
        using var writer = XmlWriter.Create(stream, settings);
        WriteWorkbook(writer, workbook, styleIds);
    }

    public static Workbook LoadTransformed(Stream sourceXml, Stream stylesheet)
        => LoadTransformed(sourceXml, stylesheet, XsltWorkbookTransform.DefaultMaxOutputBytes);

    public static Workbook LoadTransformed(
        Stream sourceXml,
        Stream stylesheet,
        IReadOnlyDictionary<string, string?> parameters)
        => LoadTransformed(
            sourceXml,
            stylesheet,
            XsltWorkbookTransform.DefaultMaxOutputBytes,
            XsltWorkbookTransform.DefaultMaxInputCharacters,
            parameters);

    public static Workbook LoadTransformed(Stream sourceXml, Stream stylesheet, long maxOutputBytes)
        => LoadTransformed(sourceXml, stylesheet, maxOutputBytes, XsltWorkbookTransform.DefaultMaxInputCharacters);

    public static Workbook LoadTransformed(
        Stream sourceXml,
        Stream stylesheet,
        long maxOutputBytes,
        long maxInputCharacters)
        => LoadTransformed(sourceXml, stylesheet, maxOutputBytes, maxInputCharacters, parameters: null);

    public static Workbook LoadTransformed(
        Stream sourceXml,
        Stream stylesheet,
        long maxOutputBytes,
        long maxInputCharacters,
        IReadOnlyDictionary<string, string?>? parameters)
    {
        using var transformed = XsltWorkbookTransform.TransformToSpreadsheetXml(
            sourceXml,
            stylesheet,
            maxOutputBytes,
            maxInputCharacters,
            parameters);
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

    private static XDocument LoadDocument(Stream stream, long maxCharactersInDocument)
    {
        using var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create(maxCharactersInDocument));
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static void ReadWorksheetVisibility(Sheet sheet, XElement worksheetElement)
    {
        var visibility = worksheetElement.Attribute(SpreadsheetVisibleAttribute)?.Value;
        sheet.IsVeryHidden = string.Equals(visibility, "SheetVeryHidden", StringComparison.OrdinalIgnoreCase);
        sheet.IsHidden = sheet.IsVeryHidden ||
                         string.Equals(visibility, "SheetHidden", StringComparison.OrdinalIgnoreCase);
    }

    private static void ReadWorksheetOptions(Sheet sheet, XElement worksheetElement)
    {
        var optionsElement = worksheetElement.Element(ExcelNs + "WorksheetOptions");
        if (optionsElement is null)
            return;

        sheet.ShowGridlines = optionsElement.Element(ExcelNs + "DoNotDisplayGridlines") is null;
        sheet.PrintGridlines = optionsElement.Element(ExcelNs + "Print")?.Element(ExcelNs + "Gridlines") is not null;
        if (optionsElement.Element(ExcelNs + "FreezePanes") is null)
            return;

        sheet.FrozenRows = ReadPaneSplit(optionsElement, ExcelNs + "SplitHorizontal", CellAddress.MaxRow);
        sheet.FrozenCols = ReadPaneSplit(optionsElement, ExcelNs + "SplitVertical", CellAddress.MaxCol);
    }

    private static Dictionary<string, StyleId> ReadStyles(Workbook workbook, XElement workbookElement)
    {
        var styles = new Dictionary<string, StyleId>(StringComparer.Ordinal);
        var stylesElement = workbookElement.Element(SpreadsheetNs + "Styles");
        if (stylesElement is null)
            return styles;

        var definitions = new Dictionary<string, StyleDefinition>(StringComparer.Ordinal);
        foreach (var styleElement in stylesElement.Elements(SpreadsheetNs + "Style"))
        {
            var id = styleElement.Attribute(SpreadsheetIdAttribute)?.Value;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            definitions[id] = new StyleDefinition(
                id,
                styleElement.Attribute(SpreadsheetParentAttribute)?.Value,
                styleElement.Element(SpreadsheetNs + "NumberFormat")?.Attribute(SpreadsheetFormatAttribute)?.Value);
        }

        foreach (var styleElement in stylesElement.Elements(SpreadsheetNs + "Style"))
        {
            var id = styleElement.Attribute(SpreadsheetIdAttribute)?.Value;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var numberFormat = ResolveNumberFormat(id, definitions, []);
            if (string.IsNullOrWhiteSpace(numberFormat))
                continue;

            styles[id] = workbook.RegisterStyle(new CellStyle { NumberFormat = numberFormat });
        }

        return styles;
    }

    private static string? ResolveNumberFormat(
        string styleId,
        IReadOnlyDictionary<string, StyleDefinition> definitions,
        HashSet<string> visited)
    {
        if (!visited.Add(styleId) || !definitions.TryGetValue(styleId, out var definition))
            return null;

        if (!string.IsNullOrWhiteSpace(definition.NumberFormat))
            return definition.NumberFormat;

        return string.IsNullOrWhiteSpace(definition.ParentId)
            ? null
            : ResolveNumberFormat(definition.ParentId, definitions, visited);
    }

    private sealed record StyleDefinition(string? Id, string? ParentId, string? NumberFormat);

    private static void ReadWorksheet(Sheet sheet, XElement worksheetElement, IReadOnlyDictionary<string, StyleId> styles)
    {
        var tableElement = worksheetElement.Element(SpreadsheetNs + "Table");
        if (tableElement is null)
            return;

        var columnStyles = ReadColumns(sheet, tableElement, styles);

        var rowIndex = 1u;
        foreach (var rowElement in tableElement.Elements(SpreadsheetNs + "Row"))
        {
            rowIndex = ReadIndex(rowElement, rowIndex);
            if (rowIndex > CellAddress.MaxRow)
                break;

            var rowSpan = ReadSpan(rowElement);
            var lastRowIndex = rowSpan > CellAddress.MaxRow - rowIndex
                ? CellAddress.MaxRow
                : rowIndex + rowSpan;
            for (var currentRowIndex = rowIndex; currentRowIndex <= lastRowIndex; currentRowIndex++)
                ReadRowLayout(sheet, rowElement, currentRowIndex);

            var rowStyleId = ReadStyleId(rowElement, styles);

            var columnIndex = 1u;
            foreach (var cellElement in rowElement.Elements(SpreadsheetNs + "Cell"))
            {
                columnIndex = ReadIndex(cellElement, columnIndex);
                if (columnIndex > CellAddress.MaxCol)
                    break;

                var address = new CellAddress(sheet.Id, rowIndex, columnIndex);
                columnStyles.TryGetValue(columnIndex, out var columnStyleId);
                var cell = ReadCell(cellElement, styles, rowStyleId, columnStyleId);
                var hyperlinkTarget = cellElement.Attribute(SpreadsheetHrefAttribute)?.Value;
                if (cell.Value is not BlankValue || cell.FormulaText is not null || !string.IsNullOrWhiteSpace(hyperlinkTarget))
                {
                    sheet.SetCell(address, cell);
                }
                else if (cell.StyleId != StyleId.Default)
                {
                    sheet.SetStyleOnly(rowIndex, columnIndex, cell.StyleId);
                }

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

            rowIndex = lastRowIndex + 1;
        }
    }

    private static void ReadNamedRanges(Workbook workbook, XElement workbookElement)
    {
        var namesElement = workbookElement.Element(SpreadsheetNs + "Names");
        if (namesElement is null)
            return;

        foreach (var namedRangeElement in namesElement.Elements(SpreadsheetNs + "NamedRange"))
        {
            var name = namedRangeElement.Attribute(SpreadsheetNameAttribute)?.Value?.Trim();
            var refersTo = namedRangeElement.Attribute(SpreadsheetRefersToAttribute)?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                workbook.ValidateNamedRangeName(name) is not null ||
                !TryParseNamedRangeRefersTo(workbook, refersTo, out var range))
            {
                continue;
            }

            workbook.DefineNamedRange(name, range);
        }
    }

    private static Dictionary<uint, StyleId> ReadColumns(
        Sheet sheet,
        XElement tableElement,
        IReadOnlyDictionary<string, StyleId> styles)
    {
        var columnStyles = new Dictionary<uint, StyleId>();
        var columnIndex = 1u;
        foreach (var columnElement in tableElement.Elements(SpreadsheetNs + "Column"))
        {
            columnIndex = ReadIndex(columnElement, columnIndex);
            if (columnIndex > CellAddress.MaxCol)
                return columnStyles;

            var span = ReadSpan(columnElement);
            var lastColumnIndex = span > CellAddress.MaxCol - columnIndex
                ? CellAddress.MaxCol
                : columnIndex + span;
            for (var currentColumnIndex = columnIndex; currentColumnIndex <= lastColumnIndex; currentColumnIndex++)
            {
                ReadColumnLayout(sheet, columnElement, currentColumnIndex);
                var styleId = ReadStyleId(columnElement, styles);
                if (styleId != StyleId.Default)
                    columnStyles[currentColumnIndex] = styleId;
            }

            columnIndex = lastColumnIndex + 1;
        }

        return columnStyles;
    }

    private static void ReadColumnLayout(Sheet sheet, XElement columnElement, uint columnIndex)
    {
        if (double.TryParse(
                columnElement.Attribute(SpreadsheetWidthAttribute)?.Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var width) &&
            IsPositiveFinite(width))
        {
            sheet.ColumnWidths[columnIndex] = width;
        }

        if (ReadBoolean(columnElement.Attribute(SpreadsheetHiddenAttribute)?.Value ?? "", out var hidden) && hidden)
            sheet.HiddenCols.Add(columnIndex);
    }

    private static void ReadRowLayout(Sheet sheet, XElement rowElement, uint rowIndex)
    {
        if (double.TryParse(
                rowElement.Attribute(SpreadsheetHeightAttribute)?.Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var height) &&
            IsPositiveFinite(height))
        {
            sheet.RowHeights[rowIndex] = height;
        }

        if (ReadBoolean(rowElement.Attribute(SpreadsheetHiddenAttribute)?.Value ?? "", out var hidden) && hidden)
            sheet.HiddenRows.Add(rowIndex);
    }

    private static Cell ReadCell(
        XElement cellElement,
        IReadOnlyDictionary<string, StyleId> styles,
        StyleId rowStyleId = default,
        StyleId columnStyleId = default)
    {
        var value = ReadValue(cellElement.Element(SpreadsheetNs + "Data"));
        var formula = cellElement.Attribute(SpreadsheetFormulaAttribute)?.Value;
        var styleId = ReadStyleId(cellElement, styles);
        if (styleId == StyleId.Default)
            styleId = rowStyleId != StyleId.Default ? rowStyleId : columnStyleId;
        if (string.IsNullOrWhiteSpace(formula))
            return new Cell { Value = value, StyleId = styleId };

        return new Cell
        {
            FormulaText = formula.StartsWith("=", StringComparison.Ordinal) ? formula[1..] : formula,
            Value = value,
            StyleId = styleId
        };
    }

    private static StyleId ReadStyleId(XElement cellElement, IReadOnlyDictionary<string, StyleId> styles)
    {
        var styleId = cellElement.Attribute(SpreadsheetStyleIdAttribute)?.Value;
        return styleId is not null && styles.TryGetValue(styleId, out var registeredStyleId)
            ? registeredStyleId
            : StyleId.Default;
    }

    private static ScalarValue ReadValue(XElement? dataElement)
    {
        if (dataElement is null)
            return BlankValue.Instance;

        var text = dataElement.Value;
        var type = dataElement.Attribute(SpreadsheetTypeAttribute)?.Value;
        return type switch
        {
            "Number" when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
                          double.IsFinite(number) =>
                new NumberValue(number),
            "Boolean" when ReadBoolean(text, out var boolean) =>
                new BoolValue(boolean),
            "DateTime" when TryParseSpreadsheetDateTime(text, out var dateTime) =>
                DateTimeValue.FromDateTime(dateTime),
            "Error" when text.Length > 0 => new ErrorValue(text),
            _ => new TextValue(text)
        };
    }

    private static bool TryParseSpreadsheetDateTime(string text, out DateTime dateTime)
    {
        if (HasExplicitTimeZoneOffset(text) &&
            DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var offset))
        {
            dateTime = offset.UtcDateTime;
            return true;
        }

        return DateTime.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out dateTime);
    }

    private static bool HasExplicitTimeZoneOffset(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.EndsWith('Z') || trimmed.EndsWith('z'))
            return true;

        var timeSeparator = Math.Max(trimmed.LastIndexOf('T'), trimmed.LastIndexOf(' '));
        if (timeSeparator < 0)
            return false;

        var zoneStart = Math.Max(trimmed.LastIndexOf('+'), trimmed.LastIndexOf('-'));
        return zoneStart > timeSeparator;
    }

    private static string? ReadComment(XElement cellElement)
    {
        var commentElement = cellElement.Element(SpreadsheetNs + "Comment");
        var text = commentElement?.Element(SpreadsheetNs + "Data")?.Value;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static bool ReadBoolean(string text, out bool value)
    {
        var normalized = text.Trim();

        if (string.Equals(normalized, "1", StringComparison.Ordinal) ||
            string.Equals(normalized, "TRUE", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (string.Equals(normalized, "0", StringComparison.Ordinal) ||
            string.Equals(normalized, "FALSE", StringComparison.OrdinalIgnoreCase))
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
        return TryParseUInt(indexText, out var index) && index >= fallback
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
        return TryParseUInt(text, out var value)
            ? value
            : 0u;
    }

    private static uint ReadSpan(XElement element)
    {
        var text = element.Attribute(SpreadsheetSpanAttribute)?.Value;
        return TryParseUInt(text, out var value)
            ? value
            : 0u;
    }

    private static uint ReadPaneSplit(XElement element, XName elementName, uint maxValue)
    {
        var text = element.Element(elementName)?.Value;
        return TryParseUInt(text, out var value) && value <= maxValue
            ? value
            : 0u;
    }

    private static bool TryParseUInt(string? text, out uint value) =>
        uint.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static uint AdvanceColumnIndex(uint columnIndex, uint mergeAcross)
    {
        if (mergeAcross > CellAddress.MaxCol - columnIndex)
            return columnIndex + 1;

        return columnIndex + mergeAcross + 1;
    }

    private static Dictionary<StyleId, string> CreateNumberFormatStyleIds(Workbook workbook)
    {
        var styleIds = new Dictionary<StyleId, string>();
        for (var index = 1; index < workbook.StyleCount; index++)
        {
            var styleId = new StyleId(index);
            var style = workbook.GetStyle(styleId);
            if (string.IsNullOrWhiteSpace(style.NumberFormat) ||
                string.Equals(style.NumberFormat, CellStyle.Default.NumberFormat, StringComparison.Ordinal))
            {
                continue;
            }

            styleIds[styleId] = $"s{index}";
        }

        return styleIds;
    }

    private static void WriteWorkbook(
        XmlWriter writer,
        Workbook workbook,
        IReadOnlyDictionary<StyleId, string> styleIds)
    {
        writer.WriteStartDocument();
        writer.WriteProcessingInstruction("mso-application", "progid=\"Excel.Sheet\"");
        writer.WriteStartElement("ss", "Workbook", SpreadsheetNs.NamespaceName);
        writer.WriteAttributeString("xmlns", "ss", null, SpreadsheetNs.NamespaceName);
        writer.WriteAttributeString("xmlns", "o", null, OfficeNs.NamespaceName);
        writer.WriteAttributeString("xmlns", "x", null, ExcelNs.NamespaceName);

        WriteStylesElement(writer, workbook, styleIds);
        WriteNamesElement(writer, workbook);

        foreach (var sheet in workbook.Sheets)
            WriteWorksheetElement(writer, sheet, styleIds);

        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteStylesElement(
        XmlWriter writer,
        Workbook workbook,
        IReadOnlyDictionary<StyleId, string> styleIds)
    {
        if (styleIds.Count == 0)
            return;

        WriteSpreadsheetStartElement(writer, "Styles");
        foreach (var (styleId, styleName) in styleIds)
        {
            WriteSpreadsheetStartElement(writer, "Style");
            WriteSpreadsheetAttribute(writer, SpreadsheetIdAttribute, styleName);
            WriteSpreadsheetStartElement(writer, "NumberFormat");
            WriteSpreadsheetAttribute(writer, SpreadsheetFormatAttribute, workbook.GetStyle(styleId).NumberFormat);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteNamesElement(XmlWriter writer, Workbook workbook)
    {
        var wroteNames = false;
        foreach (var (name, range) in workbook.NamedRanges.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryFormatNamedRangeRefersTo(workbook, name, range, out var refersTo))
                continue;

            if (!wroteNames)
            {
                WriteSpreadsheetStartElement(writer, "Names");
                wroteNames = true;
            }

            WriteSpreadsheetStartElement(writer, "NamedRange");
            WriteSpreadsheetAttribute(writer, SpreadsheetNameAttribute, name);
            WriteSpreadsheetAttribute(writer, SpreadsheetRefersToAttribute, refersTo);
            writer.WriteEndElement();
        }

        if (wroteNames)
            writer.WriteEndElement();
    }

    private static bool TryFormatNamedRangeRefersTo(
        Workbook workbook,
        string name,
        GridRange range,
        out string refersTo)
    {
        refersTo = "";
        if (workbook.ValidateNamedRangeName(name) is not null ||
            workbook.GetSheet(range.Start.Sheet) is not { } sheet ||
            !IsValidGridRange(range))
        {
            return false;
        }

        refersTo = FormatNamedRangeRefersTo(sheet.Name, range);
        return true;
    }

    private static void WriteWorksheetElement(
        XmlWriter writer,
        Sheet sheet,
        IReadOnlyDictionary<StyleId, string> styleIds)
    {
        WriteSpreadsheetStartElement(writer, "Worksheet");
        WriteSpreadsheetAttribute(writer, SpreadsheetNameAttribute, sheet.Name);
        WriteWorksheetVisibilityAttribute(writer, sheet);

        WriteSpreadsheetStartElement(writer, "Table");
        WriteTableElements(writer, sheet, styleIds);
        writer.WriteEndElement();

        WriteWorksheetOptionsElement(writer, sheet);
        writer.WriteEndElement();
    }

    private static void WriteWorksheetOptionsElement(XmlWriter writer, Sheet sheet)
    {
        var frozenRows = sheet.FrozenRows is > 0 and <= CellAddress.MaxRow ? sheet.FrozenRows : 0;
        var frozenCols = sheet.FrozenCols is > 0 and <= CellAddress.MaxCol ? sheet.FrozenCols : 0;
        if (sheet.ShowGridlines && !sheet.PrintGridlines && frozenRows == 0 && frozenCols == 0)
            return;

        writer.WriteStartElement("x", "WorksheetOptions", ExcelNs.NamespaceName);
        if (!sheet.ShowGridlines)
            WriteExcelEmptyElement(writer, "DoNotDisplayGridlines");

        if (sheet.PrintGridlines)
        {
            writer.WriteStartElement("x", "Print", ExcelNs.NamespaceName);
            WriteExcelEmptyElement(writer, "Gridlines");
            writer.WriteEndElement();
        }

        if (frozenRows > 0 || frozenCols > 0)
        {
            WriteExcelEmptyElement(writer, "FreezePanes");
            WriteExcelEmptyElement(writer, "FrozenNoSplit");
            if (frozenRows > 0)
            {
                WriteExcelTextElement(writer, "SplitHorizontal", frozenRows.ToString(CultureInfo.InvariantCulture));
                WriteExcelTextElement(writer, "TopRowBottomPane", frozenRows.ToString(CultureInfo.InvariantCulture));
            }

            if (frozenCols > 0)
            {
                WriteExcelTextElement(writer, "SplitVertical", frozenCols.ToString(CultureInfo.InvariantCulture));
                WriteExcelTextElement(writer, "LeftColumnRightPane", frozenCols.ToString(CultureInfo.InvariantCulture));
            }
        }

        writer.WriteEndElement();
    }

    private static void WriteTableElements(
        XmlWriter writer,
        Sheet sheet,
        IReadOnlyDictionary<StyleId, string> styleIds)
    {
        WriteColumnElements(writer, sheet);
        WriteRowElements(writer, sheet, styleIds);
    }

    private static void WriteWorksheetVisibilityAttribute(XmlWriter writer, Sheet sheet)
    {
        if (sheet.IsVeryHidden)
        {
            WriteSpreadsheetAttribute(writer, SpreadsheetVisibleAttribute, "SheetVeryHidden");
            return;
        }

        if (sheet.IsHidden)
            WriteSpreadsheetAttribute(writer, SpreadsheetVisibleAttribute, "SheetHidden");
    }

    private static void WriteColumnElements(XmlWriter writer, Sheet sheet)
    {
        var columnIndexes = sheet.ColumnWidths.Keys
            .Where(IsValidColumnLayoutIndex)
            .Concat(sheet.HiddenCols.Where(IsValidColumnLayoutIndex))
            .Distinct()
            .OrderBy(column => column);

        foreach (var columnIndex in columnIndexes)
        {
            WriteSpreadsheetStartElement(writer, "Column");
            WriteSpreadsheetAttribute(writer, SpreadsheetIndexAttribute, columnIndex);
            WriteColumnWidthAttribute(writer, sheet, columnIndex);
            if (sheet.HiddenCols.Contains(columnIndex))
                WriteSpreadsheetAttribute(writer, SpreadsheetHiddenAttribute, "1");
            writer.WriteEndElement();
        }
    }

    private static void WriteColumnWidthAttribute(XmlWriter writer, Sheet sheet, uint columnIndex)
    {
        if (sheet.ColumnWidths.TryGetValue(columnIndex, out var width) && IsPositiveFinite(width))
            WriteSpreadsheetAttribute(writer, SpreadsheetWidthAttribute, width.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void WriteRowElements(
        XmlWriter writer,
        Sheet sheet,
        IReadOnlyDictionary<StyleId, string> styleIds)
    {
        if (CanStreamValueCellRows(sheet))
        {
            WriteValueCellRowElements(writer, sheet, styleIds);
            return;
        }

        var cells = BuildSortedXmlCells(sheet);
        var layoutRows = BuildSortedRowLayoutIndexes(sheet);

        var cellIndex = 0;
        var layoutRowIndex = 0;
        while (cellIndex < cells.Count || layoutRowIndex < layoutRows.Count)
        {
            var cellRow = cellIndex < cells.Count ? cells[cellIndex].Row : uint.MaxValue;
            var layoutRow = layoutRowIndex < layoutRows.Count ? layoutRows[layoutRowIndex] : uint.MaxValue;
            var rowIndex = cellRow <= layoutRow ? cellRow : layoutRow;
            WriteRowStart(writer, sheet, rowIndex);

            while (cellIndex < cells.Count && cells[cellIndex].Row == rowIndex)
            {
                WriteCellElement(writer, cells[cellIndex], styleIds);
                cellIndex++;
            }

            writer.WriteEndElement();

            if (layoutRow == rowIndex)
                layoutRowIndex++;
        }
    }

    private static void WriteValueCellRowElements(
        XmlWriter writer,
        Sheet sheet,
        IReadOnlyDictionary<StyleId, string> styleIds)
    {
        var occupiedCells = sheet.GetOccupiedCellMap();
        var layoutRows = BuildSortedRowLayoutIndexes(sheet);
        if (IsRowColumnOrdered(occupiedCells))
        {
            WriteOrderedValueCellRowElements(writer, sheet, occupiedCells, layoutRows, styleIds);
            return;
        }

        var cellsByRow = BuildValueCellsByRow(occupiedCells);
        var cellRows = new List<uint>(cellsByRow.Keys);
        cellRows.Sort();

        var cellRowIndex = 0;
        var layoutRowIndex = 0;
        while (cellRowIndex < cellRows.Count || layoutRowIndex < layoutRows.Count)
        {
            var cellRow = cellRowIndex < cellRows.Count ? cellRows[cellRowIndex] : uint.MaxValue;
            var layoutRow = layoutRowIndex < layoutRows.Count ? layoutRows[layoutRowIndex] : uint.MaxValue;
            var rowIndex = cellRow <= layoutRow ? cellRow : layoutRow;
            WriteRowStart(writer, sheet, rowIndex);

            if (cellRow == rowIndex)
            {
                foreach (var cell in cellsByRow[rowIndex])
                {
                    WriteCellElement(
                        writer,
                        new SpreadsheetXmlCell(rowIndex, cell.Col, cell.Cell, null, null, null, null),
                        styleIds);
                }

                cellRowIndex++;
            }

            writer.WriteEndElement();

            if (layoutRow == rowIndex)
                layoutRowIndex++;
        }
    }

    private static void WriteOrderedValueCellRowElements(
        XmlWriter writer,
        Sheet sheet,
        IReadOnlyDictionary<(uint Row, uint Col), Cell> cells,
        IReadOnlyList<uint> layoutRows,
        IReadOnlyDictionary<StyleId, string> styleIds)
    {
        using var cellEnumerator = cells.GetEnumerator();
        var hasCell = cellEnumerator.MoveNext();
        var layoutRowIndex = 0;
        while (hasCell || layoutRowIndex < layoutRows.Count)
        {
            while (hasCell && !IsValidCellAddress(cellEnumerator.Current.Key.Row, cellEnumerator.Current.Key.Col))
                hasCell = cellEnumerator.MoveNext();

            var cellRow = hasCell ? cellEnumerator.Current.Key.Row : uint.MaxValue;
            var layoutRow = layoutRowIndex < layoutRows.Count ? layoutRows[layoutRowIndex] : uint.MaxValue;
            var rowIndex = cellRow <= layoutRow ? cellRow : layoutRow;
            WriteRowStart(writer, sheet, rowIndex);

            while (hasCell && cellEnumerator.Current.Key.Row == rowIndex)
            {
                var (key, cell) = cellEnumerator.Current;
                if (IsValidCellAddress(key.Row, key.Col))
                {
                    WriteCellElement(
                        writer,
                        new SpreadsheetXmlCell(rowIndex, key.Col, cell, null, null, null, null),
                        styleIds);
                }

                hasCell = cellEnumerator.MoveNext();
            }

            writer.WriteEndElement();

            if (layoutRow == rowIndex)
                layoutRowIndex++;
        }
    }

    private static void WriteRowStart(XmlWriter writer, Sheet sheet, uint rowIndex)
    {
        WriteSpreadsheetStartElement(writer, "Row");
        WriteSpreadsheetAttribute(writer, SpreadsheetIndexAttribute, rowIndex);
        WriteRowHeightAttribute(writer, sheet, rowIndex);
        if (sheet.HiddenRows.Contains(rowIndex))
            WriteSpreadsheetAttribute(writer, SpreadsheetHiddenAttribute, "1");
    }

    private static void WriteRowHeightAttribute(XmlWriter writer, Sheet sheet, uint rowIndex)
    {
        if (sheet.RowHeights.TryGetValue(rowIndex, out var height) && IsPositiveFinite(height))
            WriteSpreadsheetAttribute(writer, SpreadsheetHeightAttribute, height.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void WriteCellElement(
        XmlWriter writer,
        SpreadsheetXmlCell cell,
        IReadOnlyDictionary<StyleId, string> styleIds)
    {
        WriteSpreadsheetStartElement(writer, "Cell");
        WriteSpreadsheetAttribute(writer, SpreadsheetIndexAttribute, cell.Col);
        if (styleIds.TryGetValue(cell.Cell.StyleId, out var styleName))
            WriteSpreadsheetAttribute(writer, SpreadsheetStyleIdAttribute, styleName);

        if (cell.MergeRange is { } mergeRange)
        {
            if (mergeRange.ColCount > 1)
                WriteSpreadsheetAttribute(writer, SpreadsheetMergeAcrossAttribute, mergeRange.ColCount - 1);
            if (mergeRange.RowCount > 1)
                WriteSpreadsheetAttribute(writer, SpreadsheetMergeDownAttribute, mergeRange.RowCount - 1);
        }

        if (cell.Cell.FormulaText is { Length: > 0 } formulaText)
            WriteSpreadsheetAttribute(writer, SpreadsheetFormulaAttribute, formulaText.StartsWith("=", StringComparison.Ordinal) ? formulaText : $"={formulaText}");

        if (!string.IsNullOrWhiteSpace(cell.HyperlinkTarget))
        {
            WriteSpreadsheetAttribute(writer, SpreadsheetHrefAttribute, cell.HyperlinkTarget);
            if (!string.IsNullOrWhiteSpace(cell.HyperlinkMetadata?.ScreenTip))
                WriteSpreadsheetAttribute(writer, SpreadsheetHrefScreenTipAttribute, cell.HyperlinkMetadata.ScreenTip);
        }

        if (cell.Cell.Value is not BlankValue)
            WriteDataElement(writer, cell.Cell.Value);

        if (!string.IsNullOrWhiteSpace(cell.Comment))
        {
            WriteSpreadsheetStartElement(writer, "Comment");
            WriteSpreadsheetAttribute(writer, SpreadsheetAuthorAttribute, "FreeX");
            WriteSpreadsheetTextElement(writer, "Data", cell.Comment);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteDataElement(XmlWriter writer, ScalarValue value)
    {
        var (type, text) = value switch
        {
            NumberValue number when double.IsFinite(number.Value) => ("Number", number.Value.ToString("R", CultureInfo.InvariantCulture)),
            NumberValue number => ("String", number.Value.ToString("R", CultureInfo.InvariantCulture)),
            DateTimeValue dateTime when TryFormatSpreadsheetDateTime(dateTime, out var formatted) => ("DateTime", formatted),
            DateTimeValue dateTime => ("String", dateTime.Value.ToString("R", CultureInfo.InvariantCulture)),
            BoolValue boolean => ("Boolean", boolean.Value ? "1" : "0"),
            ErrorValue error => ("Error", error.Code),
            TextValue textValue => ("String", textValue.Value),
            _ => ("String", "")
        };

        WriteSpreadsheetStartElement(writer, "Data");
        WriteSpreadsheetAttribute(writer, SpreadsheetTypeAttribute, type);
        writer.WriteString(text);
        writer.WriteEndElement();
    }

    private static void WriteSpreadsheetStartElement(XmlWriter writer, string localName) =>
        writer.WriteStartElement("ss", localName, SpreadsheetNs.NamespaceName);

    private static void WriteSpreadsheetTextElement(XmlWriter writer, string localName, string value)
    {
        WriteSpreadsheetStartElement(writer, localName);
        writer.WriteString(value);
        writer.WriteEndElement();
    }

    private static void WriteSpreadsheetAttribute(XmlWriter writer, XName name, uint value) =>
        WriteSpreadsheetAttribute(writer, name, value.ToString(CultureInfo.InvariantCulture));

    private static void WriteSpreadsheetAttribute(XmlWriter writer, XName name, string value) =>
        writer.WriteAttributeString("ss", name.LocalName, SpreadsheetNs.NamespaceName, value);

    private static void WriteExcelEmptyElement(XmlWriter writer, string localName)
    {
        writer.WriteStartElement("x", localName, ExcelNs.NamespaceName);
        writer.WriteEndElement();
    }

    private static void WriteExcelTextElement(XmlWriter writer, string localName, string value)
    {
        writer.WriteStartElement("x", localName, ExcelNs.NamespaceName);
        writer.WriteString(value);
        writer.WriteEndElement();
    }

    private static List<SpreadsheetXmlCell> BuildSortedXmlCells(Sheet sheet)
    {
        var mergeStarts = new Dictionary<(uint Row, uint Col), GridRange>(sheet.MergedRegions.Count);
        foreach (var region in sheet.MergedRegions)
        {
            if (IsValidGridRange(region))
                mergeStarts.TryAdd((region.Start.Row, region.Start.Col), region);
        }

        var cellCapacity = EstimateRichCellCapacity(sheet);
        var emitted = new HashSet<(uint Row, uint Col)>(cellCapacity);
        var cells = new List<SpreadsheetXmlCell>(cellCapacity);

        foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
        {
            if (!IsValidCellAddress(row, col))
                continue;

            var address = new CellAddress(sheet.Id, row, col);
            if (IsCoveredByMergeNonAnchor(sheet, address))
                continue;

            mergeStarts.TryGetValue((row, col), out var mergeRange);
            sheet.Hyperlinks.TryGetValue(address, out var hyperlinkTarget);
            sheet.HyperlinkMetadata.TryGetValue(address, out var hyperlinkMetadata);
            sheet.Comments.TryGetValue(address, out var comment);
            emitted.Add((row, col));
            cells.Add(new SpreadsheetXmlCell(row, col, cell, mergeRange, hyperlinkTarget, hyperlinkMetadata, comment));
        }

        foreach (var (address, hyperlinkTarget) in sheet.Hyperlinks)
        {
            if (!IsValidCellAddress(address.Row, address.Col) || emitted.Contains((address.Row, address.Col)))
                continue;

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

        foreach (var (address, comment) in sheet.Comments)
        {
            if (!IsValidCellAddress(address.Row, address.Col) || emitted.Contains((address.Row, address.Col)))
                continue;

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

        foreach (var (key, styleId) in sheet.GetStyleOnlyEntries())
        {
            if (!IsValidCellAddress(key.Row, key.Col) ||
                emitted.Contains((key.Row, key.Col)) ||
                styleId == StyleId.Default)
            {
                continue;
            }

            var address = new CellAddress(sheet.Id, key.Row, key.Col);
            if (IsCoveredByMergeNonAnchor(sheet, address))
                continue;

            mergeStarts.TryGetValue((key.Row, key.Col), out var mergeRange);
            emitted.Add((key.Row, key.Col));
            cells.Add(new SpreadsheetXmlCell(
                key.Row,
                key.Col,
                new Cell { StyleId = styleId },
                mergeRange,
                HyperlinkTarget: null,
                HyperlinkMetadata: null,
                Comment: null));
        }

        foreach (var mergeRange in sheet.MergedRegions)
        {
            if (!IsValidGridRange(mergeRange) ||
                emitted.Contains((mergeRange.Start.Row, mergeRange.Start.Col)))
            {
                continue;
            }

            cells.Add(new SpreadsheetXmlCell(
                mergeRange.Start.Row,
                mergeRange.Start.Col,
                Cell.FromValue(BlankValue.Instance),
                mergeRange,
                HyperlinkTarget: null,
                HyperlinkMetadata: null,
                Comment: null));
        }

        cells.Sort(static (left, right) =>
        {
            var rowComparison = left.Row.CompareTo(right.Row);
            return rowComparison != 0 ? rowComparison : left.Col.CompareTo(right.Col);
        });
        return cells;
    }

    private static int EstimateRichCellCapacity(Sheet sheet)
    {
        var capacity = sheet.CellCount;
        capacity = AddClamped(capacity, sheet.Hyperlinks.Count);
        capacity = AddClamped(capacity, sheet.Comments.Count);
        capacity = AddClamped(capacity, sheet.MergedRegions.Count);
        return capacity;
    }

    private static int AddClamped(int value, int add) =>
        value > int.MaxValue - add ? int.MaxValue : value + add;

    private static bool IsCoveredByMergeNonAnchor(Sheet sheet, CellAddress address) =>
        sheet.GetMergeRegion(address) is { } mergeRange &&
        (mergeRange.Start.Row != address.Row || mergeRange.Start.Col != address.Col);

    private static bool IsValidCellAddress(uint row, uint column) =>
        row is >= 1 and <= CellAddress.MaxRow &&
        column is >= 1 and <= CellAddress.MaxCol;

    private static bool IsValidGridRange(GridRange range) =>
        range.Start.Sheet == range.End.Sheet &&
        IsValidCellAddress(range.Start.Row, range.Start.Col) &&
        IsValidCellAddress(range.End.Row, range.End.Col);

    private static bool CanStreamValueCellRows(Sheet sheet) =>
        sheet.MergedRegions.Count == 0 &&
        sheet.Hyperlinks.Count == 0 &&
        sheet.HyperlinkMetadata.Count == 0 &&
        sheet.Comments.Count == 0 &&
        !sheet.HasStyleOnlyCells;

    private static bool IsRowColumnOrdered(IReadOnlyDictionary<(uint Row, uint Col), Cell> cells)
    {
        var hasPrevious = false;
        var previousRow = 0u;
        var previousCol = 0u;
        foreach (var ((row, col), _) in cells)
        {
            if (hasPrevious && (row < previousRow || (row == previousRow && col < previousCol)))
                return false;

            hasPrevious = true;
            previousRow = row;
            previousCol = col;
        }

        return true;
    }

    private static Dictionary<uint, List<SpreadsheetXmlValueCell>> BuildValueCellsByRow(
        IReadOnlyDictionary<(uint Row, uint Col), Cell> cells)
    {
        var rowCounts = new Dictionary<uint, int>(Math.Min(cells.Count, 1024));
        foreach (var ((row, col), _) in cells)
        {
            if (!IsValidCellAddress(row, col))
                continue;

            if (!rowCounts.TryAdd(row, 1))
                rowCounts[row]++;
        }

        var cellsByRow = new Dictionary<uint, List<SpreadsheetXmlValueCell>>(rowCounts.Count);
        foreach (var (row, count) in rowCounts)
            cellsByRow.Add(row, new List<SpreadsheetXmlValueCell>(count));

        foreach (var ((row, col), cell) in cells)
        {
            if (IsValidCellAddress(row, col))
                cellsByRow[row].Add(new SpreadsheetXmlValueCell(col, cell));
        }

        foreach (var rowCells in cellsByRow.Values)
            rowCells.Sort(static (left, right) => left.Col.CompareTo(right.Col));

        return cellsByRow;
    }

    private static List<uint> BuildSortedRowLayoutIndexes(Sheet sheet)
    {
        if (sheet.RowHeights.Count == 0 && sheet.HiddenRows.Count == 0)
            return [];

        var rows = new List<uint>(sheet.RowHeights.Count + sheet.HiddenRows.Count);
        foreach (var row in sheet.RowHeights.Keys)
        {
            if (IsValidRowLayoutIndex(row))
                rows.Add(row);
        }

        foreach (var row in sheet.HiddenRows)
        {
            if (IsValidRowLayoutIndex(row))
                rows.Add(row);
        }

        rows.Sort();
        var writeIndex = 0;
        for (var readIndex = 0; readIndex < rows.Count; readIndex++)
        {
            if (readIndex > 0 && rows[readIndex] == rows[readIndex - 1])
                continue;

            rows[writeIndex++] = rows[readIndex];
        }

        if (writeIndex < rows.Count)
            rows.RemoveRange(writeIndex, rows.Count - writeIndex);

        return rows;
    }

    private static bool IsValidRowLayoutIndex(uint rowIndex) =>
        rowIndex is >= 1 and <= CellAddress.MaxRow;

    private static bool IsValidColumnLayoutIndex(uint columnIndex) =>
        columnIndex is >= 1 and <= CellAddress.MaxCol;

    private static bool IsPositiveFinite(double value) =>
        value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool TryParseNamedRangeRefersTo(Workbook workbook, string? refersTo, out GridRange range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(refersTo))
            return false;

        var text = refersTo.Trim();
        if (text.StartsWith("=", StringComparison.Ordinal))
            text = text[1..].Trim();

        if (!TrySplitSheetQualifiedReference(text, out var sheetName, out var rangeText))
            return false;

        var sheet = workbook.GetSheet(sheetName);
        if (sheet is null)
            return false;

        var parts = rangeText.Split(':');
        if (parts.Length is < 1 or > 2)
            return false;

        if (!TryParseA1Part(parts[0], sheet.Id, out var start))
            return false;

        var endText = parts.Length == 2 ? parts[1] : parts[0];
        if (!TryParseA1Part(endText, sheet.Id, out var end))
            return false;

        range = new GridRange(start, end);
        return true;
    }

    private static bool TrySplitSheetQualifiedReference(string text, out string sheetName, out string rangeText)
    {
        sheetName = "";
        rangeText = "";
        if (text.Length == 0)
            return false;

        if (text[0] == '\'')
        {
            var builder = new StringBuilder();
            for (var index = 1; index < text.Length; index++)
            {
                if (text[index] != '\'')
                {
                    builder.Append(text[index]);
                    continue;
                }

                if (index + 1 < text.Length && text[index + 1] == '\'')
                {
                    builder.Append('\'');
                    index++;
                    continue;
                }

                if (index + 1 >= text.Length || text[index + 1] != '!')
                    return false;

                sheetName = builder.ToString();
                rangeText = text[(index + 2)..].Trim();
                return rangeText.Length > 0;
            }

            return false;
        }

        var separator = text.IndexOf('!', StringComparison.Ordinal);
        if (separator <= 0 || separator == text.Length - 1)
            return false;

        sheetName = text[..separator].Trim();
        rangeText = text[(separator + 1)..].Trim();
        return sheetName.Length > 0 && rangeText.Length > 0;
    }

    private static bool TryParseA1Part(string text, SheetId sheetId, out CellAddress address)
    {
        var normalized = text.Trim().Replace("$", "", StringComparison.Ordinal);
        return CellAddress.TryParse(normalized, sheetId, out address);
    }

    private static string FormatNamedRangeRefersTo(string sheetName, GridRange range)
    {
        var reference = range.Start == range.End
            ? range.Start.ToA1()
            : $"{range.Start.ToA1()}:{range.End.ToA1()}";
        return $"={QuoteSheetName(sheetName)}!{reference}";
    }

    private static string QuoteSheetName(string sheetName) =>
        sheetName.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_')
            ? $"'{sheetName.Replace("'", "''", StringComparison.Ordinal)}'"
            : sheetName;

    private static bool TryFormatSpreadsheetDateTime(DateTimeValue value, out string text)
    {
        text = "";
        if (!double.IsFinite(value.Value))
            return false;

        try
        {
            text = value.ToDateTime().ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
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

    private readonly record struct SpreadsheetXmlValueCell(uint Col, Cell Cell);
}
