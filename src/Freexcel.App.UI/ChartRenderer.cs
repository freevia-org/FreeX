using System.Windows.Media;
using System.Windows.Media.Imaging;
using OxyPlot;
using OxyPlot.Axes;
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
                var catAxis = new CategoryAxis { Position = AxisPosition.Left };
                catAxis.Labels.AddRange(categories);
                if (!model.Axes.Any(a => a is CategoryAxis))
                {
                    model.Axes.Add(catAxis);
                    model.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom });
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
}
