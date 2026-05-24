using System.Globalization;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public static partial class ChartRenderer
{
    private static readonly OxyColor WaterfallPositiveColor = OxyColor.FromRgb(84, 130, 53);
    private static readonly OxyColor WaterfallNegativeColor = OxyColor.FromRgb(192, 0, 0);
    private static readonly OxyColor WaterfallTotalColor    = OxyColor.FromRgb(68, 114, 196);

    internal static PlotModel BuildWaterfallModel(
        ChartModel chart,
        PlotModel model,
        Dictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        List<string> categories,
        uint dataStartRow, uint endRow, uint dataStartCol,
        WorkbookTheme theme)
    {
        // Collect values from the first data column
        var values = new List<double>();
        for (uint r = dataStartRow; r <= endRow; r++)
        {
            if (cellLookup.TryGetValue((r, dataStartCol), out var cell) &&
                double.TryParse(cell.DisplayText, NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v))
                values.Add(v);
            else
                values.Add(0);
        }

        int n = values.Count;
        var bars = new RectangleBarSeries { FillColor = OxyColors.Transparent };

        double running = 0;
        for (int i = 0; i < n; i++)
        {
            double bottom = running;
            double top    = running + values[i];
            bool isTotal  = i == n - 1;
            bool isPos    = values[i] >= 0;

            var color = isTotal ? WaterfallTotalColor
                      : isPos   ? WaterfallPositiveColor
                                : WaterfallNegativeColor;

            var rectItem = new RectangleBarItem(i - 0.35, Math.Min(bottom, top), i + 0.35, Math.Max(bottom, top))
            {
                Color = color
            };
            bars.Items.Add(rectItem);

            if (!isTotal)
                running = top;
        }

        model.Series.Add(bars);

        var categoryAxis = new CategoryAxis
        {
            Position = AxisPosition.Bottom,
            Title = chart.XAxisTitle,
            IsTickCentered = true
        };
        foreach (var cat in categories)
            categoryAxis.Labels.Add(cat);
        if (categoryAxis.Labels.Count == 0)
            for (int i = 0; i < n; i++)
                categoryAxis.Labels.Add($"Point {i + 1}");
        model.Axes.Add(categoryAxis);
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = chart.YAxisTitle });

        return model;
    }

    internal static PlotModel BuildHistogramModel(
        ChartModel chart,
        PlotModel model,
        Dictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        uint dataStartRow, uint endRow, uint dataStartCol,
        WorkbookTheme theme)
    {
        // Collect all numeric values from the first data column
        var rawValues = new List<double>();
        for (uint r = dataStartRow; r <= endRow; r++)
            if (cellLookup.TryGetValue((r, dataStartCol), out var cell) &&
                double.TryParse(cell.DisplayText, NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v))
                rawValues.Add(v);

        if (rawValues.Count == 0) return model;

        int binCount = Math.Max(2, (int)Math.Ceiling(Math.Sqrt(rawValues.Count)));
        double min   = rawValues.Min();
        double max   = rawValues.Max();
        double range = max - min;
        if (range == 0) range = 1;
        double binWidth = range / binCount;

        var counts = new int[binCount];
        foreach (var v in rawValues)
        {
            int idx = Math.Min(binCount - 1, (int)Math.Floor((v - min) / binWidth));
            counts[idx]++;
        }

        var bars = new RectangleBarSeries { FillColor = WaterfallTotalColor };
        for (int i = 0; i < binCount; i++)
            bars.Items.Add(new RectangleBarItem(i - 0.45, 0, i + 0.45, counts[i]));
        model.Series.Add(bars);

        var catAxis = new CategoryAxis { Position = AxisPosition.Bottom, Title = chart.XAxisTitle };
        for (int i = 0; i < binCount; i++)
        {
            double binMin = min + i * binWidth;
            double binMax = binMin + binWidth;
            catAxis.Labels.Add($"{binMin:G4}–{binMax:G4}");
        }
        model.Axes.Add(catAxis);
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = chart.YAxisTitle?.Length > 0 ? chart.YAxisTitle : "Frequency" });

        return model;
    }
}
