using System.Windows.Media;
using System.Windows.Media.Imaging;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using OxyPlot.Wpf;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

/// <summary>Renders a ChartModel into a WPF ImageSource for use in DrawingContext.</summary>
public static class ChartRenderer
{
    public static ImageSource? Render(ChartModel chart, ViewportModel viewport)
    {
        var model = BuildPlotModel(chart, viewport);
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

    private static PlotModel? BuildPlotModel(ChartModel chart, ViewportModel viewport)
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
        ConfigureLegend(model, chart);

        if (chart.Type == ChartType.Pie)
        {
            var pieSeries = new PieSeries { StrokeThickness = 1.0, InsideLabelPosition = 0.8 };
            for (uint r = dataStartRow; r <= endRow; r++)
            {
                if (!cellLookup.TryGetValue((r, dataStartCol), out var cell)) continue;
                if (!double.TryParse(cell.DisplayText, out var v)) continue;
                var label = categories.Count > (int)(r - dataStartRow) ? categories[(int)(r - dataStartRow)] : "";
                pieSeries.Slices.Add(new PieSlice(label, v));
            }
            model.Series.Add(pieSeries);
            return model;
        }

        // Column / Line: one series per data column
        for (uint col = dataStartCol; col <= endCol; col++)
        {
            string seriesName = chart.FirstRowIsHeader && cellLookup.TryGetValue((startRow, col), out var hdr)
                ? hdr.DisplayText : $"Series {col - dataStartCol + 1}";

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
                }

                var series = new RectangleBarSeries { Title = seriesName };
                var i = 0;
                for (uint r = dataStartRow; r <= endRow; r++, i++)
                {
                    if (cellLookup.TryGetValue((r, col), out var cell)
                        && double.TryParse(cell.DisplayText, out var v))
                    {
                        series.Items.Add(new RectangleBarItem(i - 0.35, Math.Min(0, v), i + 0.35, Math.Max(0, v)));
                    }
                }
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

                var series = new BarSeries { Title = seriesName };
                for (uint r = dataStartRow; r <= endRow; r++)
                {
                    if (cellLookup.TryGetValue((r, col), out var cell)
                        && double.TryParse(cell.DisplayText, out var v))
                        series.Items.Add(new BarItem { Value = v });
                }
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
                }

                var series = new LineSeries { Title = seriesName };
                int i = 0;
                for (uint r = dataStartRow; r <= endRow; r++, i++)
                {
                    if (cellLookup.TryGetValue((r, col), out var cell)
                        && double.TryParse(cell.DisplayText, out var v))
                        series.Points.Add(new DataPoint(i, v));
                }
                model.Series.Add(series);
            }
        }

        return model;
    }

    private static void ConfigureLegend(PlotModel model, ChartModel chart)
    {
        if (!chart.ShowLegend || chart.LegendPosition == ChartLegendPosition.None)
            return;

        var legend = new Legend
        {
            LegendPlacement = LegendPlacement.Outside,
            LegendPosition = chart.LegendPosition switch
            {
                ChartLegendPosition.Left => OxyPlot.Legends.LegendPosition.LeftMiddle,
                ChartLegendPosition.Top => OxyPlot.Legends.LegendPosition.TopCenter,
                ChartLegendPosition.Bottom => OxyPlot.Legends.LegendPosition.BottomCenter,
                _ => OxyPlot.Legends.LegendPosition.RightMiddle
            }
        };
        model.Legends.Add(legend);
    }
}
