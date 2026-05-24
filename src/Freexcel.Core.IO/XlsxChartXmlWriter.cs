using System.Globalization;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static partial class XlsxChartXmlWriter
{
    private const int CategoryAxisId = 48650112;
    private const int ValueAxisId = 48672768;
    private const int SecondaryValueAxisId = 48672769;
    private const int SeriesAxisId = 48672770;

    public static XDocument ToChartXml(ChartModel chart, Sheet sheet)
    {
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        var plotCharts = ToPlotChartXml(chart, sheet, chartNs, drawingNs).ToList();

        return new XDocument(
                new XElement(chartNs + "chartSpace",
                    new XAttribute(XNamespace.Xmlns + "c", chartNs),
                    new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                    chart.ExternalData?.RelationshipId is null ? null : new XAttribute(XNamespace.Xmlns + "r", relNs),
                    chart.Uses1904DateSystem ? new XElement(chartNs + "date1904", new XAttribute("val", "1")) : null,
                    string.IsNullOrWhiteSpace(chart.Language) ? null : new XElement(chartNs + "lang", new XAttribute("val", chart.Language)),
                    chart.ChartStyleId is { } styleId ? new XElement(chartNs + "style", new XAttribute("val", styleId.ToString(CultureInfo.InvariantCulture))) : null,
                    ToChartColorMapOverrideXml(chart, chartNs, drawingNs),
                    chart.RoundedCorners ? new XElement(chartNs + "roundedCorners", new XAttribute("val", "1")) : null,
                    ToChartProtectionXml(chart, chartNs),
                    ToChartExternalDataXml(chart, chartNs, relNs),
                    ToChartPrintSettingsXml(chart, chartNs),
                    ToChartAreaShapeProperties(chart, chartNs, drawingNs),
                ToPivotSourceXml(chart, sheet, chartNs),
                new XElement(chartNs + "chart",
                    string.IsNullOrWhiteSpace(chart.Title)
                        ? null
                        : ToChartTitleXml(chart, chartNs, drawingNs),
                    chart.AutoTitleDeleted ? new XElement(chartNs + "autoTitleDeleted", new XAttribute("val", "1")) : null,
                    ToPivotFormatsXml(chart, chartNs),
                    ToChart3DViewXml(chart, chartNs),
                    new XElement(chartNs + "plotArea",
                        ToManualLayoutXml(chart.PlotAreaLayout, chartNs),
                        plotCharts,
                        ShouldWriteChartAxes(chart.Type)
                            ? ToChartAxesXml(chart, chartNs, drawingNs)
                            : null,
                        ToChartDataTableXml(chart, chartNs),
                        ToPlotAreaShapeProperties(chart, chartNs, drawingNs)),
                    ToLegendXml(chart, chartNs, drawingNs),
                    chart.ShowDataInHiddenRowsAndColumns ? new XElement(chartNs + "plotVisOnly", new XAttribute("val", "0")) : null,
                    ToBlankDisplayXml(chart, chartNs),
                    chart.ShowDataLabelsOverMaximum ? new XElement(chartNs + "showDLblsOverMax", new XAttribute("val", "1")) : null)));
    }

    private static bool ShouldWriteChartAxes(ChartType chartType) =>
        chartType is not ChartType.Pie and not ChartType.ThreeDPie and not ChartType.Doughnut;

    private static IEnumerable<XElement> ToPlotChartXml(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var dataLabelWritten = false;
        foreach (var (plotChart, usesSecondaryAxis) in CreatePlotCharts(chart, sheet, chartNs, drawingNs))
        {
            AddPlotChartCommonElements(plotChart, chart, chartNs, drawingNs, usesSecondaryAxis, includeDataLabels: !dataLabelWritten);
            dataLabelWritten = true;
            yield return plotChart;
        }
    }

    private static IEnumerable<(XElement PlotChart, bool UsesSecondaryAxis)> CreatePlotCharts(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var seriesCount = ChartTypeSupport.GetDataSeriesCount(chart);
        var secondaryIndexes = GetSecondaryAxisSeriesIndexes(chart, seriesCount);
        var comboLineIndexes = GetComboLineSeriesIndexes(chart, seriesCount);
        if (chart.Type == ChartType.Stock && IsVolumeStockSubtype(chart.StockSubtype))
        {
            yield return (CreateStockVolumeBarChart(chart, sheet, chartNs, drawingNs), false);
            yield return (CreateStockPlotChart(chart, sheet, chartNs, drawingNs), false);
            yield break;
        }

        if (secondaryIndexes.Count > 0 && chart.Type == ChartType.Scatter)
        {
            var primaryScatter = Enumerable.Range(0, seriesCount)
                .Where(index => !secondaryIndexes.Contains(index))
                .ToHashSet();
            if (primaryScatter.Count > 0)
                yield return (CreateScatterPlotChart(chart, sheet, chartNs, drawingNs, primaryScatter.Contains), false);
            yield return (CreateScatterPlotChart(chart, sheet, chartNs, drawingNs, secondaryIndexes.Contains), true);
            yield break;
        }

        if (secondaryIndexes.Count > 0 && chart.Type is ChartType.Line or ChartType.ThreeDLine)
        {
            var primaryLine = Enumerable.Range(0, seriesCount)
                .Where(index => !secondaryIndexes.Contains(index))
                .ToHashSet();
            if (primaryLine.Count > 0)
                yield return (CreateLinePlotChart(chart, sheet, chartNs, drawingNs, primaryLine.Contains), false);
            yield return (CreateLinePlotChart(chart, sheet, chartNs, drawingNs, secondaryIndexes.Contains), true);
            yield break;
        }

        if ((secondaryIndexes.Count > 0 || comboLineIndexes.Count > 0) &&
            chart.Type is ChartType.Column or ChartType.StackedColumn or ChartType.PercentStackedColumn or ChartType.Area or ChartType.ThreeDArea)
        {
            var primaryBase = Enumerable.Range(0, seriesCount)
                .Where(index => !secondaryIndexes.Contains(index) && !comboLineIndexes.Contains(index))
                .ToHashSet();
            var secondaryBase = secondaryIndexes
                .Where(index => !comboLineIndexes.Contains(index))
                .ToHashSet();
            var primaryLine = comboLineIndexes
                .Where(index => !secondaryIndexes.Contains(index))
                .ToHashSet();
            var secondaryLine = comboLineIndexes
                .Where(secondaryIndexes.Contains)
                .ToHashSet();

            if (primaryBase.Count > 0)
                yield return (CreateNativePlotChart(chart, sheet, chartNs, drawingNs, primaryBase.Contains), false);
            if (secondaryBase.Count > 0)
                yield return (CreateNativePlotChart(chart, sheet, chartNs, drawingNs, secondaryBase.Contains), true);
            if (primaryLine.Count > 0)
                yield return (CreateLinePlotChart(chart, sheet, chartNs, drawingNs, primaryLine.Contains), false);
            if (secondaryLine.Count > 0)
                yield return (CreateLinePlotChart(chart, sheet, chartNs, drawingNs, secondaryLine.Contains), true);

            yield break;
        }

        yield return (CreateNativePlotChart(chart, sheet, chartNs, drawingNs, _ => true), false);
    }

    private static XElement CreateNativePlotChart(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool> includeSeries) =>
        chart.Type switch
        {
            ChartType.Line => CreateLinePlotChart(chart, sheet, chartNs, drawingNs, includeSeries),
            ChartType.ThreeDLine => Create3DLinePlotChart(chart, sheet, chartNs, drawingNs, includeSeries),
            ChartType.Scatter => CreateScatterPlotChart(chart, sheet, chartNs, drawingNs, includeSeries),
            ChartType.Radar => new XElement(chartNs + "radarChart",
                new XElement(chartNs + "radarStyle", new XAttribute("val", "marker")),
                BuildChartSeries(chart, sheet, chartNs, drawingNs, includeSeries, forceLineShapeProperties: true)),
            ChartType.Stock => CreateStockPlotChart(chart, sheet, chartNs, drawingNs, includeSeries),
            ChartType.Surface => new XElement(chartNs + "surfaceChart",
                BuildChartSeries(chart, sheet, chartNs, drawingNs, includeSeries)),
            ChartType.ThreeDSurface => new XElement(chartNs + "surface3DChart",
                BuildChartSeries(chart, sheet, chartNs, drawingNs, includeSeries)),
            ChartType.Area => new XElement(chartNs + "areaChart",
                new XElement(chartNs + "grouping", new XAttribute("val", "standard")),
                BuildChartSeries(chart, sheet, chartNs, drawingNs, includeSeries)),
            ChartType.ThreeDArea => new XElement(chartNs + "area3DChart",
                new XElement(chartNs + "grouping", new XAttribute("val", "standard")),
                BuildChartSeries(chart, sheet, chartNs, drawingNs, includeSeries)),
            ChartType.ThreeDColumn or ChartType.ThreeDBar => WithBarChartSpacing(new XElement(chartNs + "bar3DChart",
                new XElement(chartNs + "barDir", new XAttribute("val", chart.Type == ChartType.ThreeDBar ? "bar" : "col")),
                new XElement(chartNs + "grouping", new XAttribute("val", "clustered")),
                ToChartBooleanValueXml(chartNs, "varyColors", chart.VaryColorsByPoint),
                BuildChartSeries(chart, sheet, chartNs, drawingNs, includeSeries)), chart, chartNs),
            ChartType.Bubble => new XElement(chartNs + "bubbleChart",
                BuildBubbleChartSeries(chart, sheet, chartNs, drawingNs),
                ToBubbleChartOptionXml(chart, chartNs)),
            ChartType.Pie => new XElement(chartNs + "pieChart",
                ToFirstSliceAngleXml(chart, chartNs),
                BuildPieFamilyChartSeries(chart, sheet, chartNs, drawingNs)),
            ChartType.ThreeDPie => new XElement(chartNs + "pie3DChart",
                ToFirstSliceAngleXml(chart, chartNs),
                BuildPieFamilyChartSeries(chart, sheet, chartNs, drawingNs)),
            ChartType.Doughnut => new XElement(chartNs + "doughnutChart",
                ToFirstSliceAngleXml(chart, chartNs),
                BuildPieFamilyChartSeries(chart, sheet, chartNs, drawingNs),
                new XElement(chartNs + "holeSize",
                    new XAttribute("val", Math.Clamp((int)Math.Round(chart.DoughnutHoleSize * 100), 10, 90)))),
            _ => WithBarChartSpacing(new XElement(chartNs + "barChart",
                new XElement(chartNs + "barDir", new XAttribute("val", ToXlsxBarDirection(chart.Type))),
                new XElement(chartNs + "grouping", new XAttribute("val", ToXlsxBarGrouping(chart.Type))),
                ToChartBooleanValueXml(chartNs, "varyColors", chart.VaryColorsByPoint),
                BuildChartSeries(chart, sheet, chartNs, drawingNs, includeSeries)), chart, chartNs)
        };

    private static XElement CreateStockVolumeBarChart(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        WithBarChartSpacing(new XElement(chartNs + "barChart",
            new XElement(chartNs + "barDir", new XAttribute("val", "col")),
            new XElement(chartNs + "grouping", new XAttribute("val", "clustered")),
            BuildChartSeries(chart, sheet, chartNs, drawingNs, index => index == 0)), chart, chartNs);

    private static XElement CreateStockPlotChart(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool>? includeSeries = null)
    {
        var stockSeries = IsVolumeStockSubtype(chart.StockSubtype)
            ? new Func<int, bool>(index => index > 0 && (includeSeries?.Invoke(index) ?? true))
            : includeSeries;

        return new XElement(chartNs + "stockChart",
            BuildChartSeries(chart, sheet, chartNs, drawingNs, stockSeries, forceLineShapeProperties: true),
            ToChartGuideLineXml(chart, chartNs, drawingNs));
    }

    private static bool IsVolumeStockSubtype(StockChartSubtype subtype) =>
        subtype is StockChartSubtype.VolumeHighLowClose or StockChartSubtype.VolumeOpenHighLowClose;

    private static XElement WithBarChartSpacing(XElement barChart, ChartModel chart, XNamespace chartNs)
    {
        if (chart.BarOverlap is { } overlap)
            barChart.Add(new XElement(chartNs + "overlap", new XAttribute("val", Math.Clamp(overlap, -100, 100))));
        if (chart.BarGapWidth is { } gapWidth)
            barChart.Add(new XElement(chartNs + "gapWidth", new XAttribute("val", Math.Clamp(gapWidth, 0, 500))));
        return barChart;
    }

    private static XElement? ToChartBooleanValueXml(XNamespace chartNs, string elementName, bool? value) =>
        value.HasValue
            ? new XElement(chartNs + elementName, new XAttribute("val", value.Value ? "1" : "0"))
            : null;

    private static IEnumerable<XElement> ToBubbleChartOptionXml(ChartModel chart, XNamespace chartNs)
    {
        if (chart.BubbleScale != 100)
            yield return new XElement(chartNs + "bubbleScale",
                new XAttribute("val", Math.Clamp(chart.BubbleScale, 0, 300)));
        if (chart.ShowNegativeBubbles)
            yield return new XElement(chartNs + "showNegBubbles", new XAttribute("val", "1"));
        if (chart.BubbleSizeRepresents == ChartBubbleSizeRepresents.Width)
            yield return new XElement(chartNs + "sizeRepresents", new XAttribute("val", "w"));
    }

    private static XElement CreateLinePlotChart(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool> includeSeries) =>
        new(chartNs + "lineChart",
            BuildChartSeries(chart, sheet, chartNs, drawingNs, includeSeries, forceLineShapeProperties: true),
            ToChartGuideLineXml(chart, chartNs, drawingNs));

    private static XElement Create3DLinePlotChart(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool> includeSeries) =>
        new(chartNs + "line3DChart",
            BuildChartSeries(chart, sheet, chartNs, drawingNs, includeSeries, forceLineShapeProperties: true),
            ToChartGuideLineXml(chart, chartNs, drawingNs));

    private static XElement CreateScatterPlotChart(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool> includeSeries) =>
        new(chartNs + "scatterChart",
            new XElement(chartNs + "scatterStyle", new XAttribute("val", "lineMarker")),
            BuildScatterChartSeries(chart, sheet, chartNs, drawingNs, includeSeries));

    private static IEnumerable<XElement> ToChartGuideLineXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        if (chart.ShowDropLines)
            yield return new XElement(chartNs + "dropLines",
                ToChartGuideLineShapeProperties(
                    chart.DropLineThemeColor,
                    chart.DropLineColor,
                    chart.DropLineThickness,
                    chart.DropLineDashStyle,
                    chartNs,
                    drawingNs));
        if (chart.ShowHighLowLines)
            yield return new XElement(chartNs + "hiLowLines",
                ToChartGuideLineShapeProperties(
                    chart.HighLowLineThemeColor,
                    chart.HighLowLineColor,
                    chart.HighLowLineThickness,
                    chart.HighLowLineDashStyle,
                    chartNs,
                    drawingNs));
        if (chart.ShowUpDownBars)
            yield return ToUpDownBarsXml(chart, chartNs, drawingNs);
    }

    private static XElement ToUpDownBarsXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs)
    {
        var upBarsShape = ToShapeProperties(
            chartNs,
            drawingNs,
            chart.UpBarFillThemeColor,
            chart.UpBarFillColor,
            chart.UpBarBorderThemeColor,
            chart.UpBarBorderColor,
            chart.UpBarBorderThickness);
        var downBarsShape = ToShapeProperties(
            chartNs,
            drawingNs,
            chart.DownBarFillThemeColor,
            chart.DownBarFillColor,
            chart.DownBarBorderThemeColor,
            chart.DownBarBorderColor,
            chart.DownBarBorderThickness);

        return new XElement(chartNs + "upDownBars",
            chart.UpDownBarGapWidth is { } gapWidth
                ? new XElement(chartNs + "gapWidth", new XAttribute("val", Math.Clamp(gapWidth, 0, 500)))
                : null,
            upBarsShape is null ? null : new XElement(chartNs + "upBars", upBarsShape),
            downBarsShape is null ? null : new XElement(chartNs + "downBars", downBarsShape));
    }

    private static XElement? ToChartGuideLineShapeProperties(
        WorkbookThemeColorReference? themeColor,
        CellColor? color,
        double thickness,
        ChartLineDashStyle dashStyle,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var fill = ToSolidFill(themeColor, color, drawingNs);
        if (fill is null && thickness == 1 && dashStyle == ChartLineDashStyle.Solid)
            return null;

        return new XElement(chartNs + "spPr",
            new XElement(drawingNs + "ln",
                new XAttribute("w", Math.Max(0, (int)Math.Round(Math.Clamp(thickness, 0.5, 10) * 12700))),
                fill,
                ToPresetDash(dashStyle, drawingNs)));
    }

    private static void AddPlotChartCommonElements(
        XElement plotChart,
        ChartModel chart,
        XNamespace chartNs,
        XNamespace drawingNs,
        bool usesSecondaryAxis,
        bool includeDataLabels)
    {
        if (includeDataLabels && ToDataLabelsXml(chart, chartNs, drawingNs) is { } dataLabels)
            plotChart.Add(dataLabels);

        if (!ShouldWriteChartAxes(chart.Type))
            return;

        plotChart.Add(
            new XElement(chartNs + "axId", new XAttribute("val", CategoryAxisId)),
            new XElement(chartNs + "axId", new XAttribute("val", usesSecondaryAxis ? SecondaryValueAxisId : ValueAxisId)),
            chart.Type is ChartType.Surface or ChartType.ThreeDSurface
                ? new XElement(chartNs + "axId", new XAttribute("val", SeriesAxisId))
                : null);
    }

    public static bool IsSupportedXlsxChart(ChartModel chart) =>
        ChartTypeSupport.GetDataSeriesCount(chart) > 0 &&
        ChartTypeSupport.GetDataPointCount(chart) > 0 &&
        (!Enum.IsDefined(chart.Type) ||
            chart.Type is ChartType.Column
                or ChartType.StackedColumn
                or ChartType.PercentStackedColumn
                or ChartType.Bar
                or ChartType.StackedBar
                or ChartType.PercentStackedBar
                or ChartType.Line
                or ChartType.ThreeDLine
                or ChartType.Scatter
                or ChartType.Area
                or ChartType.ThreeDArea
                or ChartType.Bubble
                or ChartType.Pie
                or ChartType.ThreeDPie
                or ChartType.Doughnut
                or ChartType.Radar
                or ChartType.Stock
                or ChartType.Surface
                or ChartType.ThreeDSurface
                or ChartType.ThreeDColumn
                or ChartType.ThreeDBar);

    private static string ToXlsxBarDirection(ChartType chartType) =>
        chartType is ChartType.Bar or ChartType.StackedBar or ChartType.PercentStackedBar
            ? "bar"
            : "col";

    private static string ToXlsxBarGrouping(ChartType chartType) =>
        chartType switch
        {
            ChartType.StackedColumn or ChartType.StackedBar => "stacked",
            ChartType.PercentStackedColumn or ChartType.PercentStackedBar => "percentStacked",
            _ => "clustered"
        };

    private static string FormatSheetRange(string sheetName, uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var quotedSheet = $"'{sheetName.Replace("'", "''", StringComparison.Ordinal)}'";
        var start = $"${CellAddress.NumberToColumnName(startCol)}${startRow}";
        var end = $"${CellAddress.NumberToColumnName(endCol)}${endRow}";
        return start == end
            ? $"{quotedSheet}!{start}"
            : $"{quotedSheet}!{start}:{end}";
    }

}
