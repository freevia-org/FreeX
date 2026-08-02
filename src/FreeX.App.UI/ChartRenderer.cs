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

    /// <summary>
    /// True when the pie/doughnut slice at <paramref name="sliceIndex"/> (series 0 -- the only
    /// series a pie ever plots) should render exploded. Honors BOTH the legacy scalar
    /// <see cref="ChartModel.ExplodedSliceIndex"/> (single-slice explosion) AND every entry in
    /// <see cref="ChartModel.ExplodedSlices"/> (per-point <c>&lt;c:dPt&gt;/&lt;c:explosion&gt;</c>
    /// overrides), so a chart where several slices are individually exploded renders ALL of them
    /// exploded rather than collapsing to just the first.
    /// </summary>
    private static bool IsPieSliceExploded(ChartModel chart, int sliceIndex) =>
        chart.ExplodedSliceIndex == sliceIndex ||
        chart.ExplodedSlices.Any(slice => slice.SeriesIndex == 0 && slice.PointIndex == sliceIndex);

    private static PlotModel? BuildPlotModel(ChartModel chart, ViewportModel viewport, WorkbookTheme theme)
    {
        if (!ChartTypeSupport.IsRenderable(chart.Type))
            return null;

        var cellLookup = BuildChartCellLookup(chart, viewport);

        uint startRow = chart.DataRange.Start.Row;
        uint endRow   = chart.DataRange.End.Row;
        uint startCol = chart.DataRange.Start.Col;
        uint endCol   = chart.DataRange.End.Col;
        if (chart.SeriesInRows)
            (cellLookup, endRow, endCol) = TransposeChartCellLookup(cellLookup, startRow, startCol, endRow, endCol);

        uint dataStartRow = chart.FirstRowIsHeader ? startRow + 1 : startRow;
        uint dataStartCol = chart.FirstColIsCategories ? startCol + 1 : startCol;

        List<string>? embeddedCategories = null;
        // R113-render-chart-embedded-fallback-all-types: r110-r112 taught the *readers* to fall back
        // to a series' embedded <c:numCache>/<c:strCache> (or chartEx <cx:lvl>/<cx:pt>) cache when its
        // formula is an unresolvable named range or an unreachable cross-sheet reference, and r112
        // fixed ChartTypeSupport's series/point counts to consult that cache too -- but this renderer
        // still iterated the (now correctly empty) live cellLookup directly for every chart type
        // except Column/Bar (see the old BuildPlotModelFromEmbeddedData, which implemented only
        // those two), so every other type still drew blank. Rather than adding a type-specific
        // "if embedded, do X" branch to each of the ~20 chart-type code paths below (exactly the
        // pattern that let this bug hide for three rounds), synthesize an ordinary cellLookup +
        // row/column bounds from the embedded data ONCE here, before any type-specific code runs.
        // Every branch after this point -- the inline Column/Bar/Area/Line/Scatter loop, the
        // Pie/Doughnut block, and every extracted BuildXxxModel helper (Stacked*/Radar/Stock/
        // Surface/Waterfall/Histogram/Pareto/BoxAndWhisker/Treemap/Sunburst/Funnel/Bubble) --
        // consumes cellLookup/dataStartRow/endRow/dataStartCol/endCol/startRow exactly the same way
        // whether the cells are live or synthesized from embedded cache data, so this substitution
        // point is the ONLY embedded-data-aware code the renderer needs. cellLookup.Count == 0 is a
        // safe "no live data at all" test because BuildChartCellLookup already filtered every cell to
        // chart.DataRange -- an empty result means literally nothing in the viewport fell inside it.
        if (cellLookup.Count == 0 && chart.EmbeddedSeriesData is { Count: > 0 })
        {
            (cellLookup, embeddedCategories, startRow, dataStartRow, endRow, startCol, dataStartCol, endCol) = BuildEmbeddedCellLookup(chart);
        }

        var dataPointCapacity = GetDataPointCapacity(dataStartRow, endRow);
        var categories = embeddedCategories ?? new List<string>(chart.FirstColIsCategories ? dataPointCapacity : 0);
        if (embeddedCategories is null && chart.FirstColIsCategories)
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
                    IsExploded = IsPieSliceExploded(chart, sliceIndex)
                };
                // Per-point fill (from <c:dPt> in chart XML) takes highest priority;
                // fall back to series-level fill, then palette.
                var pointFill = GetPointFillColor(chart, 0, sliceIndex, theme);
                if (pointFill is { } perPointFill)
                    slice.Fill = OxyColor.FromRgb(perPointFill.R, perPointFill.G, perPointFill.B);
                else if (pieFormat?.ResolveFillColor(theme) is { } fill)
                    slice.Fill = ApplyFillAlpha(OxyColor.FromRgb(fill.R, fill.G, fill.B), pieFormat?.FillAlpha);
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
            AddStackedSeriesLines(stackedColumnModel, chart, theme, isBar: false);
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
            AddStackedSeriesLines(stackedBarModel, chart, theme, isBar: true);
            ApplyAxisBounds(stackedBarModel, chart, theme);
            AddChartDataTableAnnotations(stackedBarModel, chart, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow);
            return stackedBarModel;
        }

        if (chart.Type is ChartType.StackedArea or ChartType.PercentStackedArea)
        {
            var stackedAreaModel = BuildStackedAreaModel(chart, model, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow, chart.Type == ChartType.PercentStackedArea, theme, pointDataLabelFormats);
            ApplyAxisBounds(stackedAreaModel, chart, theme);
            AddChartDataTableAnnotations(stackedAreaModel, chart, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow);
            return stackedAreaModel;
        }

        if (chart.Type == ChartType.Bubble)
        {
            var bubbleModel = BuildBubbleModel(chart, model, cellLookup, categories, dataStartRow, endRow, startCol, endCol, startRow, theme, pointDataLabelFormats, out var trendPoints);
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
        // Same count, computed for a plain (non-stacked) Bar/ThreeDBar chart so "Vary colors by
        // point" below can tell whether this is the chart's sole plotted series (Excel's only
        // varyColors shape for bar/column charts — see ChartStylePlanner.ResolveVaryColorsPointFill).
        var barChartSeriesCount = chart.Type is ChartType.Bar or ChartType.ThreeDBar
            ? CountClusteredBarSeries(chart, dataStartCol, endCol)
            : 0;
        var clusteredColumnOrdinal = 0;
        // Per-clustered-bar-series value lists (category index -> value), captured so a
        // Budget-vs-Actual <c:upDownBars> deviation overlay can be drawn between the first two
        // bar series after the main loop. Only collected for clustered column charts.
        var clusteredBarValues = new List<List<double?>>();
        // Lazily built only when a single-series Column/Bar chart actually needs to resolve
        // "Vary colors by point" (ChartStylePlanner.ResolveVaryColorsPointFill below).
        CellColor[]? varyColorsPalette = null;
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
                var seriesFormat = GetSeriesFormat(chart, seriesIndex);
                ApplyRectangleBarFormat(series, seriesFormat, theme);
                ApplyNativeDataLabelStyle(series, chart, theme);
                var trendPoints = firstSeriesPoints is null ? new List<DataPoint>() : null;
                var colHalfWidth = ColumnBarHalfWidth(chart);
                var (clusterLeft, clusterRight) = ClusteredBarOffsets(colHalfWidth, clusteredColumnOrdinal, clusteredColumnCount, EffectiveBarOverlap(chart));
                clusteredColumnOrdinal++;
                var barCategoryValues = new List<double?>();
                var i = 0;
                for (uint r = dataStartRow; r <= endRow; r++, i++)
                {
                    if (cellLookup.TryGetValue((r, col), out var cell)
                        && TryGetChartNumericValue(cell, out var v))
                    {
                        var columnBarItem = new RectangleBarItem(i + clusterLeft, Math.Min(0, v), i + clusterRight, Math.Max(0, v));
                        // "Vary colors by point" (c:varyColors) only applies when this is the
                        // chart's sole plotted series — matching Excel, which otherwise needs one
                        // color per series for the legend to make sense.
                        if (chart.VaryColorsByPoint == true &&
                            ChartStylePlanner.ResolveVaryColorsPointFill(chart, seriesIndex, i, clusteredColumnCount, theme, varyColorsPalette ??= ChartStylePlanner.BuildExcelSeriesPalette(theme)) is { } varyColor)
                        {
                            columnBarItem.Color = OxyColor.FromRgb(varyColor.R, varyColor.G, varyColor.B);
                        }
                        // R91-render-chart-series-format-5-2: "Invert if negative" takes precedence
                        // over vary-by-point for this bar -- it is Excel's stronger, value-specific
                        // visual cue for a negative point, distinct from the per-point palette color.
                        if (ResolveInvertIfNegativeItemColor(seriesFormat, v) is { } invertColor)
                        {
                            columnBarItem.Color = invertColor;
                        }
                        series.Items.Add(columnBarItem);
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
                        var barItem = new BarItem { Value = v };
                        if (chart.VaryColorsByPoint == true &&
                            ChartStylePlanner.ResolveVaryColorsPointFill(chart, seriesIndex, i, barChartSeriesCount, theme, varyColorsPalette ??= ChartStylePlanner.BuildExcelSeriesPalette(theme)) is { } varyColor)
                        {
                            barItem.Color = OxyColor.FromRgb(varyColor.R, varyColor.G, varyColor.B);
                        }
                        series.Items.Add(barItem);
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

        return model;
    }

    /// <summary>
    /// Resolves the effective Series Overlap percentage for a clustered column/bar chart,
    /// mirroring Excel's own native default: when the chart XML has no explicit
    /// <c>&lt;c:overlap&gt;</c> (<see cref="ChartModel.BarOverlap"/> is null), real Excel still
    /// draws clustered 2-D bar/column charts with overlap=-27 (a small gap between bars in the
    /// same cluster) -- see <c>XlsxChartPartReader.Bar.cs</c>'s NormalizeExcelNativeDefaultBarOverlap,
    /// which maps a written -27 back to null on read so a default chart round-trips cleanly.
    /// Falling back to a literal 0 here (edge-to-edge bars) would silently diverge from Excel's
    /// rendering for the overwhelming majority of real-world clustered charts, and from the
    /// equivalent default applied by the shared Avalonia ChartLayoutEngine, so the null case must
    /// resolve to -27 for the same chart-type family the writer/reader normalize, and to 0 for
    /// 3-D bar/column (which Excel does not apply the -27 default to).
    /// </summary>
    private static int EffectiveBarOverlap(ChartModel chart) =>
        chart.BarOverlap ?? (chart.Type is ChartType.Column
            or ChartType.Bar
            or ChartType.StackedColumn
            or ChartType.PercentStackedColumn
            or ChartType.StackedBar
            or ChartType.PercentStackedBar
                ? -27
                : 0);

    /// <summary>
    /// R113-render-chart-embedded-fallback-all-types: builds a synthetic cellLookup (plus matching
    /// row/column bounds and categories) from <see cref="ChartModel.EmbeddedSeriesData"/> so every
    /// chart-type branch in <see cref="BuildPlotModel"/> can render a fallback-loaded chart through
    /// EXACTLY the same code that renders a live cell-range-backed one -- see the call site's
    /// comment for why this single substitution point replaces the old per-type-only
    /// BuildPlotModelFromEmbeddedData special case (which implemented just Column/Bar).
    /// <para>
    /// Layout: row 1 holds each series' cached name (read only when <see cref="ChartModel.FirstRowIsHeader"/>
    /// is set, exactly like a live chart's header row); rows 2.. hold each series' cached values, one
    /// column per series. Scatter and Bubble reserve column 1 for a shared X column built from the
    /// FIRST series' cached X values (<see cref="ChartEmbeddedSeriesData.Categories"/> holds the
    /// &lt;c:xVal&gt; numCache, formatted as a string, for those two chart types -- see
    /// <c>XlsxChartPartReader.Scatter.cs</c>/<c>PieBubble.cs</c>, which override
    /// categoryContainerName to "xVal" when reading their embedded data). This mirrors the live-cell
    /// renderer's own assumption of one shared X column feeding every Y series
    /// (<see cref="ShouldSkipScatterXColumn"/>; BuildBubbleModel reads its X column from the
    /// <c>StartCol</c> this method returns, which -- unlike a live chart's <c>startCol</c> --
    /// always matches <c>DataStartCol</c> here since both are the synthesized column 1). Bubble
    /// additionally leaves an empty column after
    /// each Y column for the (uncached) bubble-size series -- <see cref="ChartEmbeddedSeriesData"/>
    /// never carries a bubbleSize cache at all, so those bubbles fall back to BuildBubbleModel's own
    /// existing default (uniform size) rather than being lost entirely.
    /// </para>
    /// </summary>
    private static (
        Dictionary<(uint Row, uint Col), DisplayCell> Lookup,
        List<string> Categories,
        uint StartRow,
        uint DataStartRow,
        uint EndRow,
        uint StartCol,
        uint DataStartCol,
        uint EndCol) BuildEmbeddedCellLookup(ChartModel chart)
    {
        var embeddedData = chart.EmbeddedSeriesData!;
        const uint headerRow = 1;
        const uint dataStartRow = 2;

        var categories = embeddedData.Count > 0 ? embeddedData[0].Categories.ToList() : new List<string>();

        var maxPoints = 0;
        foreach (var series in embeddedData)
            maxPoints = Math.Max(maxPoints, Math.Max(series.Values.Count, series.Categories.Count));
        var endRow = maxPoints > 0 ? dataStartRow + (uint)maxPoints - 1 : dataStartRow;

        var isXyChart = chart.Type is ChartType.Scatter or ChartType.Bubble;
        var isBubble = chart.Type == ChartType.Bubble;
        var lookup = new Dictionary<(uint Row, uint Col), DisplayCell>();

        if (isXyChart && embeddedData.Count > 0)
        {
            // Shared X column at col 1, populated from the FIRST series' cached X values (every
            // Scatter/Bubble series in a chart normally shares one X range; a chart whose series
            // genuinely disagree on X is the one case this fallback does not reproduce exactly).
            var xSource = embeddedData[0].Categories;
            for (var p = 0; p < xSource.Count; p++)
            {
                if (!double.TryParse(xSource[p], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
                    continue;
                var row = dataStartRow + (uint)p;
                lookup[(row, 1u)] = new DisplayCell(row, 1u, new NumberValue(x), xSource[p], null, StyleId.Default, null);
            }
        }

        const uint dataStartCol = 1; // Scatter/Bubble: col 1 is the shared X column (skipped as a series); others: col 1 is series 0.
        var seriesColStride = isBubble ? 2u : 1u;
        var firstSeriesCol = isXyChart ? 2u : 1u;

        for (var i = 0; i < embeddedData.Count; i++)
        {
            var col = firstSeriesCol + (uint)i * seriesColStride;
            var series = embeddedData[i];
            if (!string.IsNullOrEmpty(series.SeriesName))
                lookup[(headerRow, col)] = new DisplayCell(headerRow, col, null, series.SeriesName, null, StyleId.Default, null);

            for (var p = 0; p < series.Values.Count; p++)
            {
                if (series.Values[p] is not { } value)
                    continue;
                var row = dataStartRow + (uint)p;
                lookup[(row, col)] = new DisplayCell(row, col, new NumberValue(value), value.ToString(CultureInfo.InvariantCulture), null, StyleId.Default, null);
            }

            // R117-io-chart-embedded-bubble-size-1: populate the trailing size column (col + 1)
            // from the series' own cached <c:bubbleSize> numCache when the reader captured one, so
            // BuildBubbleModel's sizeCol lookup finds the real per-point size instead of always
            // defaulting to 1 (uniform bubbles). Falls through to the pre-existing empty-column
            // behavior when SizeValues is null (every non-Bubble chart type, or a Bubble chart whose
            // source XML genuinely had no bubbleSize cache).
            if (isBubble && series.SizeValues is { } sizeValues)
            {
                var sizeCol = col + 1;
                for (var p = 0; p < sizeValues.Count; p++)
                {
                    if (sizeValues[p] is not { } size)
                        continue;
                    var row = dataStartRow + (uint)p;
                    lookup[(row, sizeCol)] = new DisplayCell(row, sizeCol, new NumberValue(size), size.ToString(CultureInfo.InvariantCulture), null, StyleId.Default, null);
                }
            }
        }

        var lastSeriesCol = embeddedData.Count > 0 ? firstSeriesCol + (uint)(embeddedData.Count - 1) * seriesColStride : firstSeriesCol;
        // Bubble reserves one trailing column for the size series -- populated above from
        // SizeValues when the reader captured a bubbleSize cache, otherwise left empty (uncached).
        var endCol = isBubble ? lastSeriesCol + 1 : lastSeriesCol;

        // StartCol mirrors DataStartCol here (both are the synthesized column 1) so that any
        // caller which -- like BuildBubbleModel -- deliberately reads the *unshifted* start
        // column (ignoring FirstColIsCategories) still lands on the column this method actually
        // populated, instead of the stale live chart.DataRange.Start.Col the substitution never
        // rewrites.
        return (lookup, categories, headerRow, dataStartRow, endRow, dataStartCol, dataStartCol, endCol);
    }

    /// <summary>
    /// Re-keys a chart cell lookup into transposed (virtual) coordinates for Excel's
    /// "Switch Row/Column" (<see cref="ChartModel.SeriesInRows"/>): virtual (row, col) = actual
    /// (startRow + (col - startCol), startCol + (row - startRow)). The start corner is shared, so
    /// only the end extents swap; the series extraction below then reads each ROW of the actual
    /// range as one series (names from the first column, categories from the first row) without
    /// any per-chart-type changes.
    /// </summary>
    private static (Dictionary<(uint Row, uint Col), DisplayCell> Lookup, uint EndRow, uint EndCol) TransposeChartCellLookup(
        Dictionary<(uint Row, uint Col), DisplayCell> lookup,
        uint startRow,
        uint startCol,
        uint endRow,
        uint endCol)
    {
        var transposed = new Dictionary<(uint Row, uint Col), DisplayCell>(lookup.Count);
        foreach (var entry in lookup)
            transposed[(startRow + (entry.Key.Col - startCol), startCol + (entry.Key.Row - startRow))] = entry.Value;
        return (transposed, startRow + (endCol - startCol), startCol + (endRow - startRow));
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
