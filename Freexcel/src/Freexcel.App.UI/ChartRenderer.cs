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
public static class ChartRenderer
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

        if (chart.Type is ChartType.Pie or ChartType.Doughnut)
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
                    ? GetPieLabelFormat(chart, pieSeriesName)
                    : "",
                OutsideLabelFormat = chart.ShowDataLabels && chart.DataLabelPosition == ChartDataLabelPosition.OutsideEnd
                    ? GetPieLabelFormat(chart, pieSeriesName)
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
            return stackedColumnModel;
        }

        if (chart.Type is ChartType.StackedBar or ChartType.PercentStackedBar)
        {
            var stackedBarModel = BuildStackedBarModel(chart, model, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow, chart.Type == ChartType.PercentStackedBar, theme);
            ApplyAxisBounds(stackedBarModel, chart);
            return stackedBarModel;
        }

        if (chart.Type == ChartType.Bubble)
        {
            var bubbleModel = BuildBubbleModel(chart, model, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow, theme, out var trendPoints);
            AddTrendlineIfRequested(bubbleModel, chart, theme, trendPoints);
            ApplyAxisBounds(bubbleModel, chart);
            return bubbleModel;
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

            if (chart.Type == ChartType.Column)
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
                    AddLinePoints(lineSeries, cellLookup, dataStartRow, endRow, col, firstSeriesPoints is null ? new List<DataPoint>() : null, out var comboTrendPoints);
                    if (firstSeriesPoints is null)
                        firstSeriesPoints = comboTrendPoints;
                    AddLineDataLabelAnnotations(model, chart, theme, lineSeries, seriesName, seriesIndex, categories);
                    model.Series.Add(lineSeries);
                    continue;
                }

                var series = new RectangleBarSeries
                {
                    Title = seriesName,
                    LabelFormatString = GetNativeValueLabelFormat(chart, 4),
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
                            AddDataLabelAnnotation(model, chart, theme, seriesName, seriesIndex, i, GetCategory(categories, i), i, v, v);
                    }
                }
                if (firstSeriesPoints is null)
                    firstSeriesPoints = trendPoints;
                model.Series.Add(series);
            }
            else if (chart.Type == ChartType.Bar)
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
                    LabelFormatString = GetNativeValueLabelFormat(chart, 0),
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
                            AddDataLabelAnnotation(model, chart, theme, seriesName, seriesIndex, i, GetCategory(categories, i), v, i, v);
                    }
                }
                if (firstSeriesPoints is null)
                    firstSeriesPoints = trendPoints;
                model.Series.Add(series);
            }
            else if (chart.Type == ChartType.Area)
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
                    AddLinePoints(lineSeries, cellLookup, dataStartRow, endRow, col, firstSeriesPoints is null ? new List<DataPoint>() : null, out var comboTrendPoints);
                    if (firstSeriesPoints is null)
                        firstSeriesPoints = comboTrendPoints;
                    AddLineDataLabelAnnotations(model, chart, theme, lineSeries, seriesName, seriesIndex, categories);
                    model.Series.Add(lineSeries);
                    continue;
                }

                var series = new AreaSeries
                {
                    Title = seriesName,
                    LabelFormatString = GetNativeValueLabelFormat(chart, 1)
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
                            AddDataLabelAnnotation(model, chart, theme, seriesName, seriesIndex, i, GetCategory(categories, i), i, v, v);
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
                    LabelFormatString = GetNativeValueLabelFormat(chart, 1),
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
                            AddDataLabelAnnotation(model, chart, theme, seriesName, seriesIndex, (int)(r - dataStartRow), GetCategory(categories, (int)(r - dataStartRow)), x, y, y);
                    }
                }
                if (firstSeriesPoints is null)
                    firstSeriesPoints = trendPoints;
                model.Series.Add(series);
            }
            else // Line
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
                AddLinePoints(series, cellLookup, dataStartRow, endRow, col, trendPoints, out trendPoints);
                if (firstSeriesPoints is null)
                    firstSeriesPoints = trendPoints;
                AddLineDataLabelAnnotations(model, chart, theme, series, seriesName, seriesIndex, categories);
                model.Series.Add(series);
            }
        }

        AddTrendlineIfRequested(model, chart, theme, firstSeriesPoints, swapTrendlineAxes: chart.Type == ChartType.Bar);
        ApplyAxisBounds(model, chart);
        return model;
    }

    private static LineSeries CreateLineSeries(ChartModel chart, string title, int seriesIndex, WorkbookTheme theme)
    {
        var series = new LineSeries
        {
            Title = title,
            LabelFormatString = GetNativeValueLabelFormat(chart, 1),
            LabelMargin = ToLabelMargin(chart.DataLabelPosition),
            YAxisKey = UsesSecondaryAxis(chart, seriesIndex) ? SecondaryYAxisKey : null
        };
        ApplyLineFormat(series, GetSeriesFormat(chart, seriesIndex), theme);
        ApplyNativeDataLabelStyle(series, chart, theme);
        return series;
    }

    private static PlotModel BuildStackedColumnModel(
        ChartModel chart,
        PlotModel model,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        IReadOnlyList<string> categories,
        uint dataStartRow,
        uint endRow,
        uint dataStartCol,
        uint endCol,
        uint headerRow,
        bool normalizeToPercent,
        WorkbookTheme theme)
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
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = chart.YAxisTitle,
            Minimum = normalizeToPercent ? -100 : double.NaN,
            Maximum = normalizeToPercent ? 100 : double.NaN
        });

        var positiveBases = new double[categories.Count];
        var negativeBases = new double[categories.Count];
        var (positiveTotals, negativeTotals) = normalizeToPercent
            ? CalculateStackedPercentTotals(cellLookup, categories.Count, dataStartRow, endRow, dataStartCol, endCol)
            : ([], []);
        for (uint col = dataStartCol; col <= endCol; col++)
        {
            var seriesIndex = (int)(col - dataStartCol);
            var seriesName = chart.FirstRowIsHeader && cellLookup.TryGetValue((headerRow, col), out var hdr)
                ? hdr.DisplayText
                : $"Series {seriesIndex + 1}";

            if (IsComboLineSeries(chart, seriesIndex))
            {
                var lineSeries = CreateLineSeries(chart, seriesName, seriesIndex, theme);
                var pointIndex = 0;
                for (uint row = dataStartRow; row <= endRow; row++, pointIndex++)
                {
                    if (!TryGetNumericCell(cellLookup, row, col, out var value) || pointIndex >= categories.Count)
                        continue;

                    lineSeries.Points.Add(new DataPoint(pointIndex, value));
                }
                AddLineDataLabelAnnotations(model, chart, theme, lineSeries, seriesName, seriesIndex, categories);
                model.Series.Add(lineSeries);
                continue;
            }

            var series = new RectangleBarSeries
            {
                Title = seriesName,
                LabelFormatString = GetNativeValueLabelFormat(chart, 4)
            };
            ApplyRectangleBarFormat(series, GetSeriesFormat(chart, seriesIndex), theme);
            ApplyNativeDataLabelStyle(series, chart, theme);

            var i = 0;
            for (uint row = dataStartRow; row <= endRow; row++, i++)
            {
                if (!TryGetNumericCell(cellLookup, row, col, out var value) || i >= categories.Count)
                    continue;

                var displayValue = NormalizeStackedValue(value, i, positiveTotals, negativeTotals);
                var start = displayValue >= 0 ? positiveBases[i] : negativeBases[i];
                var end = start + displayValue;
                series.Items.Add(new RectangleBarItem(i - 0.35, Math.Min(start, end), i + 0.35, Math.Max(start, end)));
                if (displayValue >= 0)
                    positiveBases[i] = end;
                else
                    negativeBases[i] = end;
                if (ShouldUseAnnotationLabels(chart))
                    AddDataLabelAnnotation(model, chart, theme, seriesName, seriesIndex, i, GetCategory(categories, i), i, end, GetStackedLabelValue(chart, normalizeToPercent, value, displayValue));
            }

            model.Series.Add(series);
        }

        return model;
    }

    private static PlotModel BuildStackedBarModel(
        ChartModel chart,
        PlotModel model,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        IReadOnlyList<string> categories,
        uint dataStartRow,
        uint endRow,
        uint dataStartCol,
        uint endCol,
        uint headerRow,
        bool normalizeToPercent,
        WorkbookTheme theme)
    {
        var categoryAxis = new CategoryAxis { Position = AxisPosition.Left, Title = chart.YAxisTitle };
        categoryAxis.Labels.AddRange(categories);
        model.Axes.Add(categoryAxis);
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = chart.XAxisTitle,
            Minimum = normalizeToPercent ? -100 : double.NaN,
            Maximum = normalizeToPercent ? 100 : double.NaN
        });

        var positiveBases = new double[categories.Count];
        var negativeBases = new double[categories.Count];
        var (positiveTotals, negativeTotals) = normalizeToPercent
            ? CalculateStackedPercentTotals(cellLookup, categories.Count, dataStartRow, endRow, dataStartCol, endCol)
            : ([], []);
        for (uint col = dataStartCol; col <= endCol; col++)
        {
            var seriesIndex = (int)(col - dataStartCol);
            var seriesName = chart.FirstRowIsHeader && cellLookup.TryGetValue((headerRow, col), out var hdr)
                ? hdr.DisplayText
                : $"Series {seriesIndex + 1}";
            var series = new RectangleBarSeries
            {
                Title = seriesName,
                LabelFormatString = GetNativeValueLabelFormat(chart, 4)
            };
            ApplyRectangleBarFormat(series, GetSeriesFormat(chart, seriesIndex), theme);
            ApplyNativeDataLabelStyle(series, chart, theme);

            var i = 0;
            for (uint row = dataStartRow; row <= endRow; row++, i++)
            {
                if (!TryGetNumericCell(cellLookup, row, col, out var value) || i >= categories.Count)
                    continue;

                var displayValue = NormalizeStackedValue(value, i, positiveTotals, negativeTotals);
                var start = displayValue >= 0 ? positiveBases[i] : negativeBases[i];
                var end = start + displayValue;
                series.Items.Add(new RectangleBarItem(Math.Min(start, end), i - 0.35, Math.Max(start, end), i + 0.35));
                if (displayValue >= 0)
                    positiveBases[i] = end;
                else
                    negativeBases[i] = end;
                if (ShouldUseAnnotationLabels(chart))
                    AddDataLabelAnnotation(model, chart, theme, seriesName, seriesIndex, i, GetCategory(categories, i), end, i, GetStackedLabelValue(chart, normalizeToPercent, value, displayValue));
            }

            model.Series.Add(series);
        }

        return model;
    }

    private static bool TryGetNumericCell(
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        uint row,
        uint col,
        out double value)
    {
        value = 0;
        return cellLookup.TryGetValue((row, col), out var cell) &&
               double.TryParse(cell.DisplayText, out value);
    }

    private static PlotModel BuildBubbleModel(
        ChartModel chart,
        PlotModel model,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        IReadOnlyList<string> categories,
        uint dataStartRow,
        uint endRow,
        uint dataStartCol,
        uint endCol,
        uint headerRow,
        WorkbookTheme theme,
        out List<DataPoint> trendPoints)
    {
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = chart.XAxisTitle });
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = chart.YAxisTitle });
        trendPoints = [];

        var xCol = chart.DataRange.Start.Col;
        var seriesIndex = 0;
        for (var yCol = xCol + 1; yCol <= endCol; yCol += 2)
        {
            var sizeCol = yCol + 1;
            if (sizeCol > endCol)
                continue;

            var seriesName = chart.FirstRowIsHeader && cellLookup.TryGetValue((headerRow, yCol), out var hdr)
                ? hdr.DisplayText
                : $"Series {seriesIndex + 1}";
            var series = new ScatterSeries
            {
                Title = seriesName,
                MarkerType = MarkerType.Circle,
                LabelFormatString = GetNativeValueLabelFormat(chart, 1),
                LabelMargin = ToLabelMargin(chart.DataLabelPosition)
            };
            ApplyScatterFormat(series, GetSeriesFormat(chart, seriesIndex), theme);
            ApplyNativeDataLabelStyle(series, chart, theme);

            var fallbackIndex = 0;
            for (uint row = dataStartRow; row <= endRow; row++, fallbackIndex++)
            {
                if (!TryGetNumericCell(cellLookup, row, xCol, out var x))
                    x = fallbackIndex;
                if (!TryGetNumericCell(cellLookup, row, yCol, out var y))
                    continue;
                var size = TryGetNumericCell(cellLookup, row, sizeCol, out var rawSize)
                    ? Math.Max(1, Math.Abs(rawSize))
                    : 5;
                series.Points.Add(new ScatterPoint(x, y, size));
                if (seriesIndex == 0)
                    trendPoints.Add(new DataPoint(x, y));
                if (ShouldUseAnnotationLabels(chart))
                    AddDataLabelAnnotation(model, chart, theme, seriesName, seriesIndex, fallbackIndex, GetCategory(categories, fallbackIndex), x, y, y);
            }

            model.Series.Add(series);
            seriesIndex++;
        }

        return model;
    }

    private static (double[] PositiveTotals, double[] NegativeTotals) CalculateStackedPercentTotals(
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        int categoryCount,
        uint dataStartRow,
        uint endRow,
        uint dataStartCol,
        uint endCol)
    {
        var positiveTotals = new double[categoryCount];
        var negativeTotals = new double[categoryCount];
        for (uint col = dataStartCol; col <= endCol; col++)
        {
            var index = 0;
            for (uint row = dataStartRow; row <= endRow && index < categoryCount; row++, index++)
            {
                if (!TryGetNumericCell(cellLookup, row, col, out var value))
                    continue;
                if (value >= 0)
                    positiveTotals[index] += value;
                else
                    negativeTotals[index] += Math.Abs(value);
            }
        }

        return (positiveTotals, negativeTotals);
    }

    private static double NormalizeStackedValue(
        double value,
        int categoryIndex,
        IReadOnlyList<double> positiveTotals,
        IReadOnlyList<double> negativeTotals)
    {
        if (positiveTotals.Count == 0 && negativeTotals.Count == 0)
            return value;

        var total = value >= 0 ? positiveTotals[categoryIndex] : negativeTotals[categoryIndex];
        return total == 0 ? 0 : value / total * 100;
    }

    private static double GetStackedLabelValue(ChartModel chart, bool normalizeToPercent, double sourceValue, double displayValue) =>
        normalizeToPercent && ShouldRenderPercentageLabels(chart)
            ? displayValue / 100
            : sourceValue;

    private static bool ShouldSkipScatterXColumn(ChartModel chart, uint col, uint dataStartCol) =>
        chart.Type == ChartType.Scatter
            && !chart.FirstColIsCategories
            && col == dataStartCol;

    private static int GetSeriesIndex(ChartModel chart, uint col, uint dataStartCol) =>
        (int)(col - dataStartCol - (chart.Type == ChartType.Scatter && !chart.FirstColIsCategories ? 1 : 0));

    private static ChartSeriesFormat? GetSeriesFormat(ChartModel chart, int seriesIndex) =>
        chart.SeriesFormats.LastOrDefault(format => format.SeriesIndex == seriesIndex);

    private static ChartPointDataLabelFormat? GetPointDataLabelFormat(ChartModel chart, int seriesIndex, int pointIndex) =>
        chart.PointDataLabelFormats.LastOrDefault(format => format.SeriesIndex == seriesIndex && format.PointIndex == pointIndex);

    private static void ApplyAxisBounds(PlotModel model, ChartModel chart)
    {
        for (var index = 0; index < model.Axes.Count; index++)
        {
            var axis = model.Axes[index];
            if (axis is not LinearAxis linearAxis)
                continue;

            if (ShouldUseLogAxis(chart, linearAxis))
            {
                var logAxis = new LogarithmicAxis
                {
                    Position = linearAxis.Position,
                    Title = linearAxis.Title,
                    Key = linearAxis.Key,
                    Minimum = GetPositiveAxisValue(linearAxis.Minimum),
                    Maximum = GetPositiveAxisValue(linearAxis.Maximum),
                    MajorStep = GetPositiveAxisValue(linearAxis.MajorStep),
                    MinorStep = GetPositiveAxisValue(linearAxis.MinorStep),
                    LabelFormatter = linearAxis.LabelFormatter
                };
                model.Axes[index] = logAxis;
                axis = logAxis;
            }

            if (axis.Position is AxisPosition.Bottom or AxisPosition.Top)
            {
                ApplyAxisTitleStyle(axis, chart);
                if (ChartTypeSupport.SupportsXAxisBounds(chart.Type))
                {
                    if (chart.XAxisMinimum is { } minimum)
                        axis.Minimum = ShouldUseLogAxis(chart, axis) ? Math.Max(double.Epsilon, minimum) : minimum;
                    if (chart.XAxisMaximum is { } maximum)
                        axis.Maximum = ShouldUseLogAxis(chart, axis) ? Math.Max(double.Epsilon, maximum) : maximum;
                    if (chart.XAxisMajorUnit is { } majorUnit)
                        axis.MajorStep = majorUnit;
                    if (chart.XAxisMinorUnit is { } minorUnit)
                        axis.MinorStep = minorUnit;
                }
                if (ChartTypeSupport.SupportsXAxisBounds(chart.Type) &&
                    chart.XAxisNumberFormat != ChartDataLabelNumberFormat.General &&
                    axis.LabelFormatter is null)
                    axis.LabelFormatter = value => FormatAxisValue(chart.XAxisNumberFormat, value);
                ApplyGridlineStyle(
                    axis,
                    chart.ShowXAxisMajorGridlines,
                    chart.ShowXAxisMinorGridlines,
                    chart.XAxisMajorGridlineColor,
                    chart.XAxisMinorGridlineColor,
                    chart.XAxisGridlineThickness);
                ApplyTickAndLabelStyle(axis, chart.XAxisMajorTickStyle, chart.XAxisMinorTickStyle, chart.ShowXAxisLabels);
                ApplyAxisLabelStyle(axis, chart.XAxisLabelTextColor, chart.XAxisLabelFontSize, chart.XAxisLabelAngle);
                ApplyAxisLineStyle(axis, chart.XAxisLineColor, chart.XAxisLineThickness);
            }
            else if (axis.Position is AxisPosition.Left or AxisPosition.Right)
            {
                ApplyAxisTitleStyle(axis, chart);
                if (ChartTypeSupport.SupportsYAxisBounds(chart.Type))
                {
                    if (chart.YAxisMinimum is { } minimum)
                        axis.Minimum = ShouldUseLogAxis(chart, axis) ? Math.Max(double.Epsilon, minimum) : minimum;
                    if (chart.YAxisMaximum is { } maximum)
                        axis.Maximum = ShouldUseLogAxis(chart, axis) ? Math.Max(double.Epsilon, maximum) : maximum;
                    if (chart.YAxisMajorUnit is { } majorUnit)
                        axis.MajorStep = majorUnit;
                    if (chart.YAxisMinorUnit is { } minorUnit)
                        axis.MinorStep = minorUnit;
                }
                if (ChartTypeSupport.SupportsYAxisBounds(chart.Type) &&
                    chart.YAxisNumberFormat != ChartDataLabelNumberFormat.General &&
                    axis.LabelFormatter is null)
                    axis.LabelFormatter = value => FormatAxisValue(chart.YAxisNumberFormat, value);
                ApplyGridlineStyle(
                    axis,
                    chart.ShowYAxisMajorGridlines,
                    chart.ShowYAxisMinorGridlines,
                    chart.YAxisMajorGridlineColor,
                    chart.YAxisMinorGridlineColor,
                    chart.YAxisGridlineThickness);
                ApplyTickAndLabelStyle(axis, chart.YAxisMajorTickStyle, chart.YAxisMinorTickStyle, chart.ShowYAxisLabels);
                ApplyAxisLabelStyle(axis, chart.YAxisLabelTextColor, chart.YAxisLabelFontSize, chart.YAxisLabelAngle);
                ApplyAxisLineStyle(axis, chart.YAxisLineColor, chart.YAxisLineThickness);
            }
        }
    }

    private static void ApplyAreaStyle(PlotModel model, ChartModel chart, WorkbookTheme theme)
    {
        if (chart.ResolveChartAreaFillColor(theme) is { } chartFill)
            model.Background = OxyColor.FromRgb(chartFill.R, chartFill.G, chartFill.B);
        if (chart.ResolvePlotAreaFillColor(theme) is { } plotFill)
            model.PlotAreaBackground = OxyColor.FromRgb(plotFill.R, plotFill.G, plotFill.B);
        if (chart.ResolvePlotAreaBorderColor(theme) is { } plotBorder)
            model.PlotAreaBorderColor = OxyColor.FromRgb(plotBorder.R, plotBorder.G, plotBorder.B);
        model.PlotAreaBorderThickness = new OxyThickness(chart.PlotAreaBorderThickness);
    }

    private static void ApplyTitleStyle(PlotModel model, ChartModel chart)
    {
        model.TitleFontSize = chart.ChartTitleFontSize;
        if (chart.ChartTitleTextColor is { } titleColor)
            model.TitleColor = OxyColor.FromRgb(titleColor.R, titleColor.G, titleColor.B);
    }

    private static void ApplyAxisTitleStyle(Axis axis, ChartModel chart)
    {
        axis.TitleFontSize = chart.AxisTitleFontSize;
        if (chart.AxisTitleTextColor is { } titleColor)
            axis.TitleColor = OxyColor.FromRgb(titleColor.R, titleColor.G, titleColor.B);
    }

    private static void ApplyGridlineStyle(
        Axis axis,
        bool showMajor,
        bool showMinor,
        CellColor? majorColor,
        CellColor? minorColor,
        double thickness)
    {
        axis.MajorGridlineStyle = showMajor ? LineStyle.Solid : LineStyle.None;
        axis.MajorGridlineColor = ToOxyColor(majorColor) ?? OxyColor.FromRgb(220, 220, 220);
        axis.MajorGridlineThickness = thickness;
        axis.MinorGridlineStyle = showMinor ? LineStyle.Dot : LineStyle.None;
        axis.MinorGridlineColor = ToOxyColor(minorColor) ?? OxyColor.FromRgb(235, 235, 235);
        axis.MinorGridlineThickness = Math.Max(0.25, thickness * 0.75);
    }

    private static void ApplyTickAndLabelStyle(
        Axis axis,
        ChartAxisTickStyle majorTickStyle,
        ChartAxisTickStyle minorTickStyle,
        bool showLabels)
    {
        axis.TickStyle = ToOxyTickStyle(majorTickStyle);
        axis.MajorTickSize = GetTickSize(majorTickStyle);
        axis.MinorTickSize = GetTickSize(minorTickStyle);
        if (!showLabels)
            axis.TextColor = OxyColors.Transparent;
    }

    private static void ApplyAxisLabelStyle(Axis axis, CellColor? textColor, double fontSize, double angle)
    {
        axis.FontSize = fontSize;
        axis.Angle = angle;
        if (textColor is { } color && axis.TextColor != OxyColors.Transparent)
            axis.TextColor = OxyColor.FromRgb(color.R, color.G, color.B);
    }

    private static void ApplyAxisLineStyle(Axis axis, CellColor? color, double thickness)
    {
        axis.AxislineStyle = LineStyle.Solid;
        axis.AxislineThickness = thickness;
        if (color is { } lineColor)
            axis.AxislineColor = OxyColor.FromRgb(lineColor.R, lineColor.G, lineColor.B);
    }

    private static double GetTickSize(ChartAxisTickStyle tickStyle) =>
        tickStyle switch
        {
            ChartAxisTickStyle.None => 0,
            ChartAxisTickStyle.Inside => 4,
            ChartAxisTickStyle.Cross => 8,
            _ => 6
        };

    private static TickStyle ToOxyTickStyle(ChartAxisTickStyle tickStyle) =>
        tickStyle switch
        {
            ChartAxisTickStyle.None => TickStyle.None,
            ChartAxisTickStyle.Inside => TickStyle.Inside,
            ChartAxisTickStyle.Cross => TickStyle.Crossing,
            _ => TickStyle.Outside
        };

    private static string FormatAxisValue(ChartDataLabelNumberFormat format, double value) =>
        format switch
        {
            ChartDataLabelNumberFormat.Number => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            ChartDataLabelNumberFormat.Currency => value.ToString("$#,##0.00", System.Globalization.CultureInfo.InvariantCulture),
            ChartDataLabelNumberFormat.Percent => value.ToString("0%", System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
        };

    private static bool ShouldUseLogAxis(ChartModel chart, Axis axis) =>
        axis.Position is AxisPosition.Bottom or AxisPosition.Top
            ? chart.XAxisLogScale && ChartTypeSupport.SupportsXAxisLogScale(chart.Type)
            : chart.YAxisLogScale && ChartTypeSupport.SupportsYAxisLogScale(chart.Type);

    private static double GetPositiveAxisValue(double value) =>
        double.IsNaN(value) || value <= 0 ? double.NaN : value;

    private static void ApplyLineFormat(LineSeries series, ChartSeriesFormat? format, WorkbookTheme theme)
    {
        if (format is null)
            return;
        if (format.ResolveStrokeColor(theme) is { } stroke)
            series.Color = OxyColor.FromRgb(stroke.R, stroke.G, stroke.B);
        else if (format.ResolveFillColor(theme) is { } fill)
            series.Color = OxyColor.FromRgb(fill.R, fill.G, fill.B);
        if (format.StrokeThickness is { } thickness)
            series.StrokeThickness = thickness;
        if (format.DashStyle is { } dashStyle)
            series.LineStyle = ToOxyLineStyle(dashStyle);
        if (format.MarkerStyle is { } markerStyle)
            series.MarkerType = ToOxyMarkerType(markerStyle);
        if (format.MarkerSize is { } markerSize)
            series.MarkerSize = Math.Clamp(markerSize, 1, 20);
        if (format.ResolveFillColor(theme) is { } markerFill)
            series.MarkerFill = OxyColor.FromRgb(markerFill.R, markerFill.G, markerFill.B);
        if (format.ResolveStrokeColor(theme) is { } markerStroke)
            series.MarkerStroke = OxyColor.FromRgb(markerStroke.R, markerStroke.G, markerStroke.B);
        if (format.StrokeThickness is { } markerStrokeThickness)
            series.MarkerStrokeThickness = markerStrokeThickness;
    }

    private static void ApplyRectangleBarFormat(RectangleBarSeries series, ChartSeriesFormat? format, WorkbookTheme theme)
    {
        if (format is null)
            return;
        if (format.ResolveFillColor(theme) is { } fill)
            series.FillColor = OxyColor.FromRgb(fill.R, fill.G, fill.B);
        if (format.ResolveStrokeColor(theme) is { } stroke)
            series.StrokeColor = OxyColor.FromRgb(stroke.R, stroke.G, stroke.B);
        if (format.StrokeThickness is { } thickness)
            series.StrokeThickness = thickness;
    }

    private static void ApplyBarFormat(BarSeries series, ChartSeriesFormat? format, WorkbookTheme theme)
    {
        if (format is null)
            return;
        if (format.ResolveFillColor(theme) is { } fill)
            series.FillColor = OxyColor.FromRgb(fill.R, fill.G, fill.B);
        if (format.ResolveStrokeColor(theme) is { } stroke)
            series.StrokeColor = OxyColor.FromRgb(stroke.R, stroke.G, stroke.B);
        if (format.StrokeThickness is { } thickness)
            series.StrokeThickness = thickness;
    }

    private static void ApplyPieFormat(PieSeries series, ChartSeriesFormat? format, WorkbookTheme theme)
    {
        if (format is null)
            return;
        if (format.ResolveStrokeColor(theme) is { } stroke)
            series.Stroke = OxyColor.FromRgb(stroke.R, stroke.G, stroke.B);
        if (format.StrokeThickness is { } thickness)
            series.StrokeThickness = thickness;
    }

    private static void ApplyPieDataLabelStyle(PieSeries series, ChartModel chart, WorkbookTheme theme)
    {
        series.FontSize = chart.DataLabelFontSize;
        if (chart.ResolveDataLabelTextColor(theme) is not { } color)
            return;

        var oxyColor = OxyColor.FromRgb(color.R, color.G, color.B);
        series.TextColor = oxyColor;
        series.InsideLabelColor = oxyColor;
    }

    private static void ApplyAreaFormat(AreaSeries series, ChartSeriesFormat? format, WorkbookTheme theme)
    {
        if (format is null)
            return;
        if (format.ResolveStrokeColor(theme) is { } stroke)
            series.Color = OxyColor.FromRgb(stroke.R, stroke.G, stroke.B);
        if (format.ResolveFillColor(theme) is { } fill)
            series.Fill = OxyColor.FromRgb(fill.R, fill.G, fill.B);
        if (format.StrokeThickness is { } thickness)
            series.StrokeThickness = thickness;
        if (format.DashStyle is { } dashStyle)
            series.LineStyle = ToOxyLineStyle(dashStyle);
    }

    private static void ApplyScatterFormat(ScatterSeries series, ChartSeriesFormat? format, WorkbookTheme theme)
    {
        if (format is null)
            return;
        if (format.ResolveFillColor(theme) is { } fill)
            series.MarkerFill = OxyColor.FromRgb(fill.R, fill.G, fill.B);
        if (format.ResolveStrokeColor(theme) is { } stroke)
            series.MarkerStroke = OxyColor.FromRgb(stroke.R, stroke.G, stroke.B);
        if (format.StrokeThickness is { } thickness)
            series.MarkerStrokeThickness = thickness;
        if (format.MarkerStyle is { } markerStyle)
            series.MarkerType = ToOxyMarkerType(markerStyle);
        if (format.MarkerSize is { } markerSize)
            series.MarkerSize = Math.Clamp(markerSize, 1, 30);
    }

    private static bool ShouldUseNativeValueLabels(ChartModel chart) =>
        chart.ShowDataLabels
            && !chart.ShowDataLabelCategoryName
            && !chart.ShowDataLabelSeriesName
            && !ShouldRenderPercentageLabels(chart)
            && !IsPercentStackedChart(chart)
            && !RequiresDataLabelAnnotationFormatting(chart);

    private static void ApplyNativeDataLabelStyle(PlotElement element, ChartModel chart, WorkbookTheme theme)
    {
        if (!ShouldUseNativeValueLabels(chart))
            return;

        element.FontSize = chart.DataLabelFontSize;
        if (chart.ResolveDataLabelTextColor(theme) is { } color)
            element.TextColor = OxyColor.FromRgb(color.R, color.G, color.B);
    }

    private static bool ShouldUseAnnotationLabels(ChartModel chart) =>
        chart.ShowDataLabels
            && (chart.ShowDataLabelCategoryName
                || chart.ShowDataLabelSeriesName
                || ShouldRenderPercentageLabels(chart)
                || IsPercentStackedChart(chart)
                || RequiresDataLabelAnnotationFormatting(chart));

    private static bool ShouldRenderPercentageLabels(ChartModel chart) =>
        chart.ShowDataLabelPercentage
            && ChartTypeSupport.SupportsPercentageDataLabels(chart.Type);

    private static bool IsPercentStackedChart(ChartModel chart) =>
        chart.Type is ChartType.PercentStackedColumn or ChartType.PercentStackedBar;

    private static bool RequiresDataLabelAnnotationFormatting(ChartModel chart) =>
        chart.ShowDataLabelCallouts
            || chart.DataLabelFillColor is not null
            || chart.DataLabelFillThemeColor is not null
            || chart.DataLabelBorderColor is not null
            || chart.DataLabelBorderThemeColor is not null
            || chart.DataLabelBorderThickness > 0
            || Math.Abs(chart.DataLabelAngle) > 0.5;

    private static string GetCategory(IReadOnlyList<string> categories, int index) =>
        index >= 0 && index < categories.Count ? categories[index] : "";

    private static string FormatDataLabel(ChartModel chart, string seriesName, string categoryName, double value)
    {
        var parts = new List<string>();
        if (chart.ShowDataLabelSeriesName && !string.IsNullOrWhiteSpace(seriesName))
            parts.Add(seriesName);
        if (chart.ShowDataLabelCategoryName && !string.IsNullOrWhiteSpace(categoryName))
            parts.Add(categoryName);
        parts.Add(FormatLabelValue(chart, value));
        return string.Join(GetDataLabelSeparatorText(chart.DataLabelSeparator), parts);
    }

    private static void AddDataLabelAnnotation(
        PlotModel model,
        ChartModel chart,
        WorkbookTheme theme,
        string seriesName,
        int seriesIndex,
        int pointIndex,
        string categoryName,
        double x,
        double y,
        double value)
    {
        var pointFormat = GetPointDataLabelFormat(chart, seriesIndex, pointIndex);
        var textColor = pointFormat?.ResolveTextColor(theme) ?? chart.ResolveDataLabelTextColor(theme);
        var borderColor = pointFormat?.ResolveBorderColor(theme) ?? chart.ResolveDataLabelBorderColor(theme);
        var fillColor = pointFormat?.ResolveFillColor(theme) ?? chart.ResolveDataLabelFillColor(theme);
        model.Annotations.Add(new TextAnnotation
        {
            Text = FormatDataLabel(chart, seriesName, categoryName, value),
            TextPosition = new DataPoint(x, y),
            TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
            TextVerticalAlignment = chart.DataLabelPosition == ChartDataLabelPosition.InsideEnd
                ? OxyPlot.VerticalAlignment.Top
                : OxyPlot.VerticalAlignment.Bottom,
            TextColor = ToOxyColor(textColor) ?? OxyColors.Automatic,
            FontSize = pointFormat?.FontSize ?? chart.DataLabelFontSize,
            Stroke = ToOxyColor(borderColor) ?? (chart.ShowDataLabelCallouts ? OxyColors.Gray : OxyColors.Transparent),
            StrokeThickness = pointFormat?.BorderThickness ?? (chart.DataLabelBorderThickness > 0 ? chart.DataLabelBorderThickness : chart.ShowDataLabelCallouts ? 1 : 0),
            Background = ToOxyColor(fillColor) ?? (chart.ShowDataLabelCallouts ? OxyColor.FromAColor(235, OxyColors.White) : OxyColors.Transparent),
            TextRotation = chart.DataLabelAngle,
            Padding = new OxyThickness(chart.ShowDataLabelCallouts ? 4 : 2)
        });
    }

    private static OxyColor? ToOxyColor(CellColor? color) =>
        color is { } value ? OxyColor.FromRgb(value.R, value.G, value.B) : null;

    private static void AddLineDataLabelAnnotations(
        PlotModel model,
        ChartModel chart,
        WorkbookTheme theme,
        LineSeries series,
        string seriesName,
        int seriesIndex,
        IReadOnlyList<string> categories)
    {
        if (!ShouldUseAnnotationLabels(chart))
            return;

        for (var pointIndex = 0; pointIndex < series.Points.Count; pointIndex++)
        {
            var point = series.Points[pointIndex];
            AddDataLabelAnnotation(
                model,
                chart,
                theme,
                seriesName,
                seriesIndex,
                pointIndex,
                GetCategory(categories, (int)Math.Round(point.X)),
                point.X,
                point.Y,
                point.Y);
        }
    }

    private static string GetPieLabelFormat(ChartModel chart, string seriesName)
    {
        var valuePart = chart.ShowDataLabelPercentage
            ? "{2:0%}"
            : GetPieValueFormat(chart.DataLabelNumberFormat);
        var separator = GetDataLabelSeparatorText(chart.DataLabelSeparator);
        if (chart.ShowDataLabelSeriesName && chart.ShowDataLabelCategoryName)
            return $"{seriesName}{separator}{{1}}{separator}{valuePart}";
        if (chart.ShowDataLabelSeriesName)
            return $"{seriesName}{separator}{valuePart}";
        if (chart.ShowDataLabelCategoryName)
            return $"{{1}}{separator}{valuePart}";
        return valuePart;
    }

    private static string GetDataLabelSeparatorText(ChartDataLabelSeparator separator) =>
        separator switch
        {
            ChartDataLabelSeparator.Semicolon => "; ",
            ChartDataLabelSeparator.NewLine => Environment.NewLine,
            ChartDataLabelSeparator.Space => " ",
            _ => ", "
        };

    private static string FormatLabelValue(ChartModel chart, double value) =>
        ShouldRenderPercentageLabels(chart)
            ? value.ToString("0%", System.Globalization.CultureInfo.InvariantCulture)
            : chart.DataLabelNumberFormat switch
        {
            ChartDataLabelNumberFormat.Number => value.ToString("0.00"),
            ChartDataLabelNumberFormat.Currency => value.ToString("$#,##0.00", System.Globalization.CultureInfo.InvariantCulture),
            ChartDataLabelNumberFormat.Percent => value.ToString("0%", System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
        };

    private static string GetPieValueFormat(ChartDataLabelNumberFormat format) =>
        format switch
        {
            ChartDataLabelNumberFormat.Number => "{0:0.00}",
            ChartDataLabelNumberFormat.Currency => "{0:$#,##0.00}",
            ChartDataLabelNumberFormat.Percent => "{0:0%}",
            _ => "{0}"
        };

    private static string? GetNativeValueLabelFormat(ChartModel chart, int valueIndex)
    {
        if (!ShouldUseNativeValueLabels(chart))
            return null;

        var format = chart.DataLabelNumberFormat switch
        {
            ChartDataLabelNumberFormat.Number => ":0.00",
            ChartDataLabelNumberFormat.Currency => ":$#,##0.00",
            ChartDataLabelNumberFormat.Percent => ":0%",
            _ => ""
        };
        return $"{{{valueIndex}{format}}}";
    }

    private static bool UsesSecondaryAxis(ChartModel chart, int seriesIndex)
    {
        if (!chart.ShowSecondaryAxis || seriesIndex <= 0)
            return false;

        return chart.SecondaryAxisSeriesIndexes.Count == 0 ||
               chart.SecondaryAxisSeriesIndexes.Contains(seriesIndex);
    }

    private static bool IsComboLineSeries(ChartModel chart, int seriesIndex)
    {
        if (!ChartTypeSupport.SupportsComboLineOverlay(chart.Type) || !chart.UseComboLineForSecondarySeries || seriesIndex <= 0)
            return false;

        return chart.ComboLineSeriesIndexes.Contains(seriesIndex);
    }

    private static LabelPlacement ToOxyLabelPlacement(ChartDataLabelPosition position) =>
        position switch
        {
            ChartDataLabelPosition.Center => LabelPlacement.Middle,
            ChartDataLabelPosition.InsideEnd => LabelPlacement.Inside,
            ChartDataLabelPosition.OutsideEnd => LabelPlacement.Outside,
            _ => LabelPlacement.Outside
        };

    private static double ToLabelMargin(ChartDataLabelPosition position) =>
        position switch
        {
            ChartDataLabelPosition.Center => -8,
            ChartDataLabelPosition.InsideEnd => -4,
            ChartDataLabelPosition.OutsideEnd => 8,
            _ => 4
        };

    private static void AddLinePoints(
        LineSeries series,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        uint dataStartRow,
        uint endRow,
        uint col,
        List<DataPoint>? trendPoints,
        out List<DataPoint>? capturedTrendPoints)
    {
        var i = 0;
        for (uint r = dataStartRow; r <= endRow; r++, i++)
        {
            if (cellLookup.TryGetValue((r, col), out var cell)
                && double.TryParse(cell.DisplayText, out var v))
            {
                var point = new DataPoint(i, v);
                series.Points.Add(point);
                trendPoints?.Add(point);
            }
        }

        capturedTrendPoints = trendPoints;
    }

    private static void AddTrendlineIfRequested(
        PlotModel model,
        ChartModel chart,
        WorkbookTheme theme,
        IReadOnlyList<DataPoint>? points,
        bool swapTrendlineAxes = false)
    {
        if (!chart.ShowLinearTrendline || !ChartTypeSupport.SupportsTrendlines(chart.Type) || points is null || points.Count < 2)
            return;

        var trendPoints = chart.TrendlineType switch
        {
            ChartTrendlineType.Exponential => CalculateExponentialTrendline(points),
            ChartTrendlineType.Logarithmic => CalculateLogarithmicTrendline(points),
            ChartTrendlineType.Power => CalculatePowerTrendline(points),
            ChartTrendlineType.MovingAverage => CalculateMovingAverageTrendline(points, chart.TrendlinePeriod),
            ChartTrendlineType.Polynomial => CalculatePolynomialTrendline(points, chart.TrendlineOrder),
            _ => CalculateLinearTrendline(points)
        };
        if (trendPoints.Count < 2)
            return;

        var trendline = new LineSeries
        {
            Title = GetTrendlineTitle(chart.TrendlineType),
            LineStyle = ToOxyLineStyle(chart.TrendlineDashStyle),
            StrokeThickness = chart.TrendlineThickness,
            Color = chart.ResolveTrendlineColor(theme) is { } color
                ? OxyColor.FromRgb(color.R, color.G, color.B)
                : OxyColors.Gray
        };
        var displaySourcePoints = swapTrendlineAxes
            ? points.Select(point => new DataPoint(point.Y, point.X)).ToArray()
            : points;
        foreach (var point in trendPoints)
            trendline.Points.Add(swapTrendlineAxes ? new DataPoint(point.Y, point.X) : point);
        model.Series.Add(trendline);
        AddTrendlineInfoIfRequested(model, chart, points, trendPoints, displaySourcePoints);
    }

    private static LineStyle ToOxyLineStyle(ChartLineDashStyle dashStyle) =>
        dashStyle switch
        {
            ChartLineDashStyle.Solid => LineStyle.Solid,
            ChartLineDashStyle.Dot => LineStyle.Dot,
            _ => LineStyle.Dash
        };

    private static MarkerType ToOxyMarkerType(ChartMarkerStyle markerStyle) =>
        markerStyle switch
        {
            ChartMarkerStyle.None => MarkerType.None,
            ChartMarkerStyle.Square => MarkerType.Square,
            ChartMarkerStyle.Diamond => MarkerType.Diamond,
            ChartMarkerStyle.Triangle => MarkerType.Triangle,
            _ => MarkerType.Circle
        };

    private static void AddTrendlineInfoIfRequested(
        PlotModel model,
        ChartModel chart,
        IReadOnlyList<DataPoint> sourcePoints,
        IReadOnlyList<DataPoint> trendPoints,
        IReadOnlyList<DataPoint> displaySourcePoints)
    {
        if (!chart.ShowTrendlineEquation && !chart.ShowTrendlineRSquared)
            return;

        var lines = new List<string>();
        if (chart.ShowTrendlineEquation)
            lines.Add(GetTrendlineEquationText(chart, trendPoints));
        if (chart.ShowTrendlineRSquared && TryCalculateRSquared(sourcePoints, trendPoints, out var rSquared))
            lines.Add($"R² = {rSquared:0.0000}");
        if (lines.Count == 0)
            return;

        model.Annotations.Add(new TextAnnotation
        {
            Text = string.Join(Environment.NewLine, lines),
            TextPosition = new DataPoint(
                displaySourcePoints.Min(point => point.X),
                displaySourcePoints.Max(point => point.Y)),
            TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left,
            TextVerticalAlignment = OxyPlot.VerticalAlignment.Top,
            Background = OxyColor.FromAColor(220, OxyColors.White),
            Stroke = OxyColors.LightGray,
            StrokeThickness = 1,
            Padding = new OxyThickness(4)
        });
    }

    private static string GetTrendlineEquationText(ChartModel chart, IReadOnlyList<DataPoint> trendPoints)
    {
        if (chart.TrendlineType == ChartTrendlineType.MovingAverage)
            return $"Moving average ({Math.Max(2, chart.TrendlinePeriod)})";
        if (chart.TrendlineType == ChartTrendlineType.Polynomial)
            return $"Polynomial (order {Math.Clamp(chart.TrendlineOrder, 2, 6)})";
        if (trendPoints.Count < 2)
            return GetTrendlineTitle(chart.TrendlineType);

        var first = trendPoints[0];
        var last = trendPoints[^1];
        var dx = last.X - first.X;
        if (Math.Abs(dx) < double.Epsilon)
            return GetTrendlineTitle(chart.TrendlineType);

        return chart.TrendlineType switch
        {
            ChartTrendlineType.Exponential when first.Y > 0 && last.Y > 0 =>
                FormatExponentialEquation(first, last, dx),
            ChartTrendlineType.Logarithmic when first.X > 0 && last.X > 0 =>
                FormatLogarithmicEquation(first, last),
            ChartTrendlineType.Power when first.X > 0 && last.X > 0 && first.Y > 0 && last.Y > 0 =>
                FormatPowerEquation(first, last),
            _ => FormatLinearEquation(first, last, dx)
        };
    }

    private static string FormatLinearEquation(DataPoint first, DataPoint last, double dx)
    {
        var slope = (last.Y - first.Y) / dx;
        var intercept = first.Y - (slope * first.X);
        return $"y = {slope:0.###}x {FormatSigned(intercept)}";
    }

    private static string FormatExponentialEquation(DataPoint first, DataPoint last, double dx)
    {
        var b = Math.Log(last.Y / first.Y) / dx;
        var a = first.Y / Math.Exp(b * first.X);
        return $"y = {a:0.###}e^({b:0.###}x)";
    }

    private static string FormatLogarithmicEquation(DataPoint first, DataPoint last)
    {
        var dLogX = Math.Log(last.X) - Math.Log(first.X);
        if (Math.Abs(dLogX) < double.Epsilon)
            return "Logarithmic Trendline";

        var b = (last.Y - first.Y) / dLogX;
        var a = first.Y - (b * Math.Log(first.X));
        return $"y = {b:0.###}ln(x) {FormatSigned(a)}";
    }

    private static string FormatPowerEquation(DataPoint first, DataPoint last)
    {
        var dLogX = Math.Log(last.X) - Math.Log(first.X);
        if (Math.Abs(dLogX) < double.Epsilon)
            return "Power Trendline";

        var b = Math.Log(last.Y / first.Y) / dLogX;
        var a = first.Y / Math.Pow(first.X, b);
        return $"y = {a:0.###}x^{b:0.###}";
    }

    private static string FormatSigned(double value) =>
        value < 0 ? $"- {Math.Abs(value):0.###}" : $"+ {value:0.###}";

    private static bool TryCalculateRSquared(
        IReadOnlyList<DataPoint> sourcePoints,
        IReadOnlyList<DataPoint> trendPoints,
        out double rSquared)
    {
        rSquared = 0;
        var matches = new List<(double Actual, double Predicted)>();
        foreach (var point in sourcePoints)
        {
            if (TryInterpolateTrendY(trendPoints, point.X, out var predicted))
                matches.Add((point.Y, predicted));
        }

        if (matches.Count < 2)
            return false;

        var mean = matches.Average(match => match.Actual);
        var total = matches.Sum(match => Math.Pow(match.Actual - mean, 2));
        if (Math.Abs(total) < double.Epsilon)
            return false;

        var residual = matches.Sum(match => Math.Pow(match.Actual - match.Predicted, 2));
        rSquared = 1 - (residual / total);
        return !double.IsNaN(rSquared) && !double.IsInfinity(rSquared);
    }

    private static bool TryInterpolateTrendY(IReadOnlyList<DataPoint> trendPoints, double x, out double y)
    {
        y = 0;
        if (trendPoints.Count == 0 || x < trendPoints[0].X || x > trendPoints[^1].X)
            return false;

        for (var i = 1; i < trendPoints.Count; i++)
        {
            var left = trendPoints[i - 1];
            var right = trendPoints[i];
            if (x > right.X)
                continue;

            var dx = right.X - left.X;
            if (Math.Abs(dx) < double.Epsilon)
            {
                y = right.Y;
                return true;
            }

            var t = (x - left.X) / dx;
            y = left.Y + ((right.Y - left.Y) * t);
            return true;
        }

        return false;
    }

    private static string GetTrendlineTitle(ChartTrendlineType type) =>
        type switch
        {
            ChartTrendlineType.Exponential => "Exponential Trendline",
            ChartTrendlineType.Logarithmic => "Logarithmic Trendline",
            ChartTrendlineType.Power => "Power Trendline",
            ChartTrendlineType.MovingAverage => "Moving Average",
            ChartTrendlineType.Polynomial => "Polynomial Trendline",
            _ => "Linear Trendline"
        };

    private static IReadOnlyList<DataPoint> CalculateLinearTrendline(IReadOnlyList<DataPoint> points)
    {
        var n = points.Count;
        var sumX = points.Sum(point => point.X);
        var sumY = points.Sum(point => point.Y);
        var sumXY = points.Sum(point => point.X * point.Y);
        var sumXX = points.Sum(point => point.X * point.X);
        var denominator = (n * sumXX) - (sumX * sumX);
        if (Math.Abs(denominator) < double.Epsilon)
            return [];

        var slope = ((n * sumXY) - (sumX * sumY)) / denominator;
        var intercept = (sumY - (slope * sumX)) / n;
        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        return [new DataPoint(minX, intercept + slope * minX), new DataPoint(maxX, intercept + slope * maxX)];
    }

    private static IReadOnlyList<DataPoint> CalculateExponentialTrendline(IReadOnlyList<DataPoint> points)
    {
        var positivePoints = points.Where(point => point.Y > 0).ToList();
        if (positivePoints.Count < 2)
            return [];

        var n = positivePoints.Count;
        var sumX = positivePoints.Sum(point => point.X);
        var sumLogY = positivePoints.Sum(point => Math.Log(point.Y));
        var sumXLogY = positivePoints.Sum(point => point.X * Math.Log(point.Y));
        var sumXX = positivePoints.Sum(point => point.X * point.X);
        var denominator = (n * sumXX) - (sumX * sumX);
        if (Math.Abs(denominator) < double.Epsilon)
            return [];

        var b = ((n * sumXLogY) - (sumX * sumLogY)) / denominator;
        var logA = (sumLogY - (b * sumX)) / n;
        var a = Math.Exp(logA);
        var minX = positivePoints.Min(point => point.X);
        var maxX = positivePoints.Max(point => point.X);
        return [new DataPoint(minX, a * Math.Exp(b * minX)), new DataPoint(maxX, a * Math.Exp(b * maxX))];
    }

    private static IReadOnlyList<DataPoint> CalculateLogarithmicTrendline(IReadOnlyList<DataPoint> points)
    {
        var positiveXPoints = points.Where(point => point.X > 0).ToList();
        if (positiveXPoints.Count < 2)
            return [];

        var n = positiveXPoints.Count;
        var sumLogX = positiveXPoints.Sum(point => Math.Log(point.X));
        var sumY = positiveXPoints.Sum(point => point.Y);
        var sumLogXY = positiveXPoints.Sum(point => Math.Log(point.X) * point.Y);
        var sumLogXLogX = positiveXPoints.Sum(point => Math.Log(point.X) * Math.Log(point.X));
        var denominator = (n * sumLogXLogX) - (sumLogX * sumLogX);
        if (Math.Abs(denominator) < double.Epsilon)
            return [];

        var slope = ((n * sumLogXY) - (sumLogX * sumY)) / denominator;
        var intercept = (sumY - (slope * sumLogX)) / n;
        var minX = positiveXPoints.Min(point => point.X);
        var maxX = positiveXPoints.Max(point => point.X);
        return [
            new DataPoint(minX, intercept + slope * Math.Log(minX)),
            new DataPoint(maxX, intercept + slope * Math.Log(maxX))];
    }

    private static IReadOnlyList<DataPoint> CalculatePowerTrendline(IReadOnlyList<DataPoint> points)
    {
        var positivePoints = points.Where(point => point.X > 0 && point.Y > 0).ToList();
        if (positivePoints.Count < 2)
            return [];

        var n = positivePoints.Count;
        var sumLogX = positivePoints.Sum(point => Math.Log(point.X));
        var sumLogY = positivePoints.Sum(point => Math.Log(point.Y));
        var sumLogXLogY = positivePoints.Sum(point => Math.Log(point.X) * Math.Log(point.Y));
        var sumLogXLogX = positivePoints.Sum(point => Math.Log(point.X) * Math.Log(point.X));
        var denominator = (n * sumLogXLogX) - (sumLogX * sumLogX);
        if (Math.Abs(denominator) < double.Epsilon)
            return [];

        var b = ((n * sumLogXLogY) - (sumLogX * sumLogY)) / denominator;
        var logA = (sumLogY - (b * sumLogX)) / n;
        var a = Math.Exp(logA);
        var minX = positivePoints.Min(point => point.X);
        var maxX = positivePoints.Max(point => point.X);
        return [
            new DataPoint(minX, a * Math.Pow(minX, b)),
            new DataPoint(maxX, a * Math.Pow(maxX, b))];
    }

    private static IReadOnlyList<DataPoint> CalculateMovingAverageTrendline(IReadOnlyList<DataPoint> points, int period)
    {
        var windowSize = Math.Max(2, period);
        if (points.Count < windowSize)
            return [];

        var trendPoints = new List<DataPoint>();
        for (var i = windowSize - 1; i < points.Count; i++)
        {
            var average = points.Skip(i - windowSize + 1).Take(windowSize).Average(point => point.Y);
            trendPoints.Add(new DataPoint(points[i].X, average));
        }

        return trendPoints;
    }

    private static IReadOnlyList<DataPoint> CalculatePolynomialTrendline(IReadOnlyList<DataPoint> points, int order)
    {
        var degree = Math.Clamp(order, 2, 6);
        if (points.Count <= degree)
            return [];

        var coefficients = SolvePolynomialLeastSquares(points, degree);
        if (coefficients is null)
            return [];

        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        var samples = Math.Max(16, points.Count * 4);
        var trendPoints = new List<DataPoint>(samples);
        for (var i = 0; i < samples; i++)
        {
            var x = samples == 1 ? minX : minX + ((maxX - minX) * i / (samples - 1));
            trendPoints.Add(new DataPoint(x, EvaluatePolynomial(coefficients, x)));
        }

        return trendPoints;
    }

    private static double[]? SolvePolynomialLeastSquares(IReadOnlyList<DataPoint> points, int degree)
    {
        var size = degree + 1;
        var matrix = new double[size, size];
        var vector = new double[size];

        for (var row = 0; row < size; row++)
        {
            for (var col = 0; col < size; col++)
                matrix[row, col] = points.Sum(point => Math.Pow(point.X, row + col));

            vector[row] = points.Sum(point => point.Y * Math.Pow(point.X, row));
        }

        return SolveLinearSystem(matrix, vector);
    }

    private static double EvaluatePolynomial(IReadOnlyList<double> coefficients, double x)
    {
        var y = 0.0;
        var power = 1.0;
        foreach (var coefficient in coefficients)
        {
            y += coefficient * power;
            power *= x;
        }

        return y;
    }

    private static double[]? SolveLinearSystem(double[,] matrix, double[] vector)
    {
        var size = vector.Length;
        for (var pivot = 0; pivot < size; pivot++)
        {
            var pivotRow = pivot;
            for (var row = pivot + 1; row < size; row++)
            {
                if (Math.Abs(matrix[row, pivot]) > Math.Abs(matrix[pivotRow, pivot]))
                    pivotRow = row;
            }

            if (Math.Abs(matrix[pivotRow, pivot]) < 1e-10)
                return null;

            if (pivotRow != pivot)
            {
                for (var col = pivot; col < size; col++)
                    (matrix[pivot, col], matrix[pivotRow, col]) = (matrix[pivotRow, col], matrix[pivot, col]);
                (vector[pivot], vector[pivotRow]) = (vector[pivotRow], vector[pivot]);
            }

            var divisor = matrix[pivot, pivot];
            for (var col = pivot; col < size; col++)
                matrix[pivot, col] /= divisor;
            vector[pivot] /= divisor;

            for (var row = 0; row < size; row++)
            {
                if (row == pivot)
                    continue;

                var factor = matrix[row, pivot];
                for (var col = pivot; col < size; col++)
                    matrix[row, col] -= factor * matrix[pivot, col];
                vector[row] -= factor * vector[pivot];
            }
        }

        return vector;
    }

    private static void AddSecondaryAxisIfRequested(PlotModel model, ChartModel chart)
    {
        if (!chart.ShowSecondaryAxis || !ChartTypeSupport.SupportsSecondaryAxis(chart.Type))
            return;

        if (!HasAnySecondaryAxisSeries(chart))
            return;

        if (model.Axes.Any(axis => axis.Key == SecondaryYAxisKey))
            return;

        model.Axes.Add(new LinearAxis
        {
            Key = SecondaryYAxisKey,
            Position = AxisPosition.Right,
            Title = "Secondary"
        });
    }

    private static bool HasAnySecondaryAxisSeries(ChartModel chart)
    {
        var seriesCount = ChartTypeSupport.GetDataSeriesCount(chart);
        if (seriesCount < 2)
            return false;

        return chart.SecondaryAxisSeriesIndexes.Count == 0
            ? seriesCount > 1
            : chart.SecondaryAxisSeriesIndexes.Any(index => index > 0 && index < seriesCount);
    }

    private static void ConfigureLegend(PlotModel model, ChartModel chart, WorkbookTheme theme)
    {
        if (!chart.ShowLegend || chart.LegendPosition == ChartLegendPosition.None)
            return;

        var legend = new Legend
        {
            LegendPlacement = chart.LegendOverlay ? LegendPlacement.Inside : LegendPlacement.Outside,
            LegendTextColor = ToOxyColor(chart.ResolveLegendTextColor(theme)) ?? OxyColors.Automatic,
            LegendFontSize = chart.LegendFontSize,
            LegendBackground = ToOxyColor(chart.ResolveLegendFillColor(theme)) ?? OxyColors.Undefined,
            LegendBorder = ToOxyColor(chart.ResolveLegendBorderColor(theme)) ?? OxyColors.Undefined,
            LegendBorderThickness = chart.LegendBorderThickness,
            LegendPosition = GetLegendPosition(chart.LegendPosition, chart.LegendOverlay)
        };
        model.Legends.Add(legend);
    }

    private static OxyPlot.Legends.LegendPosition GetLegendPosition(ChartLegendPosition position, bool overlay) =>
        position switch
        {
            ChartLegendPosition.Left => overlay ? OxyPlot.Legends.LegendPosition.LeftTop : OxyPlot.Legends.LegendPosition.LeftMiddle,
            ChartLegendPosition.Top => OxyPlot.Legends.LegendPosition.TopCenter,
            ChartLegendPosition.Bottom => OxyPlot.Legends.LegendPosition.BottomCenter,
            _ => overlay ? OxyPlot.Legends.LegendPosition.RightTop : OxyPlot.Legends.LegendPosition.RightMiddle
        };
}
