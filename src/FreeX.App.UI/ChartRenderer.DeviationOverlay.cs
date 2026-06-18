using System.Globalization;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Series;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public static partial class ChartRenderer
{
    // Excel's default up/down-bar accents on a Budget-vs-Actual combo: upBars=accent6 (green),
    // downBars=accent4 (blue/orange). Used only when the chart XML omits explicit colors.
    private static readonly OxyColor DefaultUpBarColor = OxyColor.FromRgb(0x70, 0xAD, 0x47);
    private static readonly OxyColor DefaultDownBarColor = OxyColor.FromRgb(0x44, 0x72, 0xC4);

    /// <summary>
    /// Draws a thin, sign-colored "deviation" bar between the first two clustered column series
    /// for each category — Excel's <c>&lt;c:upDownBars&gt;</c> idiom on a Budget-vs-Actual combo.
    /// The bar spans from the first series' value (Budget) to the second's (Actual); it is colored
    /// with the up-bar fill when the second value exceeds the first and the down-bar fill otherwise.
    /// No-op unless <see cref="ChartModel.ShowUpDownBars"/> is set and at least two bar series exist.
    /// </summary>
    private static void AddDeviationOverlay(
        PlotModel model,
        ChartModel chart,
        WorkbookTheme theme,
        IReadOnlyList<List<double?>> clusteredBarValues)
    {
        if (!chart.ShowUpDownBars || clusteredBarValues.Count < 2)
            return;

        var first = clusteredBarValues[0];
        var second = clusteredBarValues[1];
        var upColor = ResolveUpDownBarColor(chart.UpBarFillThemeColor?.Resolve(theme) ?? chart.UpBarFillColor, DefaultUpBarColor);
        var downColor = ResolveUpDownBarColor(chart.DownBarFillThemeColor?.Resolve(theme) ?? chart.DownBarFillColor, DefaultDownBarColor);

        // Excel's up/down bars are a thin connector centered on the category. Width is driven by the
        // up/down gapWidth (larger gap => thinner bar); fall back to a slim default.
        var halfWidth = chart.UpDownBarGapWidth is { } gap
            ? Math.Clamp(0.5 * 100.0 / (100.0 + gap), 0.03, 0.25)
            : 0.06;

        var upSeries = new RectangleBarSeries { StrokeThickness = 0, Title = "" };
        var downSeries = new RectangleBarSeries { StrokeThickness = 0, Title = "" };

        var pointCount = Math.Min(first.Count, second.Count);
        for (var i = 0; i < pointCount; i++)
        {
            if (first[i] is not { } budget || second[i] is not { } actual)
                continue;
            if (Math.Abs(actual - budget) < double.Epsilon)
                continue; // zero deviation: nothing to draw

            var lo = Math.Min(budget, actual);
            var hi = Math.Max(budget, actual);
            var rising = actual > budget;
            var item = new RectangleBarItem(i - halfWidth, lo, i + halfWidth, hi)
            {
                Color = rising ? upColor : downColor
            };
            (rising ? upSeries : downSeries).Items.Add(item);
        }

        // Draw bars on top of the columns. Add down (then up) so both overlays sit above the columns.
        if (downSeries.Items.Count > 0)
            model.Series.Add(downSeries);
        if (upSeries.Items.Count > 0)
            model.Series.Add(upSeries);
    }

    private static OxyColor ResolveUpDownBarColor(CellColor? color, OxyColor fallback) =>
        color is { } value ? OxyColor.FromRgb(value.R, value.G, value.B) : fallback;

    /// <summary>
    /// Draws Excel's "Value From Cells" data labels (<c>c15:datalabelsRange</c>) as text above each
    /// category. The literal cached strings (emoji + percent) are positioned just above the taller of
    /// the first two clustered column series so they float over the cluster, matching Excel. Labels
    /// from any series index are merged per category (multiple series may each label a subset).
    /// </summary>
    private static void AddRangeDataLabelAnnotations(
        PlotModel model,
        ChartModel chart,
        WorkbookTheme theme,
        IReadOnlyList<List<double?>> clusteredBarValues,
        IReadOnlyList<string> categories)
    {
        if (chart.RangeDataLabels.Count == 0 || clusteredBarValues.Count == 0)
            return;

        // Merge labels per category (point index). When two series both label the same point keep the
        // first; in practice each category is labeled by exactly one of the Budget/Actual ranges.
        var byPoint = new Dictionary<int, string>();
        foreach (var label in chart.RangeDataLabels)
        {
            if (string.IsNullOrEmpty(label.Text))
                continue;
            byPoint.TryAdd(label.PointIndex, label.Text);
        }

        if (byPoint.Count == 0)
            return;

        var textColor = chart.ResolveDataLabelTextColor(theme);
        var oxyTextColor = textColor is { } c ? OxyColor.FromRgb(c.R, c.G, c.B) : OxyColors.Black;

        foreach (var (pointIndex, text) in byPoint)
        {
            var top = CategoryTopValue(clusteredBarValues, pointIndex);
            if (top is not { } y)
                continue;

            model.Annotations.Add(new TextAnnotation
            {
                Text = text,
                TextPosition = new DataPoint(pointIndex, y),
                TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                TextVerticalAlignment = OxyPlot.VerticalAlignment.Bottom,
                TextColor = oxyTextColor,
                FontSize = chart.DataLabelFontSize > 0 ? chart.DataLabelFontSize : 11,
                Stroke = OxyColors.Transparent,
                Background = OxyColors.Transparent,
                Padding = new OxyThickness(2)
            });
        }
    }

    /// <summary>Returns the tallest value across clustered bar series at <paramref name="pointIndex"/>.</summary>
    private static double? CategoryTopValue(IReadOnlyList<List<double?>> clusteredBarValues, int pointIndex)
    {
        double? top = null;
        foreach (var seriesValues in clusteredBarValues)
        {
            if (pointIndex < 0 || pointIndex >= seriesValues.Count)
                continue;
            if (seriesValues[pointIndex] is not { } v)
                continue;
            top = top is { } existing ? Math.Max(existing, v) : v;
        }

        return top;
    }
}
