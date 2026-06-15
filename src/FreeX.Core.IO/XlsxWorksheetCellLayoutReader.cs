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
        var explicitPopulatedCellStyles = new List<(uint Row, uint Col, int StyleIndex)>();
        var cachedFormulaErrors = new Dictionary<(uint Row, uint Col), ErrorValue>();
        var sharedStringValueCells = new List<(uint Row, uint Col)>();
        var populatedCellCount = 0;
        var styleOnlyStyleIndexes = new HashSet<string>(StringComparer.Ordinal);
        var hasDuplicateStyleOnlyCellStyleIndexes = false;

        var hasStyleOnlyCells = ReadSheetDataCells(
            worksheetXml.Root?.Element(worksheetNs + "sheetData"),
            worksheetNs,
            explicitStyleOnlyCells,
            explicitPopulatedCellStyles,
            cachedFormulaErrors,
            styleOnlyStyleIndexes,
            ref hasDuplicateStyleOnlyCellStyleIndexes,
            ref populatedCellCount,
            sharedStringValueCells);

        return new XlsxWorksheetCellLayout(
            cachedFormulaErrors,
            explicitPopulatedCellStyles,
            explicitStyleOnlyCells,
            hasStyleOnlyCells,
            hasDuplicateStyleOnlyCellStyleIndexes,
            populatedCellCount,
            sharedStringValueCells);
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
        List<(uint Row, uint Col, int StyleIndex)> explicitPopulatedCellStyles,
        Dictionary<(uint Row, uint Col), ErrorValue> cachedFormulaErrors,
        HashSet<string> styleOnlyStyleIndexes,
        ref bool hasDuplicateStyleOnlyCellStyleIndexes,
        ref int populatedCellCount,
        List<(uint Row, uint Col)>? sharedStringValueCells = null)
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
                    explicitPopulatedCellStyles,
                    cachedFormulaErrors,
                    styleOnlyStyleIndexes,
                    ref hasDuplicateStyleOnlyCellStyleIndexes,
                    ref populatedCellCount,
                    sharedStringValueCells);
            }
        }

        return hasStyleOnlyCells;
    }

    internal static bool ReadCell(
        XElement cell,
        XNamespace worksheetNs,
        List<(uint Row, uint Col, int StyleIndex)> explicitStyleOnlyCells,
        List<(uint Row, uint Col, int StyleIndex)> explicitPopulatedCellStyles,
        Dictionary<(uint Row, uint Col), ErrorValue> cachedFormulaErrors,
        HashSet<string> styleOnlyStyleIndexes,
        ref bool hasDuplicateStyleOnlyCellStyleIndexes,
        ref int populatedCellCount,
        List<(uint Row, uint Col)>? sharedStringValueCells = null)
        => ReadCell(
            cell,
            worksheetNs + "f",
            worksheetNs + "v",
            worksheetNs + "is",
            explicitStyleOnlyCells,
            explicitPopulatedCellStyles,
            cachedFormulaErrors,
            styleOnlyStyleIndexes,
            ref hasDuplicateStyleOnlyCellStyleIndexes,
            ref populatedCellCount,
            sharedStringValueCells);

    internal static bool ReadCell(
        XElement cell,
        XName formulaName,
        XName valueName,
        XName inlineStringName,
        List<(uint Row, uint Col, int StyleIndex)> explicitStyleOnlyCells,
        List<(uint Row, uint Col, int StyleIndex)> explicitPopulatedCellStyles,
        Dictionary<(uint Row, uint Col), ErrorValue> cachedFormulaErrors,
        HashSet<string> styleOnlyStyleIndexes,
        ref bool hasDuplicateStyleOnlyCellStyleIndexes,
        ref int populatedCellCount,
        List<(uint Row, uint Col)>? sharedStringValueCells = null)
    {
        var formula = cell.Element(formulaName);
        var value = cell.Element(valueName);
        var hasInlineString = cell.Element(inlineStringName) is not null;
        if (formula is not null || value is not null || hasInlineString)
            populatedCellCount++;

        var rawStyleIndex = cell.Attribute("s")?.Value;
        var hasStyle = int.TryParse(
            rawStyleIndex,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var styleIndex);
        var cellType = cell.Attribute("t")?.Value;
        var isErrorType = string.Equals(cellType, "e", StringComparison.OrdinalIgnoreCase);
        var isSharedStringType = string.Equals(cellType, "s", StringComparison.OrdinalIgnoreCase);

        // Track cells with SharedString type and a value element: ClosedXML's CellsUsed() skips
        // these when the SST entry is an empty string, so we need to handle them separately.
        if (isSharedStringType && value is not null && formula is null && sharedStringValueCells is not null)
        {
            var ssRef = cell.Attribute("r")?.Value;
            if (!string.IsNullOrWhiteSpace(ssRef) && CellAddress.TryParse(ssRef, ParseOnlySheetId, out var ssAddr))
                sharedStringValueCells.Add((ssAddr.Row, ssAddr.Col));
        }

        if (!hasStyle && !isErrorType)
            return false;

        var isStyleOnly = hasStyle && formula is null && value is null && !hasInlineString;
        var rawValue = value?.Value;
        var hasCachedFormulaError = isErrorType && formula is not null && !string.IsNullOrWhiteSpace(rawValue);
        var isExplicitlyStyledPopulatedCell = hasStyle && !isStyleOnly;
        if (!isStyleOnly && !hasCachedFormulaError && !isExplicitlyStyledPopulatedCell)
            return false;

        if (isStyleOnly && !styleOnlyStyleIndexes.Add(rawStyleIndex!))
            hasDuplicateStyleOnlyCellStyleIndexes = true;

        var reference = cell.Attribute("r")?.Value;
        if (string.IsNullOrWhiteSpace(reference) || !CellAddress.TryParse(reference, ParseOnlySheetId, out var address))
            return isStyleOnly;

        if (isStyleOnly)
            explicitStyleOnlyCells.Add((address.Row, address.Col, styleIndex));

        if (isExplicitlyStyledPopulatedCell)
            explicitPopulatedCellStyles.Add((address.Row, address.Col, styleIndex));

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
    IReadOnlyList<(uint Row, uint Col, int StyleIndex)> ExplicitPopulatedCellStyles,
    IReadOnlyList<(uint Row, uint Col, int StyleIndex)> ExplicitStyleOnlyCells,
    bool HasStyleOnlyCells,
    bool HasDuplicateStyleOnlyCellStyleIndexes,
    int PopulatedCellCount,
    IReadOnlyList<(uint Row, uint Col)> SharedStringValueCells);
