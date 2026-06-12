using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxChartSeriesRangeReader
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    public static int ReadSeriesIndex(XElement series, int fallback) =>
        int.TryParse(ElementByLocalName(series, "idx")?.Attribute("val")?.Value, out var index)
            ? index
            : fallback;

    public static bool UsesSecondaryValueAxis(XElement? plotArea, XElement plotChart)
    {
        if (plotArea is null)
            return false;

        var secondaryAxisIds = plotArea
            .Elements(ChartNs + "valAx")
            .Where(axis => axis.Element(ChartNs + "axPos")?.Attribute("val")?.Value == "r")
            .Select(axis => axis.Element(ChartNs + "axId")?.Attribute("val")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);
        if (secondaryAxisIds.Count == 0)
            return false;

        return plotChart
            .Elements(ChartNs + "axId")
            .Select(axis => axis.Attribute("val")?.Value)
            .Any(value => value is not null && secondaryAxisIds.Contains(value));
    }

    public static IEnumerable<string> ReadSeriesRangeFormulas(XElement series) =>
        ReadSeriesRangeFormulas(series, "tx", "cat", "val");

    public static bool HasSeriesRangeFormula(XElement series, string containerName) =>
        ElementByLocalName(series, containerName)?
            .Descendants()
            .Where(element => element.Name.LocalName == "f")
            .Any(element => !string.IsNullOrWhiteSpace(element.Value)) == true;

    public static IEnumerable<string> ReadSeriesRangeFormulas(XElement series, params string[] containerNames)
    {
        foreach (var containerName in containerNames)
        {
            var container = ElementByLocalName(series, containerName);
            if (container is null)
                continue;

            foreach (var element in container.Descendants())
            {
                if (element.Name.LocalName != "f")
                    continue;

                var formula = element.Value;
                if (string.IsNullOrWhiteSpace(formula))
                    continue;

                yield return formula;
            }
        }
    }

    public static XElement? ElementByLocalName(XElement element, string localName)
    {
        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName == localName)
                return child;
        }

        return null;
    }

    public static bool HasDescendant(XElement element, string localName) =>
        element.Descendants().Any(descendant => descendant.Name.LocalName == localName);

    public static bool TryParseFormulaRange(string formula, SheetId sheetId, out GridRange range)
    {
        range = default;
        var local = formula.Trim();
        var bang = local.LastIndexOf('!');
        if (bang >= 0)
            local = local[(bang + 1)..];

        local = local.Replace("$", "", StringComparison.Ordinal).Trim('\'');
        var parts = local.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1)
        {
            if (!CellAddress.TryParse(parts[0], sheetId, out var address))
                return false;

            range = new GridRange(address, address);
            return true;
        }

        if (parts.Length != 2 ||
            !CellAddress.TryParse(parts[0], sheetId, out var start) ||
            !CellAddress.TryParse(parts[1], sheetId, out var end))
        {
            return false;
        }

        range = new GridRange(start, end);
        return true;
    }

    public static GridRange UnionRanges(IReadOnlyList<GridRange> ranges)
    {
        var sheetId = ranges[0].Start.Sheet;
        var minRow = ranges.Min(range => range.Start.Row);
        var minCol = ranges.Min(range => range.Start.Col);
        var maxRow = ranges.Max(range => range.End.Row);
        var maxCol = ranges.Max(range => range.End.Col);
        return new GridRange(
            new CellAddress(sheetId, minRow, minCol),
            new CellAddress(sheetId, maxRow, maxCol));
    }

    /// <summary>
    /// Returns true when at least one formula in the series XML (val/cat/tx containers)
    /// cannot be parsed as a single rectangular range. Multi-area formulas such as
    /// "Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5" trigger this path.
    /// </summary>
    public static bool HasUnparsableFormula(XElement series, SheetId sheetId)
    {
        foreach (var formula in ReadSeriesRangeFormulas(series))
        {
            if (!TryParseFormulaRange(formula, sheetId, out _))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Reads the first formula string from a named container element (e.g. "val", "cat", "tx").
    /// Returns null when the container is absent or has no non-empty formula descendant.
    /// </summary>
    public static string? ReadFirstFormula(XElement series, string containerName) =>
        ReadSeriesRangeFormulas(series, containerName).FirstOrDefault();

    /// <summary>
    /// Collects <see cref="ChartSeriesVerbatimFormulas"/> for each series element when
    /// the chart contains at least one formula that cannot be parsed as a rectangular range.
    /// Returns null when all formulas are parseable (normal path — verbatim bypass not needed).
    /// </summary>
    public static List<ChartSeriesVerbatimFormulas>? TryCollectVerbatimFormulas(
        IEnumerable<XElement> allSeriesElements,
        SheetId sheetId)
    {
        var seriesList = allSeriesElements.ToList();
        var needsVerbatim = seriesList.Any(series => HasUnparsableFormula(series, sheetId));
        if (!needsVerbatim)
            return null;

        var result = new List<ChartSeriesVerbatimFormulas>(seriesList.Count);
        for (var i = 0; i < seriesList.Count; i++)
        {
            var series = seriesList[i];
            var seriesIndex = ReadSeriesIndex(series, i);
            result.Add(new ChartSeriesVerbatimFormulas(
                SeriesIndex: seriesIndex,
                ValFormula: ReadFirstFormula(series, "val"),
                CatFormula: ReadFirstFormula(series, "cat"),
                TxFormula: ReadFirstFormula(series, "tx")));
        }

        return result;
    }
}
