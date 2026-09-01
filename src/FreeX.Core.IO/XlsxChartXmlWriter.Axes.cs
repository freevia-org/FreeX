using System.Globalization;
using System.Xml.Linq;

using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxChartXmlWriter
{
    private static IEnumerable<XElement> ToChartAxesXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (chart.Type is ChartType.Scatter or ChartType.Bubble)
        {
            yield return ToValueAxisXml(
                chart.XAxisTitle,
                chart.XAxisTitleLayout,
                CategoryAxisId,
                ValueAxisId,
                ToXlsxAxisPosition(chart.XAxisPosition, "b"),
                chart.HideXAxis,
                chart.XAxisMinimum,
                chart.XAxisMaximum,
                chart.XAxisMajorUnit,
                chart.XAxisMinorUnit,
                chart.XAxisLogScale,
                chart.XAxisLogBase,
                chart.XAxisReverseOrder,
                chart.XAxisNumberFormat,
                chart.XAxisNumberFormatCode,
                chart.XAxisNumberFormatSourceLinked,
                chart.ShowXAxisMajorGridlines,
                chart.ShowXAxisMinorGridlines,
                chart.XAxisMajorGridlineColor,
                chart.XAxisMinorGridlineColor,
                chart.XAxisGridlineThickness,
                chart.XAxisMajorTickStyle,
                chart.XAxisMinorTickStyle,
                chart.XAxisLineColor,
                chart.XAxisLineThickness,
                chart.ShowXAxisLabels,
                chart.XAxisTickLabelPosition,
                chart.XAxisLabelTextColor,
                chart.XAxisLabelFontSize,
                chart.XAxisLabelAngle,
                chart.XAxisLabelTextThemeColor,
                chart.XAxisTitleTextThemeColor ?? chart.AxisTitleTextThemeColor,
                chart.XAxisTitleTextColor ?? chart.AxisTitleTextColor,
                chart.XAxisTitleFontSize ?? chart.AxisTitleFontSize,
                chart.XAxisCrosses,
                chart.XAxisCrossesAt,
                chart.XAxisCrossBetween,
                chart.XAxisDisplayUnit,
                chart.XAxisCustomDisplayUnit,
                chartNs,
                drawingNs,
                verbatimTitle: TryParseVerbatimAxisTitleXml(chart.XAxisTitleVerbatimXml),
                showDisplayUnitLabel: chart.ShowXAxisDisplayUnitLabel,
                axisTitleRotation: chart.XAxisTitleRotation);
            yield return ToValueAxisXml(
                chart.YAxisTitle,
                chart.YAxisTitleLayout,
                ValueAxisId,
                CategoryAxisId,
                ToXlsxAxisPosition(chart.YAxisPosition, "l"),
                chart.HideYAxis,
                chart.YAxisMinimum,
                chart.YAxisMaximum,
                chart.YAxisMajorUnit,
                chart.YAxisMinorUnit,
                chart.YAxisLogScale,
                chart.YAxisLogBase,
                chart.YAxisReverseOrder,
                chart.YAxisNumberFormat,
                chart.YAxisNumberFormatCode,
                chart.YAxisNumberFormatSourceLinked,
                chart.ShowYAxisMajorGridlines,
                chart.ShowYAxisMinorGridlines,
                chart.YAxisMajorGridlineColor,
                chart.YAxisMinorGridlineColor,
                chart.YAxisGridlineThickness,
                chart.YAxisMajorTickStyle,
                chart.YAxisMinorTickStyle,
                chart.YAxisLineColor,
                chart.YAxisLineThickness,
                chart.ShowYAxisLabels,
                chart.YAxisTickLabelPosition,
                chart.YAxisLabelTextColor,
                chart.YAxisLabelFontSize,
                chart.YAxisLabelAngle,
                chart.YAxisLabelTextThemeColor,
                chart.YAxisTitleTextThemeColor ?? chart.AxisTitleTextThemeColor,
                chart.YAxisTitleTextColor ?? chart.AxisTitleTextColor,
                chart.YAxisTitleFontSize ?? chart.AxisTitleFontSize,
                chart.YAxisCrosses,
                chart.YAxisCrossesAt,
                chart.YAxisCrossBetween,
                chart.YAxisDisplayUnit,
                chart.YAxisCustomDisplayUnit,
                chartNs,
                drawingNs,
                verbatimTitle: TryParseVerbatimAxisTitleXml(chart.YAxisTitleVerbatimXml),
                showDisplayUnitLabel: chart.ShowYAxisDisplayUnitLabel,
                axisTitleRotation: chart.YAxisTitleRotation);
            var scatterSecondaryIndexes = GetSecondaryAxisSeriesIndexes(chart, ChartTypeSupport.GetDataSeriesCount(chart));
            if (chart.Type == ChartType.Scatter && scatterSecondaryIndexes.Count > 0)
            {
                yield return ToValueAxisXml(
                    null,
                    null,
                    SecondaryValueAxisId,
                    CategoryAxisId,
                    "r",
                    chart.HideYAxis,
                    chart.YAxisMinimum,
                    chart.YAxisMaximum,
                    chart.YAxisMajorUnit,
                    chart.YAxisMinorUnit,
                    chart.YAxisLogScale,
                    chart.YAxisLogBase,
                    chart.YAxisReverseOrder,
                    chart.YAxisNumberFormat,
                    chart.YAxisNumberFormatCode,
                    chart.YAxisNumberFormatSourceLinked,
                    false,
                    false,
                    null,
                    null,
                    chart.YAxisGridlineThickness,
                    chart.YAxisMajorTickStyle,
                    chart.YAxisMinorTickStyle,
                    chart.YAxisLineColor,
                    chart.YAxisLineThickness,
                    chart.ShowYAxisLabels,
                    chart.YAxisTickLabelPosition,
                    chart.YAxisLabelTextColor,
                    chart.YAxisLabelFontSize,
                    chart.YAxisLabelAngle,
                    chart.YAxisLabelTextThemeColor,
                    chart.YAxisTitleTextThemeColor ?? chart.AxisTitleTextThemeColor,
                    chart.YAxisTitleTextColor ?? chart.AxisTitleTextColor,
                    chart.YAxisTitleFontSize ?? chart.AxisTitleFontSize,
                    chart.YAxisCrosses,
                    chart.YAxisCrossesAt,
                    chart.YAxisCrossBetween,
                    chart.YAxisDisplayUnit,
                    chart.YAxisCustomDisplayUnit,
                    chartNs,
                    drawingNs,
                    showDisplayUnitLabel: chart.ShowYAxisDisplayUnitLabel);
            }
            yield break;
        }

        yield return ToCategoryAxisXml(chart, chartNs, drawingNs);
        var valueAxisNumberFormat = ToEffectiveValueAxisNumberFormat(chart);
        // For bar-direction charts the value axis is HORIZONTAL (rendered at the bottom / X), so its
        // scaling (min/max/major/minor unit, log scale) is read from the X* fields
        // (XlsxChartAxisReader.ApplyValueAxisProperties(useXAxis: valueAxisOnX), SupportsXAxisBounds(Bar)==true).
        // Mirror that routing on write, otherwise a fixed bar-chart value range (e.g. 0..1) is silently dropped.
        var valueAxisOnX = IsHorizontalBarChart(chart.Type);
        // R71-io-chart-axis-4-4: crossBetween must follow the same valueAxisOnX routing as every other
        // value-axis property here -- for a horizontal Bar chart the value axis's own <c:crossBetween>
        // was captured into chart.XAxisCrossBetween (not YAxisCrossBetween), so reading only YAxisCrossBetween
        // silently discarded it on save.
        var valueAxisCrossBetween = ToEffectiveValueAxisCrossBetween(chart, valueAxisOnX);
        // R62-io-chart-axis-6-3: same routing, extended to the value axis's own tick-mark styles and
        // spPr line (XlsxChartAxisReader.ApplyValueAxisProperties(useXAxis: valueAxisOnX) captures
        // these into X* for bar-family charts) -- hardcoding these to Y* below silently dropped the
        // value axis's own tick/line style and instead emitted the (unrelated, unset) Y* default.
        var valueAxisMajorTickStyle = ToEffectiveAxisMajorTickStyle(chart, valueAxisOnX ? chart.XAxisMajorTickStyle : chart.YAxisMajorTickStyle);
        var valueAxisMinorTickStyle = valueAxisOnX ? chart.XAxisMinorTickStyle : chart.YAxisMinorTickStyle;
        var valueAxisLineColor = valueAxisOnX ? chart.XAxisLineColor : chart.YAxisLineColor;
        var valueAxisLineThickness = valueAxisOnX ? chart.XAxisLineThickness : chart.YAxisLineThickness;
        var valueAxisMinimum = valueAxisOnX ? chart.XAxisMinimum : chart.YAxisMinimum;
        var valueAxisMaximum = valueAxisOnX ? chart.XAxisMaximum : chart.YAxisMaximum;
        var valueAxisMajorUnit = valueAxisOnX ? chart.XAxisMajorUnit : chart.YAxisMajorUnit;
        var valueAxisMinorUnit = valueAxisOnX ? chart.XAxisMinorUnit : chart.YAxisMinorUnit;
        var valueAxisLogScale = valueAxisOnX ? chart.XAxisLogScale : chart.YAxisLogScale;
        var valueAxisLogBase = valueAxisOnX ? chart.XAxisLogBase : chart.YAxisLogBase;
        // R16-meta-1: mirror the reader's routing (XlsxChartAxisReader.ApplyValueAxisProperties /
        // ApplyCategoryAxisProperties) — for bar-family charts the value axis is physically on X and
        // the category axis is physically on Y, so their reverse-order flags were captured swapped on
        // read (XAxisReverseOrder for the value axis, YAxisReverseOrder for the category axis). Emitting
        // the value axis's <c:orientation> from YAxisReverseOrder unconditionally silently moved a
        // category-axis reversal onto the value axis on every save of a horizontal Bar chart.
        var valueAxisReverseOrder = valueAxisOnX ? chart.XAxisReverseOrder : chart.YAxisReverseOrder;
        // R47-io-chart-axis-scaling-3-1/3-2/3-3/3-4: same routing, extended to the value axis's own
        // gridlines/tick-label visibility+position/crosses(+crossesAt)/display-units — these were
        // previously hardcoded to Y* below regardless of valueAxisOnX, so for a horizontal Bar chart
        // (whose value axis was correctly captured into X* by the reader) they were written with the
        // still-default/unset Y* values, silently dropping the value axis's own settings.
        var valueAxisMinorGridlineColor = valueAxisOnX ? chart.XAxisMinorGridlineColor : chart.YAxisMinorGridlineColor;
        var valueAxisGridlineThickness = valueAxisOnX ? chart.XAxisGridlineThickness : chart.YAxisGridlineThickness;
        var showValueAxisMajorGridlines = ToEffectiveShowValueAxisMajorGridlines(chart, valueAxisOnX);
        var valueAxisMajorGridlineColor = valueAxisOnX ? chart.XAxisMajorGridlineColor : chart.YAxisMajorGridlineColor;
        var valueAxisShowMinorGridlines = valueAxisOnX ? chart.ShowXAxisMinorGridlines : chart.ShowYAxisMinorGridlines;
        var valueAxisShowLabels = valueAxisOnX ? chart.ShowXAxisLabels : chart.ShowYAxisLabels;
        var valueAxisTickLabelPosition = valueAxisOnX ? chart.XAxisTickLabelPosition : chart.YAxisTickLabelPosition;
        var valueAxisCrosses = valueAxisOnX ? chart.XAxisCrosses : chart.YAxisCrosses;
        var valueAxisCrossesAt = valueAxisOnX ? chart.XAxisCrossesAt : chart.YAxisCrossesAt;
        var valueAxisDisplayUnit = valueAxisOnX ? chart.XAxisDisplayUnit : chart.YAxisDisplayUnit;
        var valueAxisCustomDisplayUnit = valueAxisOnX ? chart.XAxisCustomDisplayUnit : chart.YAxisCustomDisplayUnit;
        var valueAxisShowDisplayUnitLabel = valueAxisOnX ? chart.ShowXAxisDisplayUnitLabel : chart.ShowYAxisDisplayUnitLabel;
        // r190: the title group, routed like every other value-axis property here. For bar-family
        // charts the value axis is physically on X, so its title lives in X* -- the complement of
        // ToCategoryAxisXml above and of what XlsxChartAxisReader now stores.
        var valueTitle = valueAxisOnX ? chart.XAxisTitle : chart.YAxisTitle;
        var valueTitleLayout = valueAxisOnX ? chart.XAxisTitleLayout : chart.YAxisTitleLayout;
        var valueTitleVerbatimXml = valueAxisOnX ? chart.XAxisTitleVerbatimXml : chart.YAxisTitleVerbatimXml;
        var valueTitleThemeColor = valueAxisOnX ? chart.XAxisTitleTextThemeColor : chart.YAxisTitleTextThemeColor;
        var valueTitleColor = valueAxisOnX ? chart.XAxisTitleTextColor : chart.YAxisTitleTextColor;
        var valueTitleFontSize = valueAxisOnX ? chart.XAxisTitleFontSize : chart.YAxisTitleFontSize;
        var valueTitleRotation = valueAxisOnX ? chart.XAxisTitleRotation : chart.YAxisTitleRotation;
        yield return ToValueAxisXml(
            valueTitle,
            valueTitleLayout,
            ValueAxisId,
            CategoryAxisId,
            ToXlsxValueAxisPosition(chart),
            chart.HideYAxis,
            valueAxisMinimum,
            valueAxisMaximum,
            valueAxisMajorUnit,
            valueAxisMinorUnit,
            valueAxisLogScale,
            valueAxisLogBase,
            valueAxisReverseOrder,
            valueAxisNumberFormat.Format,
            valueAxisNumberFormat.FormatCode,
            valueAxisNumberFormat.SourceLinked,
            showValueAxisMajorGridlines,
            valueAxisShowMinorGridlines,
            valueAxisMajorGridlineColor,
            valueAxisMinorGridlineColor,
            valueAxisGridlineThickness,
            valueAxisMajorTickStyle,
            valueAxisMinorTickStyle,
            valueAxisLineColor,
            valueAxisLineThickness,
            valueAxisShowLabels,
            valueAxisTickLabelPosition,
            chart.YAxisLabelTextColor,
            chart.YAxisLabelFontSize,
            chart.YAxisLabelAngle,
            chart.YAxisLabelTextThemeColor,
            valueTitleThemeColor ?? chart.AxisTitleTextThemeColor,
            valueTitleColor ?? chart.AxisTitleTextColor,
            valueTitleFontSize ?? chart.AxisTitleFontSize,
            valueAxisCrosses,
            valueAxisCrossesAt,
            valueAxisCrossBetween,
            valueAxisDisplayUnit,
            valueAxisCustomDisplayUnit,
            chartNs,
            drawingNs,
            useExcelNativeMajorGridlineStyle: ShouldUseExcelNativeValueAxisMajorGridlineStyle(chart, valueAxisOnX),
            verbatimTitle: TryParseVerbatimAxisTitleXml(valueTitleVerbatimXml),
            showDisplayUnitLabel: valueAxisShowDisplayUnitLabel,
            axisTitleRotation: valueTitleRotation);

        var secondaryIndexes = GetSecondaryAxisSeriesIndexes(chart, ChartTypeSupport.GetDataSeriesCount(chart));
        if (secondaryIndexes.Count > 0)
        {
            // R30-io-chart-series-cache-deep-2: prefer the secondary axis's OWN captured title/min/max/
            // number-format (set by XlsxChartAxisReader on a round-tripped chart); fall back to cloning
            // the primary (Y) axis's settings when nothing was captured, matching the prior behavior for
            // a chart built programmatically (never read from an XLSX file).
            var secondaryAxisMinimum = chart.SecondaryAxisMinimum ?? chart.YAxisMinimum;
            var secondaryAxisMaximum = chart.SecondaryAxisMaximum ?? chart.YAxisMaximum;
            var secondaryHasOwnNumberFormat = !string.IsNullOrWhiteSpace(chart.SecondaryAxisNumberFormatCode)
                || chart.SecondaryAxisNumberFormat != ChartDataLabelNumberFormat.General;
            var secondaryAxisNumberFormat = secondaryHasOwnNumberFormat ? chart.SecondaryAxisNumberFormat : valueAxisNumberFormat.Format;
            var secondaryAxisNumberFormatCode = chart.SecondaryAxisNumberFormatCode ?? valueAxisNumberFormat.FormatCode;
            var secondaryAxisNumberFormatSourceLinked = chart.SecondaryAxisNumberFormatSourceLinked ?? valueAxisNumberFormat.SourceLinked;
            // R36-io-chart-axis-scaling-2-2: same "prefer own captured value, else clone primary (Y)
            // axis" pattern as the number-format fields above, extended to orientation/log-scale/
            // tick-style/crossing so a round-tripped secondary axis keeps its own reversed/log/crossing
            // settings instead of picking up whatever the primary axis currently has.
            var secondaryAxisReverseOrder = chart.SecondaryAxisReverseOrder ?? chart.YAxisReverseOrder;
            var secondaryAxisLogScale = chart.SecondaryAxisLogScale ?? chart.YAxisLogScale;
            var secondaryAxisLogBase = chart.SecondaryAxisLogBase ?? chart.YAxisLogBase;
            var secondaryAxisMajorTickStyle = chart.SecondaryAxisMajorTickStyle ?? chart.YAxisMajorTickStyle;
            var secondaryAxisMinorTickStyle = chart.SecondaryAxisMinorTickStyle ?? chart.YAxisMinorTickStyle;
            var secondaryAxisCrosses = chart.SecondaryAxisCrosses ?? chart.YAxisCrosses;
            var secondaryAxisCrossesAt = chart.SecondaryAxisCrossesAt ?? chart.YAxisCrossesAt;
            var secondaryAxisCrossBetween = chart.SecondaryAxisCrossBetween ?? chart.YAxisCrossBetween;
            // R62-io-chart-axis-6-2: same "prefer own captured value, else clone primary (Y) axis"
            // pattern as the other secondary-axis fields above — without this, the secondary axis's
            // own majorUnit/minorUnit (e.g. 0.1 on a 0..1 percentage axis) was always overwritten with
            // whatever the primary axis's current majorUnit/minorUnit happened to be (e.g. 20).
            var secondaryAxisMajorUnit = chart.SecondaryAxisMajorUnit ?? chart.YAxisMajorUnit;
            var secondaryAxisMinorUnit = chart.SecondaryAxisMinorUnit ?? chart.YAxisMinorUnit;
            // R71-io-chart-axis-4-2: unlike every other Secondary* fallback above, the secondary axis's
            // display unit must NOT fall back to the primary (Y) axis's -- Excel's own secondary-axis
            // default is "no display unit" regardless of what the primary axis has, so cloning it here
            // silently invented a display unit the source file never had on the secondary axis.
            var secondaryAxisDisplayUnit = chart.SecondaryAxisDisplayUnit;
            var secondaryAxisCustomDisplayUnit = chart.SecondaryAxisCustomDisplayUnit;
            var secondaryAxisShowDisplayUnitLabel = chart.ShowSecondaryAxisDisplayUnitLabel;
            yield return ToValueAxisXml(
                chart.SecondaryAxisTitle,
                null,
                SecondaryValueAxisId,
                CategoryAxisId,
                "r",
                chart.HideYAxis,
                secondaryAxisMinimum,
                secondaryAxisMaximum,
                secondaryAxisMajorUnit,
                secondaryAxisMinorUnit,
                secondaryAxisLogScale,
                secondaryAxisLogBase,
                secondaryAxisReverseOrder,
                secondaryAxisNumberFormat,
                secondaryAxisNumberFormatCode,
                secondaryAxisNumberFormatSourceLinked,
                false,
                false,
                null,
                null,
                chart.YAxisGridlineThickness,
                secondaryAxisMajorTickStyle,
                secondaryAxisMinorTickStyle,
                chart.YAxisLineColor,
                chart.YAxisLineThickness,
                chart.ShowYAxisLabels,
                chart.YAxisTickLabelPosition,
                chart.YAxisLabelTextColor,
                chart.YAxisLabelFontSize,
                chart.YAxisLabelAngle,
                chart.YAxisLabelTextThemeColor,
                chart.YAxisTitleTextThemeColor ?? chart.AxisTitleTextThemeColor,
                chart.YAxisTitleTextColor ?? chart.AxisTitleTextColor,
                chart.YAxisTitleFontSize ?? chart.AxisTitleFontSize,
                secondaryAxisCrosses,
                secondaryAxisCrossesAt,
                secondaryAxisCrossBetween,
                secondaryAxisDisplayUnit,
                secondaryAxisCustomDisplayUnit,
                chartNs,
                drawingNs,
                showDisplayUnitLabel: secondaryAxisShowDisplayUnitLabel);
        }

        if (UsesSeriesAxis(chart.Type))
            yield return ToSeriesAxisXml(chartNs);
    }

    private static XElement ToCategoryAxisXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        var isDateAxis = chart.XAxisIsDateAxis;
        // R16-meta-1: mirror XlsxChartAxisReader.ApplyCategoryAxisProperties(categoryAxisOnY:
        // valueAxisOnX) — for bar-family charts the category axis is physically on Y, so its
        // reverse-order flag was captured into YAxisReverseOrder on read, not XAxisReverseOrder.
        var categoryAxisIsOnY = IsHorizontalBarChart(chart.Type);
        var categoryAxisReverseOrder = categoryAxisIsOnY ? chart.YAxisReverseOrder : chart.XAxisReverseOrder;
        // R47-io-chart-axis-scaling-3-1/3-3/3-4: mirror the same routing for the category axis's own
        // gridlines/tickLblPos/crosses(+crossesAt) — XlsxChartAxisReader.ApplyCategoryAxisProperties
        // now captures these into Y* for bar-family charts (categoryAxisOnY), so the writer must read
        // them back from Y* too, not unconditionally from X* (which belongs to the value axis for a
        // horizontal Bar chart).
        var categoryAxisShowMajorGridlines = categoryAxisIsOnY ? chart.ShowYAxisMajorGridlines : chart.ShowXAxisMajorGridlines;
        var categoryAxisShowMinorGridlines = categoryAxisIsOnY ? chart.ShowYAxisMinorGridlines : chart.ShowXAxisMinorGridlines;
        var categoryAxisMajorGridlineColor = categoryAxisIsOnY ? chart.YAxisMajorGridlineColor : chart.XAxisMajorGridlineColor;
        var categoryAxisMinorGridlineColor = categoryAxisIsOnY ? chart.YAxisMinorGridlineColor : chart.XAxisMinorGridlineColor;
        var categoryAxisGridlineThickness = categoryAxisIsOnY ? chart.YAxisGridlineThickness : chart.XAxisGridlineThickness;
        var categoryAxisShowLabels = categoryAxisIsOnY ? chart.ShowYAxisLabels : chart.ShowXAxisLabels;
        var categoryAxisTickLabelPosition = categoryAxisIsOnY ? chart.YAxisTickLabelPosition : chart.XAxisTickLabelPosition;
        var categoryAxisCrosses = categoryAxisIsOnY ? chart.YAxisCrosses : chart.XAxisCrosses;
        var categoryAxisCrossesAt = categoryAxisIsOnY ? chart.YAxisCrossesAt : chart.XAxisCrossesAt;
        // R62-io-chart-axis-6-3: same routing, extended to the category axis's own tick-mark styles
        // and spPr line — XlsxChartAxisReader.ApplyCategoryAxisProperties now captures these into Y*
        // for bar-family charts (categoryAxisOnY), so the writer must read them back from Y* too, not
        // unconditionally from X* (which belongs to the value axis for a horizontal Bar chart).
        var categoryAxisMajorTickStyle = categoryAxisIsOnY ? chart.YAxisMajorTickStyle : chart.XAxisMajorTickStyle;
        var categoryAxisMinorTickStyle = categoryAxisIsOnY ? chart.YAxisMinorTickStyle : chart.XAxisMinorTickStyle;
        var categoryAxisLineColor = categoryAxisIsOnY ? chart.YAxisLineColor : chart.XAxisLineColor;
        var categoryAxisLineThickness = categoryAxisIsOnY ? chart.YAxisLineThickness : chart.XAxisLineThickness;
        // r190: the title group, routed the same way as everything above it.
        var categoryTitle = categoryAxisIsOnY ? chart.YAxisTitle : chart.XAxisTitle;
        var categoryTitleLayout = categoryAxisIsOnY ? chart.YAxisTitleLayout : chart.XAxisTitleLayout;
        var categoryTitleVerbatimXml = categoryAxisIsOnY ? chart.YAxisTitleVerbatimXml : chart.XAxisTitleVerbatimXml;
        var categoryTitleThemeColor = categoryAxisIsOnY ? chart.YAxisTitleTextThemeColor : chart.XAxisTitleTextThemeColor;
        var categoryTitleColor = categoryAxisIsOnY ? chart.YAxisTitleTextColor : chart.XAxisTitleTextColor;
        var categoryTitleFontSize = categoryAxisIsOnY ? chart.YAxisTitleFontSize : chart.XAxisTitleFontSize;
        var categoryTitleRotation = categoryAxisIsOnY ? chart.YAxisTitleRotation : chart.XAxisTitleRotation;
        var categoryAxisPosition = ToXlsxCategoryAxisPosition(chart);
        return new XElement(chartNs + (isDateAxis ? "dateAx" : "catAx"),
            new XElement(chartNs + "axId", new XAttribute("val", CategoryAxisId)),
            new XElement(chartNs + "scaling",
                new XElement(chartNs + "orientation", new XAttribute("val", ToXlsxAxisOrientation(categoryAxisReverseOrder))),
                // R71-io-chart-axis-4-1: a date axis's own explicit min/max (a pinned date range) must
                // be re-emitted, matching XlsxChartAxisReader.ApplyCategoryAxisProperties's dateAx-only
                // read above. A plain text category axis never had bounds to begin with, so gating this
                // to isDateAxis leaves it unaffected.
                isDateAxis ? ToAxisBoundXml("max", chart.XAxisMaximum, chartNs) : null,
                isDateAxis ? ToAxisBoundXml("min", chart.XAxisMinimum, chartNs) : null),
            new XElement(chartNs + "delete", new XAttribute("val", chart.HideXAxis ? "1" : "0")),
            new XElement(chartNs + "axPos", new XAttribute("val", categoryAxisPosition)),
            ToAxisGridlinesXml("majorGridlines", categoryAxisShowMajorGridlines, categoryAxisMajorGridlineColor, categoryAxisGridlineThickness, chartNs, drawingNs),
            ToAxisGridlinesXml("minorGridlines", categoryAxisShowMinorGridlines, categoryAxisMinorGridlineColor, categoryAxisGridlineThickness, chartNs, drawingNs),
            // r190: mirror XlsxChartAxisReader's title routing, which now follows the same
            // categoryAxisIsOnY convention as every other property in this method. Reading the
            // title back from X* unconditionally would have written the VALUE axis's title onto the
            // category axis for bar-family charts once the reader started storing it in Y*.
            TryParseVerbatimAxisTitleXml(categoryTitleVerbatimXml)
                ?? ToAxisTitleXml(
                    categoryTitle,
                    categoryTitleLayout,
                    categoryTitleThemeColor ?? chart.AxisTitleTextThemeColor,
                    categoryTitleColor ?? chart.AxisTitleTextColor,
                    categoryTitleFontSize ?? chart.AxisTitleFontSize,
                    chartNs,
                    drawingNs,
                    vertical: IsVerticalAxisPosition(categoryAxisPosition),
                    rotationOverride: categoryTitleRotation),
            // R43-io-chart-axis-title-numfmt-3-1: category/date axes carry their OWN <c:numFmt>
            // (e.g. a date axis's custom "mmm-yy"), independent of the value axis's numFmt emitted
            // below in ToValueAxisXml. Without this, a round-tripped custom category/date axis
            // number format reverted to Excel's default source-cell format on reopen.
            new XElement(chartNs + "numFmt",
                new XAttribute("formatCode", ToXlsxNumberFormatCode(chart.XAxisNumberFormat, chart.XAxisNumberFormatCode)),
                new XAttribute("sourceLinked", ToXlsxNumberFormatSourceLinked(chart.XAxisNumberFormat, chart.XAxisNumberFormatSourceLinked))),
            new XElement(chartNs + "majorTickMark", new XAttribute("val", ToXlsxTickMark(ToEffectiveAxisMajorTickStyle(chart, categoryAxisMajorTickStyle)))),
            new XElement(chartNs + "minorTickMark", new XAttribute("val", ToXlsxTickMark(categoryAxisMinorTickStyle))),
            new XElement(chartNs + "tickLblPos", new XAttribute("val", ToXlsxTickLabelPosition(categoryAxisShowLabels, categoryAxisTickLabelPosition))),
            ToAxisLineShapeProperties(categoryAxisLineColor, categoryAxisLineThickness, chartNs, drawingNs),
            ToAxisLabelTextProperties(chart.XAxisLabelTextThemeColor, chart.XAxisLabelTextColor, chart.XAxisLabelFontSize, chart.XAxisLabelAngle, chartNs, drawingNs),
            new XElement(chartNs + "crossAx", new XAttribute("val", ValueAxisId)),
            ToAxisCrossesXml(categoryAxisCrosses, categoryAxisCrossesAt, chartNs),
            isDateAxis ? null : ToAxisLabelAlignmentXml(chart.XAxisLabelAlignment, chartNs),
            ToUnsignedAxisValueXml("lblOffset", chart.XAxisLabelOffset, chartNs),
            isDateAxis ? ToDateAxisUnitXml("baseTimeUnit", chart.XAxisBaseTimeUnit, chartNs) : null,
            isDateAxis ? ToAxisUnitXml("majorUnit", chart.XAxisMajorUnit, chartNs) : null,
            isDateAxis ? ToDateAxisUnitXml("majorTimeUnit", chart.XAxisMajorTimeUnit, chartNs) : null,
            isDateAxis ? ToAxisUnitXml("minorUnit", chart.XAxisMinorUnit, chartNs) : null,
            isDateAxis ? ToDateAxisUnitXml("minorTimeUnit", chart.XAxisMinorTimeUnit, chartNs) : null,
            isDateAxis ? null : ToUnsignedAxisValueXml("tickLblSkip", chart.XAxisLabelSkip, chartNs),
            isDateAxis ? null : ToUnsignedAxisValueXml("tickMarkSkip", chart.XAxisTickMarkSkip, chartNs),
            isDateAxis ? null : ToBooleanAxisValueXml("noMultiLvlLbl", chart.XAxisNoMultiLevelLabels, chartNs));
    }

    private static XElement ToValueAxisXml(
        string? title,
        ChartManualLayoutModel? titleLayout,
        int axisId,
        int crossAxisId,
        string axisPosition,
        bool hidden,
        double? minimum,
        double? maximum,
        double? majorUnit,
        double? minorUnit,
        bool logScale,
        double? logBase,
        bool reverseOrder,
        ChartDataLabelNumberFormat numberFormat,
        string? numberFormatCode,
        bool? numberFormatSourceLinked,
        bool showMajorGridlines,
        bool showMinorGridlines,
        CellColor? majorGridlineColor,
        CellColor? minorGridlineColor,
        double gridlineThickness,
        ChartAxisTickStyle majorTickStyle,
        ChartAxisTickStyle minorTickStyle,
        CellColor? lineColor,
        double lineThickness,
        bool showLabels,
        ChartAxisTickLabelPosition tickLabelPosition,
        CellColor? labelTextColor,
        double labelFontSize,
        double labelAngle,
        WorkbookThemeColorReference? labelTextThemeColor,
        WorkbookThemeColorReference? axisTitleTextThemeColor,
        CellColor? axisTitleTextColor,
        double axisTitleFontSize,
        ChartAxisCrosses crosses,
        double? crossesAt,
        ChartAxisCrossBetween? crossBetween,
        ChartAxisDisplayUnit? displayUnit,
        double? customDisplayUnit,
        XNamespace chartNs,
        XNamespace drawingNs,
        bool useExcelNativeMajorGridlineStyle = false,
        XElement? verbatimTitle = null,
        bool showDisplayUnitLabel = false,
        double? axisTitleRotation = null) =>
        new(chartNs + "valAx",
            new XElement(chartNs + "axId", new XAttribute("val", axisId)),
            new XElement(chartNs + "scaling",
                logScale ? new XElement(chartNs + "logBase", new XAttribute("val", ToXlsxLogBase(logBase))) : null,
                new XElement(chartNs + "orientation", new XAttribute("val", ToXlsxAxisOrientation(reverseOrder))),
                ToAxisBoundXml("max", maximum, chartNs),
                ToAxisBoundXml("min", minimum, chartNs)),
            new XElement(chartNs + "delete", new XAttribute("val", hidden ? "1" : "0")),
            new XElement(chartNs + "axPos", new XAttribute("val", axisPosition)),
            ToAxisGridlinesXml("majorGridlines", showMajorGridlines, majorGridlineColor, gridlineThickness, chartNs, drawingNs, useExcelNativeMajorGridlineStyle),
            ToAxisGridlinesXml("minorGridlines", showMinorGridlines, minorGridlineColor, gridlineThickness, chartNs, drawingNs),
            verbatimTitle ?? ToAxisTitleXml(title, titleLayout, axisTitleTextThemeColor, axisTitleTextColor, axisTitleFontSize, chartNs, drawingNs, vertical: IsVerticalAxisPosition(axisPosition), rotationOverride: axisTitleRotation),
            new XElement(chartNs + "numFmt",
                new XAttribute("formatCode", ToXlsxNumberFormatCode(numberFormat, numberFormatCode)),
                new XAttribute("sourceLinked", ToXlsxNumberFormatSourceLinked(numberFormat, numberFormatSourceLinked))),
            new XElement(chartNs + "majorTickMark", new XAttribute("val", ToXlsxTickMark(majorTickStyle))),
            new XElement(chartNs + "minorTickMark", new XAttribute("val", ToXlsxTickMark(minorTickStyle))),
            new XElement(chartNs + "tickLblPos", new XAttribute("val", ToXlsxTickLabelPosition(showLabels, tickLabelPosition))),
            ToAxisLineShapeProperties(lineColor, lineThickness, chartNs, drawingNs),
            ToAxisLabelTextProperties(labelTextThemeColor, labelTextColor, labelFontSize, labelAngle, chartNs, drawingNs),
            new XElement(chartNs + "crossAx", new XAttribute("val", crossAxisId)),
            ToAxisCrossesXml(crosses, crossesAt, chartNs),
            ToAxisCrossBetweenXml(crossBetween, chartNs),
            ToAxisUnitXml("majorUnit", majorUnit, chartNs),
            ToAxisUnitXml("minorUnit", minorUnit, chartNs),
            ToAxisDisplayUnitXml(displayUnit, customDisplayUnit, showDisplayUnitLabel, chartNs));

    private static XElement ToSeriesAxisXml(XNamespace chartNs) =>
        new(chartNs + "serAx",
            new XElement(chartNs + "axId", new XAttribute("val", SeriesAxisId)),
            new XElement(chartNs + "scaling",
                new XElement(chartNs + "orientation", new XAttribute("val", "minMax"))),
            new XElement(chartNs + "delete", new XAttribute("val", "0")),
            new XElement(chartNs + "axPos", new XAttribute("val", "b")),
            new XElement(chartNs + "majorTickMark", new XAttribute("val", "none")),
            new XElement(chartNs + "minorTickMark", new XAttribute("val", "none")),
            new XElement(chartNs + "tickLblPos", new XAttribute("val", "nextTo")),
            new XElement(chartNs + "crossAx", new XAttribute("val", ValueAxisId)),
            new XElement(chartNs + "crosses", new XAttribute("val", "autoZero")));

    private static XElement? ToAxisGridlinesXml(
        string elementName,
        bool visible,
        CellColor? color,
        double thickness,
        XNamespace chartNs,
        XNamespace drawingNs,
        bool useExcelNativeMajorGridlineStyle = false)
    {
        if (!visible)
            return null;

        if (useExcelNativeMajorGridlineStyle)
        {
            return new XElement(chartNs + elementName,
                ToExcelNativeMajorGridlineShapeProperties(chartNs, drawingNs));
        }

        return new XElement(chartNs + elementName,
            ToShapeProperties(
                chartNs,
                drawingNs,
                fillThemeColor: null,
                fillColor: null,
                borderThemeColor: null,
                borderColor: color,
                borderThickness: Math.Clamp(thickness, 0.25, 10)));
    }

    private static XElement ToExcelNativeMajorGridlineShapeProperties(XNamespace chartNs, XNamespace drawingNs) =>
        new(chartNs + "spPr",
            new XElement(drawingNs + "ln",
                new XAttribute("w", 9525),
                new XAttribute("cap", "flat"),
                new XAttribute("cmpd", "sng"),
                new XAttribute("algn", "ctr"),
                new XElement(drawingNs + "solidFill",
                    new XElement(drawingNs + "schemeClr",
                        new XAttribute("val", "tx1"),
                        new XElement(drawingNs + "lumMod", new XAttribute("val", "15000")),
                        new XElement(drawingNs + "lumOff", new XAttribute("val", "85000")))),
                new XElement(drawingNs + "round")),
            new XElement(drawingNs + "effectLst"));

    private static XElement? ToAxisLineShapeProperties(
        CellColor? lineColor,
        double lineThickness,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        ToShapeProperties(
            chartNs,
            drawingNs,
            fillThemeColor: null,
            fillColor: null,
            borderThemeColor: null,
            borderColor: lineColor,
            borderThickness: Math.Clamp(lineThickness, 0.5, 10));

    private static string ToXlsxTickMark(ChartAxisTickStyle tickStyle) =>
        tickStyle switch
        {
            ChartAxisTickStyle.None => "none",
            ChartAxisTickStyle.Inside => "in",
            ChartAxisTickStyle.Cross => "cross",
            _ => "out"
        };

    private static string ToXlsxAxisOrientation(bool reverseOrder) =>
        reverseOrder ? "maxMin" : "minMax";

    private static string ToXlsxAxisPosition(ChartAxisPosition position, string fallback) =>
        position switch
        {
            ChartAxisPosition.Bottom => "b",
            ChartAxisPosition.Top => "t",
            ChartAxisPosition.Left => "l",
            ChartAxisPosition.Right => "r",
            _ => fallback
        };

    private static string ToXlsxCategoryAxisPosition(ChartModel chart) =>
        IsHorizontalBarChart(chart.Type) && chart.XAxisPosition == ChartAxisPosition.Bottom
            ? "l"
            : ToXlsxAxisPosition(chart.XAxisPosition, IsHorizontalBarChart(chart.Type) ? "l" : "b");

    private static string ToXlsxValueAxisPosition(ChartModel chart) =>
        IsHorizontalBarChart(chart.Type) && chart.YAxisPosition == ChartAxisPosition.Left
            ? "b"
            : ToXlsxAxisPosition(chart.YAxisPosition, IsHorizontalBarChart(chart.Type) ? "b" : "l");

    // R43-io-chart-axis-title-numfmt-3-2: true for a left/right (vertical) axis, whose title Excel
    // renders rotated -90 degrees by default. Shared by both ToCategoryAxisXml (category axis, which
    // is vertical for horizontal Bar charts) and ToValueAxisXml (value/secondary axis, which is
    // vertical everywhere except horizontal Bar charts and Scatter/Bubble's X axis).
    private static bool IsVerticalAxisPosition(string axisPosition) =>
        axisPosition is "l" or "r";

    private static bool IsHorizontalBarChart(ChartType chartType) =>
        chartType is ChartType.Bar
            or ChartType.StackedBar
            or ChartType.PercentStackedBar
            or ChartType.ThreeDBar;

    private static (ChartDataLabelNumberFormat Format, string? FormatCode, bool? SourceLinked)
        ToEffectiveValueAxisNumberFormat(ChartModel chart)
    {
        if (chart.Type is (ChartType.PercentStackedColumn or ChartType.PercentStackedBar) &&
            chart.YAxisNumberFormat == ChartDataLabelNumberFormat.General &&
            string.IsNullOrWhiteSpace(chart.YAxisNumberFormatCode) &&
            chart.YAxisNumberFormatSourceLinked is null)
        {
            return (ChartDataLabelNumberFormat.Percent, null, true);
        }

        return (chart.YAxisNumberFormat, chart.YAxisNumberFormatCode, chart.YAxisNumberFormatSourceLinked);
    }

    // R47-io-chart-axis-scaling-3-3: routed by valueAxisOnX (true for bar-family charts, whose value
    // axis's own gridline visibility lives in X*, not Y*) so a horizontal Bar chart's value-axis
    // gridlines are read back from the field the reader actually captured them into.
    private static bool ToEffectiveShowValueAxisMajorGridlines(ChartModel chart, bool valueAxisOnX)
    {
        var showMajorGridlines = valueAxisOnX ? chart.ShowXAxisMajorGridlines : chart.ShowYAxisMajorGridlines;
        return showMajorGridlines || ShouldUseExcelNativeValueAxisMajorGridlineStyle(chart, valueAxisOnX);
    }

    private static bool ShouldUseExcelNativeValueAxisMajorGridlineStyle(ChartModel chart, bool valueAxisOnX)
    {
        var showMajorGridlines = valueAxisOnX ? chart.ShowXAxisMajorGridlines : chart.ShowYAxisMajorGridlines;
        var majorGridlineColor = valueAxisOnX ? chart.XAxisMajorGridlineColor : chart.YAxisMajorGridlineColor;
        var gridlineThickness = valueAxisOnX ? chart.XAxisGridlineThickness : chart.YAxisGridlineThickness;
        return IsClassicStackedBarOrColumnChart(chart.Type) &&
            !showMajorGridlines &&
            majorGridlineColor is null &&
            gridlineThickness == 1;
    }

    private static ChartAxisTickStyle ToEffectiveAxisMajorTickStyle(ChartModel chart, ChartAxisTickStyle tickStyle) =>
        IsClassicStackedBarOrColumnChart(chart.Type)
            && tickStyle == ChartAxisTickStyle.Outside
            && !HasAnyExplicitAxisMajorTickStyle(chart)
            ? ChartAxisTickStyle.None
            : tickStyle;

    // R62-io-chart-axis-6-3 follow-up: the Excel-native "hide default tick marks" look for a classic
    // Stacked/PercentStacked Column/Bar chart (see XlsxClassicChartDefaultTests) is only a heuristic
    // FALLBACK for a totally vanilla, never-customized chart -- both axes there are left at the
    // ChartAxisTickStyle.Outside default. Once EITHER axis's own MajorTickStyle has been explicitly set
    // away from Outside (e.g. a horizontal Bar/StackedBar chart whose value axis was explicitly hidden
    // via XAxisMajorTickStyle=None while its category axis was left at the untouched Outside default),
    // the heuristic must back off entirely for BOTH axes. Otherwise the untouched-but-genuine Outside
    // axis silently gets rewritten to None on save: for a bar-family chart, the category axis's own tick
    // is now correctly routed through Y* (see ApplyCategoryAxisProperties's categoryAxisOnY routing),
    // and unconditionally applying this heuristic to that real captured value regressed the pre-existing
    // corpus round-trip test (generated-charts-classic-extended-004, Charts[1]/[2] StackedBar/
    // PercentStackedBar): YAxis.MajorTickStyle flipped from Outside to None on every round-trip.
    private static bool HasAnyExplicitAxisMajorTickStyle(ChartModel chart) =>
        chart.XAxisMajorTickStyle != ChartAxisTickStyle.Outside || chart.YAxisMajorTickStyle != ChartAxisTickStyle.Outside;

    // R71-io-chart-axis-4-4: routed by valueAxisOnX exactly like every other value-axis property in
    // ToChartAxesXml (see the comment above this method's only call site) -- for a horizontal
    // Bar-family chart the value axis's own <c:crossBetween> lives in XAxisCrossBetween, not
    // YAxisCrossBetween.
    private static ChartAxisCrossBetween? ToEffectiveValueAxisCrossBetween(ChartModel chart, bool valueAxisOnX)
    {
        var crossBetween = valueAxisOnX ? chart.XAxisCrossBetween : chart.YAxisCrossBetween;
        return crossBetween ?? (IsClassicStackedBarOrColumnChart(chart.Type) ? ChartAxisCrossBetween.Between : null);
    }

    private static string ToXlsxTickLabelPosition(bool showLabels, ChartAxisTickLabelPosition position)
    {
        if (!showLabels)
            return "none";

        return position switch
        {
            ChartAxisTickLabelPosition.Low => "low",
            ChartAxisTickLabelPosition.High => "high",
            _ => "nextTo"
        };
    }

    private static XElement ToAxisCrossesXml(ChartAxisCrosses crosses, double? crossesAt, XNamespace chartNs)
    {
        if (crosses == ChartAxisCrosses.Custom && crossesAt is { } numeric && double.IsFinite(numeric))
            return new XElement(chartNs + "crossesAt", new XAttribute("val", numeric.ToString(CultureInfo.InvariantCulture)));

        return new XElement(chartNs + "crosses", new XAttribute("val", ToXlsxAxisCrosses(crosses)));
    }

    private static XElement? ToAxisCrossBetweenXml(ChartAxisCrossBetween? crossBetween, XNamespace chartNs) =>
        crossBetween is null
            ? null
            : new XElement(chartNs + "crossBetween", new XAttribute("val", ToXlsxAxisCrossBetween(crossBetween.Value)));

    private static string ToXlsxAxisCrosses(ChartAxisCrosses crosses) =>
        crosses switch
        {
            ChartAxisCrosses.Minimum => "min",
            ChartAxisCrosses.Maximum => "max",
            _ => "autoZero"
        };

    private static string ToXlsxAxisCrossBetween(ChartAxisCrossBetween crossBetween) =>
        crossBetween == ChartAxisCrossBetween.MidCategory ? "midCat" : "between";

    private static XElement? ToAxisLabelTextProperties(
        WorkbookThemeColorReference? textThemeColor,
        CellColor? textColor,
        double fontSize,
        double angle,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        if (textThemeColor is null && textColor is null && fontSize == 11 && angle == 0)
            return null;

        return new XElement(chartNs + "txPr",
            ToTextBodyProperties(angle, drawingNs),
            new XElement(drawingNs + "p",
                new XElement(drawingNs + "pPr",
                    new XElement(drawingNs + "defRPr",
                        new XAttribute("sz", Math.Clamp((int)Math.Round(fontSize * 100), 600, 7200)),
                        ToSolidFill(textThemeColor, textColor, drawingNs)))));
    }

    private static XElement? ToAxisBoundXml(string elementName, double? value, XNamespace chartNs) =>
        value is { } numeric && double.IsFinite(numeric)
            ? new XElement(chartNs + elementName, new XAttribute("val", numeric.ToString(CultureInfo.InvariantCulture)))
            : null;

    private static XElement? ToAxisUnitXml(string elementName, double? value, XNamespace chartNs) =>
        value is { } numeric && double.IsFinite(numeric)
            ? new XElement(chartNs + elementName, new XAttribute("val", Math.Max(numeric, double.Epsilon).ToString(CultureInfo.InvariantCulture)))
            : null;

    private static string ToXlsxLogBase(double? value)
    {
        var numeric = value is { } candidate && double.IsFinite(candidate)
            ? Math.Clamp(candidate, 2, 1000)
            : 10;
        return numeric.ToString(CultureInfo.InvariantCulture);
    }

    private static XElement? ToUnsignedAxisValueXml(string elementName, int value, XNamespace chartNs) =>
        value > 0
            ? new XElement(chartNs + elementName, new XAttribute("val", value.ToString(CultureInfo.InvariantCulture)))
            : null;

    private static XElement? ToBooleanAxisValueXml(string elementName, bool value, XNamespace chartNs) =>
        value ? new XElement(chartNs + elementName, new XAttribute("val", "1")) : null;

    private static XElement? ToAxisLabelAlignmentXml(ChartAxisLabelAlignment alignment, XNamespace chartNs) =>
        alignment == ChartAxisLabelAlignment.Center
            ? null
            : new XElement(chartNs + "lblAlgn", new XAttribute("val", ToXlsxAxisLabelAlignment(alignment)));

    private static string ToXlsxAxisLabelAlignment(ChartAxisLabelAlignment alignment) =>
        alignment == ChartAxisLabelAlignment.Right ? "r" : "l";

    private static XElement? ToDateAxisUnitXml(string elementName, ChartDateAxisUnit? unit, XNamespace chartNs) =>
        unit is null
            ? null
            : new XElement(chartNs + elementName, new XAttribute("val", ToXlsxDateAxisUnit(unit.Value)));

    private static string ToXlsxDateAxisUnit(ChartDateAxisUnit unit) =>
        unit switch
        {
            ChartDateAxisUnit.Days => "days",
            ChartDateAxisUnit.Years => "years",
            _ => "months"
        };

    private static XElement? ToAxisDisplayUnitXml(ChartAxisDisplayUnit? unit, double? customUnit, XNamespace chartNs) =>
        ToAxisDisplayUnitXml(unit, customUnit, showLabel: false, chartNs);

    // R36-io-chart-axis-scaling-2-3: emit <c:dispUnitsLbl/> when the source file had Excel's "Show
    // display units label on chart" checkbox set, so the visible caption (e.g. "Thousands") round-trips
    // alongside the numeric scaling instead of being silently dropped.
    private static XElement? ToAxisDisplayUnitXml(ChartAxisDisplayUnit? unit, double? customUnit, bool showLabel, XNamespace chartNs)
    {
        if (customUnit is { } customNumeric && double.IsFinite(customNumeric) && customNumeric > 0)
            return new XElement(chartNs + "dispUnits",
                new XElement(chartNs + "custUnit", new XAttribute("val", customNumeric.ToString(CultureInfo.InvariantCulture))),
                showLabel ? new XElement(chartNs + "dispUnitsLbl") : null);

        return unit is null
            ? null
            : new XElement(chartNs + "dispUnits",
                new XElement(chartNs + "builtInUnit", new XAttribute("val", ToXlsxAxisDisplayUnit(unit.Value))),
                showLabel ? new XElement(chartNs + "dispUnitsLbl") : null);
    }

    private static string ToXlsxAxisDisplayUnit(ChartAxisDisplayUnit unit) =>
        unit switch
        {
            ChartAxisDisplayUnit.Hundreds => "hundreds",
            ChartAxisDisplayUnit.Thousands => "thousands",
            ChartAxisDisplayUnit.TenThousands => "tenThousands",
            ChartAxisDisplayUnit.HundredThousands => "hundredThousands",
            ChartAxisDisplayUnit.Millions => "millions",
            ChartAxisDisplayUnit.TenMillions => "tenMillions",
            ChartAxisDisplayUnit.HundredMillions => "hundredMillions",
            ChartAxisDisplayUnit.Billions => "billions",
            ChartAxisDisplayUnit.Trillions => "trillions",
            _ => "thousands"
        };

    private static string ToXlsxNumberFormatCode(ChartDataLabelNumberFormat format) =>
        format switch
        {
            ChartDataLabelNumberFormat.Number => "0.00",
            ChartDataLabelNumberFormat.Currency => "$#,##0.00",
            ChartDataLabelNumberFormat.Percent => "0%",
            _ => "General"
        };

    private static string ToXlsxNumberFormatCode(ChartDataLabelNumberFormat format, string? formatCode) =>
        string.IsNullOrWhiteSpace(formatCode)
            ? ToXlsxNumberFormatCode(format)
            : formatCode;

    private static string ToXlsxNumberFormatSourceLinked(ChartDataLabelNumberFormat format, bool? sourceLinked) =>
        (sourceLinked ?? format == ChartDataLabelNumberFormat.General) ? "1" : "0";
}
