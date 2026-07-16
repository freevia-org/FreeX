using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxChartXmlWriter
{
    private static XElement ToChartTitleXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        var fontSize = ToEffectiveChartTitleFontSize(chart);
        return new XElement(chartNs + "title",
            // R42-io-chart-plotarea-legend-3-4: only emit <c:tx> when there is literal title
            // text. An auto-linked/blank title (no <c:tx> in the source file) can still carry an
            // explicit manual layout/overlay below -- omitting <c:tx> here keeps that case
            // faithful instead of writing a bogus empty <a:t/> run.
            string.IsNullOrWhiteSpace(chart.Title)
                ? null
                : new XElement(chartNs + "tx",
                    new XElement(chartNs + "rich",
                        // CT_TextBody requires bodyPr before the paragraph(s).
                        new XElement(drawingNs + "bodyPr"),
                        new XElement(drawingNs + "p",
                            new XElement(drawingNs + "r",
                                ToTextRunProperties(chart.ChartTitleTextThemeColor, chart.ChartTitleTextColor, fontSize, drawingNs),
                                new XElement(drawingNs + "t", chart.Title))))),
            ToManualLayoutXml(chart.TitleLayout, chartNs),
            chart.TitleOverlay
                ? new XElement(chartNs + "overlay", new XAttribute("val", "1"))
                : null);
    }

    /// <summary>
    /// R42-io-chart-plotarea-legend-3-4: whether the chart-level &lt;c:title&gt; element should be
    /// emitted at all. The reader populates <see cref="ChartModel.TitleLayout"/>/
    /// <see cref="ChartModel.TitleOverlay"/> directly off &lt;c:title&gt; independent of whether the
    /// title carries literal text (an auto-linked title can still be manually repositioned or set
    /// to overlay the plot area without ever being retyped) -- so gating emission on title text
    /// alone would silently drop a loaded title's manual position/overlay. Emit whenever there is
    /// *something* to write: text, a meaningful manual layout, or overlay.
    /// </summary>
    private static bool ShouldWriteChartTitle(ChartModel chart, XNamespace chartNs) =>
        !string.IsNullOrWhiteSpace(chart.Title) ||
        ToManualLayoutXml(chart.TitleLayout, chartNs) is not null ||
        chart.TitleOverlay;

    /// <summary>
    /// R41-io-hyperlink-drawing-rels-3-2: <see cref="ToChartTitleXml"/> rebuilds the main chart title
    /// purely from <see cref="ChartModel"/> scalar fields, which have no concept of a hyperlink on the
    /// title run -- unlike axis titles (see <see cref="TryParseVerbatimAxisTitleXml"/>), the main title
    /// has no verbatim-XML fallback. Rather than adding a modeled Hyperlink property, this grafts a
    /// caller-supplied <c>a:hlinkClick</c> (already resolved to a package relationship id by the caller,
    /// which captured it from the chart part's OWN pre-rebuild bytes) onto the first title run's
    /// <c>a:rPr</c> as a native passthrough. No-op if the rebuilt document has no title run (e.g. the
    /// title text itself was cleared) or already declares the "r" namespace differently.
    /// </summary>
    internal static void ApplyVerbatimTitleHyperlink(
        XDocument chartXml,
        XNamespace chartNs,
        XNamespace drawingNs,
        XNamespace relNs,
        string hyperlinkRelationshipId)
    {
        var titleRunProperties = chartXml.Root?
            .Element(chartNs + "chart")?
            .Element(chartNs + "title")?
            .Element(chartNs + "tx")?
            .Element(chartNs + "rich")?
            .Element(drawingNs + "p")?
            .Element(drawingNs + "r")?
            .Element(drawingNs + "rPr");
        if (titleRunProperties is null)
            return;

        // CT_TextCharacterProperties element order: ..., (fill group)?, effectLst|effectDag?,
        // highlight?, (uLnTx|uLn)?, (uFillTx|uFill)?, latin?, ea?, cs?, sym?, hlinkClick?, ... --
        // ToTextRunProperties only ever emits an optional solidFill before this point, so appending
        // hlinkClick as the last child keeps the run properties schema-valid.
        titleRunProperties.Add(new XElement(drawingNs + "hlinkClick", new XAttribute(relNs + "id", hyperlinkRelationshipId)));

        if (chartXml.Root?.Attribute(XNamespace.Xmlns + "r") is null)
            chartXml.Root?.SetAttributeValue(XNamespace.Xmlns + "r", relNs.NamespaceName);
    }

    /// <summary>
    /// R43-io-chart-axis-title-numfmt-3-2: <paramref name="vertical"/> is true for a title on a
    /// left/right (vertical) axis. Excel's own default for a plain (non-rich) axis title on a
    /// vertical axis is rotated -90 degrees with the run laid out horizontally within that rotated
    /// frame (&lt;a:bodyPr rot="-5400000" vert="horz"/&gt;) -- e.g. the standard "Primary Vertical"
    /// axis title. Reproducing that default here (instead of an always-bare &lt;a:bodyPr/&gt;) keeps
    /// a round-tripped vertical-axis title reading vertically instead of flattening to horizontal.
    /// </summary>
    private static XElement? ToAxisTitleXml(
        string? title,
        ChartManualLayoutModel? layout,
        WorkbookThemeColorReference? textThemeColor,
        CellColor? textColor,
        double fontSize,
        XNamespace chartNs,
        XNamespace drawingNs,
        bool vertical = false) =>
        string.IsNullOrWhiteSpace(title)
            ? null
            : new XElement(chartNs + "title",
                new XElement(chartNs + "tx",
                    new XElement(chartNs + "rich",
                        vertical
                            ? new XElement(drawingNs + "bodyPr", new XAttribute("rot", -5400000), new XAttribute("vert", "horz"))
                            : new XElement(drawingNs + "bodyPr"),
                        new XElement(drawingNs + "p",
                            new XElement(drawingNs + "r",
                                ToTextRunProperties(textThemeColor, textColor, fontSize, drawingNs),
                                new XElement(drawingNs + "t", title))))),
                ToManualLayoutXml(layout, chartNs));

    /// <summary>
    /// Returns a verbatim <c:title> element parsed from the stored string, or null if
    /// <paramref name="verbatimXml"/> is null/empty. Used for round-tripping rich axis
    /// title formatting (bold, italic, multi-run) that the model cannot represent.
    /// Falls back to null on any parse error so the caller can fall back to computed title.
    /// </summary>
    private static XElement? TryParseVerbatimAxisTitleXml(string? verbatimXml)
    {
        if (string.IsNullOrWhiteSpace(verbatimXml))
            return null;

        try
        {
            return XElement.Parse(verbatimXml, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            return null;
        }
    }

    private static XElement? ToTextRunProperties(
        WorkbookThemeColorReference? textThemeColor,
        CellColor? textColor,
        double fontSize,
        XNamespace drawingNs)
    {
        var size = Math.Clamp((int)Math.Round(fontSize * 100), 600, 7200);
        return new XElement(drawingNs + "rPr",
            new XAttribute("sz", size),
            ToTextRunPropertiesContent(textThemeColor, textColor, fontSize, drawingNs));
    }

    private static IEnumerable<object> ToTextRunPropertiesContent(
        WorkbookThemeColorReference? textThemeColor,
        CellColor? textColor,
        double fontSize,
        XNamespace drawingNs)
    {
        var fill = ToSolidFill(textThemeColor, textColor, drawingNs);
        if (fill is not null)
        {
            yield return fill;
        }
    }

    private static XElement? ToChartAreaShapeProperties(
        ChartModel chart,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        ToShapeProperties(
            chartNs,
            drawingNs,
            chart.ChartAreaFillThemeColor,
            chart.ChartAreaFillColor,
            chart.ChartAreaBorderThemeColor,
            chart.ChartAreaBorderColor,
            chart.ChartAreaBorderThickness,
            chart.ChartAreaNoFill == true,
            chart.ChartAreaNoLine == true);

    private static XElement? ToChartDefaultTextPropertiesXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (chart.ChartDefaultTextColor is null &&
            chart.ChartDefaultTextThemeColor is null &&
            chart.ChartDefaultFontSize == 11)
        {
            return null;
        }

        return new XElement(chartNs + "txPr",
            ToTextBodyProperties(0, drawingNs),
            new XElement(drawingNs + "p",
                new XElement(drawingNs + "pPr",
                    new XElement(drawingNs + "defRPr",
                        new XAttribute("sz", Math.Clamp((int)Math.Round(chart.ChartDefaultFontSize * 100), 600, 7200)),
                        ToTextRunPropertiesContent(chart.ChartDefaultTextThemeColor, chart.ChartDefaultTextColor, chart.ChartDefaultFontSize, drawingNs)))));
    }

    private static XElement? ToPlotAreaShapeProperties(
        ChartModel chart,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        ToShapeProperties(
            chartNs,
            drawingNs,
            chart.PlotAreaFillThemeColor,
            chart.PlotAreaFillColor,
            chart.PlotAreaBorderThemeColor,
            chart.PlotAreaBorderColor,
            chart.PlotAreaBorderThickness,
            chart.PlotAreaNoFill == true,
            chart.PlotAreaNoLine == true);

    /// <summary>
    /// Builds a &lt;c:spPr&gt; element from resolved fill/border data. R42-io-chart-plotarea-
    /// legend-3-1: <paramref name="noFill"/>/<paramref name="noLine"/> let a caller re-emit an
    /// explicit "No Fill"/"No Line" choice (&lt;a:noFill/&gt;) instead of silently omitting the
    /// whole shape-properties element (which would revert to Excel's themed default on reload).
    /// They take priority over any fill/border color, matching how the reader clears the color
    /// fields when it detects an explicit noFill.
    /// </summary>
    private static XElement? ToShapeProperties(
        XNamespace chartNs,
        XNamespace drawingNs,
        WorkbookThemeColorReference? fillThemeColor,
        CellColor? fillColor,
        WorkbookThemeColorReference? borderThemeColor,
        CellColor? borderColor,
        double? borderThickness,
        bool noFill = false,
        bool noLine = false)
    {
        var fill = noFill
            ? new XElement(drawingNs + "noFill")
            : ToSolidFill(fillThemeColor, fillColor, drawingNs);

        XElement? line;
        if (noLine)
        {
            line = new XElement(drawingNs + "ln", new XElement(drawingNs + "noFill"));
        }
        else
        {
            var lineFill = ToSolidFill(borderThemeColor, borderColor, drawingNs);
            line = lineFill is null && borderThickness is null
                ? null
                : new XElement(drawingNs + "ln",
                    borderThickness is null
                        ? null
                        : new XAttribute("w", Math.Max(0, (int)Math.Round(Math.Clamp(borderThickness.Value, 0, 10) * DrawingMlUnits.EmuPerPoint))),
                    lineFill);
        }

        return fill is null && line is null
            ? null
            : new XElement(chartNs + "spPr", fill, line);
    }

    private static XElement? ToSolidFill(
        WorkbookThemeColorReference? themeColor,
        CellColor? color,
        XNamespace drawingNs) =>
        XlsxDrawingColorWriter.ToSolidFill(themeColor, color, drawingNs);

    private static XElement? ToLegendXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (!chart.ShowLegend || chart.LegendPosition == ChartLegendPosition.None)
            return null;

        var legendPosition = ToEffectiveLegendPosition(chart);
        return new XElement(chartNs + "legend",
            new XElement(chartNs + "legendPos",
                new XAttribute("val", ToXlsxLegendPosition(legendPosition))),
            chart.LegendEntries
                .Where(entry => entry.Index >= 0 && entry.IsDeleted is not null)
                .Select(entry => new XElement(chartNs + "legendEntry",
                    new XElement(chartNs + "idx", new XAttribute("val", entry.Index)),
                    new XElement(chartNs + "delete", new XAttribute("val", entry.IsDeleted == true ? "1" : "0")))),
            ToManualLayoutXml(chart.LegendLayout, chartNs),
            new XElement(chartNs + "overlay",
                new XAttribute("val", chart.LegendOverlay ? "1" : "0")),
            ToShapeProperties(
                chartNs,
                drawingNs,
                chart.LegendFillThemeColor,
                chart.LegendFillColor,
                chart.LegendBorderThemeColor,
                chart.LegendBorderColor,
                chart.LegendBorderThickness),
            ToLegendTextProperties(chart, chartNs, drawingNs));
    }

    private static double ToEffectiveChartTitleFontSize(ChartModel chart) =>
        IsClassicStackedBarOrColumnChart(chart.Type) && chart.ChartTitleFontSize == 16
            ? 14
            : chart.ChartTitleFontSize;

    private static ChartLegendPosition ToEffectiveLegendPosition(ChartModel chart) =>
        IsClassicStackedBarOrColumnChart(chart.Type) &&
        chart.LegendPosition == ChartLegendPosition.Right &&
        chart.LegendLayout is null &&
        !chart.LegendOverlay
            ? ChartLegendPosition.Bottom
            : chart.LegendPosition;

    private static XElement? ToManualLayoutXml(ChartManualLayoutModel? layout, XNamespace chartNs)
    {
        if (layout is null ||
            string.IsNullOrWhiteSpace(layout.LayoutTarget) &&
            string.IsNullOrWhiteSpace(layout.XMode) &&
            string.IsNullOrWhiteSpace(layout.YMode) &&
            string.IsNullOrWhiteSpace(layout.WidthMode) &&
            string.IsNullOrWhiteSpace(layout.HeightMode) &&
            layout.X is null &&
            layout.Y is null &&
            layout.Width is null &&
            layout.Height is null)
        {
            return null;
        }

        return new XElement(chartNs + "layout",
            new XElement(chartNs + "manualLayout",
                string.IsNullOrWhiteSpace(layout.LayoutTarget) ? null : new XElement(chartNs + "layoutTarget", new XAttribute("val", layout.LayoutTarget)),
                string.IsNullOrWhiteSpace(layout.XMode) ? null : new XElement(chartNs + "xMode", new XAttribute("val", layout.XMode)),
                string.IsNullOrWhiteSpace(layout.YMode) ? null : new XElement(chartNs + "yMode", new XAttribute("val", layout.YMode)),
                string.IsNullOrWhiteSpace(layout.WidthMode) ? null : new XElement(chartNs + "wMode", new XAttribute("val", layout.WidthMode)),
                string.IsNullOrWhiteSpace(layout.HeightMode) ? null : new XElement(chartNs + "hMode", new XAttribute("val", layout.HeightMode)),
                layout.X is { } x ? new XElement(chartNs + "x", new XAttribute("val", ToChartLayoutDecimal(x))) : null,
                layout.Y is { } y ? new XElement(chartNs + "y", new XAttribute("val", ToChartLayoutDecimal(y))) : null,
                layout.Width is { } width ? new XElement(chartNs + "w", new XAttribute("val", ToChartLayoutDecimal(width))) : null,
                layout.Height is { } height ? new XElement(chartNs + "h", new XAttribute("val", ToChartLayoutDecimal(height))) : null));
    }

    private static string ToChartLayoutDecimal(double value) =>
        value.ToString("0.###############", CultureInfo.InvariantCulture);

    private static XElement? ToLegendTextProperties(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (chart.LegendTextColor is null && chart.LegendTextThemeColor is null && chart.LegendFontSize == 12)
            return null;

        return new XElement(chartNs + "txPr",
            ToTextBodyProperties(0, drawingNs),
            new XElement(drawingNs + "p",
                new XElement(drawingNs + "pPr",
                    new XElement(drawingNs + "defRPr",
                        new XAttribute("sz", Math.Clamp((int)Math.Round(chart.LegendFontSize * 100), 600, 7200)),
                        ToTextRunPropertiesContent(chart.LegendTextThemeColor, chart.LegendTextColor, chart.LegendFontSize, drawingNs)))));
    }

    private static XElement? ToDataLabelsXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (!chart.ShowDataLabels)
            return null;

        return new XElement(chartNs + "dLbls",
            new XElement(chartNs + "numFmt",
                new XAttribute("formatCode", ToXlsxNumberFormatCode(chart.DataLabelNumberFormat, chart.DataLabelNumberFormatCode)),
                new XAttribute("sourceLinked", ToXlsxNumberFormatSourceLinked(chart.DataLabelNumberFormat, chart.DataLabelNumberFormatSourceLinked))),
            ToShapeProperties(
                chartNs,
                drawingNs,
                chart.DataLabelFillThemeColor,
                chart.DataLabelFillColor,
                chart.DataLabelBorderThemeColor,
                chart.DataLabelBorderColor,
                chart.DataLabelBorderThickness),
            ToDataLabelTextProperties(chart, chartNs, drawingNs),
            GatedDataLabelPositionXml(chart, chartNs),
            new XElement(chartNs + "showLegendKey", new XAttribute("val", chart.ShowDataLabelLegendKey ? "1" : "0")),
            new XElement(chartNs + "showVal", new XAttribute("val", chart.ShowDataLabelValue ? "1" : "0")),
            new XElement(chartNs + "showCatName", new XAttribute("val", chart.ShowDataLabelCategoryName ? "1" : "0")),
            new XElement(chartNs + "showSerName", new XAttribute("val", chart.ShowDataLabelSeriesName ? "1" : "0")),
            new XElement(chartNs + "showPercent", new XAttribute("val", chart.ShowDataLabelPercentage && ChartTypeSupport.SupportsPercentageDataLabels(chart.Type) ? "1" : "0")),
            new XElement(chartNs + "showBubbleSize", new XAttribute("val", chart.ShowDataLabelBubbleSize ? "1" : "0")),
            ToDataLabelSeparatorXml(chart.DataLabelSeparator, chart.DataLabelSeparatorText, chartNs),
            new XElement(chartNs + "showLeaderLines", new XAttribute("val", chart.ShowDataLabelCallouts ? "1" : "0")),
            ToDataLabelLeaderLinesXml(chart, chartNs, drawingNs));
    }

    private static XElement? ToDataLabelLeaderLinesXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        var shapeProperties = ToChartGuideLineShapeProperties(
            chart.DataLabelLeaderLineThemeColor,
            chart.DataLabelLeaderLineColor,
            chart.DataLabelLeaderLineThickness,
            chart.DataLabelLeaderLineDashStyle,
            chartNs,
            drawingNs);

        return shapeProperties is null
            ? null
            : new XElement(chartNs + "leaderLines", shapeProperties);
    }

    private static XElement? ToDataLabelTextProperties(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (chart.DataLabelTextColor is null && chart.DataLabelTextThemeColor is null && chart.DataLabelFontSize == 11 && chart.DataLabelAngle == 0)
            return null;

        var textFill = ToSolidFill(chart.DataLabelTextThemeColor, chart.DataLabelTextColor, drawingNs);
        return new XElement(chartNs + "txPr",
            ToTextBodyProperties(chart.DataLabelAngle, drawingNs),
            new XElement(drawingNs + "p",
                new XElement(drawingNs + "pPr",
                    new XElement(drawingNs + "defRPr",
                        new XAttribute("sz", Math.Clamp((int)Math.Round(chart.DataLabelFontSize * 100), 600, 7200)),
                        textFill))));
    }

    private static XElement ToTextBodyProperties(double angle, XNamespace drawingNs)
    {
        var element = new XElement(drawingNs + "bodyPr");
        if (angle != 0 && double.IsFinite(angle))
            element.SetAttributeValue("rot", Math.Clamp((int)Math.Round(angle * 60000), -5400000, 5400000));
        return element;
    }

    private static string ToXlsxDataLabelPosition(ChartDataLabelPosition position) =>
        position switch
        {
            ChartDataLabelPosition.Center => "ctr",
            ChartDataLabelPosition.InsideEnd => "inEnd",
            ChartDataLabelPosition.OutsideEnd => "outEnd",
            ChartDataLabelPosition.InsideBase => "inBase",
            _ => "bestFit"
        };

    /// <summary>
    /// Builds the chart-level &lt;c:dLblPos&gt; element for <paramref name="chart"/>'s data labels,
    /// gated to the c:dLblPos values ISO/IEC 29500 §21.2.2.44 (and Excel itself) actually accepts for
    /// the chart's plot-group family — otherwise Excel rejects the whole chart part with a repair
    /// prompt. Per family:
    ///   - Pie/3-D pie: ctr, inEnd, outEnd, bestFit are all valid (bestFit only here).
    ///   - Doughnut: ctr, inEnd, outEnd are valid; bestFit is NOT (doughnut has no "best fit" model).
    ///   - Stacked/percent-stacked bar or column: only ctr is valid.
    ///   - Area (incl. 3-D area), radar, surface, stock: c:dLblPos has no valid value at all — the
    ///     element must be omitted entirely.
    ///   - Clustered/3-D bar or column: ctr, inEnd, outEnd, inBase are all valid; FreeX's model can
    ///     produce inBase (<see cref="ChartDataLabelPosition.InsideBase"/>) and passes it through
    ///     unchanged for this family. bestFit is invalid here, so it is remapped down to ctr.
    ///   - Line, 3-D line, scatter, bubble: only ctr, l, r, t, b are valid; FreeX's model never
    ///     produces l/r/t/b, so every position (including outEnd/inEnd/bestFit/inBase) is gated to ctr.
    /// </summary>
    private static XElement? GatedDataLabelPositionXml(ChartModel chart, XNamespace chartNs)
    {
        var position = ToXlsxDataLabelPosition(chart.DataLabelPosition);
        var gated = GateDataLabelPosition(position, chart.Type);
        return gated is null ? null : new XElement(chartNs + "dLblPos", new XAttribute("val", gated));
    }

    private static string? GateDataLabelPosition(string position, ChartType chartType)
    {
        var isStacked = chartType is ChartType.StackedColumn or ChartType.PercentStackedColumn
            or ChartType.StackedBar or ChartType.PercentStackedBar;
        if (isStacked)
            return "ctr"; // Only ctr is valid for stacked/percent-stacked bar or column.

        var isPie = chartType is ChartType.Pie or ChartType.ThreeDPie;
        if (isPie)
            // ctr, inEnd, outEnd, bestFit are all valid for 2-D/3-D pie, but inBase is not (pie has
            // no "base" concept); remap to bestFit, pie's closest equivalent.
            return position == "inBase" ? "bestFit" : position;

        if (chartType == ChartType.Doughnut)
            // Doughnut accepts ctr/inEnd/outEnd only; bestFit and inBase both fall back to ctr.
            return position is "bestFit" or "inBase" ? "ctr" : position;

        var hasNoValidPosition = chartType is ChartType.Area or ChartType.ThreeDArea
            or ChartType.Radar or ChartType.Surface or ChartType.ThreeDSurface or ChartType.Stock;
        if (hasNoValidPosition)
            return null;

        // Line/3-D line, scatter, bubble only accept ctr, l, r, t, b — FreeX's model never produces
        // l/r/t/b, and inEnd/outEnd/bestFit/inBase are all invalid here (Excel rejects the chart part
        // with a repair prompt), so gate every position down to ctr for this family.
        var isLineScatterOrBubble = chartType is ChartType.Line or ChartType.ThreeDLine
            or ChartType.Scatter or ChartType.Bubble;
        if (isLineScatterOrBubble)
            return "ctr";

        // Clustered/3-D bar/column and everything else FreeX can author: bestFit is not a valid value
        // outside pie, so fall back to ctr. inBase IS valid for this family, so it passes through
        // unchanged.
        return position == "bestFit" ? "ctr" : position;
    }

    private static string ToXlsxDataLabelSeparator(ChartDataLabelSeparator separator, string? customText = null) =>
        separator switch
        {
            ChartDataLabelSeparator.Semicolon => "; ",
            ChartDataLabelSeparator.NewLine => "\n",
            ChartDataLabelSeparator.Space => " ",
            // A literal separator string this model has no dedicated member for (e.g. Excel's
            // "Period" choice); re-emit the raw text captured by the reader. Falls back to the
            // default comma separator if somehow captured without its raw text.
            ChartDataLabelSeparator.Custom => customText ?? ", ",
            _ => ", "
        };

    /// <summary>
    /// True when the emitted separator text needs <c>xml:space="preserve"</c> to advertise that its
    /// leading/trailing/newline whitespace is significant. Matches prior behavior for the fixed enum
    /// members (only NewLine ever set it) and extends the same reasoning to a Custom literal whose
    /// captured text happens to start/end with whitespace or contain a newline.
    /// </summary>
    private static bool RequiresDataLabelSeparatorSpacePreserve(ChartDataLabelSeparator separator, string separatorText) =>
        separator == ChartDataLabelSeparator.NewLine ||
        (separator == ChartDataLabelSeparator.Custom &&
            separatorText.Length > 0 &&
            (char.IsWhiteSpace(separatorText[0]) || char.IsWhiteSpace(separatorText[^1]) || separatorText.Contains('\n')));

    private static XElement ToDataLabelSeparatorXml(
        ChartDataLabelSeparator separator, string? customText, XNamespace chartNs)
    {
        var value = ToXlsxDataLabelSeparator(separator, customText);
        return new XElement(chartNs + "separator",
            RequiresDataLabelSeparatorSpacePreserve(separator, value)
                ? new XAttribute(XNamespace.Xml + "space", "preserve")
                : null,
            value);
    }

    private static string ToXlsxLegendPosition(ChartLegendPosition position) =>
        position switch
        {
            ChartLegendPosition.Left => "l",
            ChartLegendPosition.Top => "t",
            ChartLegendPosition.Bottom => "b",
            _ => "r"
        };

}
