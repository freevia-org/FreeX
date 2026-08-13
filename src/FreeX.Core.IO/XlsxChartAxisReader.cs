using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxChartAxisReader
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    public static void ApplyAxisMetadata(XElement? plotArea, ChartModel chart)
    {
        if (plotArea is null)
            return;

        if (chart.Type is ChartType.Scatter or ChartType.Bubble)
        {
            // R62-io-chart-axis-6-1: a scatter/bubble chart with a secondary axis carries MULTIPLE
            // <c:scatterChart>/<c:bubbleChart> plot groups, each with its own axId pair — the primary
            // group is not necessarily the FIRST one in document order. Resolve which group is primary
            // vs secondary by the physical position (axPos) of each group's own value axis (l/b =
            // primary, r/t = secondary) instead of blindly taking the first group's axis pair, otherwise
            // chart.YAxis* silently ends up populated from the secondary axis's scale when the secondary
            // group happens to be declared first.
            var plotElementName = chart.Type == ChartType.Bubble
                ? ChartNs + "bubbleChart"
                : ChartNs + "scatterChart";
            var plotGroups = plotArea.Elements(plotElementName).ToList();
            var valueAxes = plotArea.Elements(ChartNs + "valAx").ToList();

            XElement? xAxis = null;
            XElement? yAxis = null;
            XElement? secondaryYAxis = null;

            foreach (var group in plotGroups)
            {
                var groupAxisIds = ReadAxisIds(group);
                var groupXAxis = FindAxisByIdOrIndex(valueAxes, groupAxisIds, 0);
                var groupYAxis = FindAxisByIdOrIndex(valueAxes, groupAxisIds, 1);
                var groupYAxisPosition = groupYAxis?.Element(ChartNs + "axPos")?.Attribute("val")?.Value;
                var isSecondaryGroup = groupYAxisPosition is "r" or "t";
                if (isSecondaryGroup)
                {
                    secondaryYAxis ??= groupYAxis;
                }
                else if (xAxis is null && yAxis is null)
                {
                    xAxis = groupXAxis;
                    yAxis = groupYAxis;
                }
            }

            // Fallback for malformed/legacy files where axPos is missing on every group (so every
            // group looked "secondary") — keep the previous first-group behavior rather than leaving
            // the primary axes unresolved.
            if (xAxis is null && yAxis is null && plotGroups.Count > 0)
            {
                var fallbackAxisIds = ReadAxisIds(plotGroups[0]);
                xAxis = FindAxisByIdOrIndex(valueAxes, fallbackAxisIds, 0);
                yAxis = FindAxisByIdOrIndex(valueAxes, fallbackAxisIds, 1);
            }

            chart.XAxisTitle = ReadAxisTitle(xAxis);
            chart.YAxisTitle = ReadAxisTitle(yAxis);
            chart.XAxisTitleLayout = XlsxChartMetadataReader.ReadManualLayout(AxisTitleLayout(xAxis));
            chart.YAxisTitleLayout = XlsxChartMetadataReader.ReadManualLayout(AxisTitleLayout(yAxis));
            chart.HideXAxis = ReadBool(xAxis?.Element(ChartNs + "delete")?.Attribute("val")?.Value);
            chart.HideYAxis = ReadBool(yAxis?.Element(ChartNs + "delete")?.Attribute("val")?.Value);
            chart.XAxisPosition = FromXlsxAxisPosition(xAxis?.Element(ChartNs + "axPos")?.Attribute("val")?.Value, ChartAxisPosition.Bottom);
            chart.YAxisPosition = FromXlsxAxisPosition(yAxis?.Element(ChartNs + "axPos")?.Attribute("val")?.Value, ChartAxisPosition.Left);
            ApplyAxisTitleFormatting(xAxis, chart, isXAxis: true);
            ApplyAxisTitleFormatting(yAxis, chart, isXAxis: false);
            ApplyValueAxisProperties(xAxis, chart, useXAxis: true);
            ApplyValueAxisProperties(yAxis, chart, useXAxis: false);
            ApplyAxisLabelFormatting(xAxis, chart, useXAxis: true);
            ApplyAxisLabelFormatting(yAxis, chart, useXAxis: false);

            // R62-io-chart-axis-6-1: capture the secondary value axis's own min/max/title/log/units
            // separately, matching the non-scatter/bubble branch below (ApplySecondaryAxisProperties)
            // — previously this was never called for scatter/bubble at all.
            ApplySecondaryAxisProperties(secondaryYAxis, chart);
            return;
        }

        // For bar-direction charts the value axis is HORIZONTAL (rendered at the bottom / X) and the
        // category axis is VERTICAL (rendered at the left / Y) — the reverse of every other chart
        // family. That means the axis scaling AND orientation each need to be routed to the field the
        // renderer/sanitizer actually reads for that physical position (see the comment further below),
        // not the field that would be "natural" for a category vs. value axis.
        var valueAxisOnX = chart.Type is ChartType.Bar
            or ChartType.StackedBar
            or ChartType.PercentStackedBar
            or ChartType.ThreeDBar;

        var categoryAxis = plotArea.Element(ChartNs + "dateAx") ?? plotArea.Element(ChartNs + "catAx");
        chart.XAxisIsDateAxis = categoryAxis?.Name == ChartNs + "dateAx";
        chart.XAxisTitle = ReadAxisTitle(categoryAxis);
        chart.XAxisTitleLayout = XlsxChartMetadataReader.ReadManualLayout(AxisTitleLayout(categoryAxis));
        chart.HideXAxis = ReadBool(categoryAxis?.Element(ChartNs + "delete")?.Attribute("val")?.Value);
        chart.XAxisPosition = FromXlsxAxisPosition(categoryAxis?.Element(ChartNs + "axPos")?.Attribute("val")?.Value, ChartAxisPosition.Bottom);
        ApplyAxisTitleFormatting(categoryAxis, chart, isXAxis: true);
        // Route the category axis's own reverse-order flag to whichever field the renderer reads for
        // the category axis's PHYSICAL position: YAxisReverseOrder for the left axis (bar-family),
        // XAxisReverseOrder for the bottom axis (everything else). Must happen before
        // ApplyValueAxisProperties below, which — for bar-family charts — also writes
        // XAxisReverseOrder (from the value axis, now on the bottom); routing the category flag to Y
        // instead of X means that write no longer clobbers this one.
        ApplyCategoryAxisProperties(categoryAxis, chart, categoryAxisOnY: valueAxisOnX);
        ApplyAxisLabelFormatting(categoryAxis, chart, useXAxis: true);
        // Combo charts (e.g. bar-primary + line-secondary) carry TWO <c:valAx> elements — the primary
        // is always emitted first (see XlsxChartXmlWriter.Axes.cs), so plain positional indexing (as
        // used throughout this non-scatter branch, which never matches axes by axId either) is enough
        // to find the secondary one without a schema-breaking axId lookup.
        var valueAxisElements = plotArea.Elements(ChartNs + "valAx").ToList();
        var valueAxis = valueAxisElements.Count > 0 ? valueAxisElements[0] : null;
        chart.YAxisTitle = ReadAxisTitle(valueAxis);
        chart.YAxisTitleLayout = XlsxChartMetadataReader.ReadManualLayout(AxisTitleLayout(valueAxis));
        chart.HideYAxis = ReadBool(valueAxis?.Element(ChartNs + "delete")?.Attribute("val")?.Value);
        chart.YAxisPosition = FromXlsxAxisPosition(valueAxis?.Element(ChartNs + "axPos")?.Attribute("val")?.Value, ChartAxisPosition.Left);
        ApplyAxisTitleFormatting(valueAxis, chart, isXAxis: false);
        // For bar-direction charts the value axis is HORIZONTAL (rendered at the bottom / X), so its
        // scaling (min/max/units/log/number-format) belongs to the X-axis bounds — that is where the
        // renderer (CreateCategoryAxis on Y + value LinearAxis on Bottom) and the sanitizer
        // (SupportsXAxisBounds(Bar)==true) read it from. Routing it to Y* would be wiped by the
        // sanitizer (SupportsYAxisBounds(Bar)==false), silently dropping e.g. a fixed 0..1 progress
        // axis. Column/line/etc. keep the value axis on Y as before.
        ApplyValueAxisProperties(valueAxis, chart, useXAxis: valueAxisOnX);
        ApplyAxisLabelFormatting(valueAxis, chart, useXAxis: false);

        // R30-io-chart-series-cache-deep-2: the secondary value axis (second <c:valAx>, e.g. a combo
        // chart's line-on-secondary-axis series group) has its OWN title/min/max/number-format that must
        // not be conflated with the primary axis captured above.
        ApplySecondaryAxisProperties(valueAxisElements.Count > 1 ? valueAxisElements[1] : null, chart);
    }

    private static void ApplySecondaryAxisProperties(XElement? axisElement, ChartModel chart)
    {
        if (axisElement is null)
            return;

        chart.SecondaryAxisTitle = ReadAxisTitle(axisElement);
        var scaling = axisElement.Element(ChartNs + "scaling");
        chart.SecondaryAxisMinimum = ReadDouble(scaling?.Element(ChartNs + "min")?.Attribute("val")?.Value);
        chart.SecondaryAxisMaximum = ReadDouble(scaling?.Element(ChartNs + "max")?.Attribute("val")?.Value);

        // R62-io-chart-axis-6-2: capture the secondary axis's OWN majorUnit/minorUnit — without this,
        // the writer has no per-secondary-axis unit to fall back on and always clones the primary (Y)
        // axis's majorUnit/minorUnit onto the secondary axis on every save.
        chart.SecondaryAxisMajorUnit = ReadDouble(axisElement.Element(ChartNs + "majorUnit")?.Attribute("val")?.Value);
        chart.SecondaryAxisMinorUnit = ReadDouble(axisElement.Element(ChartNs + "minorUnit")?.Attribute("val")?.Value);

        // R71-io-chart-axis-4-2: capture the secondary axis's OWN <c:dispUnits> — without this, the
        // writer has no per-secondary-axis display unit to read and always clones the primary (Y)
        // axis's display unit onto the secondary axis on every save.
        var secondaryDispUnitsElement = axisElement.Element(ChartNs + "dispUnits");
        chart.SecondaryAxisDisplayUnit = FromXlsxAxisDisplayUnit(
            secondaryDispUnitsElement?
                .Element(ChartNs + "builtInUnit")?
                .Attribute("val")?
                .Value);
        chart.SecondaryAxisCustomDisplayUnit = ReadDouble(
            secondaryDispUnitsElement?
                .Element(ChartNs + "custUnit")?
                .Attribute("val")?
                .Value);
        chart.ShowSecondaryAxisDisplayUnitLabel = secondaryDispUnitsElement?.Element(ChartNs + "dispUnitsLbl") is not null;

        // R36-io-chart-axis-scaling-2-2: capture the secondary axis's OWN orientation/log-scale/
        // tick-style/crossing — these must not be conflated with the primary (Y) axis's fields
        // (chart.YAxis*), otherwise the writer silently overwrites this axis's settings with the
        // primary axis's current ones on every save.
        chart.SecondaryAxisReverseOrder = IsReverseOrientation(scaling);
        var logBaseElement = scaling?.Element(ChartNs + "logBase");
        chart.SecondaryAxisLogScale = logBaseElement is not null;
        chart.SecondaryAxisLogBase = ReadDouble(logBaseElement?.Attribute("val")?.Value);
        chart.SecondaryAxisMajorTickStyle = FromXlsxTickMark(axisElement.Element(ChartNs + "majorTickMark")?.Attribute("val")?.Value, ChartAxisTickStyle.Outside);
        chart.SecondaryAxisMinorTickStyle = FromXlsxTickMark(axisElement.Element(ChartNs + "minorTickMark")?.Attribute("val")?.Value, ChartAxisTickStyle.None);
        var secondaryCrossing = ReadAxisCrossing(axisElement);
        chart.SecondaryAxisCrosses = secondaryCrossing.Crosses;
        chart.SecondaryAxisCrossesAt = secondaryCrossing.CrossesAt;
        chart.SecondaryAxisCrossBetween = secondaryCrossing.CrossBetween;

        var numberFormatElement = axisElement.Element(ChartNs + "numFmt");
        var numberFormatCode = numberFormatElement?.Attribute("formatCode")?.Value;
        chart.SecondaryAxisNumberFormat = FromXlsxNumberFormatCode(numberFormatCode);
        chart.SecondaryAxisNumberFormatCode = numberFormatCode;
        chart.SecondaryAxisNumberFormatSourceLinked = ReadNullableBool(numberFormatElement?.Attribute("sourceLinked")?.Value);
    }

    public static ChartDataLabelNumberFormat FromXlsxNumberFormatCode(string? formatCode) =>
        formatCode switch
        {
            "0.00" => ChartDataLabelNumberFormat.Number,
            "$#,##0.00" => ChartDataLabelNumberFormat.Currency,
            "0%" => ChartDataLabelNumberFormat.Percent,
            _ => ChartDataLabelNumberFormat.General
        };

    private static IReadOnlyList<string?> ReadAxisIds(XElement? chartElement) =>
        chartElement?
            .Elements(ChartNs + "axId")
            .Select(element => element.Attribute("val")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList() ?? [];

    private static XElement? FindAxisByIdOrIndex(IReadOnlyList<XElement> axes, IReadOnlyList<string?> axisIds, int index)
    {
        var axisId = AxisIdAt(axisIds, index);
        return FindAxisById(axes, axisId) ?? AxisAt(axes, index);
    }

    private static string? AxisIdAt(IReadOnlyList<string?> axisIds, int index) =>
        index >= 0 && index < axisIds.Count ? axisIds[index] : null;

    private static XElement? AxisAt(IReadOnlyList<XElement> axes, int index) =>
        index >= 0 && index < axes.Count ? axes[index] : null;

    private static XElement? AxisTitle(XElement? axisElement) =>
        axisElement?.Element(ChartNs + "title");

    private static XElement? AxisTitleLayout(XElement? axisElement) =>
        AxisTitle(axisElement)?.Element(ChartNs + "layout");

    private static string? ReadAxisTitle(XElement? axisElement) =>
        FirstNonBlankTitleText(AxisTitle(axisElement));

    /// <summary>
    /// R71-io-chart-axis-4-3: captures a plain axis title's explicit &lt;a:bodyPr&gt;@rot (e.g.
    /// rot="0" to force a vertical axis's title horizontal), stored raw (60,000ths-of-a-degree,
    /// same units as the XML attribute) so the writer can reproduce it exactly instead of always
    /// falling back to its hardcoded vertical/horizontal default.
    /// </summary>
    private static void ApplyAxisTitleRotation(XElement? titleElement, ChartModel chart, bool isXAxis)
    {
        var rotationValue = titleElement?
            .Element(ChartNs + "tx")?
            .Element(ChartNs + "rich")?
            .Element(DrawingNs + "bodyPr")?
            .Attribute("rot")?
            .Value;
        if (!int.TryParse(rotationValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rotation))
            return;

        if (isXAxis)
            chart.XAxisTitleRotation = rotation;
        else
            chart.YAxisTitleRotation = rotation;
    }

    private static string? FirstNonBlankTitleText(XElement? titleElement)
    {
        if (titleElement is null)
            return null;

        foreach (var textElement in titleElement.Descendants(DrawingNs + "t"))
        {
            var text = textElement.Value;
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return null;
    }

    private static void ApplyAxisTitleFormatting(XElement? axisElement, ChartModel chart, bool isXAxis)
    {
        var titleElement = AxisTitle(axisElement);
        ApplyAxisTitleRotation(titleElement, chart, isXAxis);

        var runProperties = FirstRunProperties(titleElement);
        if (runProperties is null)
            return;

        if (int.TryParse(runProperties.Attribute("sz")?.Value, out var size))
        {
            var fontSize = Math.Clamp(size / 100.0, 6, 72);
            // R44-meta-3: keep the shared field for back-compat (last axis read wins, as before),
            // but ALSO route into the per-axis override field per the isXAxis param so the X and Y
            // axis titles no longer clobber each other into a single shared value.
            chart.AxisTitleFontSize = fontSize;
            if (isXAxis)
                chart.XAxisTitleFontSize = fontSize;
            else
                chart.YAxisTitleFontSize = fontSize;
        }

        var solidFill = runProperties.Element(DrawingNs + "solidFill");
        if (solidFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(solidFill, DrawingNs, out var themeColor))
        {
            chart.AxisTitleTextThemeColor = themeColor;
            chart.AxisTitleTextColor = null;
            if (isXAxis)
            {
                chart.XAxisTitleTextThemeColor = themeColor;
                chart.XAxisTitleTextColor = null;
            }
            else
            {
                chart.YAxisTitleTextThemeColor = themeColor;
                chart.YAxisTitleTextColor = null;
            }
        }
        else if (solidFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(solidFill, DrawingNs, out var color))
        {
            chart.AxisTitleTextColor = color;
            chart.AxisTitleTextThemeColor = null;
            if (isXAxis)
            {
                chart.XAxisTitleTextColor = color;
                chart.XAxisTitleTextThemeColor = null;
            }
            else
            {
                chart.YAxisTitleTextColor = color;
                chart.YAxisTitleTextThemeColor = null;
            }
        }

        // Capture verbatim title XML when formatting is richer than the model fields can represent.
        // This preserves bold/italic/per-run colors/multiple-run titles on round-trip.
        if (titleElement is not null && HasRichAxisTitleFormatting(titleElement))
        {
            var verbatim = titleElement.ToString(SaveOptions.DisableFormatting);
            if (isXAxis)
                chart.XAxisTitleVerbatimXml = verbatim;
            else
                chart.YAxisTitleVerbatimXml = verbatim;
        }
    }

    /// <summary>
    /// Returns true when a &lt;c:title&gt; element contains formatting that the model
    /// fields (<see cref="ChartModel.AxisTitleFontSize"/> etc.) cannot losslessly represent:
    /// multiple text runs, bold, italic, underline, strikethrough, or baseline shifting.
    /// </summary>
    private static bool HasRichAxisTitleFormatting(XElement titleElement)
    {
        var runs = titleElement.Descendants(DrawingNs + "r").ToList();
        if (runs.Count > 1)
            return true;

        var rPr = runs.Count == 1 ? runs[0].Element(DrawingNs + "rPr") : null;
        if (rPr is null)
            return false;

        return rPr.Attribute("b") is not null ||
               rPr.Attribute("i") is not null ||
               rPr.Attribute("u") is not null ||
               rPr.Attribute("strike") is not null ||
               rPr.Attribute("baseline") is not null;
    }

    private static void ApplyAxisLabelFormatting(XElement? axisElement, ChartModel chart, bool useXAxis)
    {
        var textProperties = axisElement?.Element(ChartNs + "txPr");
        if (textProperties is null)
            return;

        var runProperties = FirstTextRunProperties(textProperties);

        var textColor = TryReadTextColor(runProperties);
        var textThemeColor = TryReadTextThemeColor(runProperties);
        var fontSize = int.TryParse(runProperties?.Attribute("sz")?.Value, out var size)
            ? Math.Clamp(size / 100.0, 6, 72)
            : (double?)null;
        var angle = int.TryParse(textProperties.Element(DrawingNs + "bodyPr")?.Attribute("rot")?.Value, out var rotation)
            ? Math.Clamp(rotation / 60000.0, -90, 90)
            : (double?)null;

        if (useXAxis)
        {
            if (textThemeColor is { } themeColor)
            {
                chart.XAxisLabelTextThemeColor = themeColor;
                chart.XAxisLabelTextColor = null;
            }
            else if (textColor is not null)
            {
                chart.XAxisLabelTextThemeColor = null;
            }
            if (textColor is { } color)
                chart.XAxisLabelTextColor = color;
            if (fontSize is { } labelFontSize)
                chart.XAxisLabelFontSize = labelFontSize;
            if (angle is { } labelAngle)
                chart.XAxisLabelAngle = labelAngle;
            return;
        }

        if (textThemeColor is { } yThemeColor)
        {
            chart.YAxisLabelTextThemeColor = yThemeColor;
            chart.YAxisLabelTextColor = null;
        }
        else if (textColor is not null)
        {
            chart.YAxisLabelTextThemeColor = null;
        }
        if (textColor is { } yColor)
            chart.YAxisLabelTextColor = yColor;
        if (fontSize is { } yFontSize)
            chart.YAxisLabelFontSize = yFontSize;
        if (angle is { } yAngle)
            chart.YAxisLabelAngle = yAngle;
    }

    private static CellColor? TryReadTextColor(XElement? runProperties)
    {
        var solidFill = runProperties?.Element(DrawingNs + "solidFill");
        return solidFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(solidFill, DrawingNs, out var color)
            ? color
            : null;
    }

    private static WorkbookThemeColorReference? TryReadTextThemeColor(XElement? runProperties)
    {
        var solidFill = runProperties?.Element(DrawingNs + "solidFill");
        return solidFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(solidFill, DrawingNs, out var themeColor)
            ? themeColor
            : null;
    }

    private static XElement? FirstRunProperties(XElement? element) =>
        FirstDescendant(element, DrawingNs + "rPr");

    private static XElement? FirstTextRunProperties(XElement? textProperties) =>
        FirstDescendant(textProperties, DrawingNs + "defRPr")
        ?? FirstDescendant(textProperties, DrawingNs + "rPr");

    private static XElement? FirstDescendant(XElement? element, XName name)
    {
        if (element is null)
            return null;

        foreach (var descendant in element.Descendants(name))
            return descendant;

        return null;
    }

    private static XElement? FindAxisById(IEnumerable<XElement> axes, string? axisId)
    {
        if (string.IsNullOrWhiteSpace(axisId))
            return null;

        foreach (var axis in axes)
        {
            if (axis.Element(ChartNs + "axId")?.Attribute("val")?.Value == axisId)
                return axis;
        }

        return null;
    }

    private static void ApplyValueAxisProperties(XElement? axisElement, ChartModel chart, bool useXAxis)
    {
        if (axisElement is null)
            return;

        var scaling = axisElement.Element(ChartNs + "scaling");
        var minimum = ReadDouble(scaling?.Element(ChartNs + "min")?.Attribute("val")?.Value);
        var maximum = ReadDouble(scaling?.Element(ChartNs + "max")?.Attribute("val")?.Value);
        var majorUnit = ReadDouble(axisElement.Element(ChartNs + "majorUnit")?.Attribute("val")?.Value);
        var minorUnit = ReadDouble(axisElement.Element(ChartNs + "minorUnit")?.Attribute("val")?.Value);
        var logScale = scaling?.Element(ChartNs + "logBase") is not null;
        var logBase = ReadDouble(scaling?.Element(ChartNs + "logBase")?.Attribute("val")?.Value);
        var reverseOrder = IsReverseOrientation(scaling);
        var numberFormatElement = axisElement.Element(ChartNs + "numFmt");
        var numberFormatCode = numberFormatElement?.Attribute("formatCode")?.Value;
        var numberFormat = FromXlsxNumberFormatCode(numberFormatCode);
        var numberFormatSourceLinked = ReadNullableBool(numberFormatElement?.Attribute("sourceLinked")?.Value);
        var majorGridline = ReadAxisGridline(axisElement.Element(ChartNs + "majorGridlines"));
        var minorGridline = ReadAxisGridline(axisElement.Element(ChartNs + "minorGridlines"));
        var majorTickStyle = FromXlsxTickMark(axisElement.Element(ChartNs + "majorTickMark")?.Attribute("val")?.Value, ChartAxisTickStyle.Outside);
        var minorTickStyle = FromXlsxTickMark(axisElement.Element(ChartNs + "minorTickMark")?.Attribute("val")?.Value, ChartAxisTickStyle.None);
        var tickLabelPositionValue = axisElement.Element(ChartNs + "tickLblPos")?.Attribute("val")?.Value;
        var showLabels = tickLabelPositionValue != "none";
        var tickLabelPosition = FromXlsxTickLabelPosition(tickLabelPositionValue);
        var axisLine = ReadAxisLine(axisElement.Element(ChartNs + "spPr"));
        var crossing = ReadAxisCrossing(axisElement);
        var dispUnitsElement = axisElement.Element(ChartNs + "dispUnits");
        var displayUnit = FromXlsxAxisDisplayUnit(
            dispUnitsElement?
                .Element(ChartNs + "builtInUnit")?
                .Attribute("val")?
                .Value);
        var customDisplayUnit = ReadDouble(
            dispUnitsElement?
                .Element(ChartNs + "custUnit")?
                .Attribute("val")?
                .Value);
        // R36-io-chart-axis-scaling-2-3: <c:dispUnitsLbl/> is the "Show display units label on chart"
        // checkbox — capture its presence so the writer can round-trip the visible caption instead of
        // silently dropping it while still preserving the numeric scaling.
        var showDisplayUnitLabel = dispUnitsElement?.Element(ChartNs + "dispUnitsLbl") is not null;

        if (useXAxis)
        {
            chart.XAxisMinimum = minimum;
            chart.XAxisMaximum = maximum;
            chart.XAxisMajorUnit = majorUnit;
            chart.XAxisMinorUnit = minorUnit;
            chart.XAxisLogScale = logScale;
            chart.XAxisLogBase = logBase;
            chart.XAxisReverseOrder = reverseOrder;
            chart.XAxisNumberFormat = numberFormat;
            chart.XAxisNumberFormatCode = numberFormatCode;
            chart.XAxisNumberFormatSourceLinked = numberFormatSourceLinked;
            ApplyXAxisGridlineProperties(chart, majorGridline, minorGridline);
            chart.XAxisMajorTickStyle = majorTickStyle;
            chart.XAxisMinorTickStyle = minorTickStyle;
            chart.ShowXAxisLabels = showLabels;
            chart.XAxisTickLabelPosition = tickLabelPosition;
            ApplyXAxisLineProperties(chart, axisLine);
            chart.XAxisCrosses = crossing.Crosses;
            chart.XAxisCrossesAt = crossing.CrossesAt;
            chart.XAxisCrossBetween = crossing.CrossBetween;
            chart.XAxisDisplayUnit = displayUnit;
            chart.XAxisCustomDisplayUnit = customDisplayUnit;
            chart.ShowXAxisDisplayUnitLabel = showDisplayUnitLabel;
            return;
        }

        chart.YAxisMinimum = minimum;
        chart.YAxisMaximum = maximum;
        chart.YAxisMajorUnit = majorUnit;
        chart.YAxisMinorUnit = minorUnit;
        chart.YAxisLogScale = logScale;
        chart.YAxisLogBase = logBase;
        chart.YAxisReverseOrder = reverseOrder;
        chart.YAxisNumberFormat = numberFormat;
        chart.YAxisNumberFormatCode = numberFormatCode;
        chart.YAxisNumberFormatSourceLinked = numberFormatSourceLinked;
        ApplyYAxisGridlineProperties(chart, majorGridline, minorGridline);
        chart.YAxisMajorTickStyle = majorTickStyle;
        chart.YAxisMinorTickStyle = minorTickStyle;
        chart.ShowYAxisLabels = showLabels;
        chart.YAxisTickLabelPosition = tickLabelPosition;
        ApplyYAxisLineProperties(chart, axisLine);
        chart.YAxisCrosses = crossing.Crosses;
        chart.YAxisCrossesAt = crossing.CrossesAt;
        chart.YAxisCrossBetween = crossing.CrossBetween;
        chart.YAxisDisplayUnit = displayUnit;
        chart.YAxisCustomDisplayUnit = customDisplayUnit;
        chart.ShowYAxisDisplayUnitLabel = showDisplayUnitLabel;
    }

    private static void ApplyCategoryAxisProperties(XElement? axisElement, ChartModel chart, bool categoryAxisOnY)
    {
        if (axisElement is null)
            return;

        // R47-io-chart-axis-scaling-3-3: route the category axis's OWN gridlines to whichever field
        // the renderer reads for the category axis's PHYSICAL position, same as the reverse-order
        // routing below — otherwise, for bar-family charts, this always writes the X* gridline fields
        // and gets clobbered a few lines later by ApplyValueAxisProperties (which, for bar charts, also
        // targets X* via useXAxis: true), losing the category axis's own gridlines and the value
        // axis's gridlines never reaching Y*.
        if (categoryAxisOnY)
            ApplyYAxisGridlineProperties(
                chart,
                ReadAxisGridline(axisElement.Element(ChartNs + "majorGridlines")),
                ReadAxisGridline(axisElement.Element(ChartNs + "minorGridlines")));
        else
            ApplyXAxisGridlineProperties(
                chart,
                ReadAxisGridline(axisElement.Element(ChartNs + "majorGridlines")),
                ReadAxisGridline(axisElement.Element(ChartNs + "minorGridlines")));
        var categoryReverseOrder = IsReverseOrientation(axisElement.Element(ChartNs + "scaling"));
        if (categoryAxisOnY)
            chart.YAxisReverseOrder = categoryReverseOrder;
        else
            chart.XAxisReverseOrder = categoryReverseOrder;
        // R62-io-chart-axis-6-3: route the category axis's OWN tick-mark styles to whichever field the
        // renderer reads for the category axis's PHYSICAL position, same as gridlines/tickLblPos/
        // crosses above/below — otherwise, for bar-family charts, this always writes the X* tick fields
        // and gets clobbered a few lines later by ApplyValueAxisProperties (which, for bar charts, also
        // targets X* via useXAxis: true), losing the category axis's own tick styles.
        var categoryMajorTickStyle = FromXlsxTickMark(axisElement.Element(ChartNs + "majorTickMark")?.Attribute("val")?.Value, ChartAxisTickStyle.Outside);
        var categoryMinorTickStyle = FromXlsxTickMark(axisElement.Element(ChartNs + "minorTickMark")?.Attribute("val")?.Value, ChartAxisTickStyle.None);
        if (categoryAxisOnY)
        {
            chart.YAxisMajorTickStyle = categoryMajorTickStyle;
            chart.YAxisMinorTickStyle = categoryMinorTickStyle;
        }
        else
        {
            chart.XAxisMajorTickStyle = categoryMajorTickStyle;
            chart.XAxisMinorTickStyle = categoryMinorTickStyle;
        }
        var tickLabelPositionValue = axisElement.Element(ChartNs + "tickLblPos")?.Attribute("val")?.Value;
        var categoryShowLabels = tickLabelPositionValue != "none";
        var categoryTickLabelPosition = FromXlsxTickLabelPosition(tickLabelPositionValue);
        // R47-io-chart-axis-scaling-3-4: same routing as gridlines above — the category axis's own
        // tick-label visibility/position must land on Y* for bar-family charts, not clobber (and then
        // be clobbered by) the value axis's X* tickLblPos.
        if (categoryAxisOnY)
        {
            chart.ShowYAxisLabels = categoryShowLabels;
            chart.YAxisTickLabelPosition = categoryTickLabelPosition;
        }
        else
        {
            chart.ShowXAxisLabels = categoryShowLabels;
            chart.XAxisTickLabelPosition = categoryTickLabelPosition;
        }
        chart.XAxisLabelSkip = Math.Max(0, ReadInt(axisElement.Element(ChartNs + "tickLblSkip")?.Attribute("val")?.Value) ?? 0);
        chart.XAxisTickMarkSkip = Math.Max(0, ReadInt(axisElement.Element(ChartNs + "tickMarkSkip")?.Attribute("val")?.Value) ?? 0);
        chart.XAxisLabelOffset = Math.Max(0, ReadInt(axisElement.Element(ChartNs + "lblOffset")?.Attribute("val")?.Value) ?? 0);
        chart.XAxisNoMultiLevelLabels = ReadBool(axisElement.Element(ChartNs + "noMultiLvlLbl")?.Attribute("val")?.Value);
        chart.XAxisLabelAlignment = FromXlsxAxisLabelAlignment(axisElement.Element(ChartNs + "lblAlgn")?.Attribute("val")?.Value);

        // Category/date axes carry their own <c:numFmt> (e.g. "[$-409]d\-mmm;@" on a date axis).
        // Capture it so the renderer can format date-serial categories as Excel does (1-Jan, not 44562).
        var categoryNumberFormatElement = axisElement.Element(ChartNs + "numFmt");
        var categoryNumberFormatCode = categoryNumberFormatElement?.Attribute("formatCode")?.Value;
        if (!string.IsNullOrWhiteSpace(categoryNumberFormatCode))
        {
            chart.XAxisNumberFormatCode = categoryNumberFormatCode;
            chart.XAxisNumberFormat = FromXlsxNumberFormatCode(categoryNumberFormatCode);
            chart.XAxisNumberFormatSourceLinked =
                ReadNullableBool(categoryNumberFormatElement?.Attribute("sourceLinked")?.Value);
        }

        if (axisElement.Name == ChartNs + "dateAx")
        {
            chart.XAxisBaseTimeUnit = FromXlsxDateAxisUnit(axisElement.Element(ChartNs + "baseTimeUnit")?.Attribute("val")?.Value);
            chart.XAxisMajorTimeUnit = FromXlsxDateAxisUnit(axisElement.Element(ChartNs + "majorTimeUnit")?.Attribute("val")?.Value);
            chart.XAxisMinorTimeUnit = FromXlsxDateAxisUnit(axisElement.Element(ChartNs + "minorTimeUnit")?.Attribute("val")?.Value);
            // The writer always emits a numeric <c:majorUnit>/<c:minorUnit> alongside the time-unit
            // elements on a date axis (ToAxisUnitXml("majorUnit", chart.XAxisMajorUnit, ...)); read them
            // back too, otherwise a round-tripped date axis loses its explicit major/minor unit count.
            chart.XAxisMajorUnit = ReadDouble(axisElement.Element(ChartNs + "majorUnit")?.Attribute("val")?.Value);
            chart.XAxisMinorUnit = ReadDouble(axisElement.Element(ChartNs + "minorUnit")?.Attribute("val")?.Value);

            // R71-io-chart-axis-4-1: a date axis's own explicit <c:scaling>/<c:min>/<c:max> (a pinned
            // date range, e.g. min="43831" max="44926") was never read — ApplyCategoryAxisProperties
            // only ever inspected <c:scaling> for its <c:orientation> above — so the writer (which never
            // emitted <c:min>/<c:max> for catAx/dateAx either) silently dropped the pinned range on
            // every round-trip.
            var dateAxisScaling = axisElement.Element(ChartNs + "scaling");
            chart.XAxisMinimum = ReadDouble(dateAxisScaling?.Element(ChartNs + "min")?.Attribute("val")?.Value);
            chart.XAxisMaximum = ReadDouble(dateAxisScaling?.Element(ChartNs + "max")?.Attribute("val")?.Value);
        }
        // R62-io-chart-axis-6-3: same routing as the tick-mark styles above — the category axis's own
        // spPr line color/thickness must land on Y* for bar-family charts, not clobber (and then be
        // clobbered by) the value axis's X* line properties.
        var categoryAxisLine = ReadAxisLine(axisElement.Element(ChartNs + "spPr"));
        if (categoryAxisOnY)
            ApplyYAxisLineProperties(chart, categoryAxisLine);
        else
            ApplyXAxisLineProperties(chart, categoryAxisLine);

        // R47-io-chart-axis-scaling-3-1: route the category axis's OWN crosses/crossesAt to Y* for
        // bar-family charts (same reasoning as gridlines/tickLblPos above) — otherwise it is always
        // written to X* and is immediately clobbered by ApplyValueAxisProperties's own X* crosses/
        // crossesAt for a horizontal Bar chart, permanently losing the category axis's crossing point.
        var crossing = ReadAxisCrossing(axisElement);
        if (categoryAxisOnY)
        {
            chart.YAxisCrosses = crossing.Crosses;
            chart.YAxisCrossesAt = crossing.CrossesAt;
        }
        else
        {
            chart.XAxisCrosses = crossing.Crosses;
            chart.XAxisCrossesAt = crossing.CrossesAt;
        }
    }

    private static void ApplyXAxisGridlineProperties(
        ChartModel chart,
        AxisGridlineProperties majorGridline,
        AxisGridlineProperties minorGridline)
    {
        chart.ShowXAxisMajorGridlines = majorGridline.Visible;
        chart.ShowXAxisMinorGridlines = minorGridline.Visible;
        if (majorGridline.Color is { } majorColor)
            chart.XAxisMajorGridlineColor = majorColor;
        if (minorGridline.Color is { } minorColor)
            chart.XAxisMinorGridlineColor = minorColor;
        if (majorGridline.Thickness is { } majorThickness)
            chart.XAxisGridlineThickness = majorThickness;
        else if (minorGridline.Thickness is { } minorThickness)
            chart.XAxisGridlineThickness = minorThickness;
    }

    private static void ApplyYAxisGridlineProperties(
        ChartModel chart,
        AxisGridlineProperties majorGridline,
        AxisGridlineProperties minorGridline)
    {
        chart.ShowYAxisMajorGridlines = majorGridline.Visible;
        chart.ShowYAxisMinorGridlines = minorGridline.Visible;
        if (majorGridline.Color is { } majorColor)
            chart.YAxisMajorGridlineColor = majorColor;
        if (minorGridline.Color is { } minorColor)
            chart.YAxisMinorGridlineColor = minorColor;
        if (majorGridline.Thickness is { } majorThickness)
            chart.YAxisGridlineThickness = majorThickness;
        else if (minorGridline.Thickness is { } minorThickness)
            chart.YAxisGridlineThickness = minorThickness;
    }

    private static AxisGridlineProperties ReadAxisGridline(XElement? gridlineElement)
    {
        if (gridlineElement is null)
            return new AxisGridlineProperties(false, null, null);

        var line = gridlineElement
            .Element(ChartNs + "spPr")?
            .Element(DrawingNs + "ln");
        var thickness = int.TryParse(line?.Attribute("w")?.Value, out var emus)
            ? Math.Clamp(emus / (double)DrawingMlCoordinateUnits.EmuPerPoint, 0.25, 10)
            : (double?)null;
        CellColor? color = null;
        var fill = line?.Element(DrawingNs + "solidFill");
        if (fill is not null && XlsxDrawingColorReader.TryReadConcreteColor(fill, DrawingNs, out var concreteColor))
            color = concreteColor;

        return new AxisGridlineProperties(true, color, thickness);
    }

    private readonly record struct AxisGridlineProperties(bool Visible, CellColor? Color, double? Thickness);

    private static AxisCrossingProperties ReadAxisCrossing(XElement axisElement)
    {
        var crossesAt = ReadDouble(axisElement.Element(ChartNs + "crossesAt")?.Attribute("val")?.Value);
        var crosses = crossesAt is not null
            ? ChartAxisCrosses.Custom
            : FromXlsxAxisCrosses(axisElement.Element(ChartNs + "crosses")?.Attribute("val")?.Value);
        var crossBetween = FromXlsxAxisCrossBetween(axisElement.Element(ChartNs + "crossBetween")?.Attribute("val")?.Value);
        return new AxisCrossingProperties(crosses, crossesAt, crossBetween);
    }

    private readonly record struct AxisCrossingProperties(
        ChartAxisCrosses Crosses,
        double? CrossesAt,
        ChartAxisCrossBetween? CrossBetween);

    private static void ApplyXAxisLineProperties(ChartModel chart, AxisLineProperties axisLine)
    {
        if (axisLine.Color is { } color)
            chart.XAxisLineColor = color;
        if (axisLine.Thickness is { } thickness)
            chart.XAxisLineThickness = thickness;
    }

    private static void ApplyYAxisLineProperties(ChartModel chart, AxisLineProperties axisLine)
    {
        if (axisLine.Color is { } color)
            chart.YAxisLineColor = color;
        if (axisLine.Thickness is { } thickness)
            chart.YAxisLineThickness = thickness;
    }

    private static AxisLineProperties ReadAxisLine(XElement? shapeProperties)
    {
        var line = shapeProperties?.Element(DrawingNs + "ln");
        if (line is null)
            return new AxisLineProperties(null, null);

        var thickness = int.TryParse(line.Attribute("w")?.Value, out var emus)
            ? Math.Clamp(emus / (double)DrawingMlCoordinateUnits.EmuPerPoint, 0.5, 10)
            : (double?)null;
        CellColor? color = null;
        var fill = line.Element(DrawingNs + "solidFill");
        if (fill is not null && XlsxDrawingColorReader.TryReadConcreteColor(fill, DrawingNs, out var concreteColor))
            color = concreteColor;

        return new AxisLineProperties(color, thickness);
    }

    private static ChartAxisTickStyle FromXlsxTickMark(string? value, ChartAxisTickStyle fallback) =>
        value switch
        {
            "in" => ChartAxisTickStyle.Inside,
            "out" => ChartAxisTickStyle.Outside,
            "cross" => ChartAxisTickStyle.Cross,
            "none" => ChartAxisTickStyle.None,
            _ => fallback
        };

    private static ChartAxisTickLabelPosition FromXlsxTickLabelPosition(string? value) =>
        value switch
        {
            "low" => ChartAxisTickLabelPosition.Low,
            "high" => ChartAxisTickLabelPosition.High,
            _ => ChartAxisTickLabelPosition.NextTo
        };

    private static ChartAxisPosition FromXlsxAxisPosition(string? value, ChartAxisPosition fallback) =>
        value switch
        {
            "b" => ChartAxisPosition.Bottom,
            "t" => ChartAxisPosition.Top,
            "l" => ChartAxisPosition.Left,
            "r" => ChartAxisPosition.Right,
            _ => fallback
        };

    private static ChartAxisCrosses FromXlsxAxisCrosses(string? value) =>
        value switch
        {
            "min" => ChartAxisCrosses.Minimum,
            "max" => ChartAxisCrosses.Maximum,
            _ => ChartAxisCrosses.AutoZero
        };

    private static ChartAxisCrossBetween? FromXlsxAxisCrossBetween(string? value) =>
        value switch
        {
            "between" => ChartAxisCrossBetween.Between,
            "midCat" => ChartAxisCrossBetween.MidCategory,
            _ => null
        };

    private static ChartAxisLabelAlignment FromXlsxAxisLabelAlignment(string? value) =>
        value switch
        {
            "l" => ChartAxisLabelAlignment.Left,
            "r" => ChartAxisLabelAlignment.Right,
            _ => ChartAxisLabelAlignment.Center
        };

    private static ChartDateAxisUnit? FromXlsxDateAxisUnit(string? value) =>
        value switch
        {
            "days" => ChartDateAxisUnit.Days,
            "months" => ChartDateAxisUnit.Months,
            "years" => ChartDateAxisUnit.Years,
            _ => null
        };

    private static ChartAxisDisplayUnit? FromXlsxAxisDisplayUnit(string? value) =>
        value switch
        {
            "hundreds" => ChartAxisDisplayUnit.Hundreds,
            "thousands" => ChartAxisDisplayUnit.Thousands,
            "tenThousands" => ChartAxisDisplayUnit.TenThousands,
            "hundredThousands" => ChartAxisDisplayUnit.HundredThousands,
            "millions" => ChartAxisDisplayUnit.Millions,
            "tenMillions" => ChartAxisDisplayUnit.TenMillions,
            "hundredMillions" => ChartAxisDisplayUnit.HundredMillions,
            "billions" => ChartAxisDisplayUnit.Billions,
            "trillions" => ChartAxisDisplayUnit.Trillions,
            _ => null
        };

    private readonly record struct AxisLineProperties(CellColor? Color, double? Thickness);

    private static double? ReadDouble(string? value) =>
        XlsxChartScalarReader.ReadOptionalDouble(value);

    private static int? ReadInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static bool ReadBool(string? value) =>
        XlsxWorksheetXmlValueParser.IsTruthy(value);

    private static bool? ReadNullableBool(string? value) =>
        value is null ? null
        : XlsxWorksheetXmlValueParser.IsTruthy(value) ? true
        : XlsxWorksheetXmlValueParser.IsFalse(value) ? false
        : null;

    private static bool IsReverseOrientation(XElement? scaling) =>
        scaling?.Element(ChartNs + "orientation")?.Attribute("val")?.Value == "maxMin";
}
