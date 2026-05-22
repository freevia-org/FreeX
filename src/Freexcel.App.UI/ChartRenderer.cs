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

        if (chart.Type == ChartType.Radar)
            return BuildRadarModel(chart, model, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow, theme);

        if (chart.Type == ChartType.Stock)
            return BuildStockModel(chart, model, cellLookup, categories, dataStartRow, endRow, dataStartCol, endCol, startRow, theme);

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

    private static PlotModel BuildRadarModel(
        ChartModel chart,
        PlotModel model,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        IReadOnlyList<string> categories,
        uint dataStartRow,
        uint endRow,
        uint dataStartCol,
        uint endCol,
        uint headerRow,
        WorkbookTheme theme)
    {
        model.PlotType = PlotType.Polar;
        var pointCount = Math.Max(1, categories.Count);
        model.Axes.Add(new AngleAxis
        {
            StartAngle = 90,
            EndAngle = 450,
            Minimum = 0,
            Maximum = 360,
            MajorStep = 360.0 / pointCount,
            MinorStep = 360.0 / pointCount,
            LabelFormatter = angle =>
            {
                var index = (int)Math.Round((((angle % 360) + 360) % 360) / (360.0 / pointCount));
                index %= pointCount;
                return index >= 0 && index < categories.Count ? categories[index] : "";
            }
        });
        model.Axes.Add(new MagnitudeAxis { Minimum = 0, Title = chart.YAxisTitle });

        for (uint col = dataStartCol; col <= endCol; col++)
        {
            var seriesIndex = GetSeriesIndex(chart, col, dataStartCol);
            var seriesName = chart.FirstRowIsHeader && cellLookup.TryGetValue((headerRow, col), out var header)
                ? header.DisplayText
                : $"Series {seriesIndex + 1}";
            var series = CreateLineSeries(chart, seriesName, seriesIndex, theme);
            series.MarkerType = MarkerType.Circle;

            DataPoint? firstPoint = null;
            var i = 0;
            for (uint row = dataStartRow; row <= endRow; row++, i++)
            {
                if (!cellLookup.TryGetValue((row, col), out var cell) ||
                    !double.TryParse(cell.DisplayText, out var value))
                    continue;

                var point = new DataPoint(i * 360.0 / pointCount, value);
                firstPoint ??= point;
                series.Points.Add(point);
            }

            if (firstPoint is { } closedPoint && series.Points.Count > 1)
                series.Points.Add(closedPoint);

            model.Series.Add(series);
        }

        return model;
    }

    private static PlotModel BuildStockModel(
        ChartModel chart,
        PlotModel model,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        IReadOnlyList<string> categories,
        uint dataStartRow,
        uint endRow,
        uint dataStartCol,
        uint endCol,
        uint headerRow,
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
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = chart.YAxisTitle });

        var valueColumnCount = endCol >= dataStartCol ? endCol - dataStartCol + 1 : 0;
        var hasOpenColumn = valueColumnCount >= 4;
        var highCol = hasOpenColumn ? dataStartCol + 1 : dataStartCol;
        var lowCol = hasOpenColumn ? dataStartCol + 2 : dataStartCol + 1;
        var closeCol = hasOpenColumn ? dataStartCol + 3 : dataStartCol + 2;
        if (valueColumnCount < 3 || closeCol > endCol)
            return model;

        var series = new HighLowSeries
        {
            Title = "Stock",
            StrokeThickness = 1.5,
            Color = OxyColors.Black
        };

        for (uint row = dataStartRow; row <= endRow; row++)
        {
            var index = row - dataStartRow;
            if (!TryGetNumericCell(cellLookup, row, highCol, out var high) ||
                !TryGetNumericCell(cellLookup, row, lowCol, out var low) ||
                !TryGetNumericCell(cellLookup, row, closeCol, out var close))
                continue;

            var open = hasOpenColumn && TryGetNumericCell(cellLookup, row, dataStartCol, out var parsedOpen)
                ? parsedOpen
                : close;
            series.Items.Add(new HighLowItem(index, high, low, open, close));
        }

        model.Series.Add(series);
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
                LabelFormatString = ChartDataLabelFormatter.GetNativeValueLabelFormat(chart, 4)
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
                    AddDataLabelAnnotation(model, chart, theme, seriesName, seriesIndex, i, ChartDataLabelFormatter.GetCategory(categories, i), i, end, GetStackedLabelValue(chart, normalizeToPercent, value, displayValue));
            }

            model.Series.Add(series);
        }

        return model;
    }

    private static void AddPivotChartFieldButtons(PlotModel model, ChartModel chart)
    {
        if (!chart.IsPivotChart || !chart.ShowPivotChartFieldButtons)
            return;

        var captions = new List<string>();
        if (chart.ShowPivotChartReportFilterButtons)
            captions.Add(string.IsNullOrWhiteSpace(chart.PivotTableName) ? "PivotTable" : chart.PivotTableName);
        if (chart.ShowPivotChartAxisFieldButtons)
            captions.Add("Axis Fields");
        if (chart.ShowPivotChartValueFieldButtons)
            captions.Add("Values");

        for (var index = 0; index < captions.Count; index++)
        {
            model.Annotations.Add(new TextAnnotation
            {
                Text = captions[index],
                TextPosition = new DataPoint(index * 1.2, 0),
                Stroke = OxyColor.FromRgb(128, 128, 128),
                StrokeThickness = 1,
                Background = OxyColor.FromRgb(242, 242, 242),
                TextColor = OxyColor.FromRgb(64, 64, 64),
                FontSize = 10,
                Padding = new OxyThickness(4, 2, 4, 2)
            });
        }
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
                LabelFormatString = ChartDataLabelFormatter.GetNativeValueLabelFormat(chart, 4)
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
                    AddDataLabelAnnotation(model, chart, theme, seriesName, seriesIndex, i, ChartDataLabelFormatter.GetCategory(categories, i), end, i, GetStackedLabelValue(chart, normalizeToPercent, value, displayValue));
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
                LabelFormatString = ChartDataLabelFormatter.GetNativeValueLabelFormat(chart, 1),
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
                    AddDataLabelAnnotation(model, chart, theme, seriesName, seriesIndex, fallbackIndex, ChartDataLabelFormatter.GetCategory(categories, fallbackIndex), x, y, y);
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
        normalizeToPercent && ChartDataLabelFormatter.ShouldRenderPercentageLabels(chart)
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
        ChartDataLabelFormatter.ShouldUseNativeValueLabels(chart);

    private static void ApplyNativeDataLabelStyle(PlotElement element, ChartModel chart, WorkbookTheme theme)
    {
        if (!ShouldUseNativeValueLabels(chart))
            return;

        element.FontSize = chart.DataLabelFontSize;
        if (chart.ResolveDataLabelTextColor(theme) is { } color)
            element.TextColor = OxyColor.FromRgb(color.R, color.G, color.B);
    }

    private static bool ShouldUseAnnotationLabels(ChartModel chart) =>
        ChartDataLabelFormatter.ShouldUseAnnotationLabels(chart);

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
            Text = ChartDataLabelFormatter.FormatDataLabel(chart, seriesName, categoryName, value),
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
                ChartDataLabelFormatter.GetCategory(categories, (int)Math.Round(point.X)),
                point.X,
                point.Y,
                point.Y);
        }
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

        var trendPoints = ChartTrendlineCalculator.Calculate(
            chart.TrendlineType,
            points,
            chart.TrendlinePeriod,
            chart.TrendlineOrder);
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
        if (chart.ShowTrendlineRSquared && ChartTrendlineCalculator.TryCalculateRSquared(sourcePoints, trendPoints, out var rSquared))
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
