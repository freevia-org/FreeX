using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetRowColumnLayoutReader
{
    public static XlsxWorksheetRowColumnLayout Read(XDocument worksheetXml, XNamespace worksheetNs)
    {
        var hiddenRows = new HashSet<uint>();
        var hiddenCols = new HashSet<uint>();
        var rowOutlineLevels = new Dictionary<uint, int>();
        var colOutlineLevels = new Dictionary<uint, int>();
        var groupHiddenRows = new HashSet<uint>();
        var groupHiddenCols = new HashSet<uint>();
        var rowHeights = new Dictionary<uint, double>();
        var columnWidths = new Dictionary<uint, double>();

        foreach (var row in worksheetXml.Descendants(worksheetNs + "row"))
        {
            if (!uint.TryParse(row.Attribute("r")?.Value, out var rowNumber))
                continue;

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

        foreach (var col in worksheetXml.Descendants(worksheetNs + "col"))
        {
            if (!uint.TryParse(col.Attribute("min")?.Value, out var min))
                continue;
            if (!uint.TryParse(col.Attribute("max")?.Value, out var max))
                continue;
            if (min > max)
                continue;

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
                    continue;

                width = Math.Floor(width);
                for (var colNumber = min; colNumber <= max; colNumber++)
                    columnWidths[colNumber] = width;
            }
        }

        return new XlsxWorksheetRowColumnLayout(
            hiddenRows,
            hiddenCols,
            rowOutlineLevels,
            colOutlineLevels,
            groupHiddenRows,
            groupHiddenCols,
            rowHeights,
            columnWidths);
    }

    private static double? ParseOptionalDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
        double.IsFinite(parsed) &&
        parsed > 0
            ? parsed
            : null;
}

internal sealed record XlsxWorksheetRowColumnLayout(
    HashSet<uint> HiddenRows,
    HashSet<uint> HiddenCols,
    Dictionary<uint, int> RowOutlineLevels,
    Dictionary<uint, int> ColOutlineLevels,
    HashSet<uint> GroupHiddenRows,
    HashSet<uint> GroupHiddenCols,
    Dictionary<uint, double> RowHeights,
    Dictionary<uint, double> ColumnWidths);
