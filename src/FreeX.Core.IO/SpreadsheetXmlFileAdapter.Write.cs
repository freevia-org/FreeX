using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class SpreadsheetXmlFileAdapter
{
    [ThreadStatic]
    private static char[]? spreadsheetMlFormatBuffer;

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
        {
            var a1 = formulaText.StartsWith("=", StringComparison.Ordinal) ? formulaText[1..] : formulaText;
            // Excel expects SpreadsheetML formulas in R1C1; convert from the model's A1 form.
            var r1c1 = ConvertA1FormulaToR1C1(a1, cell.Row, cell.Col);
            WriteSpreadsheetAttribute(writer, SpreadsheetFormulaAttribute, $"={r1c1}");
        }

        if (BuildHyperlinkHref(cell.HyperlinkTarget, cell.HyperlinkMetadata) is { } href)
        {
            WriteSpreadsheetAttribute(writer, SpreadsheetHrefAttribute, href);
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
        WriteSpreadsheetStartElement(writer, "Data");
        switch (value)
        {
            case NumberValue number when double.IsFinite(number.Value):
                WriteSpreadsheetAttribute(writer, SpreadsheetTypeAttribute, "Number");
                WriteRoundTripDoubleText(writer, number.Value);
                break;
            case NumberValue number:
                WriteSpreadsheetAttribute(writer, SpreadsheetTypeAttribute, "String");
                WriteRoundTripDoubleText(writer, number.Value);
                break;
            case DateTimeValue dateTime when TryFormatSpreadsheetDateTime(dateTime, out var formatted):
                WriteSpreadsheetAttribute(writer, SpreadsheetTypeAttribute, "DateTime");
                writer.WriteString(formatted);
                break;
            case DateTimeValue dateTime:
                WriteSpreadsheetAttribute(writer, SpreadsheetTypeAttribute, "String");
                WriteRoundTripDoubleText(writer, dateTime.Value);
                break;
            case BoolValue boolean:
                WriteSpreadsheetAttribute(writer, SpreadsheetTypeAttribute, "Boolean");
                writer.WriteString(boolean.Value ? "1" : "0");
                break;
            case ErrorValue error:
                WriteSpreadsheetAttribute(writer, SpreadsheetTypeAttribute, "Error");
                writer.WriteString(error.Code);
                break;
            case TextValue textValue:
                WriteSpreadsheetAttribute(writer, SpreadsheetTypeAttribute, "String");
                writer.WriteString(OoxmlXmlText.Sanitize(textValue.Value));
                break;
            default:
                WriteSpreadsheetAttribute(writer, SpreadsheetTypeAttribute, "String");
                break;
        }

        writer.WriteEndElement();
    }

    private static void WriteSpreadsheetStartElement(XmlWriter writer, string localName) =>
        writer.WriteStartElement("ss", localName, SpreadsheetNs.NamespaceName);

    // This writer streams straight to an XmlWriter rather than building an XDocument, so it has no
    // package boundary to sanitize at: model text is sanitized where it enters the stream instead.
    // XmlWriter validates on write, so one C0 control code or lone surrogate anywhere in a cell value,
    // comment, sheet name or formula would abort the whole save with no file written.
    private static void WriteSpreadsheetTextElement(XmlWriter writer, string localName, string value)
    {
        WriteSpreadsheetStartElement(writer, localName);
        writer.WriteString(OoxmlXmlText.Sanitize(value));
        writer.WriteEndElement();
    }

    private static void WriteRoundTripDoubleText(XmlWriter writer, double value)
    {
        var buffer = GetSpreadsheetMlFormatBuffer(32);
        if (value.TryFormat(buffer.AsSpan(), out var charsWritten, "R", CultureInfo.InvariantCulture))
        {
            writer.WriteChars(buffer, 0, charsWritten);
            return;
        }

        writer.WriteString(value.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void WriteSpreadsheetAttribute(XmlWriter writer, XName name, uint value)
    {
        var buffer = GetSpreadsheetMlFormatBuffer(10);
        if (value.TryFormat(buffer.AsSpan(), out var charsWritten, provider: CultureInfo.InvariantCulture))
        {
            writer.WriteStartAttribute("ss", name.LocalName, SpreadsheetNs.NamespaceName);
            writer.WriteChars(buffer, 0, charsWritten);
            writer.WriteEndAttribute();
            return;
        }

        WriteSpreadsheetAttribute(writer, name, value.ToString(CultureInfo.InvariantCulture));
    }

    private static char[] GetSpreadsheetMlFormatBuffer(int length)
    {
        var buffer = spreadsheetMlFormatBuffer;
        if (buffer is null || buffer.Length < length)
        {
            buffer = new char[length];
            spreadsheetMlFormatBuffer = buffer;
        }

        return buffer;
    }

    private static void WriteSpreadsheetAttribute(XmlWriter writer, XName name, string value) =>
        writer.WriteAttributeString("ss", name.LocalName, SpreadsheetNs.NamespaceName, OoxmlXmlText.Sanitize(value));

    private static void WriteExcelEmptyElement(XmlWriter writer, string localName)
    {
        writer.WriteStartElement("x", localName, ExcelNs.NamespaceName);
        writer.WriteEndElement();
    }

    private static void WriteExcelTextElement(XmlWriter writer, string localName, string value)
    {
        writer.WriteStartElement("x", localName, ExcelNs.NamespaceName);
        writer.WriteString(OoxmlXmlText.Sanitize(value));
        writer.WriteEndElement();
    }

}
