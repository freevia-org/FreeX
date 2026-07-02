using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Free.Shared.Opc;
using FreeP.Core.Model;

namespace FreeP.Core.IO;

/// <summary>
/// Writes a <see cref="ChartShape"/> model as a <c>ppt/charts/chartN.xml</c> part
/// (plus a minimal embedded workbook stub) into a .pptx <see cref="ZipArchive"/>.
///
/// Returns the OPC part path and relationship ID so the caller can wire the chart
/// into the slide's graphicFrame and rels.
/// </summary>
internal static class PptxChartWriter
{
    private static readonly XNamespace C    = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace A    = PptxColorReader.A;
    private static readonly XNamespace R    = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgR = "http://schemas.openxmlformats.org/package/2006/relationships";

    internal const string ChartRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
    internal const string ChartCT =
        "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";
    internal const string ChartWorkbookCT =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string PackageRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package";

    // ── OOXML write settings (UTF-8 no BOM, indented) ────────────────────────
    private static readonly System.Xml.XmlWriterSettings XmlSettings = new()
    {
        Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = true,
        OmitXmlDeclaration = false,
        CloseOutput = false
    };

    /// <summary>
    /// Writes the chart part and its embedded workbook into <paramref name="archive"/>,
    /// using <paramref name="chartIndex"/> to form unique part paths.
    /// </summary>
    /// <returns>The OPC path of the written chart part (e.g. "ppt/charts/chart1.xml").</returns>
    internal static string WriteChartPart(
        ZipArchive archive,
        ChartShape chart,
        int chartIndex,
        PptxPackageSnapshot? packageSnapshot = null)
    {
        var chartPath = $"ppt/charts/chart{chartIndex}.xml";

        // Write chart XML
        var chartDoc = BuildChartDoc(chart);
        if (chart.RegenerateWorkbookOnSave)
        {
            const string workbookRelId = "rIdWorkbook1";
            AddExternalData(chartDoc, workbookRelId);
            WriteEntry(archive, chartPath, chartDoc);
            WriteRegeneratedWorkbook(archive, chart, GetRegeneratedWorkbookPath(chartIndex));
            WriteRegeneratedWorkbookRelationship(archive, chartPath, workbookRelId, chartIndex);
            return chartPath;
        }

        MergePreservedExternalData(chartDoc, packageSnapshot, chartPath);
        WriteEntry(archive, chartPath, chartDoc);
        WritePreservedWorkbookRelationships(archive, packageSnapshot, chartPath);

        return chartPath;
    }

    internal static string GetRegeneratedWorkbookPath(int chartIndex) =>
        $"ppt/embeddings/chartWorkbook{chartIndex}.xlsx";

    // ── chart.xml ────────────────────────────────────────────────────────────

    private static XDocument BuildChartDoc(ChartShape chart)
    {
        var plotArea = BuildPlotArea(chart);
        var legendEl = chart.Legend.HasValue
            ? new XElement(C + "legend",
                new XElement(C + "legendPos",
                    new XAttribute("val", chart.Legend.Value switch
                    {
                        LegendPosition.Left   => "l",
                        LegendPosition.Top    => "t",
                        LegendPosition.Bottom => "b",
                        _                     => "r"
                    })))
            : null;

        var titleEl = chart.Title is not null
            ? BuildTitleEl(chart.Title)
            : null;

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(C + "chartSpace",
                NsAttr("c", C), NsAttr("a", A), NsAttr("r", R),
                new XElement(C + "chart",
                    titleEl,
                    new XElement(C + "autoTitleDeleted", new XAttribute("val", chart.Title is null ? "1" : "0")),
                    plotArea,
                    legendEl,
                    new XElement(C + "plotVisOnly", new XAttribute("val", "1")))));
    }

    private static XElement BuildTitleEl(string title) =>
        new XElement(C + "title",
            new XElement(C + "tx",
                new XElement(C + "rich",
                    new XElement(A + "bodyPr"),
                    new XElement(A + "lstStyle"),
                    new XElement(A + "p",
                        new XElement(A + "r",
                            new XElement(A + "t", title))))),
            new XElement(C + "overlay", new XAttribute("val", "0")));

    // Axis ID constants for the primary and secondary axis pairs.
    // Primary: catAx id=1, valAx id=2. Secondary: catAx id=4 (hidden), valAx id=3.
    private const int PrimaryCatAxId   = 1;
    private const int PrimaryValAxId   = 2;
    private const int SecondaryValAxId = 3;
    private const int SecondaryCatAxId = 4;  // hidden phantom cat axis for the secondary plot group

    private static XElement BuildPlotArea(ChartShape chart)
    {
        bool isScatterLike = chart.ChartType is ChartType.Scatter or ChartType.Bubble;
        bool noCatAx       = chart.ChartType is ChartType.Pie or ChartType.Doughnut or ChartType.Unknown;

        // CA1: split series by OnSecondaryAxis only when there IS a SecondaryValueAxis and at
        // least one secondary series. All other charts use a single group (no regression).
        bool hasSecondary = chart.SecondaryValueAxis is not null
                            && !noCatAx
                            && !isScatterLike
                            && chart.Series.Any(s => s.OnSecondaryAxis);

        var primarySeries   = chart.Series.Where(s => !s.OnSecondaryAxis).ToList();
        var secondarySeries = hasSecondary
            ? chart.Series.Where(s => s.OnSecondaryAxis).ToList()
            : new List<ChartSeries>();

        // Build series elements; re-index by their global position so idx/order stay consistent.
        int serOffset = 0;
        List<XElement> primarySeriesEls;
        if (isScatterLike)
            primarySeriesEls = primarySeries.Select((s, i) => BuildScatterSeriesEl(chart, s, serOffset + i)).ToList();
        else
            primarySeriesEls = primarySeries.Select((s, i) => BuildSeriesEl(chart, s, serOffset + i)).ToList();
        serOffset += primarySeries.Count;

        var secondarySeriesEls = secondarySeries
            .Select((s, i) => BuildSeriesEl(chart, s, serOffset + i)).ToList();

        // Build the primary chart-type element (references the primary axis pair).
        XElement? primaryChartTypeEl = BuildChartTypeEl(
            chart, primarySeriesEls, isScatterLike,
            catAxId: PrimaryCatAxId, valAxId: PrimaryValAxId);

        // CA1: inject chart-level data labels into the PRIMARY plot-type element only.
        if (primaryChartTypeEl is not null)
        {
            var chartDlblsEl = BuildDataLabelsEl(chart.DataLabels, chart.ChartType);
            if (chartDlblsEl is not null)
            {
                // Insert dLbls before the first c:axId child (or at end if none).
                var firstAxId = primaryChartTypeEl.Elements(C + "axId").FirstOrDefault();
                if (firstAxId is not null)
                    firstAxId.AddBeforeSelf(chartDlblsEl);
                else
                    primaryChartTypeEl.Add(chartDlblsEl);
            }
        }

        // CA1: secondary plot group — always a lineChart referencing the secondary axis pair.
        // PowerPoint requires every valAx to be referenced by at least one plot group.
        XElement? secondaryChartTypeEl = null;
        if (hasSecondary)
        {
            secondaryChartTypeEl = new XElement(C + "lineChart",
                new XElement(C + "grouping",   new XAttribute("val", "standard")),
                new XElement(C + "varyColors", new XAttribute("val", "0")),
                secondarySeriesEls,
                new XElement(C + "axId", new XAttribute("val", SecondaryCatAxId)),
                new XElement(C + "axId", new XAttribute("val", SecondaryValAxId)));
        }

        // ── Primary axis elements ─────────────────────────────────────────────
        var catAxEl  = !noCatAx && !isScatterLike
            ? BuildCatAxEl(chart.CategoryAxis, PrimaryCatAxId, PrimaryValAxId)
            : null;
        var valAxEl  = !noCatAx
            ? BuildValAxEl(chart.ValueAxis, PrimaryValAxId, PrimaryCatAxId)
            : null;
        // Scatter/bubble X value axis lives at bottom (axPos="b").
        var xValAxEl = isScatterLike
            ? BuildValAxEl(chart.CategoryAxis, PrimaryCatAxId, PrimaryValAxId, axPos: "b")
            : null;

        // ── Secondary axis elements (only when a real secondary group was emitted) ─
        // Secondary catAx: hidden phantom axis (delete=1) so PowerPoint knows what axis the
        // secondary valAx crosses; it must appear before the secondary valAx in the plotArea.
        XElement? secCatAxEl = null;
        XElement? secValAxEl = null;
        if (hasSecondary)
        {
            secCatAxEl = new XElement(C + "catAx",
                new XElement(C + "axId",  new XAttribute("val", SecondaryCatAxId)),
                new XElement(C + "scaling",
                    new XElement(C + "orientation", new XAttribute("val", "minMax"))),
                new XElement(C + "delete",  new XAttribute("val", "1")),  // hidden
                new XElement(C + "axPos",   new XAttribute("val", "b")),
                new XElement(C + "crossAx", new XAttribute("val", SecondaryValAxId)));

            secValAxEl = BuildValAxEl(
                chart.SecondaryValueAxis!, SecondaryValAxId, SecondaryCatAxId,
                axPos: "r", crosses: "max");
        }

        return new XElement(C + "plotArea",
            primaryChartTypeEl,
            secondaryChartTypeEl,
            xValAxEl,
            catAxEl,
            valAxEl,
            secCatAxEl,
            secValAxEl);
    }

    /// <summary>Dispatches to the correct chart-type builder using the given axId pair.</summary>
    private static XElement? BuildChartTypeEl(
        ChartShape chart, List<XElement> seriesEls, bool isScatterLike,
        int catAxId, int valAxId)
    {
        return chart.ChartType switch
        {
            ChartType.BarClustered or ChartType.BarStacked or ChartType.BarStacked100 =>
                BuildBarChartEl(chart, seriesEls, isBar: true,  catAxId, valAxId),
            ChartType.ColumnClustered or ChartType.ColumnStacked or ChartType.ColumnStacked100 =>
                BuildBarChartEl(chart, seriesEls, isBar: false, catAxId, valAxId),
            ChartType.Line or ChartType.LineMarkers =>
                BuildLineChartEl(chart, seriesEls, catAxId, valAxId),
            ChartType.Pie =>
                BuildPieChartEl(chart, seriesEls),
            ChartType.Doughnut =>
                BuildDoughnutChartEl(chart, seriesEls),
            ChartType.Area or ChartType.AreaStacked =>
                BuildAreaChartEl(chart, seriesEls, catAxId, valAxId),
            ChartType.Scatter =>
                BuildScatterChartEl(chart, seriesEls, catAxId, valAxId),
            ChartType.Bubble =>
                BuildBubbleChartEl(chart, seriesEls, catAxId, valAxId),
            ChartType.Radar =>
                BuildRadarChartEl(chart, seriesEls, catAxId, valAxId),
            _ =>
                BuildBarChartEl(chart, seriesEls, isBar: false, catAxId, valAxId)
        };
    }

    private static XElement BuildBarChartEl(ChartShape chart, List<XElement> seriesEls, bool isBar,
        int catAxId = PrimaryCatAxId, int valAxId = PrimaryValAxId)
    {
        var grouping = chart.ChartType switch
        {
            ChartType.ColumnStacked or ChartType.BarStacked => "stacked",
            ChartType.ColumnStacked100 or ChartType.BarStacked100 => "percentStacked",
            _ => "clustered"
        };

        return new XElement(C + "barChart",
            new XElement(C + "barDir", new XAttribute("val", isBar ? "bar" : "col")),
            new XElement(C + "grouping", new XAttribute("val", grouping)),
            seriesEls,
            new XElement(C + "axId", new XAttribute("val", catAxId)),
            new XElement(C + "axId", new XAttribute("val", valAxId)));
    }

    private static XElement BuildLineChartEl(ChartShape chart, List<XElement> seriesEls,
        int catAxId = PrimaryCatAxId, int valAxId = PrimaryValAxId) =>
        new XElement(C + "lineChart",
            new XElement(C + "grouping", new XAttribute("val", "standard")),
            seriesEls,
            new XElement(C + "axId", new XAttribute("val", catAxId)),
            new XElement(C + "axId", new XAttribute("val", valAxId)));

    private static XElement BuildPieChartEl(ChartShape chart, List<XElement> seriesEls) =>
        new XElement(C + "pieChart",
            seriesEls);

    private static XElement BuildAreaChartEl(ChartShape chart, List<XElement> seriesEls,
        int catAxId = PrimaryCatAxId, int valAxId = PrimaryValAxId) =>
        new XElement(C + "areaChart",
            new XElement(C + "grouping",
                new XAttribute("val", chart.ChartType == ChartType.AreaStacked ? "stacked" : "standard")),
            seriesEls,
            new XElement(C + "axId", new XAttribute("val", catAxId)),
            new XElement(C + "axId", new XAttribute("val", valAxId)));

    private static XElement BuildScatterChartEl(ChartShape chart, List<XElement> seriesEls,
        int catAxId = PrimaryCatAxId, int valAxId = PrimaryValAxId) =>
        new XElement(C + "scatterChart",
            new XElement(C + "scatterStyle",
                new XAttribute("val", chart.ScatterStyle switch
                {
                    ScatterStyle.Marker       => "marker",
                    ScatterStyle.Line         => "line",
                    ScatterStyle.Smooth       => "smooth",
                    ScatterStyle.SmoothMarker => "smoothMarker",
                    _                         => "lineMarker"
                })),
            seriesEls,
            new XElement(C + "axId", new XAttribute("val", catAxId)),
            new XElement(C + "axId", new XAttribute("val", valAxId)));

    private static XElement BuildDoughnutChartEl(ChartShape chart, List<XElement> seriesEls) =>
        new XElement(C + "doughnutChart",
            new XElement(C + "holeSize",
                new XAttribute("val", chart.DoughnutHolePercent.ToString(CultureInfo.InvariantCulture))),
            seriesEls);

    private static XElement BuildRadarChartEl(ChartShape chart, List<XElement> seriesEls,
        int catAxId = PrimaryCatAxId, int valAxId = PrimaryValAxId) =>
        new XElement(C + "radarChart",
            new XElement(C + "radarStyle",
                new XAttribute("val", chart.RadarStyle switch
                {
                    RadarStyle.Marker => "marker",
                    RadarStyle.Filled => "filled",
                    _                 => "standard"
                })),
            seriesEls,
            new XElement(C + "axId", new XAttribute("val", catAxId)),
            new XElement(C + "axId", new XAttribute("val", valAxId)));

    private static XElement BuildBubbleChartEl(ChartShape chart, List<XElement> seriesEls,
        int catAxId = PrimaryCatAxId, int valAxId = PrimaryValAxId) =>
        new XElement(C + "bubbleChart",
            seriesEls,
            new XElement(C + "axId", new XAttribute("val", catAxId)),
            new XElement(C + "axId", new XAttribute("val", valAxId)));

    // CA2+CA3: Build dLbls in CT_DLbls schema order and gate dLblPos by chart type.
    // CT_DLbls order: numFmt, spPr, txPr, dLblPos, showLegendKey, showVal,
    //                 showCatName, showSerName, showPercent, showBubbleSize, separator.
    private static XElement? BuildDataLabelsEl(ChartDataLabels? labels,
        ChartType chartType = ChartType.ColumnClustered)
    {
        if (labels is null || !labels.HasAny) return null;

        var el = new XElement(C + "dLbls");

        // CA2: numFmt FIRST (before dLblPos and show* flags).
        if (!string.IsNullOrEmpty(labels.NumberFormat))
            el.Add(new XElement(C + "numFmt",
                new XAttribute("formatCode", labels.NumberFormat),
                new XAttribute("sourceLinked", "0")));

        // CA2: dLblPos SECOND (before show* flags).
        // CA3: Gate by chart type / grouping — only emit positions valid for the target type.
        if (labels.Position.HasValue)
        {
            string? posVal = labels.Position.Value switch
            {
                DataLabelPosition.Center     => "ctr",
                DataLabelPosition.InsideEnd  => "inEnd",
                DataLabelPosition.OutsideEnd => "outEnd",
                DataLabelPosition.InsideBase => "inBase",
                DataLabelPosition.BestFit    => "bestFit",
                DataLabelPosition.Above      => "t",
                DataLabelPosition.Below      => "b",
                DataLabelPosition.Left       => "l",
                DataLabelPosition.Right      => "r",
                _                            => null
            };

            // CA3: restrict dLblPos per chart-type validity rules.
            posVal = GateDLblPos(posVal, chartType);
            if (posVal is not null)
                el.Add(new XElement(C + "dLblPos", new XAttribute("val", posVal)));
        }

        // CA2: show* flags LAST (in schema order).
        if (labels.ShowLegendKey)
            el.Add(new XElement(C + "showLegendKey", new XAttribute("val", "1")));
        if (labels.ShowValue)
            el.Add(new XElement(C + "showVal", new XAttribute("val", "1")));
        if (labels.ShowCategoryName)
            el.Add(new XElement(C + "showCatName", new XAttribute("val", "1")));
        if (labels.ShowSeriesName)
            el.Add(new XElement(C + "showSerName", new XAttribute("val", "1")));
        if (labels.ShowPercent)
            el.Add(new XElement(C + "showPercent", new XAttribute("val", "1")));

        return el;
    }

    /// <summary>
    /// CA3: Returns a valid dLblPos value for the given chart type, or null to suppress the element.
    /// Rules:
    ///   Stacked bar/column  → only "ctr" is valid (OOXML §21.2.2.44); coerce any other to null (suppress).
    ///   Pie / Doughnut      → only ctr / inEnd / outEnd / bestFit; suppress inBase/directional.
    ///   All other types     → pass through as-is.
    /// </summary>
    private static string? GateDLblPos(string? posVal, ChartType chartType)
    {
        if (posVal is null) return null;

        bool isStackedBar = chartType is ChartType.BarStacked or ChartType.BarStacked100
                                      or ChartType.ColumnStacked or ChartType.ColumnStacked100;
        if (isStackedBar)
        {
            // Only "ctr" is valid for stacked; outEnd/inEnd/inBase → suppress.
            return posVal == "ctr" ? "ctr" : null;
        }

        bool isPieLike = chartType is ChartType.Pie or ChartType.Doughnut;
        if (isPieLike)
        {
            // Pie allows: ctr, inEnd, outEnd, bestFit.  Suppress directional (t/b/l/r) and inBase.
            return posVal is "ctr" or "inEnd" or "outEnd" or "bestFit" ? posVal : null;
        }

        return posVal;
    }

    // ── Scatter/bubble series element (uses xVal/yVal/bubbleSize instead of cat/val) ────

    private static XElement BuildScatterSeriesEl(ChartShape chart, ChartSeries series, int index)
    {
        var el = new XElement(C + "ser",
            new XElement(C + "idx",   new XAttribute("val", index)),
            new XElement(C + "order", new XAttribute("val", index)));

        // Series name
        el.Add(new XElement(C + "tx",
            new XElement(C + "strRef",
                new XElement(C + "strCache",
                    new XElement(C + "ptCount", new XAttribute("val", "1")),
                    new XElement(C + "pt",
                        new XAttribute("idx", "0"),
                        new XElement(C + "v", series.Name))))));

        // Fill color
        if (series.FillColor is not null)
            el.Add(new XElement(C + "spPr",
                new XElement(A + "solidFill", BuildColorEl(series.FillColor))));

        // Per-series data labels
        var serDlblsEl2 = BuildDataLabelsEl(series.DataLabels, chart.ChartType);
        if (serDlblsEl2 is not null) el.Add(serDlblsEl2);

        // X values (c:xVal)
        if (series.XValues.Count > 0)
        {
            el.Add(new XElement(C + "xVal",
                new XElement(C + "numRef",
                    new XElement(C + "numCache",
                        new XElement(C + "formatCode", "General"),
                        new XElement(C + "ptCount", new XAttribute("val", series.XValues.Count)),
                        series.XValues.Select((v, vi) =>
                            v.HasValue
                                ? new XElement(C + "pt",
                                    new XAttribute("idx", vi),
                                    new XElement(C + "v", v.Value.ToString("G", CultureInfo.InvariantCulture)))
                                : null).Where(e => e is not null)))));
        }

        // Y values (c:yVal)
        if (series.Values.Count > 0)
        {
            el.Add(new XElement(C + "yVal",
                new XElement(C + "numRef",
                    new XElement(C + "numCache",
                        new XElement(C + "formatCode", "General"),
                        new XElement(C + "ptCount", new XAttribute("val", series.Values.Count)),
                        series.Values.Select((v, vi) =>
                            v.HasValue
                                ? new XElement(C + "pt",
                                    new XAttribute("idx", vi),
                                    new XElement(C + "v", v.Value.ToString("G", CultureInfo.InvariantCulture)))
                                : null).Where(e => e is not null)))));
        }

        // Bubble sizes (c:bubbleSize) — only for bubble charts
        if (series.BubbleSizes.Count > 0)
        {
            el.Add(new XElement(C + "bubbleSize",
                new XElement(C + "numRef",
                    new XElement(C + "numCache",
                        new XElement(C + "formatCode", "General"),
                        new XElement(C + "ptCount", new XAttribute("val", series.BubbleSizes.Count)),
                        series.BubbleSizes.Select((v, vi) =>
                            v.HasValue
                                ? new XElement(C + "pt",
                                    new XAttribute("idx", vi),
                                    new XElement(C + "v", v.Value.ToString("G", CultureInfo.InvariantCulture)))
                                : null).Where(e => e is not null)))));
        }

        return el;
    }

    // ── Series element ────────────────────────────────────────────────────────

    private static XElement BuildSeriesEl(ChartShape chart, ChartSeries series, int index)
    {
        var el = new XElement(C + "ser",
            new XElement(C + "idx", new XAttribute("val", index)),
            new XElement(C + "order", new XAttribute("val", index)));

        // Series name
        el.Add(new XElement(C + "tx",
            new XElement(C + "strRef",
                new XElement(C + "strCache",
                    new XElement(C + "ptCount", new XAttribute("val", "1")),
                    new XElement(C + "pt",
                        new XAttribute("idx", "0"),
                        new XElement(C + "v", series.Name))))));

        // Series fill color (spPr/solidFill)
        if (series.FillColor is not null)
        {
            el.Add(new XElement(C + "spPr",
                new XElement(A + "solidFill",
                    BuildColorEl(series.FillColor))));
        }

        // Per-point colors (dPt)
        foreach (var (ptIdx, color) in series.PointColors)
        {
            el.Add(new XElement(C + "dPt",
                new XElement(C + "idx", new XAttribute("val", ptIdx)),
                new XElement(C + "spPr",
                    new XElement(A + "solidFill",
                        BuildColorEl(color)))));
        }

        // Per-series data labels
        var serDlblsEl = BuildDataLabelsEl(series.DataLabels, chart.ChartType);
        if (serDlblsEl is not null) el.Add(serDlblsEl);

        // Categories
        if (chart.Categories.Count > 0)
        {
            el.Add(new XElement(C + "cat",
                new XElement(C + "strRef",
                    new XElement(C + "strCache",
                        new XElement(C + "ptCount",
                            new XAttribute("val", chart.Categories.Count)),
                        chart.Categories.Select((cat, ci) =>
                            new XElement(C + "pt",
                                new XAttribute("idx", ci),
                                new XElement(C + "v", cat)))))));
        }

        // Values
        if (series.Values.Count > 0)
        {
            el.Add(new XElement(C + "val",
                new XElement(C + "numRef",
                    new XElement(C + "numCache",
                        new XElement(C + "formatCode", "General"),
                        new XElement(C + "ptCount",
                            new XAttribute("val", series.Values.Count)),
                        series.Values.Select((v, vi) =>
                            v.HasValue
                                ? new XElement(C + "pt",
                                    new XAttribute("idx", vi),
                                    new XElement(C + "v",
                                        v.Value.ToString("G", CultureInfo.InvariantCulture)))
                                : null).Where(e => e is not null)))));
        }

        return el;
    }

    // ── Axis elements ─────────────────────────────────────────────────────────

    private static XElement BuildCatAxEl(ChartAxis axis, int axId, int crossAxId) =>
        new XElement(C + "catAx",
            new XElement(C + "axId", new XAttribute("val", axId)),
            new XElement(C + "scaling",
                new XElement(C + "orientation", new XAttribute("val", "minMax"))),
            new XElement(C + "delete",
                new XAttribute("val", axis.Delete ? "1" : "0")),
            new XElement(C + "axPos", new XAttribute("val", "b")),
            axis.HasMajorGridlines
                ? new XElement(C + "majorGridlines")
                : null,
            axis.Title is not null ? BuildTitleEl(axis.Title) : null,
            new XElement(C + "crossAx", new XAttribute("val", crossAxId)));

    // BV2: axPos parameter — scatter/bubble X value axis must use "b" (bottom), Y stays "l" (left).
    // CA1: crosses parameter — secondary valAx crosses at "max" (right-side position).
    private static XElement BuildValAxEl(ChartAxis axis, int axId, int crossAxId,
        string axPos = "l", string? crosses = null)
    {
        var scalingEl = new XElement(C + "scaling",
            new XElement(C + "orientation", new XAttribute("val", "minMax")));
        if (axis.Min.HasValue)
            scalingEl.Add(new XElement(C + "min",
                new XAttribute("val", axis.Min.Value.ToString("G", CultureInfo.InvariantCulture))));
        if (axis.Max.HasValue)
            scalingEl.Add(new XElement(C + "max",
                new XAttribute("val", axis.Max.Value.ToString("G", CultureInfo.InvariantCulture))));

        return new XElement(C + "valAx",
            new XElement(C + "axId", new XAttribute("val", axId)),
            scalingEl,
            new XElement(C + "delete",
                new XAttribute("val", axis.Delete ? "1" : "0")),
            new XElement(C + "axPos", new XAttribute("val", axPos)),
            axis.HasMajorGridlines
                ? new XElement(C + "majorGridlines")
                : null,
            axis.Title is not null ? BuildTitleEl(axis.Title) : null,
            crosses is not null
                ? new XElement(C + "crosses", new XAttribute("val", crosses))
                : null,
            new XElement(C + "crossAx", new XAttribute("val", crossAxId)));
    }

    // ── Color helpers ─────────────────────────────────────────────────────────

    private static XElement BuildColorEl(ThemeAwareColor color)
    {
        if (color.SchemeColor is { } sc)
        {
            var el = new XElement(A + "schemeClr",
                new XAttribute("val", PptxColorReader.ToSchemeColorString(sc.Slot)));
            if (Math.Abs(sc.LumMod - 1.0) > 1e-9)
                el.Add(new XElement(A + "lumMod",
                    new XAttribute("val", (long)Math.Round(sc.LumMod * 100000))));
            if (Math.Abs(sc.LumOff) > 1e-9)
                el.Add(new XElement(A + "lumOff",
                    new XAttribute("val", (long)Math.Round(sc.LumOff * 100000))));
            return el;
        }

        return new XElement(A + "srgbClr",
            new XAttribute("val", $"{color.Resolved.R:X2}{color.Resolved.G:X2}{color.Resolved.B:X2}"));
    }

    // ── Zip helpers ───────────────────────────────────────────────────────────

    private static void WriteEntry(ZipArchive archive, string path, XDocument doc)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = System.Xml.XmlWriter.Create(stream, XmlSettings);
        doc.Save(writer);
    }

    private static void WriteBytes(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void AddExternalData(XDocument chartDoc, string workbookRelId)
    {
        if (chartDoc.Root is null)
            return;

        chartDoc.Root.Element(C + "externalData")?.Remove();
        chartDoc.Root.Add(new XElement(C + "externalData",
            new XAttribute(R + "id", workbookRelId),
            new XElement(C + "autoUpdate", new XAttribute("val", "0"))));
    }

    private static void WriteRegeneratedWorkbook(
        ZipArchive archive,
        ChartShape chart,
        string workbookPath)
    {
        using var workbookStream = new MemoryStream();
        using (var workbookArchive = new ZipArchive(workbookStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(workbookArchive, "[Content_Types].xml", BuildWorkbookContentTypes());
            WriteEntry(workbookArchive, "_rels/.rels", BuildWorkbookRootRels());
            WriteEntry(workbookArchive, "xl/workbook.xml", BuildWorkbookXml());
            WriteEntry(workbookArchive, "xl/_rels/workbook.xml.rels", BuildWorkbookRels());
            WriteEntry(workbookArchive, "xl/styles.xml", BuildWorkbookStylesXml());
            WriteEntry(workbookArchive, "xl/worksheets/sheet1.xml", BuildWorksheetXml(chart));
        }

        WriteBytes(archive, workbookPath, workbookStream.ToArray());
    }

    private static void WriteRegeneratedWorkbookRelationship(
        ZipArchive archive,
        string chartPath,
        string workbookRelId,
        int chartIndex)
    {
        var workbookPath = GetRegeneratedWorkbookPath(chartIndex);
        var relationship = OpcRelationships.CreateRelationship(
            workbookRelId,
            PackageRelType,
            $"../embeddings/{workbookPath.Split('/').Last()}",
            false);

        WriteEntry(
            archive,
            OpcPathHelper.GetRelationshipPartPath(chartPath),
            OpcRelationships.CreateDocument(new[] { relationship }));
    }

    private static XDocument BuildWorkbookContentTypes()
    {
        var ct = OpcMediaTypes.ContentTypesNamespace;
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(ct + "Types",
                new XElement(ct + "Default",
                    new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", OpcMediaTypes.RelationshipsContentType)),
                new XElement(ct + "Default",
                    new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml")),
                new XElement(ct + "Override",
                    new XAttribute("PartName", "/xl/workbook.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                new XElement(ct + "Override",
                    new XAttribute("PartName", "/xl/worksheets/sheet1.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")),
                new XElement(ct + "Override",
                    new XAttribute("PartName", "/xl/styles.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"))));
    }

    private static XDocument BuildWorkbookRootRels()
    {
        XNamespace rels = "http://schemas.openxmlformats.org/package/2006/relationships";
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(rels + "Relationships",
                new XElement(rels + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "xl/workbook.xml"))));
    }

    private static XDocument BuildWorkbookXml()
    {
        XNamespace ss = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(ss + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", rel.NamespaceName),
                new XElement(ss + "sheets",
                    new XElement(ss + "sheet",
                        new XAttribute("name", "ChartData"),
                        new XAttribute("sheetId", "1"),
                        new XAttribute(rel + "id", "rId1")))));
    }

    private static XDocument BuildWorkbookRels()
    {
        XNamespace rels = "http://schemas.openxmlformats.org/package/2006/relationships";
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(rels + "Relationships",
                new XElement(rels + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                    new XAttribute("Target", "worksheets/sheet1.xml")),
                new XElement(rels + "Relationship",
                    new XAttribute("Id", "rId2"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                    new XAttribute("Target", "styles.xml"))));
    }

    private static XDocument BuildWorkbookStylesXml()
    {
        XNamespace ss = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(ss + "styleSheet",
                new XElement(ss + "fonts",
                    new XAttribute("count", "1"),
                    new XElement(ss + "font")),
                new XElement(ss + "fills",
                    new XAttribute("count", "1"),
                    new XElement(ss + "fill")),
                new XElement(ss + "borders",
                    new XAttribute("count", "1"),
                    new XElement(ss + "border")),
                new XElement(ss + "cellStyleXfs",
                    new XAttribute("count", "1"),
                    new XElement(ss + "xf",
                        new XAttribute("numFmtId", "0"),
                        new XAttribute("fontId", "0"),
                        new XAttribute("fillId", "0"),
                        new XAttribute("borderId", "0"))),
                new XElement(ss + "cellXfs",
                    new XAttribute("count", "1"),
                    new XElement(ss + "xf",
                        new XAttribute("numFmtId", "0"),
                        new XAttribute("fontId", "0"),
                        new XAttribute("fillId", "0"),
                        new XAttribute("borderId", "0"),
                        new XAttribute("xfId", "0")))));
    }

    private static XDocument BuildWorksheetXml(ChartShape chart)
    {
        XNamespace ss = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = new List<XElement>();

        var headerCells = new List<XElement>();
        for (var seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
            headerCells.Add(InlineStringCell(ss, seriesIndex + 2, 1, chart.Series[seriesIndex].Name));
        rows.Add(new XElement(ss + "row", new XAttribute("r", "1"), headerCells));

        var pointCount = Math.Max(
            chart.Categories.Count,
            chart.Series.Count == 0 ? 0 : chart.Series.Max(series => series.Values.Count));
        for (var pointIndex = 0; pointIndex < pointCount; pointIndex++)
        {
            var cells = new List<XElement>();
            var category = pointIndex < chart.Categories.Count
                ? chart.Categories[pointIndex]
                : string.Empty;
            cells.Add(InlineStringCell(ss, 1, pointIndex + 2, category));

            for (var seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
            {
                var values = chart.Series[seriesIndex].Values;
                if (pointIndex < values.Count && values[pointIndex].HasValue)
                    cells.Add(NumberCell(ss, seriesIndex + 2, pointIndex + 2, values[pointIndex]!.Value));
            }

            rows.Add(new XElement(ss + "row",
                new XAttribute("r", (pointIndex + 2).ToString(CultureInfo.InvariantCulture)),
                cells));
        }

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(ss + "worksheet",
                new XElement(ss + "sheetData", rows)));
    }

    private static XElement InlineStringCell(XNamespace ss, int column, int row, string value) =>
        new XElement(ss + "c",
            new XAttribute("r", CellReference(column, row)),
            new XAttribute("t", "inlineStr"),
            new XElement(ss + "is",
                new XElement(ss + "t", value)));

    private static XElement NumberCell(XNamespace ss, int column, int row, double value) =>
        new XElement(ss + "c",
            new XAttribute("r", CellReference(column, row)),
            new XElement(ss + "v", value.ToString("G", CultureInfo.InvariantCulture)));

    private static string CellReference(int column, int row) =>
        ColumnName(column) + row.ToString(CultureInfo.InvariantCulture);

    private static string ColumnName(int oneBasedColumn)
    {
        var column = oneBasedColumn;
        var name = string.Empty;
        while (column > 0)
        {
            column--;
            name = (char)('A' + column % 26) + name;
            column /= 26;
        }

        return name;
    }

    private static void MergePreservedExternalData(
        XDocument chartDoc,
        PptxPackageSnapshot? packageSnapshot,
        string chartPath)
    {
        var sourceChart = TryReadSnapshotXml(packageSnapshot, chartPath);
        var sourceExternalData = sourceChart?.Root?.Element(C + "externalData");
        if (sourceExternalData is null || chartDoc.Root is null)
            return;

        chartDoc.Root.Element(C + "externalData")?.Remove();
        chartDoc.Root.Add(new XElement(sourceExternalData));
    }

    private static void WritePreservedWorkbookRelationships(
        ZipArchive archive,
        PptxPackageSnapshot? packageSnapshot,
        string chartPath)
    {
        var relsPath = OpcPathHelper.GetRelationshipPartPath(chartPath);
        var sourceRels = TryReadSnapshotXml(packageSnapshot, relsPath);
        if (sourceRels is null)
            return;

        var workbookRelationships = OpcRelationships.Load(sourceRels)
            .Where(relationship =>
                PptxPackageWriter.TryResolveChartWorkbookPath(chartPath, relationship, out var workbookPath) &&
                packageSnapshot?.TryGetEntry(workbookPath, out _) == true)
            .ToArray();
        if (workbookRelationships.Length == 0)
            return;

        WriteEntry(
            archive,
            relsPath,
            OpcRelationships.CreateDocument(workbookRelationships.Select(relationship =>
                OpcRelationships.CreateRelationship(
                    relationship.Id,
                    relationship.Type,
                    relationship.Target,
                    relationship.IsExternal))));
    }

    private static XDocument? TryReadSnapshotXml(PptxPackageSnapshot? packageSnapshot, string path) =>
        packageSnapshot is not null && packageSnapshot.TryGetEntry(path, out var bytes)
            ? OpcXml.TryLoadXml(bytes)
            : null;

    private static XAttribute NsAttr(string prefix, XNamespace ns) =>
        new XAttribute(XNamespace.Xmlns + prefix, ns.NamespaceName);
}
