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
        var fontSize = chart.DataLabelFontSize > 0 ? chart.DataLabelFontSize : 11;

        foreach (var (pointIndex, text) in byPoint)
        {
            var top = CategoryTopValue(clusteredBarValues, pointIndex);
            if (top is not { } y)
                continue;

            AddRangeDataLabel(model, new DataPoint(pointIndex, y), text, oxyTextColor, fontSize);
        }
    }

    /// <summary>
    /// Adds a single "Value From Cells" label at <paramref name="position"/>. When the label leads with a
    /// drawable emoji (👍 👎 👌) the emoji is drawn as a COLOR image annotation and the remaining percent
    /// text as a text annotation just to its right; otherwise the whole label is one text annotation.
    /// OxyPlot.Wpf renders annotation TEXT through a monochrome glyph path, so emoji on that path come out
    /// flat black/gray — splitting them onto the image path restores Excel's colored thumbs.
    /// </summary>
    private static void AddRangeDataLabel(
        PlotModel model,
        DataPoint position,
        string label,
        OxyColor textColor,
        double fontSize)
    {
        var (emoji, rest) = ChartEmojiGlyphs.SplitLeadingDrawableEmoji(label);

        if (emoji.Length == 0)
        {
            // No drawable emoji: keep the existing single-annotation behavior unchanged.
            model.Annotations.Add(CreateLabelTextAnnotation(label, position, textColor, fontSize,
                OxyPlot.HorizontalAlignment.Center, offsetX: 0));
            return;
        }

        // Render the emoji to a colored PNG. The exporter renders at 2x+ scale, so request a crisp glyph.
        var emojiBitmap = ChartEmojiGlyphs.RenderEmojiPng(emoji, fontSize, renderScale: 4.0);
        if (emojiBitmap is not { } bmp)
        {
            // Rendering failed: fall back to the full original label on the text path.
            model.Annotations.Add(CreateLabelTextAnnotation(label, position, textColor, fontSize,
                OxyPlot.HorizontalAlignment.Center, offsetX: 0));
            return;
        }

        // Display the emoji glyph at ~font height. Width follows the glyph's aspect ratio.
        var glyphHeight = fontSize * 1.15;
        var glyphWidth = glyphHeight * (bmp.PixelWidth / (double)bmp.PixelHeight);
        const double gap = 2.0; // px between emoji and text

        // Center the (emoji + gap + text) group over the category: shift the emoji left of center and
        // the text right of center by roughly half the emoji-block width. We don't know the text's pixel
        // width here, so we bias the emoji left by half its own width plus the gap and let the text sit
        // just to its right — visually matching Excel's "👍 30%" layout.
        if (string.IsNullOrEmpty(rest))
        {
            // Emoji only — center it.
            model.Annotations.Add(new ImageAnnotation
            {
                ImageSource = new OxyImage(bmp.Png),
                X = new PlotLength(position.X, PlotLengthUnit.Data),
                Y = new PlotLength(position.Y, PlotLengthUnit.Data),
                Width = new PlotLength(glyphWidth, PlotLengthUnit.ScreenUnits),
                Height = new PlotLength(glyphHeight, PlotLengthUnit.ScreenUnits),
                HorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                VerticalAlignment = OxyPlot.VerticalAlignment.Bottom,
                Interpolate = true
            });
            return;
        }

        var emojiOffsetX = -(glyphWidth / 2.0 + gap / 2.0);
        var textOffsetX = glyphWidth / 2.0 + gap / 2.0;

        model.Annotations.Add(new ImageAnnotation
        {
            ImageSource = new OxyImage(bmp.Png),
            X = new PlotLength(position.X, PlotLengthUnit.Data),
            Y = new PlotLength(position.Y, PlotLengthUnit.Data),
            OffsetX = new PlotLength(emojiOffsetX, PlotLengthUnit.ScreenUnits),
            Width = new PlotLength(glyphWidth, PlotLengthUnit.ScreenUnits),
            Height = new PlotLength(glyphHeight, PlotLengthUnit.ScreenUnits),
            HorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
            VerticalAlignment = OxyPlot.VerticalAlignment.Bottom,
            Interpolate = true
        });

        // Percent text just to the right of the emoji, left-aligned at the gap boundary.
        model.Annotations.Add(CreateLabelTextAnnotation(rest, position, textColor, fontSize,
            OxyPlot.HorizontalAlignment.Left, offsetX: textOffsetX));
    }

    private static TextAnnotation CreateLabelTextAnnotation(
        string text,
        DataPoint position,
        OxyColor textColor,
        double fontSize,
        OxyPlot.HorizontalAlignment horizontalAlignment,
        double offsetX) => new()
    {
        Text = text,
        TextPosition = new DataPoint(position.X, position.Y),
        Offset = new ScreenVector(offsetX, 0),
        TextHorizontalAlignment = horizontalAlignment,
        TextVerticalAlignment = OxyPlot.VerticalAlignment.Bottom,
        TextColor = textColor,
        FontSize = fontSize,
        Stroke = OxyColors.Transparent,
        Background = OxyColors.Transparent,
        Padding = new OxyThickness(2)
    };

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
