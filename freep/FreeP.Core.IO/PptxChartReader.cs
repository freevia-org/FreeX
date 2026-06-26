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
        bool primaryValAxRead = false;
        foreach (var axEl in plotArea.Elements())
        {
            if (axEl.Name == C + "catAx" || axEl.Name == C + "dateAx")
                ReadAxis(axEl, shape.CategoryAxis);
            else if (axEl.Name == C + "valAx")
            {
                if (!primaryValAxRead)
                {
                    ReadAxis(axEl, shape.ValueAxis);
                    primaryValAxRead = true;
                }
                else
                {
                    shape.SecondaryValueAxis = new ChartAxis();
                    ReadAxis(axEl, shape.SecondaryValueAxis);
                }
            }
        }

        // Chart-level data labels (c:plotArea/c:xxx/c:dLbls or chart-level)
        // Per OOXML the dLbls lives inside each plot-type element, read it from the first chart type el.
        var firstChartTypeEl = plotArea.Elements().FirstOrDefault(e =>
            e.Name.LocalName is "barChart" or "lineChart" or "pieChart" or "doughnutChart"
            or "areaChart" or "scatterChart" or "bubbleChart" or "radarChart"
            or "bar3DChart" or "line3DChart" or "pie3DChart" or "area3DChart" or "ofPieChart"
            or "stockChart" or "surfaceChart" or "surface3DChart");
        shape.DataLabels = ReadDataLabels(firstChartTypeEl?.Element(C + "dLbls"));

        // Secondary value axis detection
        // Each plotType element has c:axId refs; if there's a second c:valAx, check which series use it.
        var valAxIds = new List<int>();
        foreach (var axEl in plotArea.Elements(C + "valAx"))
        {
            var axId = ParseInt(axEl.Element(C + "axId")?.Attribute("val")?.Value);
            valAxIds.Add(axId);
        }
        // If we have 2+ valAx elements, the second one is the secondary axis.
        if (valAxIds.Count >= 2)
        {
            int secondaryAxId = valAxIds[1]; // second valAx is secondary

            // Now detect which series are on the secondary axis.
            // A plot group element references its axes via c:axId children.
            // If a plot group's second c:axId equals secondaryAxId, its series are on the secondary axis.
            foreach (var plotEl in plotArea.Elements())
            {
                var axIds = plotEl.Elements(C + "axId").Select(a => ParseInt(a.Attribute("val")?.Value)).ToList();
                if (axIds.Count >= 2 && axIds.Any(id => id == secondaryAxId))
                {
                    // All series in this plot group are on the secondary axis
                    // Map them by their c:ser/c:idx to the correct ChartSeries
                    foreach (var serEl in plotEl.Elements(C + "ser"))
                    {
                        int serIdx = ParseInt(serEl.Element(C + "idx")?.Attribute("val")?.Value);
                        if (serIdx < shape.Series.Count)
                            shape.Series[serIdx].OnSecondaryAxis = true;
                    }
                }
            }
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
                case "ofPieChart":          // pie-of-pie / bar-of-pie — best effort as Pie
                    ReadPieChart(el, shape, scheme); return;
                case "doughnutChart":
                    ReadDoughnutChart(el, shape, scheme); return;
                case "areaChart":
                case "area3DChart":
                    ReadAreaChart(el, shape, scheme); return;
                case "scatterChart":
                    ReadScatterChartDistinct(el, shape, scheme); return;
                case "bubbleChart":
                    ReadBubbleChart(el, shape, scheme); return;
                case "stockChart":
                    ReadLineChart(el, shape, scheme); return;     // stock ~= line
                case "radarChart":
                    ReadRadarChart(el, shape, scheme); return;
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

    private static void ReadDoughnutChart(XElement el, ChartShape shape, PresentationColorScheme scheme)
    {
        shape.ChartType = ChartType.Doughnut;

        // c:holeSize val= gives the inner radius as a percentage (default 50).
        var holeSizeStr = el.Element(C + "holeSize")?.Attribute("val")?.Value;
        if (holeSizeStr is not null && int.TryParse(holeSizeStr,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var hs))
            shape.DoughnutHolePercent = Math.Clamp(hs, 0, 90);

        ReadSeriesFromChart(el, shape, scheme);
    }

    private static void ReadScatterChartDistinct(XElement el, ChartShape shape, PresentationColorScheme scheme)
    {
        shape.ChartType = ChartType.Scatter;

        // c:scatterStyle val= → marker/line/lineMarker/smooth/smoothMarker
        var styleStr = el.Element(C + "scatterStyle")?.Attribute("val")?.Value ?? "lineMarker";
        shape.ScatterStyle = styleStr switch
        {
            "marker"       => ScatterStyle.Marker,
            "line"         => ScatterStyle.Line,
            "lineMarker"   => ScatterStyle.LineMarker,
            "smooth"       => ScatterStyle.Smooth,
            "smoothMarker" => ScatterStyle.SmoothMarker,
            _              => ScatterStyle.LineMarker
        };

        ReadScatterSeriesFromChart(el, shape, scheme);
    }

    private static void ReadRadarChart(XElement el, ChartShape shape, PresentationColorScheme scheme)
    {
        shape.ChartType = ChartType.Radar;

        var styleStr = el.Element(C + "radarStyle")?.Attribute("val")?.Value ?? "standard";
        shape.RadarStyle = styleStr switch
        {
            "marker" => RadarStyle.Marker,
            "filled" => RadarStyle.Filled,
            _        => RadarStyle.Standard
        };

        ReadSeriesFromChart(el, shape, scheme);
    }

    private static void ReadBubbleChart(XElement el, ChartShape shape, PresentationColorScheme scheme)
    {
        shape.ChartType = ChartType.Bubble;

        // Bubble charts also have a scatterStyle-like attribute (c:bubble3D is irrelevant for us).
        // Treat as SmoothMarker by default; exact style rarely stored explicitly.
        shape.ScatterStyle = ScatterStyle.Marker;

        ReadBubbleSeriesFromChart(el, shape, scheme);
    }

    // ── Scatter series (x:xVal / c:yVal, no categories axis) ─────────────────

    private static void ReadScatterSeriesFromChart(
        XElement chartEl, ChartShape shape, PresentationColorScheme scheme)
    {
        int seriesIndex = 0;
        foreach (var serEl in chartEl.Elements(C + "ser"))
        {
            var series = new ChartSeries();
            ReadSeriesNameAndColor(serEl, shape, scheme, seriesIndex, series);

            // X values (c:xVal)
            var xValEl = serEl.Element(C + "xVal");
            if (xValEl is not null)
                ReadValues(xValEl, series.XValues);

            // Y values (c:yVal)
            var yValEl = serEl.Element(C + "yVal");
            if (yValEl is not null)
                ReadValues(yValEl, series.Values);

            // If categories are empty but we have X values, build string labels from them
            if (shape.Categories.Count == 0 && series.XValues.Count > 0)
            {
                foreach (var xv in series.XValues)
                    shape.Categories.Add(xv.HasValue
                        ? xv.Value.ToString("G4", System.Globalization.CultureInfo.InvariantCulture)
                        : string.Empty);
            }

            // Per-series data labels override
            series.DataLabels = ReadDataLabels(serEl.Element(C + "dLbls"));

            shape.Series.Add(series);
            seriesIndex++;
        }
    }

    // ── Bubble series (c:xVal / c:yVal / c:bubbleSize) ───────────────────────

    private static void ReadBubbleSeriesFromChart(
        XElement chartEl, ChartShape shape, PresentationColorScheme scheme)
    {
        int seriesIndex = 0;
        foreach (var serEl in chartEl.Elements(C + "ser"))
        {
            var series = new ChartSeries();
            ReadSeriesNameAndColor(serEl, shape, scheme, seriesIndex, series);

            // X values (c:xVal)
            var xValEl = serEl.Element(C + "xVal");
            if (xValEl is not null)
                ReadValues(xValEl, series.XValues);

            // Y values (c:yVal)
            var yValEl = serEl.Element(C + "yVal");
            if (yValEl is not null)
                ReadValues(yValEl, series.Values);

            // Bubble sizes (c:bubbleSize)
            var sizeEl = serEl.Element(C + "bubbleSize");
            if (sizeEl is not null)
                ReadValues(sizeEl, series.BubbleSizes);

            // Per-series data labels override
            series.DataLabels = ReadDataLabels(serEl.Element(C + "dLbls"));

            shape.Series.Add(series);
            seriesIndex++;
        }
    }

    // ── Shared series header reader ───────────────────────────────────────────

    private static void ReadSeriesNameAndColor(
        XElement serEl, ChartShape shape, PresentationColorScheme scheme,
        int seriesIndex, ChartSeries series)
    {
        // Series name
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

        // Fall back to theme accent cycle
        if (series.FillColor is null)
        {
            var slot = AccentSlots[seriesIndex % AccentSlots.Length];
            series.FillColor = new ThemeAwareColor(
                new SrgbColor(0x4F, 0x81, 0xBD),
                new SchemeColorRef { Slot = slot, LumMod = 1.0, LumOff = 0.0 });
        }
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

            // Per-series data labels override
            series.DataLabels = ReadDataLabels(serEl.Element(C + "dLbls"));

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

    // ── Data-label parsing ─────────────────────────────────────────────────────

    private static ChartDataLabels? ReadDataLabels(XElement? dLblsEl)
    {
        if (dLblsEl is null) return null;

        // Check if labels are explicitly turned off (c:showVal val="0" and nothing else)
        bool showVal     = ParseBoolAttr(dLblsEl.Element(C + "showVal"));
        bool showPct     = ParseBoolAttr(dLblsEl.Element(C + "showPercent"));
        bool showCat     = ParseBoolAttr(dLblsEl.Element(C + "showCatName"));
        bool showSer     = ParseBoolAttr(dLblsEl.Element(C + "showSerName"));
        bool showLegend  = ParseBoolAttr(dLblsEl.Element(C + "showLegendKey"));

        // If nothing is shown this is a no-op element — return null to keep model clean.
        if (!showVal && !showPct && !showCat && !showSer && !showLegend)
            return null;

        var posStr = dLblsEl.Element(C + "dLblPos")?.Attribute("val")?.Value;
        var numFmt = dLblsEl.Element(C + "numFmt")?.Attribute("formatCode")?.Value;

        return new ChartDataLabels
        {
            ShowValue        = showVal,
            ShowPercent      = showPct,
            ShowCategoryName = showCat,
            ShowSeriesName   = showSer,
            ShowLegendKey    = showLegend,
            NumberFormat     = string.IsNullOrEmpty(numFmt) ? null : numFmt,
            Position         = posStr switch
            {
                "ctr"      => DataLabelPosition.Center,
                "inEnd"    => DataLabelPosition.InsideEnd,
                "outEnd"   => DataLabelPosition.OutsideEnd,
                "inBase"   => DataLabelPosition.InsideBase,
                "bestFit"  => DataLabelPosition.BestFit,
                "t"        => DataLabelPosition.Above,
                "b"        => DataLabelPosition.Below,
                "l"        => DataLabelPosition.Left,
                "r"        => DataLabelPosition.Right,
                _          => (DataLabelPosition?)null
            }
        };
    }

    private static bool ParseBoolAttr(XElement? el)
    {
        if (el is null) return false;
        var val = el.Attribute("val")?.Value;
        // No val attribute = true (OOXML boolean element default)
        return val is null || val == "1" || val == "true";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int ParseInt(string? s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
