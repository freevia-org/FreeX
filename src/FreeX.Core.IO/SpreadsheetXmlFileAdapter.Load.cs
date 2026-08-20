using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class SpreadsheetXmlFileAdapter
{
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

    /// <summary>
    /// Reads a worksheet's rows/cells, returning whether the row loop or the column loop hit this
    /// sheet's grid limit. Excel's own SpreadsheetML writer represents sparse rows with an explicit
    /// <c>ss:Index</c> jump (e.g. row 5 followed directly by row 2,000,000) rather than emitting every
    /// row in between, so a break here can skip straight past the limit without ever writing a cell
    /// at the boundary -- the caller can no longer infer truncation from the loaded sheet's used
    /// range afterwards the way WorkbookOpenService.DetectGridLimitTruncationWarnings does for
    /// readers that stop exactly at the boundary. Reporting it here, at the break, is what those
    /// jumps need.
    /// </summary>
    private static (bool RowLimitExceeded, bool ColLimitExceeded) ReadWorksheet(
        Sheet sheet, XElement worksheetElement, IReadOnlyDictionary<string, StyleId> styles)
    {
        var tableElement = worksheetElement.Element(SpreadsheetNs + "Table");
        if (tableElement is null)
            return (false, false);

        var columnStyles = ReadColumns(sheet, tableElement, styles);

        var rowLimitExceeded = false;
        var colLimitExceeded = false;

        var rowIndex = 1u;
        foreach (var rowElement in tableElement.Elements(SpreadsheetNs + "Row"))
        {
            rowIndex = ReadIndex(rowElement, rowIndex);
            if (rowIndex > CellAddress.MaxRow)
            {
                rowLimitExceeded = true;
                break;
            }

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
                {
                    colLimitExceeded = true;
                    break;
                }

                var address = new CellAddress(sheet.Id, rowIndex, columnIndex);
                columnStyles.TryGetValue(columnIndex, out var columnStyleId);
                var cell = ReadCell(cellElement, styles, rowIndex, columnIndex, rowStyleId, columnStyleId);
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

        return (rowLimitExceeded, colLimitExceeded);
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
        uint row,
        uint column,
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

        var formulaText = formula.StartsWith("=", StringComparison.Ordinal) ? formula[1..] : formula;
        // Excel saves SpreadsheetML formulas in R1C1; convert to the A1 the model expects. A1 formulas
        // (e.g. from a FreeX-authored file) are left untouched.
        if (LooksLikeR1C1(formulaText))
            formulaText = ConvertR1C1FormulaToA1(formulaText, row, column);

        return new Cell
        {
            FormulaText = formulaText,
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
}
