using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxChartXmlWriter
{
    private const int CategoryAxisId = 48650112;
    private const int ValueAxisId = 48672768;
    private const int SecondaryValueAxisId = 48672769;
    private const int SeriesAxisId = 48672770;

    public static XDocument ToChartXml(ChartModel chart, Workbook workbook, Sheet sheet)
    {
        if (ChartTypeSupport.IsChartExFamily(chart.Type))
            return ToChartExXml(chart, sheet);

        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        // R57-io-chart-series-refs-5-1: series ranges/values/categories must come from the sheet
        // chart.DataRange actually points at (which for a cross-sheet chart differs from the
        // chart's own anchor sheet), not from the anchor sheet unconditionally. ToPivotSourceXml's
        // fallback source-sheet name below intentionally keeps using the anchor `sheet` — that
        // fallback only applies when the chart doesn't already carry an explicit
        // PivotSourceSheetName, and mirrors the chart's own hosting sheet, not its data range.
        var dataSheet = ResolveChartDataSheet(chart, workbook, sheet);

        var plotCharts = ToPlotChartXml(chart, workbook, dataSheet, chartNs, drawingNs).ToList();

        return new XDocument(
                new XElement(chartNs + "chartSpace",
                    new XAttribute(XNamespace.Xmlns + "c", chartNs),
                    new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                    chart.ExternalData?.RelationshipId is null && chart.UserShapes?.RelationshipId is null ? null : new XAttribute(XNamespace.Xmlns + "r", relNs),
                    chart.Uses1904DateSystem ? new XElement(chartNs + "date1904", new XAttribute("val", "1")) : null,
                    string.IsNullOrWhiteSpace(chart.Language) ? null : new XElement(chartNs + "lang", new XAttribute("val", chart.Language)),
                    chart.RoundedCorners ? new XElement(chartNs + "roundedCorners", new XAttribute("val", "1")) : null,
                    chart.ChartStyleId is { } styleId ? new XElement(chartNs + "style", new XAttribute("val", styleId.ToString(CultureInfo.InvariantCulture))) : null,
                    ToChartColorMapOverrideXml(chart, chartNs, drawingNs),
                    ToPivotSourceXml(chart, sheet, chartNs),
                    ToChartProtectionXml(chart, chartNs),
                    new XElement(chartNs + "chart",
                        ShouldWriteChartTitle(chart, chartNs)
                            ? ToChartTitleXml(chart, chartNs, drawingNs)
                            : null,
                        chart.AutoTitleDeleted ? new XElement(chartNs + "autoTitleDeleted", new XAttribute("val", "1")) : null,
                        ToPivotFormatsXml(chart, chartNs),
                        ToChart3DViewXml(chart, chartNs),
                        ToChartSurfaceFormatXml(chart, chartNs, drawingNs, "floor", chart.FloorFormat),
                        ToChartSurfaceFormatXml(chart, chartNs, drawingNs, "sideWall", chart.SideWallFormat),
                        ToChartSurfaceFormatXml(chart, chartNs, drawingNs, "backWall", chart.BackWallFormat),
                        new XElement(chartNs + "plotArea",
                            ToManualLayoutXml(chart.PlotAreaLayout, chartNs),
                            plotCharts,
                            ShouldWriteChartAxes(chart.Type)
                                ? ToChartAxesXml(chart, chartNs, drawingNs)
                                : null,
                            ToChartDataTableXml(chart, chartNs, drawingNs),
                            ToPlotAreaShapeProperties(chart, chartNs, drawingNs)),
                        ToLegendXml(chart, chartNs, drawingNs),
                        chart.ShowDataInHiddenRowsAndColumns ? new XElement(chartNs + "plotVisOnly", new XAttribute("val", "0")) : null,
                        ToBlankDisplayXml(chart, chartNs),
                        chart.ShowDataLabelsOverMaximum ? new XElement(chartNs + "showDLblsOverMax", new XAttribute("val", "1")) : null,
                        ToPivotChartOptionsExtensionXml(chart, chartNs)),
                    ToChartAreaShapeProperties(chart, chartNs, drawingNs),
                    ToChartDefaultTextPropertiesXml(chart, chartNs, drawingNs),
                    ToChartExternalDataXml(chart, chartNs, relNs),
                    ToChartPrintSettingsXml(chart, chartNs),
                    ToChartUserShapesXml(chart, chartNs, relNs)));
    }

    /// <summary>
    /// R57-io-chart-series-refs-5-1: resolves the actual <see cref="Sheet"/> that
    /// <see cref="ChartModel.DataRange"/> points at, so series formulas/caches are keyed to the
    /// real data sheet rather than always the chart's own anchor sheet — required for a chart
    /// anchored on one sheet whose series reference a different sheet's cells (a normal,
    /// Excel-supported "dashboard tab charts data on another tab" scenario). Falls back to the
    /// anchor sheet when the range's sheet is the anchor sheet itself, or when it cannot be
    /// resolved (deleted sheet, orphaned reference, etc.) — matches
    /// <c>XlsxSparklineMapper.ResolveSheetName</c>'s same defensive fallback pattern.
    /// </summary>
    private static Sheet ResolveChartDataSheet(ChartModel chart, Workbook workbook, Sheet anchorSheet) =>
        chart.DataRange.Start.Sheet == anchorSheet.Id
            ? anchorSheet
            : workbook.GetSheet(chart.DataRange.Start.Sheet) ?? anchorSheet;

    private static bool ShouldWriteChartAxes(ChartType chartType) =>
        chartType is not ChartType.Pie and not ChartType.ThreeDPie and not ChartType.Doughnut;

    private static bool UsesSeriesAxis(ChartType chartType) =>
        chartType is ChartType.Surface
            or ChartType.ThreeDSurface
            or ChartType.ThreeDLine
            or ChartType.ThreeDArea;

    private static int? AdditionalPlotAxisId(ChartType chartType) =>
        UsesSeriesAxis(chartType)
            ? SeriesAxisId
            : chartType is ChartType.ThreeDColumn or ChartType.ThreeDBar
                ? 0
                : null;

    private static IEnumerable<XElement> ToPlotChartXml(
        ChartModel chart,
        Workbook workbook,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var groupIndex = 0;
        foreach (var (plotChart, usesSecondaryAxis) in CreatePlotCharts(chart, workbook, sheet, chartNs, drawingNs))
        {
            // Only the FIRST native plot-chart-type group's <c:dLbls> is modeled by the chart-wide
            // ShowDataLabels*/DataLabel* scalars. Later groups (combo charts) instead re-emit any
            // <c:dLbls> preserved verbatim from the source file for that same group index — see
            // ChartModel.AdditionalPlotGroupDataLabels.
            AddPlotChartCommonElements(plotChart, chart, chartNs, drawingNs, usesSecondaryAxis, includeDataLabels: groupIndex == 0);
            if (groupIndex > 0)
                AddPreservedGroupDataLabels(plotChart, chart, chartNs, groupIndex);
            groupIndex++;
            yield return plotChart;
        }
    }

    /// <summary>
    /// Re-attaches a non-first combo-chart plot group's original &lt;c:dLbls&gt; (preserved verbatim
    /// by <see cref="XlsxChartDataLabelReader.ApplyDataLabels"/>) to the group at the same yield
    /// index on save, so it round-trips instead of being dropped. See
    /// <see cref="ChartModel.AdditionalPlotGroupDataLabels"/>.
    /// </summary>
    private static void AddPreservedGroupDataLabels(XElement plotChart, ChartModel chart, XNamespace chartNs, int groupIndex)
    {
        var preserved = chart.AdditionalPlotGroupDataLabels
            .FirstOrDefault(group => group.GroupIndex == groupIndex);
        if (preserved is null)
            return;

        XElement dataLabels;
        try
        {
            dataLabels = XElement.Parse(preserved.RawXml);
        }
        catch (System.Xml.XmlException)
        {
            return;
        }

        InsertAfterLastSeries(plotChart, dataLabels, chartNs);
    }

    private static IEnumerable<(XElement PlotChart, bool UsesSecondaryAxis)> CreatePlotCharts(
        ChartModel chart,
        Workbook workbook,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs)
    {
        var seriesCount = ChartTypeSupport.GetDataSeriesCount(chart);
        var secondaryIndexes = GetSecondaryAxisSeriesIndexes(chart, seriesCount);
        var comboLineIndexes = GetComboLineSeriesIndexes(chart, seriesCount);
        if (chart.Type == ChartType.Stock && IsVolumeStockSubtype(chart.StockSubtype))
        {
            yield return (CreateStockVolumeBarChart(chart, workbook, sheet, chartNs, drawingNs), false);
            yield return (CreateStockPlotChart(chart, workbook, sheet, chartNs, drawingNs), false);
            yield break;
        }

        if (secondaryIndexes.Count > 0 && chart.Type == ChartType.Scatter)
        {
            var primaryScatter = Enumerable.Range(0, seriesCount)
                .Where(index => !secondaryIndexes.Contains(index))
                .ToHashSet();
            if (primaryScatter.Count > 0)
                yield return (CreateScatterPlotChart(chart, workbook, sheet, chartNs, drawingNs, primaryScatter.Contains), false);
            yield return (CreateScatterPlotChart(chart, workbook, sheet, chartNs, drawingNs, secondaryIndexes.Contains), true);
            yield break;
        }

        if (secondaryIndexes.Count > 0 && chart.Type is ChartType.Line or ChartType.ThreeDLine)
        {
            var primaryLine = Enumerable.Range(0, seriesCount)
                .Where(index => !secondaryIndexes.Contains(index))
                .ToHashSet();
            if (primaryLine.Count > 0)
                yield return (CreateLinePlotChart(chart, workbook, sheet, chartNs, drawingNs, primaryLine.Contains), false);
            yield return (CreateLinePlotChart(chart, workbook, sheet, chartNs, drawingNs, secondaryIndexes.Contains), true);
            yield break;
        }

        if ((secondaryIndexes.Count > 0 || comboLineIndexes.Count > 0) &&
            chart.Type is ChartType.Column or ChartType.StackedColumn or ChartType.PercentStackedColumn
                or ChartType.Area or ChartType.StackedArea or ChartType.PercentStackedArea or ChartType.ThreeDArea)
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
                yield return (CreateNativePlotChart(chart, workbook, sheet, chartNs, drawingNs, primaryBase.Contains), false);
            if (secondaryBase.Count > 0)
                yield return (CreateNativePlotChart(chart, workbook, sheet, chartNs, drawingNs, secondaryBase.Contains), true);
            if (primaryLine.Count > 0)
                yield return (CreateLinePlotChart(chart, workbook, sheet, chartNs, drawingNs, primaryLine.Contains), false);
            if (secondaryLine.Count > 0)
                yield return (CreateLinePlotChart(chart, workbook, sheet, chartNs, drawingNs, secondaryLine.Contains), true);

            yield break;
        }

        yield return (CreateNativePlotChart(chart, workbook, sheet, chartNs, drawingNs, _ => true), false);
    }

    private static XElement CreateNativePlotChart(
        ChartModel chart,
        Workbook workbook,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool> includeSeries) =>
        chart.Type switch
        {
            ChartType.Line => CreateLinePlotChart(chart, workbook, sheet, chartNs, drawingNs, includeSeries),
            ChartType.ThreeDLine => Create3DLinePlotChart(chart, workbook, sheet, chartNs, drawingNs, includeSeries),
            ChartType.Scatter => CreateScatterPlotChart(chart, workbook, sheet, chartNs, drawingNs, includeSeries),
            ChartType.Radar => new XElement(chartNs + "radarChart",
                new XElement(chartNs + "radarStyle", new XAttribute("val", "marker")),
                BuildChartSeries(chart, workbook, sheet, chartNs, drawingNs, includeSeries, forceLineShapeProperties: true)),
            ChartType.Stock => CreateStockPlotChart(chart, workbook, sheet, chartNs, drawingNs, includeSeries),
            ChartType.Surface => new XElement(chartNs + "surfaceChart",
                BuildChartSeries(chart, workbook, sheet, chartNs, drawingNs, includeSeries)),
            ChartType.ThreeDSurface => new XElement(chartNs + "surface3DChart",
                new XElement(chartNs + "wireframe", new XAttribute("val", "0")),
                BuildChartSeries(chart, workbook, sheet, chartNs, drawingNs, includeSeries)),
            ChartType.Area or ChartType.StackedArea or ChartType.PercentStackedArea => new XElement(chartNs + "areaChart",
                new XElement(chartNs + "grouping", new XAttribute("val", ToXlsxAreaGrouping(chart.Type))),
                BuildChartSeries(chart, workbook, sheet, chartNs, drawingNs, includeSeries)),
            ChartType.ThreeDArea => new XElement(chartNs + "area3DChart",
                new XElement(chartNs + "grouping", new XAttribute("val", "standard")),
                BuildChartSeries(chart, workbook, sheet, chartNs, drawingNs, includeSeries)),
            ChartType.ThreeDColumn or ChartType.ThreeDBar => new XElement(chartNs + "bar3DChart",
                new XElement(chartNs + "barDir", new XAttribute("val", chart.Type == ChartType.ThreeDBar ? "bar" : "col")),
                new XElement(chartNs + "grouping", new XAttribute("val", "clustered")),
                ToChartBooleanValueXml(chartNs, "varyColors", chart.VaryColorsByPoint),
                BuildChartSeries(chart, workbook, sheet, chartNs, drawingNs, includeSeries),
                ToBarGapWidthXml(chart, chartNs),
                new XElement(chartNs + "shape", new XAttribute("val", "box"))),
            ChartType.Bubble => new XElement(chartNs + "bubbleChart",
                BuildBubbleChartSeries(chart, workbook, sheet, chartNs, drawingNs),
                ToBubbleChartOptionXml(chart, chartNs)),
            // R42-io-chart-plotarea-legend-3-2: varyColors must precede the <c:ser> elements per
            // CT_PieChart/CT_Pie3DChart/CT_DoughnutChart -- an explicit "Vary Colors by Point"
            // choice (including turning it OFF, val="0") is only preserved on round-trip if it is
            // actually written; previously only bar/bar3D charts emitted this element.
            ChartType.Pie => new XElement(chartNs + "pieChart",
                ToChartBooleanValueXml(chartNs, "varyColors", chart.VaryColorsByPoint),
                BuildPieFamilyChartSeries(chart, workbook, sheet, chartNs, drawingNs),
                ToFirstSliceAngleXml(chart, chartNs)),
            ChartType.ThreeDPie => new XElement(chartNs + "pie3DChart",
                ToChartBooleanValueXml(chartNs, "varyColors", chart.VaryColorsByPoint),
                BuildPieFamilyChartSeries(chart, workbook, sheet, chartNs, drawingNs)),
            ChartType.Doughnut => new XElement(chartNs + "doughnutChart",
                ToChartBooleanValueXml(chartNs, "varyColors", chart.VaryColorsByPoint),
                BuildPieFamilyChartSeries(chart, workbook, sheet, chartNs, drawingNs),
                ToFirstSliceAngleXml(chart, chartNs),
                new XElement(chartNs + "holeSize",
                    new XAttribute("val", Math.Clamp((int)Math.Round(chart.DoughnutHoleSize * 100), 10, 90)))),
            _ => new XElement(chartNs + "barChart",
                new XElement(chartNs + "barDir", new XAttribute("val", ToXlsxBarDirection(chart.Type))),
                new XElement(chartNs + "grouping", new XAttribute("val", ToXlsxBarGrouping(chart.Type))),
                ToChartBooleanValueXml(chartNs, "varyColors", chart.VaryColorsByPoint),
                BuildChartSeries(chart, workbook, sheet, chartNs, drawingNs, includeSeries),
                ToBarGapWidthXml(chart, chartNs),
                ToBarOverlapXml(chart, chartNs),
                ToSeriesLinesXml(chart, chartNs, drawingNs))
        };

    private static XElement CreateStockVolumeBarChart(
        ChartModel chart,
        Workbook workbook,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs) =>
        new(chartNs + "barChart",
            new XElement(chartNs + "barDir", new XAttribute("val", "col")),
            new XElement(chartNs + "grouping", new XAttribute("val", "clustered")),
            BuildChartSeries(chart, workbook, sheet, chartNs, drawingNs, index => index == 0),
            ToBarGapWidthXml(chart, chartNs),
            ToBarOverlapXml(chart, chartNs));

    private static XElement CreateStockPlotChart(
        ChartModel chart,
        Workbook workbook,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool>? includeSeries = null)
    {
        var stockSeries = IsVolumeStockSubtype(chart.StockSubtype)
            ? new Func<int, bool>(index => index > 0 && (includeSeries?.Invoke(index) ?? true))
            : includeSeries;

        return new XElement(chartNs + "stockChart",
            BuildChartSeries(chart, workbook, sheet, chartNs, drawingNs, stockSeries, forceLineShapeProperties: true),
            ToChartGuideLineXml(chart, chartNs, drawingNs));
    }

    private static bool IsVolumeStockSubtype(StockChartSubtype subtype) =>
        subtype is StockChartSubtype.VolumeHighLowClose or StockChartSubtype.VolumeOpenHighLowClose;

    private static XElement? ToBarGapWidthXml(ChartModel chart, XNamespace chartNs)
    {
        var gapWidth = chart.BarGapWidth ?? ToExcelNativeDefaultBarGapWidth(chart.Type);
        return gapWidth is { } value
            ? new XElement(chartNs + "gapWidth", new XAttribute("val", Math.Clamp(value, 0, 500)))
            : null;
    }

    private static XElement? ToBarOverlapXml(ChartModel chart, XNamespace chartNs)
    {
        var overlap = chart.BarOverlap ?? ToExcelNativeDefaultBarOverlap(chart.Type);
        return overlap is { } value
            ? new XElement(chartNs + "overlap", new XAttribute("val", Math.Clamp(value, -100, 100)))
            : null;
    }

    // Modern Excel writes gapWidth=219 for every bar/column grouping.
    private static int? ToExcelNativeDefaultBarGapWidth(ChartType chartType) =>
        chartType is ChartType.Column
            or ChartType.Bar
            or ChartType.StackedColumn
            or ChartType.PercentStackedColumn
            or ChartType.StackedBar
            or ChartType.PercentStackedBar
            or ChartType.ThreeDColumn
            or ChartType.ThreeDBar
                ? 219
                : null;

    // Excel writes overlap=-27 for clustered AND stacked/100%-stacked 2-D bar/column
    // (verified against Excel native output); 3-D bar/column do not write overlap.
    private static int? ToExcelNativeDefaultBarOverlap(ChartType chartType) =>
        chartType is ChartType.Column
            or ChartType.Bar
            or ChartType.StackedColumn
            or ChartType.PercentStackedColumn
            or ChartType.StackedBar
            or ChartType.PercentStackedBar
                ? -27
                : null;

    private static bool IsClassicStackedBarOrColumnChart(ChartType chartType) =>
        chartType is ChartType.StackedColumn
            or ChartType.PercentStackedColumn
            or ChartType.StackedBar
            or ChartType.PercentStackedBar;

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
        Workbook workbook,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool> includeSeries) =>
        new(chartNs + "lineChart",
            // CT_LineChart requires <c:grouping> before the series.
            new XElement(chartNs + "grouping", new XAttribute("val", "standard")),
            BuildChartSeries(chart, workbook, sheet, chartNs, drawingNs, includeSeries, forceLineShapeProperties: true),
            ToChartGuideLineXml(chart, chartNs, drawingNs));

    private static XElement Create3DLinePlotChart(
        ChartModel chart,
        Workbook workbook,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool> includeSeries) =>
        new(chartNs + "line3DChart",
            new XElement(chartNs + "grouping", new XAttribute("val", "standard")),
            BuildChartSeries(chart, workbook, sheet, chartNs, drawingNs, includeSeries, forceLineShapeProperties: true),
            ToChartGuideLineXml(chart, chartNs, drawingNs));

    private static XElement CreateScatterPlotChart(
        ChartModel chart,
        Workbook workbook,
        Sheet sheet,
        XNamespace chartNs,
        XNamespace drawingNs,
        Func<int, bool> includeSeries) =>
        new(chartNs + "scatterChart",
            new XElement(chartNs + "scatterStyle", new XAttribute("val", "lineMarker")),
            BuildScatterChartSeries(chart, workbook, sheet, chartNs, drawingNs, includeSeries));

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

    private static XElement? ToSeriesLinesXml(ChartModel chart, XNamespace chartNs, XNamespace drawingNs) =>
        ChartTypeSupport.SupportsSeriesLines(chart.Type) && chart.ShowSeriesLines
            ? new XElement(chartNs + "serLines",
                ToChartGuideLineShapeProperties(
                    chart.SeriesLineThemeColor,
                    chart.SeriesLineColor,
                    chart.SeriesLineThickness,
                    chart.SeriesLineDashStyle,
                    chartNs,
                    drawingNs))
            : null;

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
                new XAttribute("w", Math.Max(0, (int)Math.Round(Math.Clamp(thickness, 0.5, 10) * DrawingMlCoordinateUnits.EmuPerPoint))),
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
        if (includeDataLabels && ChartTypeSupport.SupportsDataLabels(chart.Type) &&
            ToDataLabelsXml(chart, chartNs, drawingNs) is { } dataLabels)
            InsertAfterLastSeries(plotChart, dataLabels, chartNs);

        if (!ShouldWriteChartAxes(chart.Type))
            return;

        plotChart.Add(
            new XElement(chartNs + "axId", new XAttribute("val", CategoryAxisId)),
            new XElement(chartNs + "axId", new XAttribute("val", usesSecondaryAxis ? SecondaryValueAxisId : ValueAxisId)),
            AdditionalPlotAxisId(chart.Type) is { } additionalAxisId
                ? new XElement(chartNs + "axId", new XAttribute("val", additionalAxisId))
                : null);
    }

    private static void InsertAfterLastSeries(XElement plotChart, XElement element, XNamespace chartNs)
    {
        if (plotChart.Elements(chartNs + "ser").LastOrDefault() is { } lastSeries)
            lastSeries.AddAfterSelf(element);
        else
            plotChart.Add(element);
    }

    public static bool IsSupportedXlsxChart(ChartModel chart) =>
        ChartTypeSupport.GetDataSeriesCount(chart) > 0 &&
        ChartTypeSupport.GetDataPointCount(chart) > 0 &&
        (!Enum.IsDefined(chart.Type) ||
            ChartTypeSupport.IsAuthorable(chart.Type));

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

    // c:areaChart uses ST_Grouping (percentStacked | standard | stacked) — no "clustered" member,
    // unlike the bar/column ST_BarGrouping above — so a plain/3-D Area chart writes "standard".
    private static string ToXlsxAreaGrouping(ChartType chartType) =>
        chartType switch
        {
            ChartType.StackedArea => "stacked",
            ChartType.PercentStackedArea => "percentStacked",
            _ => "standard"
        };

    private static string FormatSheetRange(string sheetName, uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var quotedSheet = SheetNameFormatter.QuoteIfNeeded(sheetName);
        var start = $"${CellAddress.NumberToColumnName(startCol)}${startRow}";
        var end = $"${CellAddress.NumberToColumnName(endCol)}${endRow}";
        return start == end
            ? $"{quotedSheet}!{start}"
            : $"{quotedSheet}!{start}:{end}";
    }

}
