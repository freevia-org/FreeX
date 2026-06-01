using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetRowColumnLayoutReader
{
    public static XlsxWorksheetRowColumnLayout Read(XDocument worksheetXml, XNamespace worksheetNs)
        => ReadSheetDataLayout(worksheetXml, worksheetNs).RowColumnLayout;

    public static XlsxWorksheetSheetDataLayout ReadSheetDataLayout(XDocument worksheetXml, XNamespace worksheetNs)
    {
        var hiddenRows = new HashSet<uint>();
        var hiddenCols = new HashSet<uint>();
        var rowOutlineLevels = new Dictionary<uint, int>();
        var colOutlineLevels = new Dictionary<uint, int>();
        var groupHiddenRows = new HashSet<uint>();
        var groupHiddenCols = new HashSet<uint>();
        var rowHeights = new Dictionary<uint, double>();
        var columnWidths = new Dictionary<uint, double>();
        var explicitStyleOnlyCells = new List<(uint Row, uint Col, int StyleIndex)>();
        var cachedFormulaErrors = new Dictionary<(uint Row, uint Col), ErrorValue>();
        var hasStyleOnlyCells = false;

        var root = worksheetXml.Root;
        if (root is not null)
        {
            var rowName = worksheetNs + "row";
            var cellName = worksheetNs + "c";
            var formulaName = worksheetNs + "f";
            var valueName = worksheetNs + "v";
            var inlineStringName = worksheetNs + "is";
            foreach (var row in root.Element(worksheetNs + "sheetData")?.Elements(rowName) ?? [])
            {
                if (uint.TryParse(row.Attribute("r")?.Value, out var rowNumber))
                    ReadRowLayout(row, rowNumber, hiddenRows, rowOutlineLevels, groupHiddenRows, rowHeights);

                foreach (var cell in row.Elements(cellName))
                {
                    hasStyleOnlyCells |= XlsxWorksheetCellLayoutReader.ReadCell(
                        cell,
                        formulaName,
                        valueName,
                        inlineStringName,
                        explicitStyleOnlyCells,
                        cachedFormulaErrors);
                }
            }

            foreach (var cols in root.Elements(worksheetNs + "cols"))
            {
                foreach (var col in cols.Elements(worksheetNs + "col"))
                    ReadColumnLayout(col, hiddenCols, colOutlineLevels, groupHiddenCols, columnWidths);
            }
        }

        return new XlsxWorksheetSheetDataLayout(
            new XlsxWorksheetRowColumnLayout(
                hiddenRows,
                hiddenCols,
                rowOutlineLevels,
                colOutlineLevels,
                groupHiddenRows,
                groupHiddenCols,
                rowHeights,
                columnWidths),
            new XlsxWorksheetCellLayout(cachedFormulaErrors, explicitStyleOnlyCells, hasStyleOnlyCells));
    }

    private static void ReadRowLayout(
        XElement row,
        uint rowNumber,
        HashSet<uint> hiddenRows,
        Dictionary<uint, int> rowOutlineLevels,
        HashSet<uint> groupHiddenRows,
        Dictionary<uint, double> rowHeights)
    {
        if (XlsxWorksheetXmlValueParser.IsTruthy(row.Attribute("hidden")?.Value))
            hiddenRows.Add(rowNumber);

        if (ParseOptionalDouble(row.Attribute("ht")?.Value) is { } heightPoints && heightPoints > 0)
            rowHeights[rowNumber] = heightPoints * (96.0 / 72.0);

        var outlineStr = row.Attribute("outlineLevel")?.Value;
        if (int.TryParse(outlineStr, out var outlineLevel) && outlineLevel > 0)
        {
            rowOutlineLevels[rowNumber] = outlineLevel;
            if (XlsxWorksheetXmlValueParser.IsTruthy(row.Attribute("collapsed")?.Value))
                groupHiddenRows.Add(rowNumber);
        }
    }

    private static void ReadColumnLayout(
        XElement col,
        HashSet<uint> hiddenCols,
        Dictionary<uint, int> colOutlineLevels,
        HashSet<uint> groupHiddenCols,
        Dictionary<uint, double> columnWidths)
    {
        if (!uint.TryParse(col.Attribute("min")?.Value, out var min))
            return;
        if (!uint.TryParse(col.Attribute("max")?.Value, out var max))
            return;
        if (min > max)
            return;

        if (XlsxWorksheetXmlValueParser.IsTruthy(col.Attribute("hidden")?.Value))
        {
            for (var colNumber = min; colNumber <= max; colNumber++)
                hiddenCols.Add(colNumber);
        }

        var colOutlineStr = col.Attribute("outlineLevel")?.Value;
        if (int.TryParse(colOutlineStr, out var colOutlineLevel) && colOutlineLevel > 0)
        {
            var collapsed = XlsxWorksheetXmlValueParser.IsTruthy(col.Attribute("collapsed")?.Value);
            for (var colNumber = min; colNumber <= max; colNumber++)
            {
                colOutlineLevels[colNumber] = colOutlineLevel;
                if (collapsed)
                    groupHiddenCols.Add(colNumber);
            }
        }

        if (XlsxWorksheetXmlValueParser.IsTruthy(col.Attribute("customWidth")?.Value) &&
            ParseOptionalDouble(col.Attribute("width")?.Value) is { } width &&
            width > 0)
        {
            if (col.Attribute("style") is not null && width <= 9.2)
                return;

            width = Math.Floor(width);
            for (var colNumber = min; colNumber <= max; colNumber++)
                columnWidths[colNumber] = width;
        }
    }

    private static double? ParseOptionalDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
        double.IsFinite(parsed) &&
        parsed > 0
            ? parsed
            : null;
}

internal sealed record XlsxWorksheetSheetDataLayout(
    XlsxWorksheetRowColumnLayout RowColumnLayout,
    XlsxWorksheetCellLayout CellLayout);

internal sealed record XlsxWorksheetRowColumnLayout(
    HashSet<uint> HiddenRows,
    HashSet<uint> HiddenCols,
    Dictionary<uint, int> RowOutlineLevels,
    Dictionary<uint, int> ColOutlineLevels,
    HashSet<uint> GroupHiddenRows,
    HashSet<uint> GroupHiddenCols,
    Dictionary<uint, double> RowHeights,
    Dictionary<uint, double> ColumnWidths);
