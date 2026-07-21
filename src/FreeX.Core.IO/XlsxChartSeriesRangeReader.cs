using System.Globalization;
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

    public static bool TryParseFormulaRange(string formula, SheetId sheetId, out GridRange range) =>
        TryParseFormulaRange(formula, sheetId, null, out range);

    /// <summary>
    /// Parses a chart series formula string into a <see cref="GridRange"/>.
    /// When <paramref name="sheetNameResolver"/> is supplied and the formula contains a sheet-name
    /// prefix (e.g. <c>'DataSheet'!$B$2:$B$6</c>), the prefix is looked up in the resolver to
    /// obtain the correct <see cref="SheetId"/> for the referenced sheet — enabling cross-sheet
    /// data ranges to point at the sheet that actually holds the data.
    /// When the resolver is <see langword="null"/> or the sheet name is not found, the supplied
    /// <paramref name="sheetId"/> (the chart's own sheet) is used as a fallback.
    /// </summary>
    public static bool TryParseFormulaRange(
        string formula,
        SheetId sheetId,
        IReadOnlyDictionary<string, SheetId>? sheetNameResolver,
        out GridRange range)
    {
        range = default;
        var local = formula.Trim();

        // Extract and resolve the sheet name from the formula prefix (e.g. 'Sheet1'!$A$1:$A$5).
        var bang = local.LastIndexOf('!');
        if (bang >= 0)
        {
            // Unquote single-quoted sheet names: 'Sheet Name' → Sheet Name, or bare SheetName
            var sheetPrefix = local[..bang].Trim('\'');

            // A bracketed prefix (e.g. '[1]Sheet1' or '[Budget.xlsx]Sheet1') is Excel's external-
            // workbook reference form addressing xl/workbook.xml's externalReferences list. It must
            // never be silently reinterpreted as a same-workbook sheet reference: doing so would
            // discard the external link and rebind the chart series to unrelated local-sheet cells
            // on save. Report unparsable so callers preserve the formula verbatim instead.
            if (sheetPrefix.Length > 0 && sheetPrefix[0] == '[')
                return false;

            if (sheetNameResolver is not null)
            {
                // Also handle doubled single-quotes inside quoted names per OOXML spec
                sheetPrefix = sheetPrefix.Replace("''", "'", StringComparison.Ordinal);
                if (sheetNameResolver.TryGetValue(sheetPrefix, out var resolvedId))
                    sheetId = resolvedId;
            }
            local = local[(bang + 1)..];
        }

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

    /// <summary>
    /// Reads the worksheet value column for a series from its <c>&lt;c:val&gt;</c> range formula
    /// (e.g. <c>Chart_Target!$D$4:$D$9</c> -> column D). Returns null when the val formula is absent,
    /// unparseable, a named range, a cross-sheet ref, or spans multiple columns (in which case the
    /// caller should not record a single-column mapping).
    /// </summary>
    public static uint? TryReadSeriesValueColumn(
        XElement series,
        SheetId sheetId,
        IReadOnlyDictionary<string, SheetId>? sheetNameResolver,
        string valueContainerName = "val")
    {
        var formula = ReadFirstFormula(series, valueContainerName);
        if (string.IsNullOrWhiteSpace(formula))
            return null;
        if (!TryParseFormulaRange(formula, sheetId, sheetNameResolver, out var range))
            return null;
        if (range.Start.Sheet != sheetId || range.Start.Col != range.End.Col)
            return null;
        return range.Start.Col;
    }

    /// <summary>
    /// Detects Excel's "Switch Row/Column" orientation from the series value formulas: returns true
    /// when every parseable value range is a single-row horizontal strip and at least one spans
    /// multiple columns. Column-major charts emit single-column vertical strips, so all-1×1 ranges
    /// stay column-major (ambiguous → keep the default).
    /// </summary>
    public static bool DetectSeriesInRows(
        IEnumerable<XElement> seriesElements,
        SheetId sheetId,
        IReadOnlyDictionary<string, SheetId>? sheetNameResolver,
        string valueContainerName = "val")
    {
        var anyMultiColumn = false;
        foreach (var series in seriesElements)
        {
            var formula = ReadFirstFormula(series, valueContainerName);
            if (string.IsNullOrWhiteSpace(formula))
                continue;
            if (!TryParseFormulaRange(formula, sheetId, sheetNameResolver, out var range))
                continue;

            if (range.Start.Row != range.End.Row)
                return false;
            if (range.End.Col > range.Start.Col)
                anyMultiColumn = true;
        }

        return anyMultiColumn;
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
    /// R57-io-chart-series-refs-5-2: a Scatter/Bubble series has no <c>cat</c>/<c>val</c>
    /// containers — its point data lives in <c>xVal</c>/<c>yVal</c>[/<c>bubbleSize</c>] instead.
    /// Detecting this from the series' own child elements (rather than requiring the caller to
    /// know the chart type) lets <see cref="HasUnparsableFormula"/> and
    /// <see cref="TryCollectVerbatimFormulas"/> see an unparsable xVal/yVal/bubbleSize formula —
    /// previously invisible to both, since they only ever inspected tx/cat/val.
    /// </summary>
    private static string[] GetSeriesRangeContainerNames(XElement series)
    {
        if (ElementByLocalName(series, "xVal") is null && ElementByLocalName(series, "yVal") is null)
            return ["tx", "cat", "val"];

        return ElementByLocalName(series, "bubbleSize") is null
            ? ["tx", "xVal", "yVal"]
            : ["tx", "xVal", "yVal", "bubbleSize"];
    }

    /// <summary>
    /// Returns true when at least one formula in the series XML (val/cat/tx containers, or
    /// xVal/yVal/bubbleSize for a Scatter/Bubble series) cannot be parsed as a single rectangular
    /// range. Multi-area formulas such as "Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5" trigger this path.
    /// </summary>
    public static bool HasUnparsableFormula(XElement series, SheetId sheetId)
    {
        foreach (var formula in ReadSeriesRangeFormulas(series, GetSeriesRangeContainerNames(series)))
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
            var isScatterOrBubble = ElementByLocalName(series, "xVal") is not null ||
                ElementByLocalName(series, "yVal") is not null;
            // R57-io-chart-series-refs-5-2: a Scatter/Bubble series has no cat/val containers —
            // CatFormula/ValFormula are repurposed to carry xVal/yVal instead (matching how
            // XlsxChartXmlWriter.BuildScatterChartSeries already reads them back on write), and
            // bubbleSize (Bubble only) gets its own dedicated field since it has no equivalent
            // to repurpose.
            result.Add(new ChartSeriesVerbatimFormulas(
                SeriesIndex: seriesIndex,
                ValFormula: ReadFirstFormula(series, isScatterOrBubble ? "yVal" : "val"),
                CatFormula: ReadFirstFormula(series, isScatterOrBubble ? "xVal" : "cat"),
                TxFormula: ReadFirstFormula(series, "tx"),
                BubbleSizeFormula: ReadFirstFormula(series, "bubbleSize")));
        }

        return result;
    }

    /// <summary>
    /// Returns true when a formula string refers to a named range rather than a direct
    /// cell/range address (i.e. the part after the last '!' cannot be parsed as a cell address).
    /// Examples: <c>'Sheet1'!rngCount</c> → true; <c>'Sheet1'!$A$1:$A$10</c> → false.
    /// Formulas with no '!' are also considered named ranges (workbook-scope names).
    /// </summary>
    public static bool IsNamedRangeFormula(string formula, SheetId sheetId)
    {
        if (string.IsNullOrWhiteSpace(formula))
            return false;
        // If it's parseable as a cell/range reference it is NOT a named range
        return !TryParseFormulaRange(formula, sheetId, out _);
    }

    /// <summary>
    /// Returns true when the val or cat formula of every series in <paramref name="seriesElements"/>
    /// that has a formula is a named range (not a direct cell address). When this returns true,
    /// the embedded numCache/strCache values should be used for rendering.
    /// </summary>
    public static bool AllValCatFormulasAreNamedRanges(IReadOnlyList<XElement> seriesElements, SheetId sheetId)
    {
        var anyNamedRange = false;
        foreach (var series in seriesElements)
        {
            foreach (var containerName in new[] { "val", "cat" })
            {
                var formula = ReadFirstFormula(series, containerName);
                if (string.IsNullOrWhiteSpace(formula))
                    continue;

                if (!IsNamedRangeFormula(formula, sheetId))
                    return false; // at least one val/cat formula IS a direct cell reference

                anyNamedRange = true;
            }
        }

        return anyNamedRange; // true only if we saw at least one named-range formula and NO direct cell refs
    }

    /// <summary>
    /// Reads embedded data from numCache/strCache elements in a series list.
    /// Returns a list of <see cref="ChartEmbeddedSeriesData"/> (one per series) when:
    /// <list type="bullet">
    ///   <item>All val/cat formulas are named ranges (not direct cell addresses), AND</item>
    ///   <item>At least one series has non-empty embedded numCache values.</item>
    /// </list>
    /// Returns null when formulas are direct cell references (normal cell-lookup path) or when
    /// the named-range formulas have no embedded cache (e.g. full-column refs like Sheet1!$B:$B
    /// that lack a numCache — use the verbatim-formula path instead).
    /// </summary>
    public static List<ChartEmbeddedSeriesData>? TryReadEmbeddedSeriesData(
        IReadOnlyList<XElement> seriesElements,
        SheetId sheetId)
    {
        if (!AllValCatFormulasAreNamedRanges(seriesElements, sheetId))
            return null;

        var result = new List<ChartEmbeddedSeriesData>(seriesElements.Count);
        for (var i = 0; i < seriesElements.Count; i++)
        {
            var series = seriesElements[i];
            var seriesIndex = ReadSeriesIndex(series, i);
            var seriesName = ReadEmbeddedStringCacheFirstValue(series, "tx");
            var categories = ReadEmbeddedStringCacheValues(series, "cat");
            var values = ReadEmbeddedNumericCacheValues(series, "val");
            result.Add(new ChartEmbeddedSeriesData(seriesIndex, seriesName, categories, values));
        }

        // Only use embedded data when at least one series has actual numeric cache values.
        // If no series has cache data (e.g. full-column references Sheet1!$B:$B with no numCache),
        // fall through to the verbatim-formula path instead.
        return result.Any(s => s.Values.Count > 0) ? result : null;
    }

    /// <summary>
    /// Returns embedded numCache/strCache data when ALL val/cat series formulas are direct cell
    /// references that resolve to a <em>different</em> sheet than <paramref name="chartSheetId"/>
    /// (i.e. cross-sheet references such as <c>'4. Dynamic Histogram'!$B$31:$B$32</c>).
    /// <para>
    /// When the referenced cells live on another sheet, the renderer's live-cell lookup will
    /// find nothing (viewport only carries the host-sheet cells), so the embedded numCache values
    /// should be used as a fallback.  Returns <see langword="null"/> when:
    /// </para>
    /// <list type="bullet">
    ///   <item>No <paramref name="sheetNameResolver"/> is provided (cannot confirm cross-sheet-ness).</item>
    ///   <item>Any formula references the chart's own sheet (live cells can be used).</item>
    ///   <item>Any formula is a named range (handled by <see cref="TryReadEmbeddedSeriesData"/>).</item>
    ///   <item>No series has non-empty numCache values.</item>
    /// </list>
    /// </summary>
    public static List<ChartEmbeddedSeriesData>? TryReadCrossSheetEmbeddedData(
        IEnumerable<XElement> seriesElements,
        SheetId chartSheetId,
        IReadOnlyDictionary<string, SheetId>? sheetNameResolver)
    {
        if (sheetNameResolver is null)
            return null;

        var seriesList = seriesElements.ToList();
        var anyCrossSheet = false;

        foreach (var series in seriesList)
        {
            foreach (var containerName in new[] { "val", "cat" })
            {
                var formula = ReadFirstFormula(series, containerName);
                if (string.IsNullOrWhiteSpace(formula))
                    continue;

                // Must be a parseable cell range (not a named range — those are handled elsewhere)
                if (!TryParseFormulaRange(formula, chartSheetId, sheetNameResolver, out var range))
                    return null; // named range or unparseable — not the cross-sheet case

                if (range.Start.Sheet == chartSheetId)
                    return null; // references the chart's own sheet — live cells can be used

                anyCrossSheet = true;
            }
        }

        if (!anyCrossSheet)
            return null;

        var result = new List<ChartEmbeddedSeriesData>(seriesList.Count);
        for (var i = 0; i < seriesList.Count; i++)
        {
            var series = seriesList[i];
            var seriesIndex = ReadSeriesIndex(series, i);
            var seriesName = ReadEmbeddedStringCacheFirstValue(series, "tx");
            var categories = ReadEmbeddedStringCacheValues(series, "cat");
            var values = ReadEmbeddedNumericCacheValues(series, "val");
            result.Add(new ChartEmbeddedSeriesData(seriesIndex, seriesName, categories, values));
        }

        // Only use embedded data when at least one series has numeric cache values.
        return result.Any(s => s.Values.Count > 0) ? result : null;
    }

    /// <summary>Reads the first string value from a &lt;c:strCache&gt; inside the named container.</summary>
    private static string? ReadEmbeddedStringCacheFirstValue(XElement series, string containerName)
    {
        var container = ElementByLocalName(series, containerName);
        if (container is null)
            return null;

        // Try strRef/strCache first, then numRef/numCache
        var cache = FindDescendantByLocalName(container, "strCache")
                    ?? FindDescendantByLocalName(container, "numCache");
        if (cache is null)
            return null;

        return cache.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "pt")?
            .Element(ChartNs + "v")?
            .Value;
    }

    /// <summary>Reads all string values from a &lt;c:strCache&gt; (or &lt;c:numCache&gt; fallback) inside the named container.</summary>
    private static IReadOnlyList<string> ReadEmbeddedStringCacheValues(XElement series, string containerName)
    {
        var container = ElementByLocalName(series, containerName);
        if (container is null)
            return [];

        // Try strRef/strCache first, then numRef/numCache (numeric/date category axes).
        var cache = FindDescendantByLocalName(container, "strCache")
                    ?? FindDescendantByLocalName(container, "numCache");
        if (cache is null)
            return [];

        return cache.Elements()
            .Where(e => e.Name.LocalName == "pt")
            .Select(pt => pt.Element(ChartNs + "v")?.Value ?? "")
            .ToList();
    }

    /// <summary>Reads all numeric values from a &lt;c:numCache&gt; inside the named container.</summary>
    private static IReadOnlyList<double?> ReadEmbeddedNumericCacheValues(XElement series, string containerName)
    {
        var container = ElementByLocalName(series, containerName);
        if (container is null)
            return [];

        var cache = FindDescendantByLocalName(container, "numCache");
        if (cache is null)
            return [];

        var ptCount = int.TryParse(
            cache.Elements().FirstOrDefault(e => e.Name.LocalName == "ptCount")?.Attribute("val")?.Value,
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? count : 0;

        // Build sparse array — pts use idx attribute to indicate position
        var values = new double?[Math.Max(ptCount, 0)];
        foreach (var pt in cache.Elements().Where(e => e.Name.LocalName == "pt"))
        {
            if (!int.TryParse(pt.Attribute("idx")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
                continue;
            if (idx < 0 || idx >= values.Length)
                continue;

            var raw = pt.Element(ChartNs + "v")?.Value;
            values[idx] = double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
        }

        return values;
    }

    private static XElement? FindDescendantByLocalName(XElement element, string localName)
    {
        foreach (var descendant in element.Descendants())
        {
            if (descendant.Name.LocalName == localName)
                return descendant;
        }

        return null;
    }
}
