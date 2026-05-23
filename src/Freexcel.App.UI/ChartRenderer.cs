using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using OxyPlot.Wpf;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

/// <summary>Renders a ChartModel into a WPF ImageSource for use in DrawingContext.</summary>
public static partial class ChartRenderer
{
    private const string SecondaryYAxisKey = "SecondaryY";
    private static readonly OxyColor[] PieSlicePalette =
    [
        OxyColor.FromRgb(68, 114, 196),
        OxyColor.FromRgb(237, 125, 49),
        OxyColor.FromRgb(165, 165, 165),
        OxyColor.FromRgb(255, 192, 0),
        OxyColor.FromRgb(91, 155, 213),
        OxyColor.FromRgb(112, 173, 71)
    ];

    public static ImageSource? Render(ChartModel chart, ViewportModel viewport) =>
        Render(chart, viewport, WorkbookTheme.Office);

    public static ImageSource? Render(ChartModel chart, ViewportModel viewport, WorkbookTheme? theme)
    {
        var model = BuildPlotModel(chart, viewport, theme ?? WorkbookTheme.Office);
        if (model == null) return null;

        var exporter = new PngExporter
        {
            Width  = (int)chart.Width,
            Height = (int)chart.Height,
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
        return bitmap;
    }

    private static PlotModel? BuildPlotModel(ChartModel chart, ViewportModel viewport) =>
        BuildPlotModel(chart, viewport, WorkbookTheme.Office);

    private static PlotModel? BuildPlotModel(ChartModel chart, ViewportModel viewport, WorkbookTheme theme)
    {
        if (!ChartTypeSupport.IsRenderable(chart.Type))
            return null;

        var cellLookup = viewport.Cells.ToDictionary(c => (c.Row, c.Col));

        uint startRow = chart.DataRange.Start.Row;
        uint endRow   = chart.DataRange.End.Row;
        uint startCol = chart.DataRange.Start.Col;
        uint endCol   = chart.DataRange.End.Col;

        uint dataStartRow = chart.FirstRowIsHeader ? startRow + 1 : startRow;
        uint dataStartCol = chart.FirstColIsCategories ? startCol + 1 : startCol;

        var categories = new List<string>();
        if (chart.FirstColIsCategories)
            for (uint r = dataStartRow; r <= endRow; r++)
                categories.Add(cellLookup.TryGetValue((r, startCol), out var c) ? c.DisplayText : "");

        var model = new PlotModel { Title = chart.Title };
        ApplyTitleStyle(model, chart);
        ApplyAreaStyle(model, chart, theme);
        ConfigureLegend(model, chart, theme);
        AddPivotChartFieldButtons(model, chart);

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
                AreInsideLabelsAngled = Math.Abs(chart.DataLabelAngle) > 0.5,
                InsideLabelFormat = chart.ShowDataLabels && chart.DataLabelPosition != ChartDataLabelPosition.OutsideEnd
                    ? ChartDataLabelFormatter.GetPieLabelFormat(chart, pieSeriesName)
                    : "",
                OutsideLabelFormat = chart.ShowDataLabels && chart.DataLabelPosition == ChartDataLabelPosition.OutsideEnd
                    ? ChartDataLabelFormatter.GetPieLabelFormat(chart, pieSeriesName)
                    : ""
            };
            var pieFormat = GetSeriesFormat(chart, 0);
            ApplyPieFormat(pieSeries, pieFormat, theme);
            ApplyPieDataLabelStyle(pieSeries, chart, theme);
            for (uint r = dataStartRow; r <= endRow; r++)
            {
                if (!cellLookup.TryGetValue((r, dataStartCol), out var cell)) continue;
                if (!double.TryParse(cell.DisplayText, out var v)) continue;
                var label = categories.Count > (int)(r - dataStartRow) ? categories[(int)(r - dataStartRow)] : "";
                var sliceIndex = pieSeries.Slices.Count;
                var slice = new PieSlice(label, v)
                {
                    IsExploded = chart.ExplodedSliceIndex == sliceIndex
                };
                if (pieFormat?.ResolveFillColor(theme) is { } fill)
                    slice.Fill = OxyColor.FromRgb(fill.R, fill.G, fill.B);
                else
                    slice.Fill = PieSlicePalette[sliceIndex % PieSlicePalette.Length];
                pieSeries.Slices.Add(slice);
            }
            model.Series.Add(pieSeries);
            return model;
        }

        if (chart.Type is ChartType.StackedColumn or ChartType.PercentStackedColumn)
        {
            var stackedColumnModel = BuildStackedColumnModel(chart, model, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow, chart.Type == ChartType.PercentStackedColumn, theme);
            ApplyAxisBounds(stackedColumnModel, chart);
            AddChartDataTableAnnotations(stackedColumnModel, chart, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow);
            return stackedColumnModel;
        }

        if (chart.Type is ChartType.StackedBar or ChartType.PercentStackedBar)
        {
            var stackedBarModel = BuildStackedBarModel(chart, model, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow, chart.Type == ChartType.PercentStackedBar, theme);
            ApplyAxisBounds(stackedBarModel, chart);
            AddChartDataTableAnnotations(stackedBarModel, chart, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow);
            return stackedBarModel;
        }

        if (chart.Type == ChartType.Bubble)
        {
            var bubbleModel = BuildBubbleModel(chart, model, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow, theme, out var trendPoints);
            AddTrendlineIfRequested(bubbleModel, chart, theme, trendPoints);
            ApplyAxisBounds(bubbleModel, chart);
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

        // Column / Line: one series per data column
        List<DataPoint>? firstSeriesPoints = null;
        for (uint col = dataStartCol; col <= endCol; col++)
        {
            if (ShouldSkipScatterXColumn(chart, col, dataStartCol))
                continue;

            var seriesIndex = GetSeriesIndex(chart, col, dataStartCol);
            string seriesName = chart.FirstRowIsHeader && cellLookup.TryGetValue((startRow, col), out var hdr)
                ? hdr.DisplayText : $"Series {seriesIndex + 1}";

            if (chart.Type is ChartType.Column or ChartType.ThreeDColumn)
            {
                if (!model.Axes.Any())
                {
                    model.Axes.Add(new LinearAxis
                    {
                        Position = AxisPosition.Bottom,
                        Title = chart.XAxisTitle,
                        Minimum = -0.5,
                        Maximum = Math.Max(0.5, categories.Count - 0.5),
                        MajorStep = 1,
                        MinorStep = 1,
                        LabelFormatter = value =>
                        {
                            var index = (int)Math.Round(value);
                            return index >= 0 && index < categories.Count ? categories[index] : "";
                        }
                    });
                    model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = chart.YAxisTitle });
                    AddSecondaryAxisIfRequested(model, chart);
                }

                if (IsComboLineSeries(chart, seriesIndex))
                {
                    var lineSeries = CreateLineSeries(chart, seriesName, seriesIndex, theme);
                    AddLinePoints(lineSeries, chart, cellLookup, dataStartRow, endRow, col, firstSeriesPoints is null ? new List<DataPoint>() : null, out var comboTrendPoints);
                    if (firstSeriesPoints is null)
                        firstSeriesPoints = comboTrendPoints;
                    AddLineDataLabelAnnotations(model, chart, theme, lineSeries, seriesName, seriesIndex, categories);
                    model.Series.Add(lineSeries);
                    continue;
                }

                var series = new RectangleBarSeries
                {
                    Title = seriesName,
                    LabelFormatString = ChartDataLabelFormatter.GetNativeValueLabelFormat(chart, 4),
                    YAxisKey = UsesSecondaryAxis(chart, seriesIndex) ? SecondaryYAxisKey : null
                };
                ApplyRectangleBarFormat(series, GetSeriesFormat(chart, seriesIndex), theme);
                ApplyNativeDataLabelStyle(series, chart, theme);
                var trendPoints = firstSeriesPoints is null ? new List<DataPoint>() : null;
                var i = 0;
                for (uint r = dataStartRow; r <= endRow; r++, i++)
                {
                    if (cellLookup.TryGetValue((r, col), out var cell)
                        && double.TryParse(cell.DisplayText, out var v))
                    {
                        series.Items.Add(new RectangleBarItem(i - 0.35, Math.Min(0, v), i + 0.35, Math.Max(0, v)));
                        trendPoints?.Add(new DataPoint(i, v));
                        if (ShouldUseAnnotationLabels(chart))
                            AddDataLabelAnnotation(model, chart, theme, seriesName, seriesIndex, i, ChartDataLabelFormatter.GetCategory(categories, i), i, v, v);
                    }
                    else if (chart.BlankDisplayMode == ChartBlankDisplayMode.Zero
                        && cellLookup.TryGetValue((r, col), out cell)
                        && string.IsNullOrWhiteSpace(cell.DisplayText))
                    {
                        series.Items.Add(new RectangleBarItem(i - 0.35, 0, i + 0.35, 0));
                        trendPoints?.Add(new DataPoint(i, 0));
                        if (ShouldUseAnnotationLabels(chart))
                            AddDataLabelAnnotation(model, chart, theme, seriesName, seriesIndex, i, ChartDataLabelFormatter.GetCategory(categories, i), i, 0, 0);
                    }
                }
                if (firstSeriesPoints is null)
                    firstSeriesPoints = trendPoints;
                model.Series.Add(series);
            }
            else if (chart.Type is ChartType.Bar or ChartType.ThreeDBar)
            {
                var catAxis = new CategoryAxis { Position = AxisPosition.Left };
                catAxis.Labels.AddRange(categories);
                if (!model.Axes.Any(a => a is CategoryAxis))
                {
                    catAxis.Title = chart.YAxisTitle;
                    model.Axes.Add(catAxis);
                    model.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = chart.XAxisTitle });
                }

                var series = new BarSeries
                {
                    Title = seriesName,
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
                        && double.TryParse(cell.DisplayText, out var v))
                    {
                        series.Items.Add(new BarItem { Value = v });
                        trendPoints?.Add(new DataPoint(i, v));
                        if (ShouldUseAnnotationLabels(chart))
                            AddDataLabelAnnotation(model, chart, theme, seriesName, seriesIndex, i, ChartDataLabelFormatter.GetCategory(categories, i), v, i, v);
                    }
                    else if (chart.BlankDisplayMode == ChartBlankDisplayMode.Zero
                        && cellLookup.TryGetValue((r, col), out cell)
                        && string.IsNullOrWhiteSpace(cell.DisplayText))
                    {
                        series.Items.Add(new BarItem { Value = 0 });
                        trendPoints?.Add(new DataPoint(i, 0));
                        if (ShouldUseAnnotationLabels(chart))
                            AddDataLabelAnnotation(model, chart, theme, seriesName, seriesIndex, i, ChartDataLabelFormatter.GetCategory(categories, i), 0, i, 0);
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
                    var categoryAxis = new LinearAxis
                    {
                        Position = AxisPosition.Bottom,
                        Title = chart.XAxisTitle,
                        Minimum = 0,
                        Maximum = Math.Max(1, categories.Count - 1),
                        MajorStep = 1,
                        MinorStep = 1,
                        LabelFormatter = value =>
                        {
                            var index = (int)Math.Round(value);
                            return index >= 0 && index < categories.Count ? categories[index] : "";
                        }
                    };
                    model.Axes.Add(categoryAxis);
                    model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = chart.YAxisTitle });
                    AddSecondaryAxisIfRequested(model, chart);
                }

                if (IsComboLineSeries(chart, seriesIndex))
                {
                    var lineSeries = CreateLineSeries(chart, seriesName, seriesIndex, theme);
                    AddLinePoints(lineSeries, chart, cellLookup, dataStartRow, endRow, col, firstSeriesPoints is null ? new List<DataPoint>() : null, out var comboTrendPoints);
                    if (firstSeriesPoints is null)
                        firstSeriesPoints = comboTrendPoints;
                    AddLineDataLabelAnnotations(model, chart, theme, lineSeries, seriesName, seriesIndex, categories);
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
                        && double.TryParse(cell.DisplayText, out var v))
                    {
                        series.Points.Add(new DataPoint(i, v));
                        trendPoints?.Add(new DataPoint(i, v));
                        if (ShouldUseAnnotationLabels(chart))
                            AddDataLabelAnnotation(model, chart, theme, seriesName, seriesIndex, i, ChartDataLabelFormatter.GetCategory(categories, i), i, v, v);
                    }
                    else if (cellLookup.TryGetValue((r, col), out cell) && string.IsNullOrWhiteSpace(cell.DisplayText))
                    {
                        if (chart.BlankDisplayMode == ChartBlankDisplayMode.Zero)
                        {
                            series.Points.Add(new DataPoint(i, 0));
                            trendPoints?.Add(new DataPoint(i, 0));
                            if (ShouldUseAnnotationLabels(chart))
                                AddDataLabelAnnotation(model, chart, theme, seriesName, seriesIndex, i, ChartDataLabelFormatter.GetCategory(categories, i), i, 0, 0);
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
                        !double.TryParse(xCell.DisplayText, out var x))
                        x = r - dataStartRow;

                    if (cellLookup.TryGetValue((r, col), out var yCell)
                        && double.TryParse(yCell.DisplayText, out var y))
                    {
                        series.Points.Add(new ScatterPoint(x, y));
                        trendPoints?.Add(new DataPoint(x, y));
                        if (ShouldUseAnnotationLabels(chart))
                            AddDataLabelAnnotation(model, chart, theme, seriesName, seriesIndex, (int)(r - dataStartRow), ChartDataLabelFormatter.GetCategory(categories, (int)(r - dataStartRow)), x, y, y);
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
                    var categoryAxis = new LinearAxis
                    {
                        Position = AxisPosition.Bottom,
                        Title = chart.XAxisTitle,
                        Minimum = 0,
                        Maximum = Math.Max(1, categories.Count - 1),
                        MajorStep = 1,
                        MinorStep = 1,
                        LabelFormatter = value =>
                        {
                            var index = (int)Math.Round(value);
                            return index >= 0 && index < categories.Count ? categories[index] : "";
                        }
                    };
                    model.Axes.Add(categoryAxis);
                    model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = chart.YAxisTitle });
                    AddSecondaryAxisIfRequested(model, chart);
                }

                var series = CreateLineSeries(chart, seriesName, seriesIndex, theme);
                var trendPoints = firstSeriesPoints is null ? new List<DataPoint>() : null;
                AddLinePoints(series, chart, cellLookup, dataStartRow, endRow, col, trendPoints, out trendPoints);
                if (firstSeriesPoints is null)
                    firstSeriesPoints = trendPoints;
                AddLineDataLabelAnnotations(model, chart, theme, series, seriesName, seriesIndex, categories);
                model.Series.Add(series);
            }
        }

        AddTrendlineIfRequested(model, chart, theme, firstSeriesPoints, swapTrendlineAxes: chart.Type == ChartType.Bar);
        ApplyAxisBounds(model, chart);
        AddChartDataTableAnnotations(model, chart, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow);
        return model;
    }

    private static LineSeries CreateLineSeries(ChartModel chart, string title, int seriesIndex, WorkbookTheme theme)
    {
        var series = new LineSeries
        {
            Title = title,
            LabelFormatString = ChartDataLabelFormatter.GetNativeValueLabelFormat(chart, 1),
            LabelMargin = ToLabelMargin(chart.DataLabelPosition),
            YAxisKey = UsesSecondaryAxis(chart, seriesIndex) ? SecondaryYAxisKey : null
        };
        ApplyLineFormat(series, GetSeriesFormat(chart, seriesIndex), theme);
        ApplyNativeDataLabelStyle(series, chart, theme);
        return series;
    }

}
