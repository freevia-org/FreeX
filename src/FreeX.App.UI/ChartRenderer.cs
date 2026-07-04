using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using OxyPlot.Wpf;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.UI;

/// <summary>Renders a ChartModel into a WPF ImageSource for use in DrawingContext.</summary>
public static partial class ChartRenderer
{
    private const string SecondaryYAxisKey = "SecondaryY";

    public static ImageSource? Render(ChartModel chart, ViewportModel viewport) =>
        Render(chart, viewport, WorkbookTheme.Office);

    public static ImageSource? Render(ChartModel chart, ViewportModel viewport, WorkbookTheme? theme)
        => Render(chart, viewport, theme, renderScale: 1.0);

    public static ImageSource? Render(ChartModel chart, ViewportModel viewport, WorkbookTheme? theme, double renderScale)
    {
        var resolvedTheme = theme ?? WorkbookTheme.Office;
        var model = BuildPlotModel(chart, viewport, resolvedTheme);
        if (model == null) return null;

        renderScale = NormalizeRenderScale(renderScale);
        var exporter = new PngExporter
        {
            Width  = Math.Max(1, (int)Math.Ceiling(chart.Width * renderScale)),
            Height = Math.Max(1, (int)Math.Ceiling(chart.Height * renderScale)),
        };

        using var stream = new System.IO.MemoryStream();
        exporter.Export(model, stream);
        stream.Position = 0;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = stream;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        if (IsVisiblyBlank(bitmap) &&
            RenderDirectFallback(chart, viewport, resolvedTheme, renderScale) is { } fallback)
        {
            return fallback;
        }

        return bitmap;
    }

    private static double NormalizeRenderScale(double renderScale)
    {
        if (!double.IsFinite(renderScale))
            return 2.0;

        renderScale = Math.Clamp(renderScale, 0.25, 4.0);
        return Math.Clamp(Math.Max(2.0, Math.Ceiling(renderScale)), 2.0, 4.0);
    }

    private static PlotModel? BuildPlotModel(ChartModel chart, ViewportModel viewport) =>
        BuildPlotModel(chart, viewport, WorkbookTheme.Office);

    private static PlotModel? BuildPlotModel(ChartModel chart, ViewportModel viewport, WorkbookTheme theme)
    {
        if (!ChartTypeSupport.IsRenderable(chart.Type))
            return null;

        var cellLookup = BuildChartCellLookup(chart, viewport);

        uint startRow = chart.DataRange.Start.Row;
        uint endRow   = chart.DataRange.End.Row;
        uint startCol = chart.DataRange.Start.Col;
        uint endCol   = chart.DataRange.End.Col;

        uint dataStartRow = chart.FirstRowIsHeader ? startRow + 1 : startRow;
        uint dataStartCol = chart.FirstColIsCategories ? startCol + 1 : startCol;

        var dataPointCapacity = GetDataPointCapacity(dataStartRow, endRow);
        var categories = new List<string>(chart.FirstColIsCategories ? dataPointCapacity : 0);
        if (chart.FirstColIsCategories)
            for (uint r = dataStartRow; r <= endRow; r++)
                categories.Add(cellLookup.TryGetValue((r, startCol), out var c) ? FormatCategoryLabel(chart, c) : "");

        var model = new PlotModel { Title = chart.Title };
        model.DefaultColors = BuildExcelSeriesPalette(theme);
        ApplyTitleStyle(model, chart, theme);
        ApplyAreaStyle(model, chart, theme);
        ConfigureLegend(model, chart, theme);
        AddPivotChartFieldButtons(model, chart);
        var pointDataLabelFormats = ShouldUseAnnotationLabels(chart)
            ? new ChartPointDataLabelFormatLookup(chart.PointDataLabelFormats)
            : default;

        if (chart.Type is ChartType.Pie or ChartType.ThreeDPie or ChartType.Doughnut)
        {
            var pieSeriesName = chart.FirstRowIsHeader && cellLookup.TryGetValue((startRow, dataStartCol), out var pieHeader)
                ? pieHeader.DisplayText
                : "Series 1";
            var pieSeries = new PieSeries
            {
                StrokeThickness = 1.0,
                InnerDiameter = chart.Type == ChartType.Doughnut ? chart.DoughnutHoleSize : 0,
                StartAngle = chart.FirstSliceAngle,
                ExplodedDistance = chart.ExplodedSliceDistance,
                InsideLabelPosition = chart.DataLabelPosition switch
                {
                    ChartDataLabelPosition.Center => 0.5,
                    ChartDataLabelPosition.InsideEnd => 0.8,
                    _ => 0.8
                },
                AreInsideLabelsAngled = ShouldUseNativePieLabels(chart) && Math.Abs(chart.DataLabelAngle) > 0.5,
                InsideLabelFormat = ShouldUseNativePieLabels(chart) && chart.DataLabelPosition != ChartDataLabelPosition.OutsideEnd
                    ? ChartDataLabelFormatter.GetPieLabelFormat(chart, pieSeriesName)
                    : "",
                OutsideLabelFormat = ShouldUseNativePieLabels(chart) && chart.DataLabelPosition == ChartDataLabelPosition.OutsideEnd
                    ? ChartDataLabelFormatter.GetPieLabelFormat(chart, pieSeriesName)
                    : ""
            };
            var pieFormat = GetSeriesFormat(chart, 0);
            ApplyPieFormat(pieSeries, pieFormat, theme);
            ApplyPieDataLabelStyle(pieSeries, chart, theme);
            var piePalette = BuildExcelSeriesPalette(theme);
            var pieLabelPoints = new List<PieDataLabelPoint>(dataPointCapacity);
            for (uint r = dataStartRow; r <= endRow; r++)
            {
                if (!cellLookup.TryGetValue((r, dataStartCol), out var cell)) continue;
                if (!TryGetChartNumericValue(cell, out var v)) continue;
                var label = categories.Count > (int)(r - dataStartRow) ? categories[(int)(r - dataStartRow)] : "";
                var sliceIndex = pieSeries.Slices.Count;
                var slice = new PieSlice(label, v)
                {
                    IsExploded = chart.ExplodedSliceIndex == sliceIndex
                };
                // Per-point fill (from <c:dPt> in chart XML) takes highest priority;
                // fall back to series-level fill, then palette.
                var pointFill = GetPointFillColor(chart, 0, sliceIndex, theme);
                if (pointFill is { } perPointFill)
                    slice.Fill = OxyColor.FromRgb(perPointFill.R, perPointFill.G, perPointFill.B);
                else if (pieFormat?.ResolveFillColor(theme) is { } fill)
                    slice.Fill = OxyColor.FromRgb(fill.R, fill.G, fill.B);
                else
                    slice.Fill = piePalette[sliceIndex % piePalette.Count];
                pieSeries.Slices.Add(slice);
                pieLabelPoints.Add(new PieDataLabelPoint(label, v));
            }
            model.Series.Add(pieSeries);
            AddPieDataLabelAnnotations(model, chart, theme, pieSeriesName, pieLabelPoints);
            // OxyPlot 2.x PieSeries does not surface per-slice legend entries, so draw a custom
            // legend (color swatch + category label per slice) to match Excel's pie legend.
            AddPieLegendAnnotations(model, chart, theme, pieSeries);
            return model;
        }

        if (chart.Type is ChartType.StackedColumn or ChartType.PercentStackedColumn)
        {
            // Progress-bar idiom: N single-cell series in one column with no categories — synthesize
            // one segment per row in a single category instead of rendering blank.
            if (IsSingleColumnStackedSeriesShape(chart, categories, dataStartRow, endRow, dataStartCol, endCol))
            {
                var synthesized = BuildSingleColumnStackedModel(chart, model, cellLookup, dataStartRow, endRow, dataStartCol, startRow, isBar: false, chart.Type == ChartType.PercentStackedColumn, theme);
                ApplyAxisBounds(synthesized, chart, theme);
                return synthesized;
            }

            var stackedColumnModel = BuildStackedColumnModel(chart, model, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow, chart.Type == ChartType.PercentStackedColumn, theme, pointDataLabelFormats);
            ApplyAxisBounds(stackedColumnModel, chart, theme);
            AddChartDataTableAnnotations(stackedColumnModel, chart, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow);
            return stackedColumnModel;
        }

        if (chart.Type is ChartType.StackedBar or ChartType.PercentStackedBar)
        {
            if (IsSingleColumnStackedSeriesShape(chart, categories, dataStartRow, endRow, dataStartCol, endCol))
            {
                var synthesized = BuildSingleColumnStackedModel(chart, model, cellLookup, dataStartRow, endRow, dataStartCol, startRow, isBar: true, chart.Type == ChartType.PercentStackedBar, theme);
                ApplyAxisBounds(synthesized, chart, theme);
                return synthesized;
            }

            var stackedBarModel = BuildStackedBarModel(chart, model, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow, chart.Type == ChartType.PercentStackedBar, theme, pointDataLabelFormats);
            ApplyAxisBounds(stackedBarModel, chart, theme);
            AddChartDataTableAnnotations(stackedBarModel, chart, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow);
            return stackedBarModel;
        }

        if (chart.Type == ChartType.Bubble)
        {
            var bubbleModel = BuildBubbleModel(chart, model, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow, theme, pointDataLabelFormats, out var trendPoints);
            AddTrendlineIfRequested(bubbleModel, chart, theme, trendPoints);
            ApplyAxisBounds(bubbleModel, chart, theme);
            AddChartDataTableAnnotations(bubbleModel, chart, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow);
            return bubbleModel;
        }

        if (chart.Type == ChartType.Radar)
        {
            var radarModel = BuildRadarModel(chart, model, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow, theme);
            AddChartDataTableAnnotations(radarModel, chart, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow);
            return radarModel;
        }

        if (chart.Type == ChartType.Stock)
        {
            var stockModel = BuildStockModel(chart, model, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow, theme);
            AddChartDataTableAnnotations(stockModel, chart, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow);
            return stockModel;
        }

        if (chart.Type is ChartType.Surface or ChartType.ThreeDSurface)
        {
            var surfaceModel = BuildSurfaceModel(chart, model, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow, theme);
            AddChartDataTableAnnotations(surfaceModel, chart, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow);
            return surfaceModel;
        }

        if (chart.Type == ChartType.Waterfall)
            return BuildWaterfallModel(chart, model, cellLookup, categories, dataStartRow, endRow, dataStartCol, theme);

        if (chart.Type == ChartType.Histogram)
            return BuildHistogramModel(chart, model, cellLookup, dataStartRow, endRow, dataStartCol, theme);

        if (chart.Type == ChartType.Pareto)
            return BuildParetoModel(chart, model, cellLookup, categories, dataStartRow, endRow, dataStartCol, theme);

        if (chart.Type == ChartType.BoxAndWhisker)
            return BuildBoxAndWhiskerModel(chart, model, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow, theme);

        if (chart.Type == ChartType.Treemap)
            return BuildTreemapModel(chart, model, cellLookup, categories, dataStartRow, endRow, dataStartCol, theme);

        if (chart.Type == ChartType.Sunburst)
            return BuildSunburstModel(chart, model, cellLookup, categories, dataStartRow, endRow, dataStartCol, theme);

        if (chart.Type == ChartType.Funnel)
            return BuildFunnelModel(chart, model, cellLookup, categories, dataStartRow, endRow, dataStartCol, theme);

        // Column / Line: one series per data column
        List<DataPoint>? firstSeriesPoints = null;
        // For clustered (grouped) Column charts each non-overlay series gets a disjoint sub-slot
        // within the category so the bars sit side by side rather than overdrawing each other.
        var clusteredColumnCount = chart.Type is ChartType.Column or ChartType.ThreeDColumn
            ? CountClusteredBarSeries(chart, dataStartCol, endCol)
            : 0;
        var clusteredColumnOrdinal = 0;
        // Per-clustered-bar-series value lists (category index -> value), captured so a
        // Budget-vs-Actual <c:upDownBars> deviation overlay can be drawn between the first two
        // bar series after the main loop. Only collected for clustered column charts.
        var clusteredBarValues = new List<List<double?>>();
        for (uint col = dataStartCol; col <= endCol; col++)
        {
            if (ShouldSkipScatterXColumn(chart, col, dataStartCol))
                continue;

            if (!ShouldRenderColumnAsSeries(chart, col, dataStartCol, endCol))
                continue;

            var seriesIndex = GetSeriesIndex(chart, col, dataStartCol, endCol);
            string seriesName = chart.FirstRowIsHeader && cellLookup.TryGetValue((startRow, col), out var hdr)
                ? hdr.DisplayText : $"Series {seriesIndex + 1}";

            if (chart.Type is ChartType.Column or ChartType.ThreeDColumn)
            {
                if (!model.Axes.Any())
                {
                    // When there are no category labels (no <c:cat> in the series) the effective
                    // data-point count comes from the row range so the x-axis spans all points.
                    var categoryOrRowCount = categories.Count > 0
                        ? categories.Count
                        : (int)(endRow - dataStartRow + 1);
                    model.Axes.Add(CreateCenteredIndexedCategoryAxis(AxisPosition.Bottom, chart.XAxisTitle, categories, categoryOrRowCount));
                    model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = chart.YAxisTitle });
                    AddSecondaryAxisIfRequested(model, chart);
                }

                if (IsComboScatterSeries(chart, seriesIndex))
                {
                    var scatterSeries = new ScatterSeries
                    {
                        Title = seriesName,
                        MarkerType = MarkerType.Circle,
                        MarkerSize = 4,
                        YAxisKey = UsesSecondaryAxis(chart, seriesIndex) ? SecondaryYAxisKey : null
                    };
                    ApplyScatterFormat(scatterSeries, GetSeriesFormat(chart, seriesIndex), theme);
                    var scatterPointIndex = 0;
                    for (uint r = dataStartRow; r <= endRow; r++, scatterPointIndex++)
                    {
                        if (cellLookup.TryGetValue((r, col), out var cell)
                            && TryGetChartNumericValue(cell, out var v))
                        {
                            scatterSeries.Points.Add(new ScatterPoint(scatterPointIndex, v));
                        }
                    }
                    model.Series.Add(scatterSeries);
                    continue;
                }

                if (IsComboLineSeries(chart, seriesIndex))
                {
                    var lineSeries = CreateLineSeries(chart, seriesName, seriesIndex, theme);
                    AddLinePoints(lineSeries, chart, cellLookup, dataStartRow, endRow, col, firstSeriesPoints is null ? new List<DataPoint>() : null, out var comboTrendPoints);
                    if (firstSeriesPoints is null)
                        firstSeriesPoints = comboTrendPoints;
                    AddLineDataLabelAnnotations(model, chart, theme, pointDataLabelFormats, lineSeries, seriesName, seriesIndex, categories);
                    model.Series.Add(lineSeries);
                    continue;
                }

                var series = new RectangleBarSeries
                {
                    // When the chart XML marks this series' legend entry as deleted
                    // (e.g. bullet-chart helper series "Max Invisible", "Max Outline"),
                    // suppress the legend entry by leaving the title blank.
                    Title = IsLegendEntryDeleted(chart, seriesIndex) ? "" : seriesName,
                    LabelFormatString = ChartDataLabelFormatter.GetNativeValueLabelFormat(chart, 4),
                    YAxisKey = UsesSecondaryAxis(chart, seriesIndex) ? SecondaryYAxisKey : null
                };
                ApplyRectangleBarFormat(series, GetSeriesFormat(chart, seriesIndex), theme);
                ApplyNativeDataLabelStyle(series, chart, theme);
                var trendPoints = firstSeriesPoints is null ? new List<DataPoint>() : null;
                var colHalfWidth = ColumnBarHalfWidth(chart);
                var (clusterLeft, clusterRight) = ClusteredBarOffsets(colHalfWidth, clusteredColumnOrdinal, clusteredColumnCount);
                clusteredColumnOrdinal++;
                var barCategoryValues = new List<double?>();
                var i = 0;
                for (uint r = dataStartRow; r <= endRow; r++, i++)
                {
                    if (cellLookup.TryGetValue((r, col), out var cell)
                        && TryGetChartNumericValue(cell, out var v))
                    {
                        series.Items.Add(new RectangleBarItem(i + clusterLeft, Math.Min(0, v), i + clusterRight, Math.Max(0, v)));
                        trendPoints?.Add(new DataPoint(i, v));
                        barCategoryValues.Add(v);
                        if (ShouldUseAnnotationLabels(chart))
                            AddDataLabelAnnotation(model, chart, theme, pointDataLabelFormats, seriesName, seriesIndex, i, ChartDataLabelTextPlanner.GetCategory(categories, i), i, v, v);
                    }
                    else if (chart.BlankDisplayMode == ChartBlankDisplayMode.Zero
                        && cellLookup.TryGetValue((r, col), out cell)
                        && IsChartBlank(cell))
                    {
                        series.Items.Add(new RectangleBarItem(i + clusterLeft, 0, i + clusterRight, 0));
                        trendPoints?.Add(new DataPoint(i, 0));
                        barCategoryValues.Add(0);
                        if (ShouldUseAnnotationLabels(chart))
                            AddDataLabelAnnotation(model, chart, theme, pointDataLabelFormats, seriesName, seriesIndex, i, ChartDataLabelTextPlanner.GetCategory(categories, i), i, 0, 0);
                    }
                    else
                    {
                        barCategoryValues.Add(null);
                    }
                }
                if (firstSeriesPoints is null)
                    firstSeriesPoints = trendPoints;
                clusteredBarValues.Add(barCategoryValues);
                model.Series.Add(series);
            }
            else if (chart.Type is ChartType.Bar or ChartType.ThreeDBar)
            {
                if (!model.Axes.Any(a => a is CategoryAxis))
                {
                    model.Axes.Add(CreateCategoryAxis(AxisPosition.Left, chart.YAxisTitle, categories));
                    model.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = chart.XAxisTitle });
                }

                var series = new BarSeries
                {
                    Title = IsLegendEntryDeleted(chart, seriesIndex) ? "" : seriesName,
                    LabelFormatString = ChartDataLabelFormatter.GetNativeValueLabelFormat(chart, 0),
                    LabelPlacement = ToOxyLabelPlacement(chart.DataLabelPosition)
                };
                ApplyBarFormat(series, GetSeriesFormat(chart, seriesIndex), theme);
                ApplyNativeDataLabelStyle(series, chart, theme);
                var trendPoints = firstSeriesPoints is null ? new List<DataPoint>() : null;
                var i = 0;
                for (uint r = dataStartRow; r <= endRow; r++, i++)
                {
                    if (cellLookup.TryGetValue((r, col), out var cell)
                        && TryGetChartNumericValue(cell, out var v))
                    {
                        series.Items.Add(new BarItem { Value = v });
                        trendPoints?.Add(new DataPoint(i, v));
                        if (ShouldUseAnnotationLabels(chart))
                            AddDataLabelAnnotation(model, chart, theme, pointDataLabelFormats, seriesName, seriesIndex, i, ChartDataLabelTextPlanner.GetCategory(categories, i), v, i, v);
                    }
                    else if (chart.BlankDisplayMode == ChartBlankDisplayMode.Zero
                        && cellLookup.TryGetValue((r, col), out cell)
                        && IsChartBlank(cell))
                    {
                        series.Items.Add(new BarItem { Value = 0 });
                        trendPoints?.Add(new DataPoint(i, 0));
                        if (ShouldUseAnnotationLabels(chart))
                            AddDataLabelAnnotation(model, chart, theme, pointDataLabelFormats, seriesName, seriesIndex, i, ChartDataLabelTextPlanner.GetCategory(categories, i), 0, i, 0);
                    }
                }
                if (firstSeriesPoints is null)
                    firstSeriesPoints = trendPoints;
                model.Series.Add(series);
            }
            else if (chart.Type is ChartType.Area or ChartType.ThreeDArea)
            {
                if (!model.Axes.Any())
                {
                    model.Axes.Add(CreateZeroBasedIndexedCategoryAxis(AxisPosition.Bottom, chart.XAxisTitle, categories));
                    model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = chart.YAxisTitle });
                    AddSecondaryAxisIfRequested(model, chart);
                }

                if (IsComboLineSeries(chart, seriesIndex))
                {
                    var lineSeries = CreateLineSeries(chart, seriesName, seriesIndex, theme);
                    AddLinePoints(lineSeries, chart, cellLookup, dataStartRow, endRow, col, firstSeriesPoints is null ? new List<DataPoint>() : null, out var comboTrendPoints);
                    if (firstSeriesPoints is null)
                        firstSeriesPoints = comboTrendPoints;
                    AddLineDataLabelAnnotations(model, chart, theme, pointDataLabelFormats, lineSeries, seriesName, seriesIndex, categories);
                    model.Series.Add(lineSeries);
                    continue;
                }

                var series = new AreaSeries
                {
                    Title = seriesName,
                    LabelFormatString = ChartDataLabelFormatter.GetNativeValueLabelFormat(chart, 1)
                };
                ApplyAreaFormat(series, GetSeriesFormat(chart, seriesIndex), theme);
                ApplyNativeDataLabelStyle(series, chart, theme);
                var trendPoints = firstSeriesPoints is null ? new List<DataPoint>() : null;
                int i = 0;
                for (uint r = dataStartRow; r <= endRow; r++, i++)
                {
                    if (cellLookup.TryGetValue((r, col), out var cell)
                        && TryGetChartNumericValue(cell, out var v))
                    {
                        series.Points.Add(new DataPoint(i, v));
                        trendPoints?.Add(new DataPoint(i, v));
                        if (ShouldUseAnnotationLabels(chart))
                            AddDataLabelAnnotation(model, chart, theme, pointDataLabelFormats, seriesName, seriesIndex, i, ChartDataLabelTextPlanner.GetCategory(categories, i), i, v, v);
                    }
                    else if (cellLookup.TryGetValue((r, col), out cell) && IsChartBlank(cell))
                    {
                        if (chart.BlankDisplayMode == ChartBlankDisplayMode.Zero)
                        {
                            series.Points.Add(new DataPoint(i, 0));
                            trendPoints?.Add(new DataPoint(i, 0));
                            if (ShouldUseAnnotationLabels(chart))
                                AddDataLabelAnnotation(model, chart, theme, pointDataLabelFormats, seriesName, seriesIndex, i, ChartDataLabelTextPlanner.GetCategory(categories, i), i, 0, 0);
                        }
                        else if (chart.BlankDisplayMode == ChartBlankDisplayMode.Gap)
                        {
                            series.Points.Add(new DataPoint(i, double.NaN));
                        }
                    }
                }
                if (firstSeriesPoints is null)
                    firstSeriesPoints = trendPoints;
                model.Series.Add(series);
            }
            else if (chart.Type == ChartType.Scatter)
            {
                if (!model.Axes.Any())
                {
                    model.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = chart.XAxisTitle });
                    model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = chart.YAxisTitle });
                    AddSecondaryAxisIfRequested(model, chart);
                }

                var series = new ScatterSeries
                {
                    Title = seriesName,
                    MarkerType = MarkerType.Circle,
                    LabelFormatString = ChartDataLabelFormatter.GetNativeValueLabelFormat(chart, 1),
                    LabelMargin = ToLabelMargin(chart.DataLabelPosition),
                    YAxisKey = UsesSecondaryAxis(chart, seriesIndex) ? SecondaryYAxisKey : null
                };
                ApplyScatterFormat(series, GetSeriesFormat(chart, seriesIndex), theme);
                ApplyNativeDataLabelStyle(series, chart, theme);
                var xCol = chart.FirstColIsCategories ? startCol : dataStartCol;
                var trendPoints = firstSeriesPoints is null ? new List<DataPoint>() : null;
                for (uint r = dataStartRow; r <= endRow; r++)
                {
                    if (!cellLookup.TryGetValue((r, xCol), out var xCell) ||
                        !TryGetChartNumericValue(xCell, out var x))
                        x = r - dataStartRow;

                    if (cellLookup.TryGetValue((r, col), out var yCell)
                        && TryGetChartNumericValue(yCell, out var y))
                    {
                        series.Points.Add(new ScatterPoint(x, y));
                        trendPoints?.Add(new DataPoint(x, y));
                        if (ShouldUseAnnotationLabels(chart))
                            AddDataLabelAnnotation(model, chart, theme, pointDataLabelFormats, seriesName, seriesIndex, (int)(r - dataStartRow), ChartDataLabelTextPlanner.GetCategory(categories, (int)(r - dataStartRow)), x, y, y);
                    }
                }
                if (firstSeriesPoints is null)
                    firstSeriesPoints = trendPoints;
                model.Series.Add(series);
            }
            else // Line / 3D Line
            {
                if (!model.Axes.Any())
                {
                    model.Axes.Add(CreateZeroBasedIndexedCategoryAxis(AxisPosition.Bottom, chart.XAxisTitle, categories));
                    model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = chart.YAxisTitle });
                    AddSecondaryAxisIfRequested(model, chart);
                }

                var series = CreateLineSeries(chart, seriesName, seriesIndex, theme);
                var trendPoints = firstSeriesPoints is null ? new List<DataPoint>() : null;
                AddLinePoints(series, chart, cellLookup, dataStartRow, endRow, col, trendPoints, out trendPoints);
                if (firstSeriesPoints is null)
                    firstSeriesPoints = trendPoints;
                AddLineDataLabelAnnotations(model, chart, theme, pointDataLabelFormats, series, seriesName, seriesIndex, categories);
                model.Series.Add(series);
            }
        }

        AddDeviationOverlay(model, chart, theme, clusteredBarValues);
        AddRangeDataLabelAnnotations(model, chart, theme, clusteredBarValues, categories);
        AddTrendlineIfRequested(model, chart, theme, firstSeriesPoints, swapTrendlineAxes: chart.Type == ChartType.Bar);
        ApplyAxisBounds(model, chart, theme);
        AddChartDataTableAnnotations(model, chart, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow);

        // If the live-cell lookup produced no data for any series and embedded cache data is
        // available (e.g. cross-sheet refs whose data sheet cells are not in the viewport),
        // fall back to the embedded numCache/strCache values so the chart renders rather than
        // appearing blank.  Charts that DO have live data continue to use live cells.
        if (chart.EmbeddedSeriesData is { Count: > 0 } && AllSeriesEmpty(model))
            return BuildPlotModelFromEmbeddedData(chart, theme) ?? model;

        return model;
    }

    /// <summary>
    /// Returns true when every series in the model has zero data points (items/points).
    /// Used to decide whether to fall back to embedded numCache data.
    /// </summary>
    private static bool AllSeriesEmpty(PlotModel model)
    {
        if (model.Series.Count == 0)
            return true;

        foreach (var series in model.Series)
        {
            if (series is RectangleBarSeries rbs && rbs.Items.Count > 0)
                return false;
            if (series is BarSeries bs && bs.Items.Count > 0)
                return false;
            if (series is LineSeries ls && ls.Points.Count > 0)
                return false;
            if (series is AreaSeries als && als.Points.Count > 0)
                return false;
            if (series is ScatterSeries ss && ss.Points.Count > 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Renders a Column or Bar chart entirely from <see cref="ChartModel.EmbeddedSeriesData"/>,
    /// bypassing the cell lookup.  Used when the series data formulas are unresolvable named
    /// ranges (e.g. OFFSET-based dynamic names like <c>'Sheet1'!rngCount</c>) but the chart XML
    /// carries embedded <c>&lt;c:numCache&gt;</c> / <c>&lt;c:strCache&gt;</c> values.
    /// </summary>
    private static PlotModel? BuildPlotModelFromEmbeddedData(ChartModel chart, WorkbookTheme theme)
    {
        var embeddedData = chart.EmbeddedSeriesData!;
        var categories = embeddedData.Count > 0 ? embeddedData[0].Categories.ToList() : new List<string>();

        var model = new PlotModel { Title = chart.Title };
        model.DefaultColors = BuildExcelSeriesPalette(theme);
        ApplyTitleStyle(model, chart, theme);
        ApplyAreaStyle(model, chart, theme);
        ConfigureLegend(model, chart, theme);
        AddPivotChartFieldButtons(model, chart);
        var pointDataLabelFormats = ShouldUseAnnotationLabels(chart)
            ? new ChartPointDataLabelFormatLookup(chart.PointDataLabelFormats)
            : default;

        if (chart.Type is ChartType.Column or ChartType.ThreeDColumn)
        {
            // When there are no category labels the effective count comes from the value series
            // so the x-axis spans all data points (same fix as the live-cell path).
            var maxValueCount = embeddedData.Count > 0 ? embeddedData.Max(s => s.Values.Count) : 0;
            var categoryOrValueCount = categories.Count > 0 ? categories.Count : maxValueCount;
            model.Axes.Add(CreateCenteredIndexedCategoryAxis(AxisPosition.Bottom, chart.XAxisTitle, categories, categoryOrValueCount));
            model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = chart.YAxisTitle });

            foreach (var seriesData in embeddedData)
            {
                var seriesName = seriesData.SeriesName ?? $"Series {seriesData.SeriesIndex + 1}";
                var series = new RectangleBarSeries
                {
                    Title = IsLegendEntryDeleted(chart, seriesData.SeriesIndex) ? "" : seriesName,
                    LabelFormatString = ChartDataLabelFormatter.GetNativeValueLabelFormat(chart, 4)
                };
                ApplyRectangleBarFormat(series, GetSeriesFormat(chart, seriesData.SeriesIndex), theme);
                ApplyNativeDataLabelStyle(series, chart, theme);
                var colHalfWidth = ColumnBarHalfWidth(chart);
                for (var i = 0; i < seriesData.Values.Count; i++)
                {
                    var v = seriesData.Values[i];
                    if (v.HasValue)
                    {
                        series.Items.Add(new RectangleBarItem(i - colHalfWidth, Math.Min(0, v.Value), i + colHalfWidth, Math.Max(0, v.Value)));
                        if (ShouldUseAnnotationLabels(chart))
                            AddDataLabelAnnotation(model, chart, theme, pointDataLabelFormats, seriesName, seriesData.SeriesIndex, i, ChartDataLabelTextPlanner.GetCategory(categories, i), i, v.Value, v.Value);
                    }
                }
                model.Series.Add(series);
            }
        }
        else if (chart.Type is ChartType.Bar or ChartType.ThreeDBar)
        {
            model.Axes.Add(CreateCategoryAxis(AxisPosition.Left, chart.YAxisTitle, categories));
            model.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = chart.XAxisTitle });

            foreach (var seriesData in embeddedData)
            {
                var seriesName = seriesData.SeriesName ?? $"Series {seriesData.SeriesIndex + 1}";
                var series = new BarSeries
                {
                    Title = IsLegendEntryDeleted(chart, seriesData.SeriesIndex) ? "" : seriesName,
                    LabelFormatString = ChartDataLabelFormatter.GetNativeValueLabelFormat(chart, 0),
                    LabelPlacement = ToOxyLabelPlacement(chart.DataLabelPosition)
                };
                ApplyBarFormat(series, GetSeriesFormat(chart, seriesData.SeriesIndex), theme);
                ApplyNativeDataLabelStyle(series, chart, theme);
                for (var i = 0; i < seriesData.Values.Count; i++)
                {
                    var v = seriesData.Values[i];
                    if (v.HasValue)
                    {
                        series.Items.Add(new BarItem { Value = v.Value });
                        if (ShouldUseAnnotationLabels(chart))
                            AddDataLabelAnnotation(model, chart, theme, pointDataLabelFormats, seriesName, seriesData.SeriesIndex, i, ChartDataLabelTextPlanner.GetCategory(categories, i), v.Value, i, v.Value);
                    }
                }
                model.Series.Add(series);
            }
        }
        else
        {
            // Unsupported type for embedded data path — fall through to null (empty render)
            return null;
        }

        ApplyAxisBounds(model, chart, theme);
        return model;
    }

    private static Dictionary<(uint Row, uint Col), DisplayCell> BuildChartCellLookup(ChartModel chart, ViewportModel viewport)
    {
        var capacity = GetChartCellLookupCapacity(chart.DataRange, viewport);
        var lookup = new Dictionary<(uint Row, uint Col), DisplayCell>(capacity);
        if (viewport.ChartDataCells is { Count: > 0 })
        {
            var sheetId = chart.DataRange.Start.Sheet;
            foreach (var cell in viewport.ChartDataCells)
            {
                if (cell.SheetId != sheetId)
                    continue;
                if (!IsInChartDataRange(cell.Row, cell.Col, chart.DataRange))
                    continue;

                lookup[(cell.Row, cell.Col)] = new DisplayCell(
                    cell.Row,
                    cell.Col,
                    cell.RawValue,
                    cell.DisplayText,
                    null,
                    StyleId.Default,
                    null);
            }
        }

        foreach (var cell in viewport.Cells)
        {
            if (!IsInChartDataRange(cell.Row, cell.Col, chart.DataRange))
                continue;

            lookup.TryAdd((cell.Row, cell.Col), cell);
        }

        return lookup;
    }

    private static int GetChartCellLookupCapacity(GridRange dataRange, ViewportModel viewport)
    {
        var dataRangeCells = GetChartDataRangeCellCount(dataRange);
        var visibleCapacity = dataRangeCells > int.MaxValue
            ? viewport.Cells.Count
            : Math.Min(viewport.Cells.Count, (int)dataRangeCells);
        var chartDataCapacity = dataRangeCells > int.MaxValue
            ? viewport.ChartDataCells?.Count ?? 0
            : Math.Min(viewport.ChartDataCells?.Count ?? 0, (int)dataRangeCells);
        return SaturatingAdd(visibleCapacity, chartDataCapacity);
    }

    private static ulong GetChartDataRangeCellCount(GridRange dataRange)
    {
        if (dataRange.End.Row < dataRange.Start.Row || dataRange.End.Col < dataRange.Start.Col)
            return 0;

        return ((ulong)dataRange.End.Row - dataRange.Start.Row + 1) *
               ((ulong)dataRange.End.Col - dataRange.Start.Col + 1);
    }

    private static int SaturatingAdd(int left, int right)
    {
        var sum = (long)left + right;
        return sum > int.MaxValue ? int.MaxValue : (int)sum;
    }

    private static bool IsInChartDataRange(uint row, uint column, GridRange dataRange) =>
        row >= dataRange.Start.Row &&
        row <= dataRange.End.Row &&
        column >= dataRange.Start.Col &&
        column <= dataRange.End.Col;

    private static int GetDataPointCapacity(uint dataStartRow, uint endRow)
    {
        if (endRow < dataStartRow)
            return 0;

        var count = endRow - dataStartRow + 1;
        return count > int.MaxValue ? int.MaxValue : (int)count;
    }

    /// <summary>
    /// Formats a category-axis label. When the chart's category axis carries an explicit number
    /// format (e.g. a date axis with <c>[$-409]d\-mmm;@</c>) and the underlying cell holds a numeric
    /// or date value, the value is formatted through that code so date-serial categories render as
    /// Excel shows them (1-Jan) instead of the raw serial (44562). Otherwise the cell's own display
    /// text is used unchanged.
    /// </summary>
    private static string FormatCategoryLabel(ChartModel chart, DisplayCell cell)
    {
        var formatCode = chart.XAxisNumberFormatCode;
        if (string.IsNullOrWhiteSpace(formatCode) ||
            formatCode.Equals("General", StringComparison.OrdinalIgnoreCase))
        {
            return cell.DisplayText;
        }

        ScalarValue? numericValue = cell.RawValue switch
        {
            NumberValue or DateTimeValue => cell.RawValue,
            _ => null
        };
        if (numericValue is null)
            return cell.DisplayText;

        try
        {
            var formatted = FreeX.Core.Formula.NumberFormatter.Format(numericValue, formatCode, chart.Uses1904DateSystem);
            return string.IsNullOrEmpty(formatted) ? cell.DisplayText : formatted;
        }
        catch
        {
            return cell.DisplayText;
        }
    }

    private static bool TryGetChartNumericValue(DisplayCell cell, out double value)
    {
        switch (cell.RawValue)
        {
            case NumberValue number:
                value = number.Value;
                return double.IsFinite(value);
            case DateTimeValue dateTime:
                value = dateTime.Value;
                return double.IsFinite(value);
            case BoolValue boolean:
                value = boolean.Value ? 1 : 0;
                return true;
        }

        return double.TryParse(cell.DisplayText, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsChartBlank(DisplayCell cell) =>
        cell.RawValue is null or BlankValue || string.IsNullOrWhiteSpace(cell.DisplayText);

    private static LineSeries CreateLineSeries(ChartModel chart, string title, int seriesIndex, WorkbookTheme theme)
    {
        var series = new LineSeries
        {
            Title = IsLegendEntryDeleted(chart, seriesIndex) ? "" : title,
            LabelFormatString = ChartDataLabelFormatter.GetNativeValueLabelFormat(chart, 1),
            LabelMargin = ToLabelMargin(chart.DataLabelPosition),
            YAxisKey = UsesSecondaryAxis(chart, seriesIndex) ? SecondaryYAxisKey : null
        };
        ApplyLineFormat(series, GetSeriesFormat(chart, seriesIndex), theme);
        ApplyNativeDataLabelStyle(series, chart, theme);
        return series;
    }

    /// <summary>
    /// Builds an Excel-matching series color palette from the workbook theme's Accent1–Accent6 colors.
    /// Round 0 = base accent colors, subsequent rounds apply luminance tints to extend past 6 series.
    /// </summary>
    private static IList<OxyColor> BuildExcelSeriesPalette(WorkbookTheme theme)
    {
        var colors = ChartStylePlanner.BuildExcelSeriesPalette(theme);
        var palette = new List<OxyColor>(colors.Length);
        foreach (var color in colors)
        {
            palette.Add(OxyColor.FromRgb(color.R, color.G, color.B));
        }

        return palette;
    }

}
