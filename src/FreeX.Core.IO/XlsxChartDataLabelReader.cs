using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxChartDataLabelReader
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Chart2012Ns = "http://schemas.microsoft.com/office/drawing/2012/chart";

    /// <summary>
    /// Reads Excel's "Value From Cells" data-label strings for a series
    /// (<c>c15:datalabelsRange</c> under the series <c>extLst</c>) and appends them to
    /// <see cref="ChartModel.RangeDataLabels"/>. These literal cached strings (e.g.
    /// <c>"👍 10%"</c>) override the numeric value Excel would otherwise display.
    /// </summary>
    public static void ApplyRangeDataLabels(XElement series, int seriesIndex, ChartModel chart)
    {
        var dataLabelsRange = series
            .Element(ChartNs + "extLst")?
            .Elements(ChartNs + "ext")
            .Select(ext => ext.Element(Chart2012Ns + "datalabelsRange"))
            .FirstOrDefault(range => range is not null);
        if (dataLabelsRange is null)
            return;

        // c15:f holds the worksheet formula the labels are sourced from; preserve it verbatim.
        var formula = dataLabelsRange.Element(Chart2012Ns + "f")?.Value;
        if (string.IsNullOrEmpty(formula))
            formula = null;

        var rangeCache = dataLabelsRange.Element(Chart2012Ns + "dlblRangeCache");

        int? pointCount = null;
        if (int.TryParse(rangeCache?.Element(Chart2012Ns + "ptCount")?.Attribute("val")?.Value, out var parsedCount)
            && parsedCount >= 0)
        {
            pointCount = parsedCount;
        }

        var points = new List<ChartRangeDataLabelPoint>();
        if (rangeCache is not null)
        {
            // Excel emits cached points in the c15 namespace, but tolerate the bare c (ChartNs)
            // namespace too, which some producers (and the legacy reader) used.
            foreach (var point in rangeCache.Elements(Chart2012Ns + "pt").Concat(rangeCache.Elements(ChartNs + "pt")))
            {
                if (!int.TryParse(point.Attribute("idx")?.Value, out var pointIndex) || pointIndex < 0)
                    continue;

                var text = (point.Element(Chart2012Ns + "v") ?? point.Element(ChartNs + "v"))?.Value;
                if (string.IsNullOrEmpty(text))
                    continue;

                points.Add(new ChartRangeDataLabelPoint(pointIndex, text));
            }
        }

        if (formula is null && pointCount is null && points.Count == 0)
            return;

        chart.SeriesRangeDataLabels.RemoveAll(existing => existing.SeriesIndex == seriesIndex);
        chart.SeriesRangeDataLabels.Add(new ChartSeriesRangeDataLabels(seriesIndex, formula, pointCount, points));

        // Keep the flat list the renderer consumes in sync.
        foreach (var point in points)
        {
            chart.RangeDataLabels.RemoveAll(existing =>
                existing.SeriesIndex == seriesIndex && existing.PointIndex == point.PointIndex);
            chart.RangeDataLabels.Add(new ChartRangeDataLabel(seriesIndex, point.PointIndex, point.Text));
        }
    }

    public static void ApplyDataLabels(XElement? plotArea, ChartModel chart)
    {
        chart.AdditionalPlotGroupDataLabels.Clear();

        var groups = FindAllPlotChartElements(plotArea);
        if (groups.Count == 0)
            return;

        var dataLabels = groups[0].Element(ChartNs + "dLbls");
        if (dataLabels is not null)
        {
            chart.DataLabelPosition = FromXlsxDataLabelPosition(dataLabels.Element(ChartNs + "dLblPos")?.Attribute("val")?.Value);
            var numberFormatElement = dataLabels.Element(ChartNs + "numFmt");
            var numberFormatCode = numberFormatElement?.Attribute("formatCode")?.Value;
            chart.DataLabelNumberFormat = XlsxChartAxisReader.FromXlsxNumberFormatCode(numberFormatCode);
            // The writer always emits the required numFmt with formatCode="General" for the default format, so
            // treat that as "no explicit code" — otherwise an unformatted chart's data-label format drifts from
            // <none> to "General" on round-trip.
            chart.DataLabelNumberFormatCode =
                string.Equals(numberFormatCode, "General", StringComparison.Ordinal) ? null : numberFormatCode;
            chart.DataLabelNumberFormatSourceLinked = ReadNullableBool(numberFormatElement?.Attribute("sourceLinked")?.Value);
            chart.ShowDataLabelValue = XlsxChartScalarReader.IsTrue(dataLabels.Element(ChartNs + "showVal")?.Attribute("val")?.Value);
            chart.ShowDataLabelLegendKey = XlsxChartScalarReader.IsTrue(dataLabels.Element(ChartNs + "showLegendKey")?.Attribute("val")?.Value);
            chart.ShowDataLabelBubbleSize = XlsxChartScalarReader.IsTrue(dataLabels.Element(ChartNs + "showBubbleSize")?.Attribute("val")?.Value);
            chart.ShowDataLabelCategoryName = XlsxChartScalarReader.IsTrue(dataLabels.Element(ChartNs + "showCatName")?.Attribute("val")?.Value);
            chart.ShowDataLabelSeriesName = XlsxChartScalarReader.IsTrue(dataLabels.Element(ChartNs + "showSerName")?.Attribute("val")?.Value);
            chart.ShowDataLabelPercentage = ChartTypeSupport.SupportsPercentageDataLabels(chart.Type)
                && XlsxChartScalarReader.IsTrue(dataLabels.Element(ChartNs + "showPercent")?.Attribute("val")?.Value);
            chart.ShowDataLabelCallouts = XlsxChartScalarReader.IsTrue(dataLabels.Element(ChartNs + "showLeaderLines")?.Attribute("val")?.Value);

            // Only mark ShowDataLabels=true when at least one visible label component is requested.
            // A <c:dLbls> element with all show flags = 0 means "labels present in XML but effectively
            // disabled"; rendering it would produce spurious per-point numeric labels on the chart.
            chart.ShowDataLabels = chart.ShowDataLabelValue
                || chart.ShowDataLabelCategoryName
                || chart.ShowDataLabelSeriesName
                || chart.ShowDataLabelPercentage
                || chart.ShowDataLabelBubbleSize
                || chart.ShowDataLabelLegendKey;
            var separator = dataLabels.Element(ChartNs + "separator");
            var separatorLiteral = separator?.Attribute("val")?.Value ?? separator?.Value;
            chart.DataLabelSeparator = FromXlsxDataLabelSeparator(separatorLiteral);
            // Custom (e.g. "Period") has no dedicated enum representation; preserve the literal
            // text out-of-band so the writer can re-emit it verbatim instead of coercing to Comma.
            chart.DataLabelSeparatorText = chart.DataLabelSeparator == ChartDataLabelSeparator.Custom
                ? separatorLiteral
                : null;
            ApplyDataLabelShapeProperties(dataLabels.Element(ChartNs + "spPr"), chart);
            ApplyDataLabelTextProperties(dataLabels.Element(ChartNs + "txPr"), chart);
            ApplyDataLabelLeaderLineProperties(dataLabels.Element(ChartNs + "leaderLines")?.Element(ChartNs + "spPr"), chart);
        }

        // Combo charts (bar+line, etc.) write one native plot-chart-type group per series subset.
        // Only the first group's <c:dLbls> is modeled above as chart-wide scalars; a later group
        // (e.g. a secondary-axis line series with its own data labels) would otherwise be silently
        // dropped on open. Preserve each later group's <c:dLbls> verbatim, keyed by its 0-based
        // group index, so XlsxChartXmlWriter can re-attach it to the same group on save.
        for (var groupIndex = 1; groupIndex < groups.Count; groupIndex++)
        {
            var groupDataLabels = groups[groupIndex].Element(ChartNs + "dLbls");
            if (groupDataLabels is null)
                continue;

            chart.AdditionalPlotGroupDataLabels.Add(
                new ChartPlotGroupDataLabelsXml(groupIndex, groupDataLabels.ToString(SaveOptions.DisableFormatting)));
        }
    }

    public static void ApplyPointDataLabels(XElement series, int seriesIndex, ChartModel chart)
    {
        var dataLabels = series.Element(ChartNs + "dLbls");
        if (dataLabels is null)
            return;

        var seriesFormat = ReadSeriesDataLabelFormat(dataLabels, seriesIndex);
        if (HasSeriesDataLabelMetadata(seriesFormat))
        {
            chart.SeriesDataLabelFormats.RemoveAll(existing => existing.SeriesIndex == seriesIndex);
            chart.SeriesDataLabelFormats.Add(seriesFormat);
        }

        foreach (var label in dataLabels.Elements(ChartNs + "dLbl"))
        {
            if (!int.TryParse(label.Element(ChartNs + "idx")?.Attribute("val")?.Value, out var pointIndex) ||
                pointIndex < 0)
            {
                continue;
            }

            var format = ReadPointDataLabelFormat(label, seriesIndex, pointIndex);
            if (!HasPointDataLabelMetadata(format))
            {
                continue;
            }

            chart.PointDataLabelFormats.RemoveAll(existing =>
                existing.SeriesIndex == seriesIndex &&
                existing.PointIndex == pointIndex);
            chart.PointDataLabelFormats.Add(format);
        }
    }

    /// <summary>
    /// Returns every native plot-chart-type group (barChart/lineChart/etc.) that is a direct child
    /// of <paramref name="plotArea"/>, in document order. A combo chart (e.g. bar+line) has more
    /// than one of these; each can carry its own group-level <c:dLbls>.
    /// </summary>
    private static List<XElement> FindAllPlotChartElements(XElement? plotArea)
    {
        var groups = new List<XElement>();
        if (plotArea is null)
            return groups;

        foreach (var element in plotArea.Elements())
        {
            if (IsPlotChartElement(element.Name))
                groups.Add(element);
        }

        return groups;
    }

    private static bool IsPlotChartElement(XName name) =>
        name == ChartNs + "barChart"
        || name == ChartNs + "lineChart"
        || name == ChartNs + "line3DChart"
        || name == ChartNs + "scatterChart"
        || name == ChartNs + "areaChart"
        || name == ChartNs + "area3DChart"
        || name == ChartNs + "radarChart"
        || name == ChartNs + "stockChart"
        || name == ChartNs + "bubbleChart"
        || name == ChartNs + "pie3DChart"
        || name == ChartNs + "pieChart"
        || name == ChartNs + "doughnutChart";

    private static XElement? FirstDescendant(XElement? element, XName name)
    {
        if (element is null)
            return null;

        foreach (var descendant in element.Descendants(name))
            return descendant;

        return null;
    }

    private static void ApplyDataLabelShapeProperties(XElement? shapeProperties, ChartModel chart)
    {
        var fill = shapeProperties?.Element(DrawingNs + "solidFill");
        if (fill is not null)
        {
            if (XlsxDrawingColorReader.TryReadThemeColorReference(fill, DrawingNs, out var fillThemeColor))
            {
                chart.DataLabelFillThemeColor = fillThemeColor;
                chart.DataLabelFillColor = null;
            }
            else if (XlsxDrawingColorReader.TryReadConcreteColor(fill, DrawingNs, out var fillColor))
            {
                chart.DataLabelFillColor = fillColor;
                chart.DataLabelFillThemeColor = null;
            }
        }

        var line = shapeProperties?.Element(DrawingNs + "ln");
        if (line is null)
            return;

        if (int.TryParse(line.Attribute("w")?.Value, out var emus))
            chart.DataLabelBorderThickness = Math.Clamp(emus / (double)DrawingMlCoordinateUnits.EmuPerPoint, 0, 10);

        var lineFill = line.Element(DrawingNs + "solidFill");
        if (lineFill is null)
            return;

        if (XlsxDrawingColorReader.TryReadThemeColorReference(lineFill, DrawingNs, out var borderThemeColor))
        {
            chart.DataLabelBorderThemeColor = borderThemeColor;
            chart.DataLabelBorderColor = null;
        }
        else if (XlsxDrawingColorReader.TryReadConcreteColor(lineFill, DrawingNs, out var borderColor))
        {
            chart.DataLabelBorderColor = borderColor;
            chart.DataLabelBorderThemeColor = null;
        }
    }

    private static void ApplyDataLabelTextProperties(XElement? textPropertiesRoot, ChartModel chart)
    {
        var bodyProperties = textPropertiesRoot?.Element(DrawingNs + "bodyPr");
        if (int.TryParse(bodyProperties?.Attribute("rot")?.Value, out var rotation))
            chart.DataLabelAngle = Math.Clamp(rotation / 60000.0, -90, 90);

        var textProperties = FirstDescendant(textPropertiesRoot, DrawingNs + "defRPr");
        if (textProperties is null)
            return;

        if (int.TryParse(textProperties.Attribute("sz")?.Value, out var size))
            chart.DataLabelFontSize = Math.Clamp(size / 100.0, 6, 72);

        var textFill = textProperties.Element(DrawingNs + "solidFill");
        if (textFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(textFill, DrawingNs, out var textThemeColor))
        {
            chart.DataLabelTextThemeColor = textThemeColor;
            chart.DataLabelTextColor = null;
        }
        else if (textFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(textFill, DrawingNs, out var textColor))
        {
            chart.DataLabelTextColor = textColor;
            chart.DataLabelTextThemeColor = null;
        }
    }

    private static ChartSeriesDataLabelFormat ReadSeriesDataLabelFormat(XElement dataLabels, int seriesIndex)
    {
        var pointStyle = ReadDataLabelStyle(dataLabels);
        var numberFormat = dataLabels.Element(ChartNs + "numFmt");
        var separator = dataLabels.Element(ChartNs + "separator");

        return new ChartSeriesDataLabelFormat(
            seriesIndex,
            pointStyle.FillColor,
            pointStyle.BorderColor,
            pointStyle.BorderThickness,
            pointStyle.TextColor,
            pointStyle.FontSize,
            pointStyle.FillThemeColor,
            pointStyle.BorderThemeColor,
            pointStyle.TextThemeColor,
            // R62-io-chart-legend-datalabels-6-1: a series-level <c:delete val="1"/> means "no
            // labels for this series", overriding the chart-wide default -- must be read like the
            // per-point delete (ReadPointDataLabelFormat) or the override is silently lost.
            // R62-io-chart-legend-datalabels-6-1: a series-level <c:delete val="1"/> means "no
            // labels for this series", overriding the chart-wide default -- must be read like the
            // per-point delete (ReadPointDataLabelFormat) or the override is silently lost.
            ReadNullableBool(dataLabels.Element(ChartNs + "delete")?.Attribute("val")?.Value),
            dataLabels.Element(ChartNs + "dLblPos") is { } position
                ? FromXlsxDataLabelPosition(position.Attribute("val")?.Value)
                : null,
            ReadNullableBool(dataLabels.Element(ChartNs + "showVal")?.Attribute("val")?.Value),
            ReadNullableBool(dataLabels.Element(ChartNs + "showCatName")?.Attribute("val")?.Value),
            ReadNullableBool(dataLabels.Element(ChartNs + "showSerName")?.Attribute("val")?.Value),
            ReadNullableBool(dataLabels.Element(ChartNs + "showLegendKey")?.Attribute("val")?.Value),
            ReadNullableBool(dataLabels.Element(ChartNs + "showPercent")?.Attribute("val")?.Value),
            ReadNullableBool(dataLabels.Element(ChartNs + "showBubbleSize")?.Attribute("val")?.Value),
            numberFormat?.Attribute("formatCode")?.Value,
            ReadNullableBool(numberFormat?.Attribute("sourceLinked")?.Value),
            separator?.Attribute("val")?.Value ?? separator?.Value);
    }

    private static void ApplyDataLabelLeaderLineProperties(XElement? shapeProperties, ChartModel chart)
    {
        var line = shapeProperties?.Element(DrawingNs + "ln");
        if (line is null)
            return;

        if (int.TryParse(line.Attribute("w")?.Value, out var emus))
            chart.DataLabelLeaderLineThickness = Math.Clamp(emus / (double)DrawingMlCoordinateUnits.EmuPerPoint, 0.5, 10);

        chart.DataLabelLeaderLineDashStyle = XlsxChartTrendlineErrorBarReader.FromXlsxPresetDash(
            line.Element(DrawingNs + "prstDash")?.Attribute("val")?.Value);

        var lineFill = line.Element(DrawingNs + "solidFill");
        if (lineFill is null)
            return;

        if (XlsxDrawingColorReader.TryReadThemeColorReference(lineFill, DrawingNs, out var themeColor))
        {
            chart.DataLabelLeaderLineThemeColor = themeColor;
            chart.DataLabelLeaderLineColor = null;
        }
        else if (XlsxDrawingColorReader.TryReadConcreteColor(lineFill, DrawingNs, out var color))
        {
            chart.DataLabelLeaderLineColor = color;
            chart.DataLabelLeaderLineThemeColor = null;
        }
    }

    private static ChartPointDataLabelFormat ReadPointDataLabelFormat(
        XElement label,
        int seriesIndex,
        int pointIndex)
    {
        var style = ReadDataLabelStyle(label);
        var numberFormat = label.Element(ChartNs + "numFmt");
        var separator = label.Element(ChartNs + "separator");
        var layout = XlsxChartMetadataReader.ReadManualLayout(label.Element(ChartNs + "layout"));
        var customText = label.Element(ChartNs + "tx");

        return new ChartPointDataLabelFormat(
            seriesIndex,
            pointIndex,
            style.FillColor,
            style.BorderColor,
            style.BorderThickness,
            style.TextColor,
            style.FontSize,
            style.FillThemeColor,
            style.BorderThemeColor,
            style.TextThemeColor,
            ReadNullableBool(label.Element(ChartNs + "delete")?.Attribute("val")?.Value),
            label.Element(ChartNs + "dLblPos") is { } position
                ? FromXlsxDataLabelPosition(position.Attribute("val")?.Value)
                : null,
            ReadNullableBool(label.Element(ChartNs + "showVal")?.Attribute("val")?.Value),
            ReadNullableBool(label.Element(ChartNs + "showCatName")?.Attribute("val")?.Value),
            ReadNullableBool(label.Element(ChartNs + "showSerName")?.Attribute("val")?.Value),
            ReadNullableBool(label.Element(ChartNs + "showLegendKey")?.Attribute("val")?.Value),
            ReadNullableBool(label.Element(ChartNs + "showPercent")?.Attribute("val")?.Value),
            ReadNullableBool(label.Element(ChartNs + "showBubbleSize")?.Attribute("val")?.Value),
            numberFormat?.Attribute("formatCode")?.Value,
            ReadNullableBool(numberFormat?.Attribute("sourceLinked")?.Value),
            separator?.Attribute("val")?.Value ?? separator?.Value,
            layout,
            customText?.ToString(SaveOptions.DisableFormatting));
    }

    private static DataLabelStyle ReadDataLabelStyle(XElement label)
    {
        CellColor? fillColor = null;
        WorkbookThemeColorReference? fillThemeColor = null;
        CellColor? borderColor = null;
        WorkbookThemeColorReference? borderThemeColor = null;
        double? borderThickness = null;
        CellColor? textColor = null;
        WorkbookThemeColorReference? textThemeColor = null;
        double? fontSize = null;
        var shapeProperties = label.Element(ChartNs + "spPr");
        var fill = shapeProperties?.Element(DrawingNs + "solidFill");
        if (fill is not null)
        {
            if (XlsxDrawingColorReader.TryReadThemeColorReference(fill, DrawingNs, out var theme))
                fillThemeColor = theme;
            else if (XlsxDrawingColorReader.TryReadConcreteColor(fill, DrawingNs, out var color))
                fillColor = color;
        }

        var line = shapeProperties?.Element(DrawingNs + "ln");
        if (line is not null)
        {
            if (int.TryParse(line.Attribute("w")?.Value, out var emus))
                borderThickness = Math.Clamp(emus / (double)DrawingMlCoordinateUnits.EmuPerPoint, 0, 10);

            var lineFill = line.Element(DrawingNs + "solidFill");
            if (lineFill is not null)
            {
                if (XlsxDrawingColorReader.TryReadThemeColorReference(lineFill, DrawingNs, out var theme))
                    borderThemeColor = theme;
                else if (XlsxDrawingColorReader.TryReadConcreteColor(lineFill, DrawingNs, out var color))
                    borderColor = color;
            }
        }

        var textProperties = FirstDescendant(label.Element(ChartNs + "txPr"), DrawingNs + "defRPr");
        if (textProperties is not null)
        {
            if (int.TryParse(textProperties.Attribute("sz")?.Value, out var size))
                fontSize = Math.Clamp(size / 100.0, 6, 72);

            var textFill = textProperties.Element(DrawingNs + "solidFill");
            if (textFill is not null)
            {
                if (XlsxDrawingColorReader.TryReadThemeColorReference(textFill, DrawingNs, out var theme))
                    textThemeColor = theme;
                else if (XlsxDrawingColorReader.TryReadConcreteColor(textFill, DrawingNs, out var color))
                    textColor = color;
            }
        }

        return new DataLabelStyle(
            fillColor,
            borderColor,
            borderThickness,
            textColor,
            fontSize,
            fillThemeColor,
            borderThemeColor,
            textThemeColor);
    }

    private static bool HasPointDataLabelMetadata(ChartPointDataLabelFormat format) =>
        format.FillColor is not null
        || format.BorderColor is not null
        || format.BorderThickness is not null
        || format.TextColor is not null
        || format.FontSize is not null
        || format.FillThemeColor is not null
        || format.BorderThemeColor is not null
        || format.TextThemeColor is not null
        || format.IsDeleted is not null
        || format.Position is not null
        || format.ShowValue is not null
        || format.ShowCategoryName is not null
        || format.ShowSeriesName is not null
        || format.ShowLegendKey is not null
        || format.ShowPercentage is not null
        || format.ShowBubbleSize is not null
        || !string.IsNullOrEmpty(format.NumberFormatCode)
        || format.NumberFormatSourceLinked is not null
        || format.SeparatorText is not null
        || format.Layout is not null
        || !string.IsNullOrEmpty(format.CustomTextXml);

    private static bool HasSeriesDataLabelMetadata(ChartSeriesDataLabelFormat format) =>
        format.FillColor is not null
        || format.BorderColor is not null
        || format.BorderThickness is not null
        || format.TextColor is not null
        || format.FontSize is not null
        || format.FillThemeColor is not null
        || format.BorderThemeColor is not null
        || format.TextThemeColor is not null
        // R62-io-chart-legend-datalabels-6-1: a delete-only series <c:dLbls> (no other children)
        // must still count as metadata, or the per-series "hide all labels" override is discarded.
        || format.IsDeleted is not null
        || format.Position is not null
        || format.ShowValue is not null
        || format.ShowCategoryName is not null
        || format.ShowSeriesName is not null
        || format.ShowLegendKey is not null
        || format.ShowPercentage is not null
        || format.ShowBubbleSize is not null
        || !string.IsNullOrEmpty(format.NumberFormatCode)
        || format.NumberFormatSourceLinked is not null
        || format.SeparatorText is not null;

    private sealed record DataLabelStyle(
        CellColor? FillColor,
        CellColor? BorderColor,
        double? BorderThickness,
        CellColor? TextColor,
        double? FontSize,
        WorkbookThemeColorReference? FillThemeColor,
        WorkbookThemeColorReference? BorderThemeColor,
        WorkbookThemeColorReference? TextThemeColor);

    private static ChartDataLabelPosition FromXlsxDataLabelPosition(string? value) =>
        value switch
        {
            "ctr" => ChartDataLabelPosition.Center,
            "inEnd" => ChartDataLabelPosition.InsideEnd,
            "outEnd" => ChartDataLabelPosition.OutsideEnd,
            "inBase" => ChartDataLabelPosition.InsideBase,
            // R65-default-fallback-swallow-sweep-1: l/r/t/b are the side positions Excel allows for
            // the line/scatter/bubble family (ISO/IEC 29500 §21.2.2.44); without these cases they fell
            // through to BestFit, silently losing which side the label was pinned to.
            "l" => ChartDataLabelPosition.Left,
            "r" => ChartDataLabelPosition.Right,
            "t" => ChartDataLabelPosition.Top,
            "b" => ChartDataLabelPosition.Bottom,
            _ => ChartDataLabelPosition.BestFit
        };

    private static bool? ReadNullableBool(string? value) =>
        value switch
        {
            "1" or "true" => true,
            "0" or "false" => false,
            _ => null
        };

    private static ChartDataLabelSeparator FromXlsxDataLabelSeparator(string? value) =>
        value is null
            // The element itself (or the chart-wide default when absent) means Excel's own
            // default separator, which is a comma.
            ? ChartDataLabelSeparator.Comma
            : value.Contains('\n')
                ? ChartDataLabelSeparator.NewLine
                : value switch
                {
                    "semicolon" => ChartDataLabelSeparator.Semicolon,
                    "newLine" => ChartDataLabelSeparator.NewLine,
                    "space" => ChartDataLabelSeparator.Space,
                    "comma" => ChartDataLabelSeparator.Comma,
                    "; " or ";" => ChartDataLabelSeparator.Semicolon,
                    " " => ChartDataLabelSeparator.Space,
                    "," or ", " => ChartDataLabelSeparator.Comma,
                    // Any other literal (e.g. Excel's "Period" separator, ". ") has no dedicated
                    // member; the raw text is preserved separately (see FromXlsxDataLabelSeparator
                    // callers) so it can be re-emitted verbatim instead of silently becoming Comma.
                    _ => ChartDataLabelSeparator.Custom
                };
}
