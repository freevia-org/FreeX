using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetCellLayoutReader
{
    private static readonly SheetId ParseOnlySheetId = default;

    public static XlsxWorksheetCellLayout Read(
        XDocument worksheetXml,
        XNamespace worksheetNs)
    {
        var explicitStyleOnlyCells = new List<(uint Row, uint Col, int StyleIndex)>();
        var cachedFormulaErrors = new Dictionary<(uint Row, uint Col), ErrorValue>();

        var hasStyleOnlyCells = ReadSheetDataCells(
            worksheetXml.Root?.Element(worksheetNs + "sheetData"),
            worksheetNs,
            explicitStyleOnlyCells,
            cachedFormulaErrors);

        return new XlsxWorksheetCellLayout(cachedFormulaErrors, explicitStyleOnlyCells, hasStyleOnlyCells);
    }

    public static IReadOnlyList<(uint Row, uint Col, int StyleIndex)> ReadExplicitStyleOnlyCells(
        XDocument worksheetXml,
        XNamespace worksheetNs)
        => Read(worksheetXml, worksheetNs).ExplicitStyleOnlyCells;

    public static Dictionary<(uint Row, uint Col), ErrorValue> ReadCachedFormulaErrors(
        XDocument worksheetXml,
        XNamespace worksheetNs)
        => Read(worksheetXml, worksheetNs).CachedFormulaErrors;

    internal static bool ReadSheetDataCells(
        XElement? sheetData,
        XNamespace worksheetNs,
        List<(uint Row, uint Col, int StyleIndex)> explicitStyleOnlyCells,
        Dictionary<(uint Row, uint Col), ErrorValue> cachedFormulaErrors)
    {
        if (sheetData is null)
            return false;

        var rowName = worksheetNs + "row";
        var cellName = worksheetNs + "c";
        var formulaName = worksheetNs + "f";
        var valueName = worksheetNs + "v";
        var inlineStringName = worksheetNs + "is";
        var hasStyleOnlyCells = false;

        foreach (var row in sheetData.Elements(rowName))
        {
            foreach (var cell in row.Elements(cellName))
            {
                hasStyleOnlyCells |= ReadCell(
                    cell,
                    formulaName,
                    valueName,
                    inlineStringName,
                    explicitStyleOnlyCells,
                    cachedFormulaErrors);
            }
        }

        return hasStyleOnlyCells;
    }

    internal static bool ReadCell(
        XElement cell,
        XNamespace worksheetNs,
        List<(uint Row, uint Col, int StyleIndex)> explicitStyleOnlyCells,
        Dictionary<(uint Row, uint Col), ErrorValue> cachedFormulaErrors)
        => ReadCell(
            cell,
            worksheetNs + "f",
            worksheetNs + "v",
            worksheetNs + "is",
            explicitStyleOnlyCells,
            cachedFormulaErrors);

    internal static bool ReadCell(
        XElement cell,
        XName formulaName,
        XName valueName,
        XName inlineStringName,
        List<(uint Row, uint Col, int StyleIndex)> explicitStyleOnlyCells,
        Dictionary<(uint Row, uint Col), ErrorValue> cachedFormulaErrors)
    {
        var hasStyle = int.TryParse(
            cell.Attribute("s")?.Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var styleIndex);
        var isErrorType = string.Equals(cell.Attribute("t")?.Value, "e", StringComparison.OrdinalIgnoreCase);

        if (!hasStyle && !isErrorType)
            return false;

        var formula = cell.Element(formulaName);
        var value = cell.Element(valueName);
        var hasInlineString = hasStyle && cell.Element(inlineStringName) is not null;

        var isStyleOnly = hasStyle && formula is null && value is null && !hasInlineString;
        var rawValue = value?.Value;
        var hasCachedFormulaError = isErrorType && formula is not null && !string.IsNullOrWhiteSpace(rawValue);
        if (!isStyleOnly && !hasCachedFormulaError)
            return false;

        var reference = cell.Attribute("r")?.Value;
        if (string.IsNullOrWhiteSpace(reference) || !CellAddress.TryParse(reference, ParseOnlySheetId, out var address))
            return isStyleOnly;

        if (isStyleOnly)
            explicitStyleOnlyCells.Add((address.Row, address.Col, styleIndex));

        if (hasCachedFormulaError)
            cachedFormulaErrors[(address.Row, address.Col)] = MapCachedFormulaError(rawValue!);

        return isStyleOnly;
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

internal sealed record XlsxWorksheetCellLayout(
    Dictionary<(uint Row, uint Col), ErrorValue> CachedFormulaErrors,
    IReadOnlyList<(uint Row, uint Col, int StyleIndex)> ExplicitStyleOnlyCells,
    bool HasStyleOnlyCells);
