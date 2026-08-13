using System.Globalization;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using FreeX.App.Presentation.Charts;
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
        var totalsByLabel = new Dictionary<string, double>(StringComparer.Ordinal);
        var labels = new List<string>();
        var total = 0.0;
        for (uint r = dataStartRow; r <= endRow; r++)
        {
            if (cellLookup.TryGetValue((r, dataStartCol), out var cell) &&
                TryGetChartNumericValue(cell, out var v))
            {
                var label = (int)(r - dataStartRow) < categories.Count
                    ? categories[(int)(r - dataStartRow)]
                    : $"Item {r - dataStartRow + 1}";
                if (!totalsByLabel.ContainsKey(label))
                    labels.Add(label);
                totalsByLabel[label] = totalsByLabel.TryGetValue(label, out var current) ? current + v : v;
                total += v;
            }
        }

        var values = new List<(string Label, double Value)>(labels.Count);
        for (var i = 0; i < labels.Count; i++)
            values.Add((labels[i], totalsByLabel[labels[i]]));
        values.Sort((a, b) => b.Value.CompareTo(a.Value));
        if (values.Count == 0) return model;

        // Excel-native Pareto carries owner-linked chartEx metadata that ChartModel does not expose;
        // this renderer keeps a local approximation with aggregated bars and a percentage axis.
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
            Maximum = 100,
            MajorStep = 20,
            LabelFormatter = value => value.ToString("0", CultureInfo.InvariantCulture) + "%"
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
                    TryGetChartNumericValue(cell, out var v))
                    colValues.Add(v);

            var statistics = ChartRenderPolicyPlanner.PlanBoxAndWhisker(colValues);
            if (statistics is not null)
            {
                var item = new BoxPlotItem(
                    boxIndex,
                    statistics.LowerWhisker,
                    statistics.FirstQuartile,
                    statistics.Median,
                    statistics.ThirdQuartile,
                    statistics.UpperWhisker);
                foreach (var outlier in statistics.Outliers)
                    item.Outliers.Add(outlier);

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

    internal static PlotModel BuildTreemapModel(
        ChartModel chart,
        PlotModel model,
        Dictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        List<string> categories,
        uint dataStartRow, uint endRow, uint dataStartCol,
        WorkbookTheme theme)
    {
        var values = new List<(string Label, double Value)>();
        var total = 0.0;
        for (uint r = dataStartRow; r <= endRow; r++)
        {
            if (cellLookup.TryGetValue((r, dataStartCol), out var cell) &&
                TryGetChartNumericValue(cell, out var v) && v > 0)
            {
                var label = (int)(r - dataStartRow) < categories.Count
                    ? categories[(int)(r - dataStartRow)]
                    : $"Item {r - dataStartRow + 1}";
                values.Add((label, v));
                total += v;
            }
        }

        if (values.Count == 0) return model;

        var treemapPalette = BuildExcelSeriesPalette(theme);
        double x = 0;

        for (int i = 0; i < values.Count; i++)
        {
            double w = values[i].Value / total;
            var color = treemapPalette[i % treemapPalette.Count];
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

        var sunburstPalette = BuildExcelSeriesPalette(theme);
        for (uint r = dataStartRow; r <= endRow; r++)
        {
            if (!cellLookup.TryGetValue((r, dataStartCol), out var cell)) continue;
            if (!TryGetChartNumericValue(cell, out var v) || v <= 0) continue;
            var label = (int)(r - dataStartRow) < categories.Count
                ? categories[(int)(r - dataStartRow)]
                : $"Item {r - dataStartRow + 1}";
            var sliceIndex = pieSeries.Slices.Count;
            pieSeries.Slices.Add(new PieSlice(label, v)
            {
                Fill = sunburstPalette[sliceIndex % sunburstPalette.Count]
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
        var maxVal = 0.0;
        for (uint r = dataStartRow; r <= endRow; r++)
        {
            if (cellLookup.TryGetValue((r, dataStartCol), out var cell) &&
                TryGetChartNumericValue(cell, out var v))
            {
                var label = (int)(r - dataStartRow) < categories.Count
                    ? categories[(int)(r - dataStartRow)]
                    : $"Stage {r - dataStartRow + 1}";
                var value = Math.Abs(v);
                values.Add((label, value));
                if (value > maxVal)
                    maxVal = value;
            }
        }

        if (values.Count == 0) return model;

        if (maxVal == 0) return model;

        var funnelPalette = BuildExcelSeriesPalette(theme);
        for (int i = 0; i < values.Count; i++)
        {
            double halfWidth = values[i].Value / maxVal * 0.45;
            double yTop = -(i);
            double yBot = -(i + 0.9);
            var color = funnelPalette[i % funnelPalette.Count];

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
