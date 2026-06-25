using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
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
        ZipArchive archive, ChartShape chart, int chartIndex)
    {
        var chartPath = $"ppt/charts/chart{chartIndex}.xml";

        // Write chart XML
        var chartDoc = BuildChartDoc(chart);
        WriteEntry(archive, chartPath, chartDoc);

        return chartPath;
    }

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

    private static XElement BuildPlotArea(ChartShape chart)
    {
        var seriesEls = chart.Series.Select((s, i) => BuildSeriesEl(chart, s, i)).ToList();

        XElement? chartTypeEl = chart.ChartType switch
        {
            ChartType.BarClustered or ChartType.BarStacked or ChartType.BarStacked100 =>
                BuildBarChartEl(chart, seriesEls, isBar: true),
            ChartType.ColumnClustered or ChartType.ColumnStacked or ChartType.ColumnStacked100 =>
                BuildBarChartEl(chart, seriesEls, isBar: false),
            ChartType.Line or ChartType.LineMarkers =>
                BuildLineChartEl(chart, seriesEls),
            ChartType.Pie =>
                BuildPieChartEl(chart, seriesEls),
            ChartType.Area or ChartType.AreaStacked =>
                BuildAreaChartEl(chart, seriesEls),
            ChartType.Scatter =>
                BuildScatterChartEl(chart, seriesEls),
            _ =>
                BuildBarChartEl(chart, seriesEls, isBar: false) // default fallback
        };

        var catAxEl = chart.ChartType is not (ChartType.Pie or ChartType.Unknown)
            ? BuildCatAxEl(chart.CategoryAxis, 1, 2)
            : null;
        var valAxEl = chart.ChartType is not (ChartType.Pie or ChartType.Unknown)
            ? BuildValAxEl(chart.ValueAxis, 2, 1)
            : null;

        return new XElement(C + "plotArea",
            chartTypeEl,
            catAxEl,
            valAxEl);
    }

    private static XElement BuildBarChartEl(ChartShape chart, List<XElement> seriesEls, bool isBar)
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
            new XElement(C + "axId", new XAttribute("val", "1")),
            new XElement(C + "axId", new XAttribute("val", "2")));
    }

    private static XElement BuildLineChartEl(ChartShape chart, List<XElement> seriesEls) =>
        new XElement(C + "lineChart",
            new XElement(C + "grouping", new XAttribute("val", "standard")),
            seriesEls,
            new XElement(C + "axId", new XAttribute("val", "1")),
            new XElement(C + "axId", new XAttribute("val", "2")));

    private static XElement BuildPieChartEl(ChartShape chart, List<XElement> seriesEls) =>
        new XElement(C + "pieChart",
            seriesEls);

    private static XElement BuildAreaChartEl(ChartShape chart, List<XElement> seriesEls) =>
        new XElement(C + "areaChart",
            new XElement(C + "grouping",
                new XAttribute("val", chart.ChartType == ChartType.AreaStacked ? "stacked" : "standard")),
            seriesEls,
            new XElement(C + "axId", new XAttribute("val", "1")),
            new XElement(C + "axId", new XAttribute("val", "2")));

    private static XElement BuildScatterChartEl(ChartShape chart, List<XElement> seriesEls) =>
        new XElement(C + "scatterChart",
            new XElement(C + "scatterStyle", new XAttribute("val", "lineMarker")),
            seriesEls,
            new XElement(C + "axId", new XAttribute("val", "1")),
            new XElement(C + "axId", new XAttribute("val", "2")));

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

    private static XElement BuildValAxEl(ChartAxis axis, int axId, int crossAxId)
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
            new XElement(C + "axPos", new XAttribute("val", "l")),
            axis.HasMajorGridlines
                ? new XElement(C + "majorGridlines")
                : null,
            axis.Title is not null ? BuildTitleEl(axis.Title) : null,
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

    private static XAttribute NsAttr(string prefix, XNamespace ns) =>
        new XAttribute(XNamespace.Xmlns + prefix, ns.NamespaceName);
}
