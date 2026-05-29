using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetCellLayoutReader
{
    private static readonly SheetId ParseOnlySheetId = default;

    public static IReadOnlyList<(uint Row, uint Col, int StyleIndex)> ReadExplicitStyleOnlyCells(
        XDocument worksheetXml,
        XNamespace worksheetNs)
    {
        var result = new List<(uint Row, uint Col, int StyleIndex)>();

        foreach (var cell in worksheetXml.Descendants(worksheetNs + "c"))
        {
            if (!int.TryParse(cell.Attribute("s")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var styleIndex) ||
                cell.Element(worksheetNs + "f") is not null ||
                cell.Element(worksheetNs + "v") is not null ||
                cell.Element(worksheetNs + "is") is not null)
            {
                continue;
            }

            var reference = cell.Attribute("r")?.Value;
            if (string.IsNullOrWhiteSpace(reference) || !CellAddress.TryParse(reference, ParseOnlySheetId, out var address))
                continue;

            result.Add((address.Row, address.Col, styleIndex));
        }

        return result;
    }

    public static Dictionary<(uint Row, uint Col), ErrorValue> ReadCachedFormulaErrors(
        XDocument worksheetXml,
        XNamespace worksheetNs)
    {
        var result = new Dictionary<(uint Row, uint Col), ErrorValue>();

        foreach (var cell in worksheetXml.Descendants(worksheetNs + "c"))
        {
            if (!string.Equals(cell.Attribute("t")?.Value, "e", StringComparison.OrdinalIgnoreCase))
                continue;
            if (cell.Element(worksheetNs + "f") is null)
                continue;
            var rawValue = cell.Element(worksheetNs + "v")?.Value;
            if (string.IsNullOrWhiteSpace(rawValue))
                continue;
            var reference = cell.Attribute("r")?.Value;
            if (string.IsNullOrWhiteSpace(reference) || !CellAddress.TryParse(reference, ParseOnlySheetId, out var address))
                continue;

            result[(address.Row, address.Col)] = MapCachedFormulaError(rawValue);
        }

        return result;
    }

    private static ErrorValue MapCachedFormulaError(string rawValue) =>
        rawValue.ToUpperInvariant() switch
        {
            "#NULL!" => ErrorValue.Null,
            "#DIV/0!" => ErrorValue.DivByZero,
            "#VALUE!" => ErrorValue.Value,
            "#REF!" => ErrorValue.Ref,
            "#NAME?" => ErrorValue.Name,
            "#NUM!" => ErrorValue.Num,
            "#N/A" => ErrorValue.NA,
            "#SPILL!" => ErrorValue.Spill,
            "#CALC!" => ErrorValue.Calc,
            _ => new ErrorValue(rawValue)
        };
}
