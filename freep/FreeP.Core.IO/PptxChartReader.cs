using System.Globalization;
using System.IO.Compression;
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
            doc = OpcXml.LoadXml(entry);
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
        shape.PlotAreaManualLayout = ReadManualLayout(plotArea.Element(C + "layout"));

        var serIdxMap = DetectChartTypeAndSeries(plotArea, shape, scheme);

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
        shape.DataTable = ReadDataTable(plotArea.Element(C + "dTable"), scheme);

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
                    // All series in this plot group are on the secondary axis.
                    // Resolve each c:ser's c:idx through the idx→ChartSeries map built during reading.
                    // This is correct for combo charts where c:idx values are interleaved across
                    // chart-type groups (e.g. primary group has idx 0,2 and secondary group has idx 1)
                    // — positional indexing into shape.Series would flag the wrong series in that case.
                    foreach (var serEl in plotEl.Elements(C + "ser"))
                    {
                        int serIdx = ParseInt(serEl.Element(C + "idx")?.Attribute("val")?.Value);
                        if (serIdxMap.TryGetValue(serIdx, out var mappedSeries))
                            mappedSeries.OnSecondaryAxis = true;
                        else if (serIdx < shape.Series.Count)
                            // Fall back to positional index for series with no recorded c:idx
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
        if (legendEl is not null)
        {
            shape.LegendManualLayout = ReadManualLayout(legendEl.Element(C + "layout"));
            shape.LegendOverlay = ParseNullableBoolAttr(
                legendEl.Element(C + "overlay")?.Attribute("val")?.Value);
        }

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

    private static Dictionary<int, ChartSeries> DetectChartTypeAndSeries(
        XElement plotArea, ChartShape shape, PresentationColorScheme scheme)
    {
        // idx→ChartSeries map: populated as series are read so secondary-axis detection
        // can resolve a c:idx value to the right ChartSeries regardless of append order.
        var idxMap = new Dictionary<int, ChartSeries>();
        bool primaryFound = false;

        foreach (var el in plotArea.Elements())
        {
            bool isChartType = el.Name.LocalName is
                "barChart" or "bar3DChart" or "lineChart" or "line3DChart" or
                "pieChart" or "pie3DChart" or "ofPieChart" or "doughnutChart" or
                "areaChart" or "area3DChart" or "scatterChart" or "bubbleChart" or
                "stockChart" or "radarChart" or "surfaceChart" or "surface3DChart";

            if (!isChartType) continue;

            if (!primaryFound)
            {
                // First chart-type group: sets shape.ChartType and reads primary series.
                primaryFound = true;
                switch (el.Name.LocalName)
                {
                    case "barChart":
                    case "bar3DChart":
                        ReadBarChart(el, shape, scheme, idxMap); break;
                    case "lineChart":
                    case "line3DChart":
                        ReadLineChart(el, shape, scheme, idxMap); break;
                    case "pieChart":
                    case "pie3DChart":
                    case "ofPieChart":
                        ReadPieChart(el, shape, scheme, idxMap); break;
                    case "doughnutChart":
                        ReadDoughnutChart(el, shape, scheme, idxMap); break;
                    case "areaChart":
                    case "area3DChart":
                        ReadAreaChart(el, shape, scheme, idxMap); break;
                    case "scatterChart":
                        ReadScatterChartDistinct(el, shape, scheme, idxMap); break;
                    case "bubbleChart":
                        ReadBubbleChart(el, shape, scheme, idxMap); break;
                    case "stockChart":
                        ReadLineChart(el, shape, scheme, idxMap); break;    // stock ~= line
                    case "radarChart":
                        ReadRadarChart(el, shape, scheme, idxMap); break;
                    case "surfaceChart":
                    case "surface3DChart":
                        ReadBarChart(el, shape, scheme, idxMap); break;     // surface ~= column best-effort
                }
            }
            else
            {
                // CA4: Secondary chart-type group in a combo chart (e.g. lineChart holding secondary
                // series). Read its c:ser elements without changing shape.ChartType.
                // The secondary axis detection (valAxIds loop below) will then mark these series
                // with OnSecondaryAxis = true via their c:idx values resolved through idxMap.
                // CA4b: Also stamp OverrideChartType on each newly-added series so the renderer
                // knows to draw them with the secondary group's chart type (e.g. Line) rather
                // than the primary chart type (e.g. ColumnClustered).
                // Snapshot which series indices already exist before reading the secondary group.
                var keysBefore = new System.Collections.Generic.HashSet<int>(idxMap.Keys);
                switch (el.Name.LocalName)
                {
                    case "scatterChart":
                        ReadScatterSeriesFromChart(el, shape, scheme, idxMap); break;
                    case "bubbleChart":
                        ReadBubbleSeriesFromChart(el, shape, scheme, idxMap); break;
                    default:
                        // All other combo secondaries (lineChart, barChart, areaChart, etc.)
                        // use the standard cat/val series format.
                        ReadSeriesFromChart(el, shape, scheme, idxMap); break;
                }
                // Derive override chart type from the secondary group element name.
                ChartType? overrideType;
                if (el.Name.LocalName is "lineChart" or "line3DChart" or "stockChart")
                {
                    bool hasMarkers = el.Elements(C + "ser").Any(s =>
                    {
                        var sym = s.Element(C + "marker")?.Element(C + "symbol")?.Attribute("val")?.Value;
                        return sym is null || sym != "none";
                    });
                    overrideType = hasMarkers ? ChartType.LineMarkers : ChartType.Line;
                }
                else if (el.Name.LocalName is "barChart" or "bar3DChart")
                {
                    var barDir   = el.Element(C + "barDir")?.Attribute("val")?.Value ?? "col";
                    var grouping = el.Element(C + "grouping")?.Attribute("val")?.Value ?? "clustered";
                    overrideType = (barDir, grouping) switch
                    {
                        ("col", "stacked")        => ChartType.ColumnStacked,
                        ("col", "percentStacked") => ChartType.ColumnStacked100,
                        ("bar", _)                => ChartType.BarClustered,
                        _                         => ChartType.ColumnClustered
                    };
                }
                else if (el.Name.LocalName is "areaChart" or "area3DChart")
                {
                    var grouping = el.Element(C + "grouping")?.Attribute("val")?.Value ?? "standard";
                    overrideType = grouping == "stacked" ? ChartType.AreaStacked : ChartType.Area;
                }
                else
                {
                    overrideType = null;
                }
                // Stamp the override on series that were just added by this secondary group.
                if (overrideType.HasValue)
                {
                    foreach (var kvp in idxMap)
                        if (!keysBefore.Contains(kvp.Key))
                            kvp.Value.OverrideChartType = overrideType;
                }
            }
        }

        if (!primaryFound)
            shape.ChartType = ChartType.Unknown;

        return idxMap;
    }

    private static void ReadBarChart(XElement el, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        ReadVaryColors(el, shape);

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

        ReadSeriesFromChart(el, shape, scheme, idxMap);
    }

    private static void ReadLineChart(XElement el, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        ReadVaryColors(el, shape);

        // A line chart "has markers" when any series has an explicit marker that is not "none",
        // or has no marker element at all (OOXML default for lineChart is to show markers).
        bool hasMarkers = el.Elements(C + "ser").Any(s =>
        {
            var sym = s.Element(C + "marker")?.Element(C + "symbol")?.Attribute("val")?.Value;
            return sym is null || sym != "none";
        });

        shape.ChartType = hasMarkers ? ChartType.LineMarkers : ChartType.Line;
        ReadSeriesFromChart(el, shape, scheme, idxMap);
    }

    private static void ReadPieChart(XElement el, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        ReadVaryColors(el, shape);
        shape.ChartType = ChartType.Pie;
        shape.FirstSliceAngleDegrees = ReadFirstSliceAngle(el);
        ReadSeriesFromChart(el, shape, scheme, idxMap);
    }

    private static void ReadAreaChart(XElement el, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        ReadVaryColors(el, shape);

        var grouping = el.Element(C + "grouping")?.Attribute("val")?.Value ?? "standard";
        shape.ChartType = grouping == "stacked" ? ChartType.AreaStacked : ChartType.Area;
        ReadSeriesFromChart(el, shape, scheme, idxMap);
    }

    private static void ReadScatterChart(XElement el, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        ReadVaryColors(el, shape);
        shape.ChartType = ChartType.Scatter;
        ReadSeriesFromChart(el, shape, scheme, idxMap);
    }

    private static void ReadDoughnutChart(XElement el, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        ReadVaryColors(el, shape);
        shape.ChartType = ChartType.Doughnut;
        shape.FirstSliceAngleDegrees = ReadFirstSliceAngle(el);

        // c:holeSize val= gives the inner radius as a percentage (default 50).
        var holeSizeStr = el.Element(C + "holeSize")?.Attribute("val")?.Value;
        if (holeSizeStr is not null && int.TryParse(holeSizeStr,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var hs))
            shape.DoughnutHolePercent = Math.Clamp(hs, 0, 90);

        ReadSeriesFromChart(el, shape, scheme, idxMap);
    }

    private static void ReadScatterChartDistinct(XElement el, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        ReadVaryColors(el, shape);
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

        ReadScatterSeriesFromChart(el, shape, scheme, idxMap);
    }

    private static void ReadRadarChart(XElement el, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        ReadVaryColors(el, shape);
        shape.ChartType = ChartType.Radar;

        var styleStr = el.Element(C + "radarStyle")?.Attribute("val")?.Value ?? "standard";
        shape.RadarStyle = styleStr switch
        {
            "marker" => RadarStyle.Marker,
            "filled" => RadarStyle.Filled,
            _        => RadarStyle.Standard
        };

        ReadSeriesFromChart(el, shape, scheme, idxMap);
    }

    private static void ReadBubbleChart(XElement el, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        ReadVaryColors(el, shape);
        shape.ChartType = ChartType.Bubble;

        // Bubble charts also have a scatterStyle-like attribute (c:bubble3D is irrelevant for us).
        // Treat as SmoothMarker by default; exact style rarely stored explicitly.
        shape.ScatterStyle = ScatterStyle.Marker;

        ReadBubbleSeriesFromChart(el, shape, scheme, idxMap);
    }

    private static void ReadVaryColors(XElement chartTypeEl, ChartShape shape) =>
        shape.VaryColors = ParseBoolAttr(chartTypeEl.Element(C + "varyColors"));

    // ── Scatter series (x:xVal / c:yVal, no categories axis) ─────────────────

    private static void ReadScatterSeriesFromChart(
        XElement chartEl, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        int seriesIndex = 0;
        foreach (var serEl in chartEl.Elements(C + "ser"))
        {
            var series = new ChartSeries();
            ReadSeriesNameAndColor(serEl, shape, scheme, seriesIndex, series);

            // X values (c:xVal)
            var xValEl = serEl.Element(C + "xVal");
            if (xValEl is not null)
            {
                series.FormulaReferences.XValues = ReadFormula(xValEl.Element(C + "numRef"));
                ReadValues(xValEl, series.XValues);
            }

            // Y values (c:yVal)
            var yValEl = serEl.Element(C + "yVal");
            if (yValEl is not null)
            {
                series.FormulaReferences.YValues = ReadFormula(yValEl.Element(C + "numRef"));
                ReadValues(yValEl, series.Values);
            }

            ReadPointStyles(serEl, scheme, series);

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

            // Record idx→series mapping for secondary-axis detection.
            // c:idx is the OOXML series index; fall back to append position if absent.
            var idxStr = serEl.Element(C + "idx")?.Attribute("val")?.Value;
            int serIdx = idxStr is not null ? ParseInt(idxStr) : seriesIndex;
            idxMap.TryAdd(serIdx, series);

            seriesIndex++;
        }
    }

    // ── Bubble series (c:xVal / c:yVal / c:bubbleSize) ───────────────────────

    private static void ReadBubbleSeriesFromChart(
        XElement chartEl, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        int seriesIndex = 0;
        foreach (var serEl in chartEl.Elements(C + "ser"))
        {
            var series = new ChartSeries();
            ReadSeriesNameAndColor(serEl, shape, scheme, seriesIndex, series);

            // X values (c:xVal)
            var xValEl = serEl.Element(C + "xVal");
            if (xValEl is not null)
            {
                series.FormulaReferences.XValues = ReadFormula(xValEl.Element(C + "numRef"));
                ReadValues(xValEl, series.XValues);
            }

            // Y values (c:yVal)
            var yValEl = serEl.Element(C + "yVal");
            if (yValEl is not null)
            {
                series.FormulaReferences.YValues = ReadFormula(yValEl.Element(C + "numRef"));
                ReadValues(yValEl, series.Values);
            }

            // Bubble sizes (c:bubbleSize)
            var sizeEl = serEl.Element(C + "bubbleSize");
            if (sizeEl is not null)
            {
                series.FormulaReferences.BubbleSizes = ReadFormula(sizeEl.Element(C + "numRef"));
                ReadValues(sizeEl, series.BubbleSizes);
            }

            ReadPointStyles(serEl, scheme, series);

            // Per-series data labels override
            series.DataLabels = ReadDataLabels(serEl.Element(C + "dLbls"));

            shape.Series.Add(series);

            // Record idx→series mapping for secondary-axis detection.
            var idxStr = serEl.Element(C + "idx")?.Attribute("val")?.Value;
            int serIdx = idxStr is not null ? ParseInt(idxStr) : seriesIndex;
            idxMap.TryAdd(serIdx, series);

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
            series.FormulaReferences.SeriesName = ReadFormula(txEl.Element(C + "strRef"));
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
            ReadSeriesShapeProperties(spPr, scheme, series);

        series.MarkerStyle = ReadMarkerStyle(serEl.Element(C + "marker"), scheme);

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
        XElement chartEl, ChartShape shape, PresentationColorScheme scheme,
        Dictionary<int, ChartSeries> idxMap)
    {
        int seriesIndex = 0;
        foreach (var serEl in chartEl.Elements(C + "ser"))
        {
            var series = new ChartSeries();

            // Series name (c:tx → strRef cache or direct c:v)
            var txEl = serEl.Element(C + "tx");
            if (txEl is not null)
            {
                series.FormulaReferences.SeriesName = ReadFormula(txEl.Element(C + "strRef"));
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
                ReadSeriesShapeProperties(spPr, scheme, series);

            series.MarkerStyle = ReadMarkerStyle(serEl.Element(C + "marker"), scheme);

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
            var seriesCatEl = serEl.Element(C + "cat") ?? serEl.Element(C + "xVal");
            if (seriesCatEl is not null)
                series.FormulaReferences.Category = ReadCategoryFormula(seriesCatEl);

            // Values (c:val or c:yVal)
            var valEl = serEl.Element(C + "val") ?? serEl.Element(C + "yVal");
            if (valEl is not null)
            {
                series.FormulaReferences.Values = ReadFormula(valEl.Element(C + "numRef"));
                ReadValues(valEl, series.Values);
            }

            // Per-point colors (c:dPt) — mainly used by pie/doughnut charts
            foreach (var dptEl in serEl.Elements(C + "dPt"))
            {
                var idx = ParseInt(dptEl.Element(C + "idx")?.Attribute("val")?.Value);
                var dptSpPr = dptEl.Element(C + "spPr");
                ReadPointColorCompatibility(dptSpPr, scheme, series, idx);

                var pointStyle = ReadPointStyle(dptSpPr, dptEl.Element(C + "marker"), scheme);
                if (pointStyle is not null)
                    series.PointStyles[idx] = pointStyle;
            }

            // Per-series data labels override
            series.DataLabels = ReadDataLabels(serEl.Element(C + "dLbls"));

            shape.Series.Add(series);

            // Record idx→series mapping for secondary-axis detection.
            // c:idx is the OOXML series index; fall back to append position if absent.
            var idxStr = serEl.Element(C + "idx")?.Attribute("val")?.Value;
            int serIdx = idxStr is not null ? ParseInt(idxStr) : seriesIndex;
            idxMap.TryAdd(serIdx, series);

            seriesIndex++;
        }
    }

    private static void ReadSeriesShapeProperties(
        XElement spPr,
        PresentationColorScheme scheme,
        ChartSeries series)
    {
        var fill = PptxColorReader.TryReadFill(spPr, scheme);
        switch (fill)
        {
            case ShapeFill.Solid solid:
                series.FillColor = solid.Color;
                break;
            case ShapeFill.Gradient gradient:
                series.Fill = gradient;
                break;
            case ShapeFill.Pattern pattern:
                series.Fill = pattern;
                break;
        }

        series.LineStyle = ReadLineStyle(spPr.Element(A + "ln"), scheme);
    }

    private static void ReadPointStyles(
        XElement serEl,
        PresentationColorScheme scheme,
        ChartSeries series)
    {
        foreach (var dptEl in serEl.Elements(C + "dPt"))
        {
            var idx = ParseInt(dptEl.Element(C + "idx")?.Attribute("val")?.Value);
            var dptSpPr = dptEl.Element(C + "spPr");
            ReadPointColorCompatibility(dptSpPr, scheme, series, idx);

            var pointStyle = ReadPointStyle(dptSpPr, dptEl.Element(C + "marker"), scheme);
            if (pointStyle is not null)
                series.PointStyles[idx] = pointStyle;
        }
    }

    private static ChartLineStyle? ReadLineStyle(XElement? lnEl, PresentationColorScheme scheme)
    {
        if (lnEl is null)
            return null;

        var style = new ChartLineStyle();
        if (lnEl.Element(A + "noFill") is not null)
            style.NoFill = true;

        var solidFill = lnEl.Element(A + "solidFill");
        if (solidFill is not null)
            style.Color = PptxColorReader.TryReadColor(solidFill, scheme);

        style.Dash = ReadLineDash(lnEl.Element(A + "prstDash")?.Attribute("val")?.Value);

        if (long.TryParse(lnEl.Attribute("w")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var widthEmu) && widthEmu > 0)
            style.WidthPt = DrawingMlUnits.EmuToPoints(widthEmu);

        return style;
    }

    private static OutlineDash ReadLineDash(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "dash" => OutlineDash.Dash,
            "dot" => OutlineDash.Dot,
            "dashdot" => OutlineDash.DashDot,
            "lgdash" => OutlineDash.LongDash,
            "lgdashdot" => OutlineDash.LongDashDot,
            "lgdashdotdot" => OutlineDash.LongDashDotDot,
            "sysdash" => OutlineDash.SystemDash,
            "sysdot" => OutlineDash.SystemDot,
            "sysdashdot" => OutlineDash.SystemDashDot,
            _ => OutlineDash.Solid
        };

    private static ChartMarkerStyle? ReadMarkerStyle(XElement? markerEl, PresentationColorScheme scheme)
    {
        if (markerEl is null)
            return null;

        var style = new ChartMarkerStyle();
        var symbol = ReadMarkerSymbol(markerEl.Element(C + "symbol")?.Attribute("val")?.Value);
        if (symbol.HasValue)
            style.Symbol = symbol.Value;

        if (double.TryParse(markerEl.Element(C + "size")?.Attribute("val")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var sizePt))
            style.SizePt = sizePt;

        var spPr = markerEl.Element(C + "spPr");
        if (spPr is not null)
            ApplyMarkerShapeProperties(spPr, scheme, style);

        return style;
    }

    private static ChartPointStyle? ReadPointStyle(
        XElement? spPr,
        XElement? markerEl,
        PresentationColorScheme scheme)
    {
        ChartPointStyle? pointStyle = null;
        if (spPr is not null)
        {
            pointStyle = new ChartPointStyle();
            var fill = PptxColorReader.TryReadFill(spPr, scheme);
            switch (fill)
            {
                case ShapeFill.Solid solid:
                    pointStyle.FillColor = solid.Color;
                    break;
                case ShapeFill.Gradient gradient:
                    pointStyle.Fill = gradient;
                    break;
                case ShapeFill.Pattern pattern:
                    pointStyle.Fill = pattern;
                    break;
            }

            var lineStyle = ReadLineStyle(spPr.Element(A + "ln"), scheme);
            if (lineStyle is not null)
            {
                pointStyle.StrokeColor = lineStyle.Color;
                pointStyle.StrokeWidthPt = lineStyle.WidthPt;
            }
        }

        var markerStyle = ReadMarkerStyle(markerEl, scheme);
        if (markerStyle is not null)
        {
            pointStyle ??= new ChartPointStyle();
            pointStyle.Marker = markerStyle;
        }

        return pointStyle;
    }

    private static void ApplyMarkerShapeProperties(
        XElement spPr,
        PresentationColorScheme scheme,
        ChartMarkerStyle style)
    {
        if (spPr.Element(A + "noFill") is not null)
            style.NoFill = true;

        var fill = PptxColorReader.TryReadFill(spPr, scheme);
        switch (fill)
        {
            case ShapeFill.Solid solid:
                style.FillColor = solid.Color;
                break;
            case ShapeFill.Gradient gradient:
                style.Fill = gradient;
                break;
            case ShapeFill.Pattern pattern:
                style.Fill = pattern;
                break;
        }

        var line = ReadLineStyle(spPr.Element(A + "ln"), scheme);
        if (line is not null)
        {
            style.NoStroke = line.NoFill;
            style.StrokeColor = line.Color;
            style.StrokeWidthPt = line.WidthPt;
        }
    }

    private static void ReadPointColorCompatibility(
        XElement? spPr,
        PresentationColorScheme scheme,
        ChartSeries series,
        int pointIndex)
    {
        var dptSolid = spPr?.Element(A + "solidFill");
        if (dptSolid is null)
            return;

        var color = PptxColorReader.TryReadColor(dptSolid, scheme);
        if (color is not null)
            series.PointColors[pointIndex] = color;
    }

    private static int? ReadFirstSliceAngle(XElement chartEl)
    {
        var value = ParseNullableInt(chartEl.Element(C + "firstSliceAng")?.Attribute("val")?.Value);
        return value.HasValue ? Math.Clamp(value.Value, 0, 360) : null;
    }

    private static ChartMarkerSymbol? ReadMarkerSymbol(string? value) =>
        value switch
        {
            "auto" => ChartMarkerSymbol.Auto,
            "circle" => ChartMarkerSymbol.Circle,
            "dash" => ChartMarkerSymbol.Dash,
            "diamond" => ChartMarkerSymbol.Diamond,
            "dot" => ChartMarkerSymbol.Dot,
            "none" => ChartMarkerSymbol.None,
            "picture" => ChartMarkerSymbol.Picture,
            "plus" => ChartMarkerSymbol.Plus,
            "square" => ChartMarkerSymbol.Square,
            "star" => ChartMarkerSymbol.Star,
            "triangle" => ChartMarkerSymbol.Triangle,
            "x" => ChartMarkerSymbol.X,
            _ => null
        };

    private static string? ReadCategoryFormula(XElement catEl) =>
        ReadFormula(catEl.Element(C + "strRef")) ??
        ReadFormula(catEl.Element(C + "numRef"));

    private static string? ReadFormula(XElement? refEl)
    {
        var value = refEl?.Element(C + "f")?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
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

        var numFmt = axEl.Element(C + "numFmt");
        if (numFmt is not null)
        {
            var formatCode = numFmt.Attribute("formatCode")?.Value;
            axis.NumberFormatCode = string.IsNullOrWhiteSpace(formatCode) ? null : formatCode;
            axis.NumberFormatSourceLinked =
                ParseNullableBoolAttr(numFmt.Attribute("sourceLinked")?.Value);
        }

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

    private static ChartDataTableSettings? ReadDataTable(XElement? dTableEl, PresentationColorScheme scheme)
    {
        if (dTableEl is null) return null;

        return new ChartDataTableSettings
        {
            ShowHorizontalBorder = ParseBoolAttr(dTableEl.Element(C + "showHorzBorder")),
            ShowVerticalBorder   = ParseBoolAttr(dTableEl.Element(C + "showVertBorder")),
            ShowOutlineBorder    = ParseBoolAttr(dTableEl.Element(C + "showOutline")),
            ShowLegendKeys       = ParseBoolAttr(dTableEl.Element(C + "showKeys")),
            BackgroundFill       = ReadDataTableBackgroundFill(dTableEl, scheme),
            BorderOutline        = ReadDataTableBorderOutline(dTableEl, scheme),
            TextStyle            = ReadDataTableTextStyle(dTableEl, scheme),
        };
    }

    private static ChartManualLayout? ReadManualLayout(XElement? layoutEl)
    {
        var manualLayoutEl = layoutEl?.Element(C + "manualLayout");
        if (manualLayoutEl is null)
            return null;

        var layout = new ChartManualLayout
        {
            LayoutTarget = EmptyToNull(manualLayoutEl.Element(C + "layoutTarget")?.Attribute("val")?.Value),
            XMode = ReadManualLayoutMode(manualLayoutEl.Element(C + "xMode")?.Attribute("val")?.Value),
            YMode = ReadManualLayoutMode(manualLayoutEl.Element(C + "yMode")?.Attribute("val")?.Value),
            WidthMode = ReadManualLayoutMode(manualLayoutEl.Element(C + "wMode")?.Attribute("val")?.Value),
            HeightMode = ReadManualLayoutMode(manualLayoutEl.Element(C + "hMode")?.Attribute("val")?.Value),
            X = ParseDouble(manualLayoutEl.Element(C + "x")?.Attribute("val")?.Value),
            Y = ParseDouble(manualLayoutEl.Element(C + "y")?.Attribute("val")?.Value),
            Width = ParseDouble(manualLayoutEl.Element(C + "w")?.Attribute("val")?.Value),
            Height = ParseDouble(manualLayoutEl.Element(C + "h")?.Attribute("val")?.Value),
        };

        return layout.LayoutTarget is not null ||
               layout.X.HasValue ||
               layout.Y.HasValue ||
               layout.Width.HasValue ||
               layout.Height.HasValue ||
               layout.XMode != ChartManualLayoutMode.Factor ||
               layout.YMode != ChartManualLayoutMode.Factor ||
               layout.WidthMode != ChartManualLayoutMode.Factor ||
               layout.HeightMode != ChartManualLayoutMode.Factor
            ? layout
            : null;
    }

    private static ChartManualLayoutMode ReadManualLayoutMode(string? value) =>
        value switch
        {
            null or "factor" => ChartManualLayoutMode.Factor,
            "edge" => ChartManualLayoutMode.Edge,
            _ => ChartManualLayoutMode.Unsupported
        };

    private static ShapeFill? ReadDataTableBackgroundFill(XElement dTableEl, PresentationColorScheme scheme)
    {
        var spPr = dTableEl.Element(C + "spPr");
        return spPr is null ? null : PptxColorReader.TryReadFill(spPr, scheme);
    }

    private static ShapeOutline? ReadDataTableBorderOutline(XElement dTableEl, PresentationColorScheme scheme)
    {
        // Preserve every outline kind TryReadOutline can produce, including
        // ShapeOutline.GradientVisible (a:ln/a:gradFill). Previously only
        // Visible/None were kept here and a gradient data-table border was
        // silently discarded, causing it to be replaced by the default gray
        // outline on round-trip.
        return PptxColorReader.TryReadOutline(
            dTableEl.Element(C + "spPr")?.Element(A + "ln"),
            scheme);
    }

    private static ChartTextStyle? ReadDataTableTextStyle(XElement dTableEl, PresentationColorScheme scheme)
    {
        var defRPr = dTableEl
            .Element(C + "txPr")
            ?.Element(A + "p")
            ?.Element(A + "pPr")
            ?.Element(A + "defRPr");
        if (defRPr is null)
            return null;

        double? fontSizePt = null;
        if (int.TryParse(defRPr.Attribute("sz")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sz)
            && sz > 0)
        {
            fontSizePt = sz / 100.0;
        }

        bool? bold = ParseNullableBoolAttr(defRPr.Attribute("b")?.Value);
        bool? italic = ParseNullableBoolAttr(defRPr.Attribute("i")?.Value);
        var color = PptxColorReader.TryReadColor(defRPr.Element(A + "solidFill"), scheme);
        string? fontFamily = defRPr.Element(A + "latin")?.Attribute("typeface")?.Value;

        return fontSizePt.HasValue || bold.HasValue || italic.HasValue || color is not null || fontFamily is not null
            ? new ChartTextStyle
            {
                FontSizePt = fontSizePt,
                Bold       = bold,
                Italic     = italic,
                Color      = color,
                FontFamily = fontFamily,
            }
            : null;
    }

    private static bool? ParseNullableBoolAttr(string? value) =>
        value switch
        {
            null => null,
            "1" or "true" => true,
            "0" or "false" => false,
            _ => null
        };

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

    private static int? ParseNullableInt(string? s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static double? ParseDouble(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
