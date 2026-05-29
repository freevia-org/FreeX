using System.Globalization;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public static partial class ChartRenderer
{
    internal static PlotModel BuildParetoModel(
        ChartModel chart,
        PlotModel model,
        Dictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        List<string> categories,
        uint dataStartRow, uint endRow, uint dataStartCol,
        WorkbookTheme theme)
    {
        var values = new List<(string Label, double Value)>();
        for (uint r = dataStartRow; r <= endRow; r++)
        {
            if (cellLookup.TryGetValue((r, dataStartCol), out var cell) &&
                double.TryParse(cell.DisplayText, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            {
                var label = (int)(r - dataStartRow) < categories.Count
                    ? categories[(int)(r - dataStartRow)]
                    : $"Item {r - dataStartRow + 1}";
                values.Add((label, v));
            }
        }

        values.Sort((a, b) => b.Value.CompareTo(a.Value));
        if (values.Count == 0) return model;

        var total = 0.0;
        for (var index = 0; index < values.Count; index++)
            total += values[index].Value;

        var bars = new RectangleBarSeries { FillColor = OxyColor.FromRgb(68, 114, 196) };
        var cumulativeLine = new LineSeries
        {
            Color = OxyColors.OrangeRed,
            StrokeThickness = 2.0,
            MarkerType = MarkerType.Circle,
            MarkerSize = 4,
            YAxisKey = "right"
        };

        double runningSum = 0;
        for (int i = 0; i < values.Count; i++)
        {
            bars.Items.Add(new RectangleBarItem(i - 0.4, 0, i + 0.4, values[i].Value));
            runningSum += values[i].Value;
            cumulativeLine.Points.Add(new DataPoint(i, total > 0 ? 100.0 * runningSum / total : 0));
        }
        model.Series.Add(bars);
        model.Series.Add(cumulativeLine);

        var catAxis = new CategoryAxis { Position = AxisPosition.Bottom, Title = chart.XAxisTitle };
        foreach (var (label, _) in values)
            catAxis.Labels.Add(label);
        model.Axes.Add(catAxis);
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = chart.YAxisTitle?.Length > 0 ? chart.YAxisTitle : "Count",
            Minimum = 0
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Right,
            Key = "right",
            Title = "%",
            Minimum = 0,
            Maximum = 100
        });

        return model;
    }

    internal static PlotModel BuildBoxAndWhiskerModel(
        ChartModel chart,
        PlotModel model,
        Dictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        List<string> categories,
        uint dataStartRow, uint endRow, uint dataStartCol, uint endCol, uint startRow,
        WorkbookTheme theme)
    {
        var boxSeries = new BoxPlotSeries
        {
            Fill = OxyColor.FromRgb(91, 155, 213),
            Stroke = OxyColor.FromRgb(31, 73, 125),
            StrokeThickness = 1.5,
            WhiskerWidth = 0.5,
            BoxWidth = 0.4
        };

        var seriesLabels = new List<string>();
        if (chart.FirstRowIsHeader)
            for (uint col = dataStartCol; col <= endCol; col++)
            {
                var name = cellLookup.TryGetValue((startRow, col), out var h) ? h.DisplayText : $"S{col - dataStartCol + 1}";
                seriesLabels.Add(name);
            }

        int boxIndex = 0;
        for (uint col = dataStartCol; col <= endCol; col++)
        {
            var colValues = new List<double>();
            for (uint r = dataStartRow; r <= endRow; r++)
                if (cellLookup.TryGetValue((r, col), out var cell) &&
                    double.TryParse(cell.DisplayText, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                    colValues.Add(v);

            if (colValues.Count > 0)
            {
                colValues.Sort();
                double q1 = BoxPercentile(colValues, 25);
                double median = BoxPercentile(colValues, 50);
                double q3 = BoxPercentile(colValues, 75);
                double iqr = q3 - q1;
                double lowerFence = q1 - 1.5 * iqr;
                double upperFence = q3 + 1.5 * iqr;
                var lowerWhisker = colValues[0];
                for (var index = 0; index < colValues.Count; index++)
                {
                    if (colValues[index] >= lowerFence)
                    {
                        lowerWhisker = colValues[index];
                        break;
                    }
                }

                var upperWhisker = colValues[^1];
                for (var index = colValues.Count - 1; index >= 0; index--)
                {
                    if (colValues[index] <= upperFence)
                    {
                        upperWhisker = colValues[index];
                        break;
                    }
                }

                var item = new BoxPlotItem(boxIndex, lowerWhisker, q1, median, q3, upperWhisker);
                for (var index = 0; index < colValues.Count; index++)
                {
                    var value = colValues[index];
                    if (value < lowerFence || value > upperFence)
                        item.Outliers.Add(value);
                }

                boxSeries.Items.Add(item);
            }
            boxIndex++;
        }

        model.Series.Add(boxSeries);

        var catAxis = new CategoryAxis { Position = AxisPosition.Bottom, Title = chart.XAxisTitle };
        for (int i = 0; i < boxIndex; i++)
            catAxis.Labels.Add(seriesLabels.Count > i ? seriesLabels[i] : $"Series {i + 1}");
        model.Axes.Add(catAxis);
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = chart.YAxisTitle });

        return model;
    }

    private static double BoxPercentile(List<double> sorted, double pct)
    {
        if (sorted.Count == 1) return sorted[0];
        double pos = pct / 100.0 * (sorted.Count - 1);
        int lo = (int)pos;
        int hi = lo + 1;
        if (hi >= sorted.Count) return sorted[^1];
        return sorted[lo] + (pos - lo) * (sorted[hi] - sorted[lo]);
    }

    internal static PlotModel BuildTreemapModel(
        ChartModel chart,
        PlotModel model,
        Dictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        List<string> categories,
        uint dataStartRow, uint endRow, uint dataStartCol,
        WorkbookTheme theme)
    {
        var values = new List<(string Label, double Value)>();
        for (uint r = dataStartRow; r <= endRow; r++)
        {
            if (cellLookup.TryGetValue((r, dataStartCol), out var cell) &&
                double.TryParse(cell.DisplayText, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) && v > 0)
            {
                var label = (int)(r - dataStartRow) < categories.Count
                    ? categories[(int)(r - dataStartRow)]
                    : $"Item {r - dataStartRow + 1}";
                values.Add((label, v));
            }
        }

        if (values.Count == 0) return model;

        var total = 0.0;
        for (var index = 0; index < values.Count; index++)
            total += values[index].Value;

        double x = 0;

        for (int i = 0; i < values.Count; i++)
        {
            double w = values[i].Value / total;
            var color = PieSlicePalette[i % PieSlicePalette.Length];
            model.Annotations.Add(new RectangleAnnotation
            {
                MinimumX = x,
                MaximumX = x + w,
                MinimumY = 0,
                MaximumY = 1,
                Fill = OxyColor.FromArgb(220, color.R, color.G, color.B),
                Stroke = OxyColors.White,
                StrokeThickness = 2
            });
            // Label in the center of each tile
            model.Annotations.Add(new TextAnnotation
            {
                Text = values[i].Label,
                TextPosition = new DataPoint(x + w / 2, 0.5),
                TextColor = OxyColors.White,
                FontSize = 10,
                Stroke = OxyColors.Transparent,
                Background = OxyColors.Undefined
            });
            x += w;
        }

        model.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, IsAxisVisible = false, Minimum = 0, Maximum = 1 });
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, IsAxisVisible = false, Minimum = 0, Maximum = 1 });

        return model;
    }

    internal static PlotModel BuildSunburstModel(
        ChartModel chart,
        PlotModel model,
        Dictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        List<string> categories,
        uint dataStartRow, uint endRow, uint dataStartCol,
        WorkbookTheme theme)
    {
        var pieSeries = new PieSeries
        {
            StrokeThickness = 1.5,
            InnerDiameter = 0.35,
            StartAngle = 0,
            OutsideLabelFormat = "{0}",
            InsideLabelFormat = "",
            InsideLabelPosition = 0.6
        };

        for (uint r = dataStartRow; r <= endRow; r++)
        {
            if (!cellLookup.TryGetValue((r, dataStartCol), out var cell)) continue;
            if (!double.TryParse(cell.DisplayText, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) || v <= 0) continue;
            var label = (int)(r - dataStartRow) < categories.Count
                ? categories[(int)(r - dataStartRow)]
                : $"Item {r - dataStartRow + 1}";
            var sliceIndex = pieSeries.Slices.Count;
            pieSeries.Slices.Add(new PieSlice(label, v)
            {
                Fill = PieSlicePalette[sliceIndex % PieSlicePalette.Length]
            });
        }

        model.Series.Add(pieSeries);
        return model;
    }

    internal static PlotModel BuildFunnelModel(
        ChartModel chart,
        PlotModel model,
        Dictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        List<string> categories,
        uint dataStartRow, uint endRow, uint dataStartCol,
        WorkbookTheme theme)
    {
        var values = new List<(string Label, double Value)>();
        for (uint r = dataStartRow; r <= endRow; r++)
        {
            if (cellLookup.TryGetValue((r, dataStartCol), out var cell) &&
                double.TryParse(cell.DisplayText, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            {
                var label = (int)(r - dataStartRow) < categories.Count
                    ? categories[(int)(r - dataStartRow)]
                    : $"Stage {r - dataStartRow + 1}";
                values.Add((label, Math.Abs(v)));
            }
        }

        if (values.Count == 0) return model;

        var maxVal = 0.0;
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index].Value > maxVal)
                maxVal = values[index].Value;
        }

        if (maxVal == 0) return model;

        for (int i = 0; i < values.Count; i++)
        {
            double halfWidth = values[i].Value / maxVal * 0.45;
            double yTop = -(i);
            double yBot = -(i + 0.9);
            var color = PieSlicePalette[i % PieSlicePalette.Length];

            model.Annotations.Add(new RectangleAnnotation
            {
                MinimumX = 0.5 - halfWidth,
                MaximumX = 0.5 + halfWidth,
                MinimumY = yBot,
                MaximumY = yTop,
                Fill = OxyColor.FromArgb(210, color.R, color.G, color.B),
                Stroke = OxyColors.White,
                StrokeThickness = 1.5
            });
            model.Annotations.Add(new TextAnnotation
            {
                Text = values[i].Label,
                TextPosition = new DataPoint(0.5, yBot + 0.45),
                TextColor = OxyColors.White,
                FontSize = 10,
                Stroke = OxyColors.Transparent,
                Background = OxyColors.Undefined
            });
        }

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            IsAxisVisible = false,
            Minimum = 0,
            Maximum = 1,
            Title = chart.XAxisTitle
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            IsAxisVisible = false,
            Minimum = -(values.Count + 0.1),
            Maximum = 0.5,
            Title = chart.YAxisTitle
        });

        return model;
    }
}
