using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.Opc;
using FreeP.Core.Model;

namespace FreeP.Core.IO;

/// <summary>
/// Parses a <c>ppt/charts/chartN.xml</c> part from a .pptx archive and returns a
/// <see cref="ChartShape"/> model. Entry point: <see cref="ReadChartPart"/>.
/// </summary>
internal static class PptxChartReader
{
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace A = PptxColorReader.A;

    // Default accent color cycle (theme slots accent1..6).
    private static readonly ThemeColorSlot[] AccentSlots =
    [
        ThemeColorSlot.Accent1, ThemeColorSlot.Accent2, ThemeColorSlot.Accent3,
        ThemeColorSlot.Accent4, ThemeColorSlot.Accent5, ThemeColorSlot.Accent6
    ];

    /// <summary>
    /// Reads a chart part from the archive and returns the parsed <see cref="ChartShape"/>,
    /// or null if the part is missing or malformed.
    /// </summary>
    internal static ChartShape? ReadChartPart(
        ZipArchive archive, string chartPath, PresentationColorScheme scheme)
    {
        var entry = archive.GetEntry(chartPath);
        if (entry is null) return null;

        XDocument doc;
        try
        {
            using var s = entry.Open();
            using var reader = XmlReader.Create(s, SecureXmlReaderSettings.Create());
            doc = XDocument.Load(reader);
        }
        catch { return null; }

        var chartSpace = doc.Root; // c:chartSpace
        if (chartSpace is null) return null;

        var chartEl = chartSpace.Element(C + "chart");
        if (chartEl is null) return null;

        var shape = new ChartShape();

        // Title
        shape.Title = ReadTitle(chartEl.Element(C + "title"));

        // plotArea
        var plotArea = chartEl.Element(C + "plotArea");
        if (plotArea is null) return shape;

        DetectChartTypeAndSeries(plotArea, shape, scheme);

        // Axes (catAx / dateAx = category axis; valAx = value axis)
        foreach (var axEl in plotArea.Elements())
        {
            if (axEl.Name == C + "catAx" || axEl.Name == C + "dateAx")
                ReadAxis(axEl, shape.CategoryAxis);
            else if (axEl.Name == C + "valAx")
                ReadAxis(axEl, shape.ValueAxis);
        }

        // Legend
        var legendEl = chartEl.Element(C + "legend");
        shape.Legend = legendEl is not null
            ? legendEl.Element(C + "legendPos")?.Attribute("val")?.Value switch
            {
                "r" or "rt" => LegendPosition.Right,
                "l"         => LegendPosition.Left,
                "t"         => LegendPosition.Top,
                "b"         => LegendPosition.Bottom,
                _           => LegendPosition.Right
            }
            : (LegendPosition?)null;

        return shape;
    }

    // ── Title ─────────────────────────────────────────────────────────────────

    private static string? ReadTitle(XElement? titleEl)
    {
        if (titleEl is null) return null;

        var tx = titleEl.Element(C + "tx");
        if (tx is not null)
        {
            // Rich text path: c:tx/c:rich/a:p/a:r/a:t
            var rich = tx.Element(C + "rich");
            if (rich is not null)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var t in rich.Descendants(A + "t"))
                    sb.Append(t.Value);
                var text = sb.ToString().Trim();
                if (text.Length > 0) return text;
            }

            // Cached string ref path: c:tx/c:strRef/c:strCache/c:pt/c:v
            var v = tx.Element(C + "strRef")
                ?.Element(C + "strCache")
                ?.Elements(C + "pt").FirstOrDefault()
                ?.Element(C + "v")?.Value;
            if (!string.IsNullOrWhiteSpace(v)) return v;
        }

        return null;
    }

    // ── Chart type dispatch ───────────────────────────────────────────────────

    private static void DetectChartTypeAndSeries(
        XElement plotArea, ChartShape shape, PresentationColorScheme scheme)
    {
        foreach (var el in plotArea.Elements())
        {
            switch (el.Name.LocalName)
            {
                case "barChart":
                case "bar3DChart":          // 3-D column/bar charts — treat same as 2D
                    ReadBarChart(el, shape, scheme); return;
                case "lineChart":
                case "line3DChart":
                    ReadLineChart(el, shape, scheme); return;
                case "pieChart":
                case "pie3DChart":
                case "doughnutChart":
                case "ofPieChart":          // pie-of-pie / bar-of-pie — best effort as Pie
                    ReadPieChart(el, shape, scheme); return;
                case "areaChart":
                case "area3DChart":
                    ReadAreaChart(el, shape, scheme); return;
                case "scatterChart":
                    ReadScatterChart(el, shape, scheme); return;
                case "bubbleChart":
                    ReadScatterChart(el, shape, scheme); return;  // bubble ~= scatter
                case "stockChart":
                    ReadLineChart(el, shape, scheme); return;     // stock ~= line
                case "radarChart":
                    ReadLineChart(el, shape, scheme); return;     // radar ~= line best-effort
                case "surfaceChart":
                case "surface3DChart":
                    ReadBarChart(el, shape, scheme); return;      // surface ~= column best-effort
            }
        }
        shape.ChartType = ChartType.Unknown;
    }

    private static void ReadBarChart(XElement el, ChartShape shape, PresentationColorScheme scheme)
    {
        var barDir   = el.Element(C + "barDir")?.Attribute("val")?.Value   ?? "col";
        var grouping = el.Element(C + "grouping")?.Attribute("val")?.Value ?? "clustered";

        shape.ChartType = (barDir, grouping) switch
        {
            ("col", "clustered")      => ChartType.ColumnClustered,
            ("col", "stacked")        => ChartType.ColumnStacked,
            ("col", "percentStacked") => ChartType.ColumnStacked100,
            ("bar", "clustered")      => ChartType.BarClustered,
            ("bar", "stacked")        => ChartType.BarStacked,
            ("bar", "percentStacked") => ChartType.BarStacked100,
            _                         => ChartType.ColumnClustered
        };

        ReadSeriesFromChart(el, shape, scheme);
    }

    private static void ReadLineChart(XElement el, ChartShape shape, PresentationColorScheme scheme)
    {
        // A line chart "has markers" when any series has an explicit marker that is not "none",
        // or has no marker element at all (OOXML default for lineChart is to show markers).
        bool hasMarkers = el.Elements(C + "ser").Any(s =>
        {
            var sym = s.Element(C + "marker")?.Element(C + "symbol")?.Attribute("val")?.Value;
            return sym is null || sym != "none";
        });

        shape.ChartType = hasMarkers ? ChartType.LineMarkers : ChartType.Line;
        ReadSeriesFromChart(el, shape, scheme);
    }

    private static void ReadPieChart(XElement el, ChartShape shape, PresentationColorScheme scheme)
    {
        shape.ChartType = ChartType.Pie;
        ReadSeriesFromChart(el, shape, scheme);
    }

    private static void ReadAreaChart(XElement el, ChartShape shape, PresentationColorScheme scheme)
    {
        var grouping = el.Element(C + "grouping")?.Attribute("val")?.Value ?? "standard";
        shape.ChartType = grouping == "stacked" ? ChartType.AreaStacked : ChartType.Area;
        ReadSeriesFromChart(el, shape, scheme);
    }

    private static void ReadScatterChart(XElement el, ChartShape shape, PresentationColorScheme scheme)
    {
        shape.ChartType = ChartType.Scatter;
        ReadSeriesFromChart(el, shape, scheme);
    }

    // ── Series parsing ────────────────────────────────────────────────────────

    private static void ReadSeriesFromChart(
        XElement chartEl, ChartShape shape, PresentationColorScheme scheme)
    {
        int seriesIndex = 0;
        foreach (var serEl in chartEl.Elements(C + "ser"))
        {
            var series = new ChartSeries();

            // Series name (c:tx → strRef cache or direct c:v)
            var txEl = serEl.Element(C + "tx");
            if (txEl is not null)
            {
                var nameV = txEl.Element(C + "strRef")
                    ?.Element(C + "strCache")
                    ?.Elements(C + "pt").FirstOrDefault()
                    ?.Element(C + "v")?.Value;
                if (nameV is not null)
                    series.Name = nameV;
                else
                {
                    var directV = txEl.Element(C + "v")?.Value;
                    if (directV is not null) series.Name = directV;
                }
            }
            if (string.IsNullOrWhiteSpace(series.Name))
                series.Name = $"Series {seriesIndex + 1}";

            // Series fill color from c:spPr/a:solidFill
            var spPr = serEl.Element(C + "spPr");
            if (spPr is not null)
            {
                var solidFill = spPr.Element(A + "solidFill");
                if (solidFill is not null)
                    series.FillColor = PptxColorReader.TryReadColor(solidFill, scheme);
            }

            // Fall back to theme accent cycle if no explicit color
            if (series.FillColor is null)
            {
                var slot = AccentSlots[seriesIndex % AccentSlots.Length];
                series.FillColor = new ThemeAwareColor(
                    new SrgbColor(0x4F, 0x81, 0xBD),  // sRGB fallback
                    new SchemeColorRef { Slot = slot, LumMod = 1.0, LumOff = 0.0 });
            }

            // Categories (c:cat or c:xVal) — read only from the first series
            if (shape.Categories.Count == 0)
            {
                var catEl = serEl.Element(C + "cat") ?? serEl.Element(C + "xVal");
                if (catEl is not null)
                    ReadCategories(catEl, shape.Categories);
            }

            // Values (c:val or c:yVal)
            var valEl = serEl.Element(C + "val") ?? serEl.Element(C + "yVal");
            if (valEl is not null)
                ReadValues(valEl, series.Values);

            // Per-point colors (c:dPt) — mainly used by pie/doughnut charts
            foreach (var dptEl in serEl.Elements(C + "dPt"))
            {
                var idx = ParseInt(dptEl.Element(C + "idx")?.Attribute("val")?.Value);
                var dptSolid = dptEl.Element(C + "spPr")?.Element(A + "solidFill");
                if (dptSolid is not null)
                {
                    var color = PptxColorReader.TryReadColor(dptSolid, scheme);
                    if (color is not null)
                        series.PointColors[idx] = color;
                }
            }

            shape.Series.Add(series);
            seriesIndex++;
        }
    }

    private static void ReadCategories(XElement catEl, List<string> categories)
    {
        // strRef → strCache → pt/v
        var strRef = catEl.Element(C + "strRef");
        if (strRef is not null)
        {
            foreach (var pt in strRef.Element(C + "strCache")?.Elements(C + "pt")
                     ?? Enumerable.Empty<XElement>())
                categories.Add(pt.Element(C + "v")?.Value ?? string.Empty);
            return;
        }

        // numRef → numCache → pt/v (numeric categories)
        var numRef = catEl.Element(C + "numRef");
        if (numRef is not null)
        {
            foreach (var pt in numRef.Element(C + "numCache")?.Elements(C + "pt")
                     ?? Enumerable.Empty<XElement>())
                categories.Add(pt.Element(C + "v")?.Value ?? string.Empty);
            return;
        }

        // strLit (literal inline strings)
        var strLit = catEl.Element(C + "strLit");
        if (strLit is not null)
        {
            foreach (var pt in strLit.Elements(C + "pt"))
                categories.Add(pt.Element(C + "v")?.Value ?? string.Empty);
        }
    }

    private static void ReadValues(XElement valEl, List<double?> values)
    {
        var numRef = valEl.Element(C + "numRef");
        if (numRef is not null)
        {
            var cache = numRef.Element(C + "numCache");
            int ptCount = ParseInt(cache?.Element(C + "ptCount")?.Attribute("val")?.Value);

            // Pre-size with nulls
            for (int i = 0; i < ptCount; i++) values.Add(null);

            foreach (var pt in cache?.Elements(C + "pt") ?? Enumerable.Empty<XElement>())
            {
                int idx = ParseInt(pt.Attribute("idx")?.Value);
                while (values.Count <= idx) values.Add(null);
                var v = pt.Element(C + "v")?.Value;
                if (v is not null &&
                    double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var dv))
                    values[idx] = dv;
            }
            return;
        }

        // numLit (inline literal values)
        var numLit = valEl.Element(C + "numLit");
        if (numLit is not null)
        {
            foreach (var pt in numLit.Elements(C + "pt"))
            {
                var v = pt.Element(C + "v")?.Value;
                values.Add(v is not null &&
                    double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var dv)
                    ? dv : null);
            }
        }
    }

    // ── Axis parsing ──────────────────────────────────────────────────────────

    private static void ReadAxis(XElement axEl, ChartAxis axis)
    {
        axis.Delete = axEl.Element(C + "delete")?.Attribute("val")?.Value is "1" or "true";
        axis.HasMajorGridlines = axEl.Element(C + "majorGridlines") is not null;
        axis.Title = ReadTitle(axEl.Element(C + "title"));

        var scaling = axEl.Element(C + "scaling");
        if (scaling is not null)
        {
            var minStr = scaling.Element(C + "min")?.Attribute("val")?.Value;
            var maxStr = scaling.Element(C + "max")?.Attribute("val")?.Value;
            if (minStr is not null &&
                double.TryParse(minStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var minV))
                axis.Min = minV;
            if (maxStr is not null &&
                double.TryParse(maxStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var maxV))
                axis.Max = maxV;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int ParseInt(string? s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
