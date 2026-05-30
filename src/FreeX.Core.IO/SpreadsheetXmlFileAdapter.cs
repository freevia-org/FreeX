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
    private static readonly XName SpreadsheetStyleIdAttribute = SpreadsheetNs + "StyleID";
    private static readonly XName SpreadsheetFormatAttribute = SpreadsheetNs + "Format";
    private static readonly XName SpreadsheetHrefAttribute = SpreadsheetNs + "HRef";
    private static readonly XName SpreadsheetHrefScreenTipAttribute = SpreadsheetNs + "HRefScreenTip";
    private static readonly XName SpreadsheetAuthorAttribute = SpreadsheetNs + "Author";
    private static readonly XName SpreadsheetVisibleAttribute = SpreadsheetNs + "Visible";
    private static readonly XName SpreadsheetHeightAttribute = SpreadsheetNs + "Height";
    private static readonly XName SpreadsheetWidthAttribute = SpreadsheetNs + "Width";
    private static readonly XName SpreadsheetHiddenAttribute = SpreadsheetNs + "Hidden";

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

        return workbook;
    }

    public void Save(Workbook workbook, Stream stream)
    {
        var styleIds = CreateNumberFormatStyleIds(workbook);
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XProcessingInstruction("mso-application", "progid=\"Excel.Sheet\""),
            new XElement(
                SpreadsheetNs + "Workbook",
                new XAttribute(XNamespace.Xmlns + "ss", SpreadsheetNs),
                new XAttribute(XNamespace.Xmlns + "o", OfficeNs),
                new XAttribute(XNamespace.Xmlns + "x", ExcelNs),
                ToStylesElement(workbook, styleIds),
                workbook.Sheets.Select(sheet => ToWorksheetElement(sheet, styleIds))));

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

    private static void ReadWorksheetOptions(Sheet sheet, XElement worksheetElement)
    {
        var optionsElement = worksheetElement.Element(ExcelNs + "WorksheetOptions");
        if (optionsElement is null)
            return;

        sheet.ShowGridlines = optionsElement.Element(ExcelNs + "DoNotDisplayGridlines") is null;
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

        foreach (var styleElement in stylesElement.Elements(SpreadsheetNs + "Style"))
        {
            var id = styleElement.Attribute(SpreadsheetIdAttribute)?.Value;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var numberFormat = styleElement
                .Element(SpreadsheetNs + "NumberFormat")
                ?.Attribute(SpreadsheetFormatAttribute)
                ?.Value;
            if (string.IsNullOrWhiteSpace(numberFormat))
                continue;

            styles[id] = workbook.RegisterStyle(new CellStyle { NumberFormat = numberFormat });
        }

        return styles;
    }

    private static void ReadWorksheet(Sheet sheet, XElement worksheetElement, IReadOnlyDictionary<string, StyleId> styles)
    {
        var tableElement = worksheetElement.Element(SpreadsheetNs + "Table");
        if (tableElement is null)
            return;

        ReadColumns(sheet, tableElement);

        var rowIndex = 1u;
        foreach (var rowElement in tableElement.Elements(SpreadsheetNs + "Row"))
        {
            rowIndex = ReadIndex(rowElement, rowIndex);
            if (rowIndex > CellAddress.MaxRow)
                break;

            ReadRowLayout(sheet, rowElement, rowIndex);

            var columnIndex = 1u;
            foreach (var cellElement in rowElement.Elements(SpreadsheetNs + "Cell"))
            {
                columnIndex = ReadIndex(cellElement, columnIndex);
                if (columnIndex > CellAddress.MaxCol)
                    break;

                var address = new CellAddress(sheet.Id, rowIndex, columnIndex);
                var cell = ReadCell(cellElement, styles);
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

            rowIndex++;
        }
    }

    private static void ReadColumns(Sheet sheet, XElement tableElement)
    {
        var columnIndex = 1u;
        foreach (var columnElement in tableElement.Elements(SpreadsheetNs + "Column"))
        {
            columnIndex = ReadIndex(columnElement, columnIndex);
            if (columnIndex > CellAddress.MaxCol)
                break;

            var span = ReadSpan(columnElement);
            var lastColumnIndex = span > CellAddress.MaxCol - columnIndex
                ? CellAddress.MaxCol
                : columnIndex + span;
            for (var currentColumnIndex = columnIndex; currentColumnIndex <= lastColumnIndex; currentColumnIndex++)
                ReadColumnLayout(sheet, columnElement, currentColumnIndex);

            columnIndex = lastColumnIndex + 1;
        }
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

    private static Cell ReadCell(XElement cellElement, IReadOnlyDictionary<string, StyleId> styles)
    {
        var value = ReadValue(cellElement.Element(SpreadsheetNs + "Data"));
        var formula = cellElement.Attribute(SpreadsheetFormulaAttribute)?.Value;
        var styleId = ReadStyleId(cellElement, styles);
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

    private static uint ReadSpan(XElement element)
    {
        var text = element.Attribute(SpreadsheetSpanAttribute)?.Value;
        return uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0u;
    }

    private static uint ReadPaneSplit(XElement element, XName elementName, uint maxValue)
    {
        var text = element.Element(elementName)?.Value;
        return uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value <= maxValue
            ? value
            : 0u;
    }

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

    private static XElement? ToStylesElement(Workbook workbook, IReadOnlyDictionary<StyleId, string> styleIds)
    {
        if (styleIds.Count == 0)
            return null;

        return new XElement(
            SpreadsheetNs + "Styles",
            styleIds.Select(entry => new XElement(
                SpreadsheetNs + "Style",
                new XAttribute(SpreadsheetIdAttribute, entry.Value),
                new XElement(
                    SpreadsheetNs + "NumberFormat",
                    new XAttribute(SpreadsheetFormatAttribute, workbook.GetStyle(entry.Key).NumberFormat)))));
    }

    private static XElement ToWorksheetElement(Sheet sheet, IReadOnlyDictionary<StyleId, string> styleIds) =>
        new(
            SpreadsheetNs + "Worksheet",
            new XAttribute(SpreadsheetNameAttribute, sheet.Name),
            ToWorksheetVisibilityAttribute(sheet),
            new XElement(
                SpreadsheetNs + "Table",
                ToTableElements(sheet, styleIds)),
            ToWorksheetOptionsElement(sheet));

    private static XElement? ToWorksheetOptionsElement(Sheet sheet)
    {
        var frozenRows = sheet.FrozenRows is > 0 and <= CellAddress.MaxRow ? sheet.FrozenRows : 0;
        var frozenCols = sheet.FrozenCols is > 0 and <= CellAddress.MaxCol ? sheet.FrozenCols : 0;
        if (sheet.ShowGridlines && frozenRows == 0 && frozenCols == 0)
            return null;

        return new XElement(
            ExcelNs + "WorksheetOptions",
            sheet.ShowGridlines ? null : new XElement(ExcelNs + "DoNotDisplayGridlines"),
            frozenRows > 0 || frozenCols > 0
                ? new object?[]
                {
                    new XElement(ExcelNs + "FreezePanes"),
                    new XElement(ExcelNs + "FrozenNoSplit"),
                    frozenRows > 0 ? new XElement(ExcelNs + "SplitHorizontal", frozenRows.ToString(CultureInfo.InvariantCulture)) : null,
                    frozenRows > 0 ? new XElement(ExcelNs + "TopRowBottomPane", frozenRows.ToString(CultureInfo.InvariantCulture)) : null,
                    frozenCols > 0 ? new XElement(ExcelNs + "SplitVertical", frozenCols.ToString(CultureInfo.InvariantCulture)) : null,
                    frozenCols > 0 ? new XElement(ExcelNs + "LeftColumnRightPane", frozenCols.ToString(CultureInfo.InvariantCulture)) : null
                }
                : null);
    }

    private static IEnumerable<XElement> ToTableElements(Sheet sheet, IReadOnlyDictionary<StyleId, string> styleIds) =>
        ToColumnElements(sheet).Concat(ToRowElements(sheet, styleIds));

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

        foreach (var (key, styleId) in sheet.GetStyleOnlyEntries()
                     .Where(entry => !emitted.Contains((entry.Key.Row, entry.Key.Col)) && entry.StyleId != StyleId.Default)
                     .OrderBy(entry => entry.Key.Row)
                     .ThenBy(entry => entry.Key.Col))
        {
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

    private static IEnumerable<XElement> ToColumnElements(Sheet sheet)
    {
        var columnIndexes = sheet.ColumnWidths.Keys
            .Where(IsValidColumnLayoutIndex)
            .Concat(sheet.HiddenCols.Where(IsValidColumnLayoutIndex))
            .Distinct()
            .OrderBy(column => column);

        foreach (var columnIndex in columnIndexes)
        {
            yield return new XElement(
                SpreadsheetNs + "Column",
                new XAttribute(SpreadsheetIndexAttribute, columnIndex),
                ToColumnWidthAttribute(sheet, columnIndex),
                sheet.HiddenCols.Contains(columnIndex) ? new XAttribute(SpreadsheetHiddenAttribute, "1") : null);
        }
    }

    private static XAttribute? ToColumnWidthAttribute(Sheet sheet, uint columnIndex) =>
        sheet.ColumnWidths.TryGetValue(columnIndex, out var width) && IsPositiveFinite(width)
            ? new XAttribute(SpreadsheetWidthAttribute, width.ToString("R", CultureInfo.InvariantCulture))
            : null;

    private static IEnumerable<XElement> ToRowElements(Sheet sheet, IReadOnlyDictionary<StyleId, string> styleIds)
    {
        var cellsByRow = EnumerateXmlCells(sheet)
            .GroupBy(entry => entry.Row)
            .ToDictionary(group => group.Key, group => group.OrderBy(cell => cell.Col).ToList());

        var rowIndexes = cellsByRow.Keys
            .Concat(sheet.RowHeights.Keys.Where(IsValidRowLayoutIndex))
            .Concat(sheet.HiddenRows.Where(IsValidRowLayoutIndex))
            .Distinct()
            .OrderBy(row => row);

        foreach (var rowIndex in rowIndexes)
            yield return ToRowElement(sheet, rowIndex, cellsByRow.GetValueOrDefault(rowIndex) ?? [], styleIds);
    }

    private static XElement ToRowElement(
        Sheet sheet,
        uint rowIndex,
        IEnumerable<SpreadsheetXmlCell> cells,
        IReadOnlyDictionary<StyleId, string> styleIds)
    {
        var rowElement = new XElement(
            SpreadsheetNs + "Row",
            new XAttribute(SpreadsheetIndexAttribute, rowIndex),
            ToRowHeightAttribute(sheet, rowIndex),
            sheet.HiddenRows.Contains(rowIndex) ? new XAttribute(SpreadsheetHiddenAttribute, "1") : null);

        foreach (var cell in cells)
            rowElement.Add(ToCellElement(cell, styleIds));

        return rowElement;
    }

    private static XAttribute? ToRowHeightAttribute(Sheet sheet, uint rowIndex) =>
        sheet.RowHeights.TryGetValue(rowIndex, out var height) && IsPositiveFinite(height)
            ? new XAttribute(SpreadsheetHeightAttribute, height.ToString("R", CultureInfo.InvariantCulture))
            : null;

    private static bool IsValidRowLayoutIndex(uint rowIndex) =>
        rowIndex is >= 1 and <= CellAddress.MaxRow;

    private static bool IsValidColumnLayoutIndex(uint columnIndex) =>
        columnIndex is >= 1 and <= CellAddress.MaxCol;

    private static bool IsPositiveFinite(double value) =>
        value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);

    private static XElement ToCellElement(SpreadsheetXmlCell cell, IReadOnlyDictionary<StyleId, string> styleIds)
    {
        var element = new XElement(SpreadsheetNs + "Cell", new XAttribute(SpreadsheetIndexAttribute, cell.Col));
        if (styleIds.TryGetValue(cell.Cell.StyleId, out var styleName))
            element.SetAttributeValue(SpreadsheetStyleIdAttribute, styleName);

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
