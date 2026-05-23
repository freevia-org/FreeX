using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

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
            foreach (var formula in series
                         .Elements()
                         .FirstOrDefault(element => element.Name.LocalName == containerName)?
                         .Descendants()
                         .Where(element => element.Name.LocalName == "f")
                         .Select(element => element.Value)
                         .Where(text => !string.IsNullOrWhiteSpace(text))
                     ?? [])
            {
                yield return formula;
            }
        }
    }

    public static XElement? ElementByLocalName(XElement element, string localName) =>
        element.Elements().FirstOrDefault(child => child.Name.LocalName == localName);

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
}
