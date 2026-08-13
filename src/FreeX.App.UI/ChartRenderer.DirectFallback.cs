using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public static partial class ChartRenderer
{
    private static readonly Color[] DirectChartPalette =
    [
        Color.FromRgb(68, 114, 196),
        Color.FromRgb(237, 125, 49),
        Color.FromRgb(165, 165, 165),
        Color.FromRgb(255, 192, 0),
        Color.FromRgb(91, 155, 213),
        Color.FromRgb(112, 173, 71)
    ];

    private static readonly Typeface DirectChartTypeface = new("Segoe UI");
    private static readonly Pen DirectChartAxisPen = CreateFrozenPen(Color.FromRgb(89, 89, 89), 1);
    private static readonly Pen DirectChartGridlinePen = CreateFrozenPen(Color.FromRgb(226, 226, 226), 1);
    private static readonly Brush DirectChartTextBrush = CreateFrozenBrush(Color.FromRgb(64, 64, 64));
    private static readonly Brush DirectChartPlotFillBrush = CreateFrozenBrush(Colors.White);

    internal static ImageSource? RenderDirectFallback(
        ChartModel chart,
        ViewportModel viewport,
        WorkbookTheme theme,
        double renderScale)
    {
        var data = BuildDirectChartData(chart, viewport, theme);
        if (data is null || data.Series.Count == 0)
            return null;

        var width = Math.Max(1.0, chart.Width);
        var height = Math.Max(1.0, chart.Height);
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(width * renderScale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(height * renderScale));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            RenderDirectChart(dc, chart, data, theme, new Rect(0, 0, width, height));
        }

        var bitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            96.0 * renderScale,
            96.0 * renderScale,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static bool IsVisiblyBlank(BitmapSource bitmap)
    {
        if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            return true;

        var source = bitmap.Format == PixelFormats.Bgra32
            ? bitmap
            : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);

        var visiblePixels = 0;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            var blue = pixels[offset];
            var green = pixels[offset + 1];
            var red = pixels[offset + 2];
            var alpha = pixels[offset + 3];
            if (alpha > 10 && (red < 245 || green < 245 || blue < 245) && ++visiblePixels >= 8)
                return false;
        }

        return true;
    }

    private sealed record DirectChartData(
        IReadOnlyList<string> Categories,
        IReadOnlyList<DirectChartSeries> Series);

    private sealed record DirectChartSeries(
        string Name,
        IReadOnlyList<double?> Values,
        Brush Fill,
        Pen Stroke);

    internal enum DirectLegendFlow
    {
        None,
        Vertical,
        Horizontal
    }

    internal readonly record struct DirectChartLayout(
        Rect Plot,
        Rect Legend,
        DirectLegendFlow LegendFlow);

    private static DirectChartData? BuildDirectChartData(ChartModel chart, ViewportModel viewport, WorkbookTheme theme)
    {
        var accessor = ChartViewportCellAccessorBuilder.BuildValueAccessor(
            viewport,
            chart.DataRange.Start.Sheet,
            chart.DataRange);
        var dataPlan = ChartLayoutRequestBuilder.TryResolveData(
            chart,
            accessor,
            missingScatterXOffset: 1);
        if (dataPlan is null || dataPlan.Series.Count == 0)
            return null;

        if (chart.Type == ChartType.Scatter && !chart.FirstColIsCategories)
            return BuildDirectScatterData(chart, dataPlan, theme);

        var categoryCount = Math.Max(
            dataPlan.Categories.Count,
            dataPlan.Series.Count == 0 ? 0 : dataPlan.Series.Max(series => series.Values.Count));
        var categories = new List<string>(categoryCount);
        for (var pointIndex = 0; pointIndex < categoryCount; pointIndex++)
        {
            categories.Add(pointIndex < dataPlan.Categories.Count &&
                !string.IsNullOrWhiteSpace(dataPlan.Categories[pointIndex])
                    ? dataPlan.Categories[pointIndex]
                    : (pointIndex + 1).ToString(CultureInfo.InvariantCulture));
        }

        var series = new List<DirectChartSeries>();
        foreach (var sourceSeries in dataPlan.Series)
        {
            if (!sourceSeries.Values.Any(value => value is not null))
                continue;

            var name = string.IsNullOrWhiteSpace(sourceSeries.Name)
                ? $"Series {sourceSeries.SeriesIndex + 1}"
                : sourceSeries.Name!;
            series.Add(CreateDirectSeries(
                chart,
                theme,
                sourceSeries.SeriesIndex,
                name,
                sourceSeries.Values));
        }

        return series.Count == 0 ? null : new DirectChartData(categories, series);
    }

    private static DirectChartData? BuildDirectScatterData(
        ChartModel chart,
        ChartDataPlan dataPlan,
        WorkbookTheme theme)
    {
        var sourceSeries = dataPlan.Series.FirstOrDefault();
        if (sourceSeries is null || !sourceSeries.Values.Any(value => value is not null))
            return null;

        var categories = new List<string>(sourceSeries.Values.Count);
        for (var pointIndex = 0; pointIndex < sourceSeries.Values.Count; pointIndex++)
        {
            var x = sourceSeries.XValues is { } xValues && pointIndex < xValues.Count
                ? xValues[pointIndex]
                : pointIndex + 1;
            categories.Add(x.ToString(CultureInfo.InvariantCulture));
        }

        var name = string.IsNullOrWhiteSpace(sourceSeries.Name) ? "Series 1" : sourceSeries.Name!;
        return new DirectChartData(
            categories,
            [CreateDirectSeries(chart, theme, sourceSeries.SeriesIndex, name, sourceSeries.Values)]);
    }

    private static DirectChartSeries CreateDirectSeries(
        ChartModel chart,
        WorkbookTheme theme,
        int seriesIndex,
        string name,
        IReadOnlyList<double?> values)
    {
        var format = GetSeriesFormat(chart, seriesIndex);
        var fillColor = format?.ResolveFillColor(theme) is { } fill
            ? ToMediaColor(fill)
            : DirectChartPalette[Math.Abs(seriesIndex) % DirectChartPalette.Length];
        var strokeColor = format?.ResolveStrokeColor(theme) is { } stroke
            ? ToMediaColor(stroke)
            : Darken(fillColor, 0.68);
        var thickness = Math.Clamp(format?.StrokeThickness ?? 1.0, 0.5, 6.0);
        return new DirectChartSeries(
            name,
            values,
            CreateFrozenBrush(fillColor),
            CreateFrozenPen(strokeColor, thickness));
    }

    private static void RenderDirectChart(
        DrawingContext dc,
        ChartModel chart,
        DirectChartData data,
        WorkbookTheme theme,
        Rect rect)
    {
        if (chart.Type is ChartType.Pie or ChartType.ThreeDPie or ChartType.Doughnut)
        {
            DrawDirectPieChart(dc, chart, data, rect);
            return;
        }

        var titleHeight = string.IsNullOrWhiteSpace(chart.Title) ? 8.0 : 34.0;
        if (!string.IsNullOrWhiteSpace(chart.Title))
            DrawCenteredText(dc, chart.Title!, rect.Left, rect.Top + 6, rect.Width, chart.ChartTitleFontSize, DirectChartTextBrush);

        var layout = PlanDirectChartLayout(chart, data.Series.Count, rect, titleHeight);
        dc.DrawRectangle(DirectChartPlotFillBrush, null, layout.Plot);

        if (chart.Type is ChartType.Bar or ChartType.ThreeDBar or ChartType.StackedBar or ChartType.PercentStackedBar)
            DrawDirectBarChart(dc, chart, data, layout.Plot);
        else
            DrawDirectCartesianChart(dc, chart, data, layout.Plot);

        if (layout.LegendFlow != DirectLegendFlow.None)
            DrawDirectLegend(dc, chart, data, theme, layout.Legend, layout.LegendFlow);
    }

    internal static DirectChartLayout PlanDirectChartLayout(
        ChartModel chart,
        int seriesCount,
        Rect rect,
        double titleHeight)
    {
        var showLegend = chart.ShowLegend &&
            chart.LegendPosition != ChartLegendPosition.None &&
            seriesCount > 1;
        if (!showLegend)
        {
            var plot = new Rect(
                rect.Left + 46,
                rect.Top + titleHeight,
                Math.Max(24, rect.Width - 60),
                Math.Max(24, rect.Height - titleHeight - 42));
            return new DirectChartLayout(plot, Rect.Empty, DirectLegendFlow.None);
        }

        var legendFontSize = GetDirectLegendFontSize(chart);
        var legendRowHeight = GetDirectLegendRowHeight(legendFontSize);
        if (chart.LegendPosition == ChartLegendPosition.Bottom)
        {
            var legendHeight = Math.Min(56.0, Math.Max(28.0, legendRowHeight * Math.Min(2, seriesCount) + 2));
            var plot = new Rect(
                rect.Left + 46,
                rect.Top + titleHeight,
                Math.Max(24, rect.Width - 60),
                Math.Max(24, rect.Height - titleHeight - 42 - legendHeight));
            var legend = new Rect(
                plot.Left,
                plot.Bottom + 22,
                plot.Width,
                Math.Max(18, rect.Bottom - plot.Bottom - 24));
            return new DirectChartLayout(plot, legend, DirectLegendFlow.Horizontal);
        }

        const double legendWidth = 84.0;
        var verticalPlot = new Rect(
            rect.Left + 46,
            rect.Top + titleHeight,
            Math.Max(24, rect.Width - 60 - legendWidth),
            Math.Max(24, rect.Height - titleHeight - 42));
        var verticalLegend = new Rect(
            verticalPlot.Right + 10,
            verticalPlot.Top + 6,
            Math.Max(12, legendWidth - 12),
            Math.Max(12, verticalPlot.Height - 12));
        return new DirectChartLayout(verticalPlot, verticalLegend, DirectLegendFlow.Vertical);
    }

    private static void DrawDirectCartesianChart(DrawingContext dc, ChartModel chart, DirectChartData data, Rect plot)
    {
        var (minimum, maximum) = GetDirectValueRange(data, includeZero: true);
        DrawDirectValueGrid(dc, plot, minimum, maximum, horizontal: true, hideLabels: chart.HideYAxis);
        if (!chart.HideXAxis)
            dc.DrawLine(DirectChartAxisPen, new Point(plot.Left, MapY(0, minimum, maximum, plot)), new Point(plot.Right, MapY(0, minimum, maximum, plot)));
        if (!chart.HideYAxis)
            dc.DrawLine(DirectChartAxisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));

        if (chart.Type is ChartType.Line or ChartType.ThreeDLine)
            DrawDirectLineChart(dc, data, plot, minimum, maximum, fillArea: false);
        else if (chart.Type is ChartType.Area or ChartType.StackedArea or ChartType.PercentStackedArea or ChartType.ThreeDArea)
            DrawDirectLineChart(dc, data, plot, minimum, maximum, fillArea: true);
        else if (chart.Type == ChartType.Scatter)
            DrawDirectScatterChart(dc, data, plot, minimum, maximum);
        else
            DrawDirectColumnChart(dc, data, plot, minimum, maximum);

        DrawDirectCategoryLabels(dc, data.Categories, plot);
    }

    private static void DrawDirectColumnChart(
        DrawingContext dc,
        DirectChartData data,
        Rect plot,
        double minimum,
        double maximum)
    {
        var categoryCount = Math.Max(1, data.Categories.Count);
        var seriesCount = Math.Max(1, data.Series.Count);
        var groupWidth = plot.Width / categoryCount;
        var barWidth = Math.Max(2, groupWidth * 0.72 / seriesCount);
        var baseline = MapY(0, minimum, maximum, plot);

        for (var categoryIndex = 0; categoryIndex < data.Categories.Count; categoryIndex++)
        {
            var groupLeft = plot.Left + groupWidth * categoryIndex + (groupWidth - barWidth * seriesCount) / 2.0;
            for (var seriesIndex = 0; seriesIndex < data.Series.Count; seriesIndex++)
            {
                var value = data.Series[seriesIndex].Values.ElementAtOrDefault(categoryIndex);
                if (value is not { } number)
                    continue;

                var y = MapY(number, minimum, maximum, plot);
                var top = Math.Min(y, baseline);
                var height = Math.Max(1.0, Math.Abs(baseline - y));
                var left = groupLeft + seriesIndex * barWidth;
                dc.DrawRectangle(data.Series[seriesIndex].Fill, data.Series[seriesIndex].Stroke, new Rect(left, top, Math.Max(1, barWidth - 1), height));
            }
        }
    }

    private static void DrawDirectLineChart(
        DrawingContext dc,
        DirectChartData data,
        Rect plot,
        double minimum,
        double maximum,
        bool fillArea)
    {
        var categoryCount = Math.Max(1, data.Categories.Count);
        var xStep = categoryCount == 1 ? plot.Width : plot.Width / (categoryCount - 1);
        foreach (var series in data.Series)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                var started = false;
                for (var i = 0; i < series.Values.Count; i++)
                {
                    if (series.Values[i] is not { } number)
                        continue;

                    var point = new Point(plot.Left + xStep * i, MapY(number, minimum, maximum, plot));
                    if (!started)
                    {
                        context.BeginFigure(point, fillArea, false);
                        started = true;
                    }
                    else
                    {
                        context.LineTo(point, true, false);
                    }
                }
            }

            if (geometry.CanFreeze)
                geometry.Freeze();
            if (fillArea)
                dc.DrawGeometry(CreateFrozenBrush(Color.FromArgb(72, ((SolidColorBrush)series.Fill).Color.R, ((SolidColorBrush)series.Fill).Color.G, ((SolidColorBrush)series.Fill).Color.B)), series.Stroke, geometry);
            else
                dc.DrawGeometry(null, series.Stroke, geometry);

            for (var i = 0; i < series.Values.Count; i++)
            {
                if (series.Values[i] is not { } number)
                    continue;
                var center = new Point(plot.Left + xStep * i, MapY(number, minimum, maximum, plot));
                dc.DrawEllipse(series.Fill, series.Stroke, center, 3.0, 3.0);
            }
        }
    }

    private static void DrawDirectScatterChart(DrawingContext dc, DirectChartData data, Rect plot, double minimum, double maximum)
    {
        var xValues = data.Categories
            .Select(label => double.TryParse(label, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0)
            .ToArray();
        var xMinimum = xValues.Length == 0 ? 0 : xValues.Min();
        var xMaximum = xValues.Length == 0 ? 1 : xValues.Max();
        if (Math.Abs(xMaximum - xMinimum) < 0.000001)
            xMaximum = xMinimum + 1;

        foreach (var series in data.Series)
        {
            for (var i = 0; i < series.Values.Count && i < xValues.Length; i++)
            {
                if (series.Values[i] is not { } number)
                    continue;
                var x = plot.Left + (xValues[i] - xMinimum) / (xMaximum - xMinimum) * plot.Width;
                var y = MapY(number, minimum, maximum, plot);
                dc.DrawEllipse(series.Fill, series.Stroke, new Point(x, y), 3.5, 3.5);
            }
        }
    }

    private static void DrawDirectBarChart(DrawingContext dc, ChartModel chart, DirectChartData data, Rect plot)
    {
        var (minimum, maximum) = GetDirectValueRange(data, includeZero: true);
        DrawDirectValueGrid(dc, plot, minimum, maximum, horizontal: false, hideLabels: chart.HideXAxis);
        if (!chart.HideXAxis)
            dc.DrawLine(DirectChartAxisPen, new Point(MapX(0, minimum, maximum, plot), plot.Top), new Point(MapX(0, minimum, maximum, plot), plot.Bottom));
        if (!chart.HideYAxis)
            dc.DrawLine(DirectChartAxisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));

        var categoryCount = Math.Max(1, data.Categories.Count);
        var seriesCount = Math.Max(1, data.Series.Count);
        var groupHeight = plot.Height / categoryCount;
        var barHeight = Math.Max(2, groupHeight * 0.72 / seriesCount);
        var baseline = MapX(0, minimum, maximum, plot);
        for (var categoryIndex = 0; categoryIndex < data.Categories.Count; categoryIndex++)
        {
            var groupTop = plot.Top + groupHeight * categoryIndex + (groupHeight - barHeight * seriesCount) / 2.0;
            for (var seriesIndex = 0; seriesIndex < data.Series.Count; seriesIndex++)
            {
                var value = data.Series[seriesIndex].Values.ElementAtOrDefault(categoryIndex);
                if (value is not { } number)
                    continue;

                var x = MapX(number, minimum, maximum, plot);
                var left = Math.Min(x, baseline);
                var width = Math.Max(1.0, Math.Abs(baseline - x));
                var top = groupTop + seriesIndex * barHeight;
                dc.DrawRectangle(data.Series[seriesIndex].Fill, data.Series[seriesIndex].Stroke, new Rect(left, top, width, Math.Max(1, barHeight - 1)));
            }
        }
    }

    private static void DrawDirectPieChart(DrawingContext dc, ChartModel chart, DirectChartData data, Rect rect)
    {
        if (!string.IsNullOrWhiteSpace(chart.Title))
            DrawCenteredText(dc, chart.Title!, rect.Left, rect.Top + 6, rect.Width, chart.ChartTitleFontSize, DirectChartTextBrush);

        var values = data.Series[0].Values.Select(value => Math.Max(0, value ?? 0)).ToArray();
        var total = values.Sum();
        if (total <= 0)
            return;

        var top = string.IsNullOrWhiteSpace(chart.Title) ? rect.Top + 16 : rect.Top + 42;
        var diameter = Math.Max(24, Math.Min(rect.Width - 40, rect.Bottom - top - 24));
        var pieRect = new Rect(rect.Left + (rect.Width - diameter) / 2.0, top, diameter, diameter);
        var center = new Point(pieRect.Left + pieRect.Width / 2.0, pieRect.Top + pieRect.Height / 2.0);
        var radius = diameter / 2.0;
        var angle = chart.FirstSliceAngle - 90.0;
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] <= 0)
                continue;

            var sweep = values[i] / total * 360.0;
            var geometry = CreatePieSliceGeometry(center, radius, angle, sweep, chart.Type == ChartType.Doughnut ? radius * chart.DoughnutHoleSize : 0);
            dc.DrawGeometry(CreateFrozenBrush(DirectChartPalette[i % DirectChartPalette.Length]), CreateFrozenPen(Colors.White, 1), geometry);
            angle += sweep;
        }
    }

    private static StreamGeometry CreatePieSliceGeometry(Point center, double radius, double startAngle, double sweepAngle, double innerRadius)
    {
        var start = PointOnCircle(center, radius, startAngle);
        var end = PointOnCircle(center, radius, startAngle + sweepAngle);
        var largeArc = sweepAngle > 180.0;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, true, true);
            context.ArcTo(end, new Size(radius, radius), 0, largeArc, SweepDirection.Clockwise, true, false);
            if (innerRadius > 0)
            {
                var innerEnd = PointOnCircle(center, innerRadius, startAngle + sweepAngle);
                var innerStart = PointOnCircle(center, innerRadius, startAngle);
                context.LineTo(innerEnd, true, false);
                context.ArcTo(innerStart, new Size(innerRadius, innerRadius), 0, largeArc, SweepDirection.Counterclockwise, true, false);
            }
            else
            {
                context.LineTo(center, true, false);
            }
        }

        if (geometry.CanFreeze)
            geometry.Freeze();
        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point(center.X + Math.Cos(radians) * radius, center.Y + Math.Sin(radians) * radius);
    }

    private static void DrawDirectValueGrid(DrawingContext dc, Rect plot, double minimum, double maximum, bool horizontal, bool hideLabels)
    {
        const int tickCount = 4;
        for (var i = 0; i <= tickCount; i++)
        {
            var value = minimum + (maximum - minimum) * i / tickCount;
            if (horizontal)
            {
                var y = MapY(value, minimum, maximum, plot);
                dc.DrawLine(DirectChartGridlinePen, new Point(plot.Left, y), new Point(plot.Right, y));
                if (!hideLabels)
                    DrawRightAlignedText(dc, FormatDirectAxisLabel(value), plot.Left - 42, y - 8, 36, 10, DirectChartTextBrush);
            }
            else
            {
                var x = MapX(value, minimum, maximum, plot);
                dc.DrawLine(DirectChartGridlinePen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                if (!hideLabels)
                    DrawCenteredText(dc, FormatDirectAxisLabel(value), x - 20, plot.Bottom + 6, 40, 10, DirectChartTextBrush);
            }
        }
    }

    private static void DrawDirectCategoryLabels(DrawingContext dc, IReadOnlyList<string> categories, Rect plot)
    {
        if (categories.Count == 0)
            return;

        var step = plot.Width / categories.Count;
        var labelEvery = Math.Max(1, (int)Math.Ceiling(categories.Count / Math.Max(1.0, plot.Width / 48.0)));
        for (var i = 0; i < categories.Count; i += labelEvery)
        {
            var x = plot.Left + step * i + step / 2.0;
            DrawCenteredText(dc, categories[i], x - step / 2.0, plot.Bottom + 6, step, 10, DirectChartTextBrush);
        }
    }

    private static void DrawDirectLegend(
        DrawingContext dc,
        ChartModel chart,
        DirectChartData data,
        WorkbookTheme theme,
        Rect rect,
        DirectLegendFlow flow)
    {
        var textBrush = chart.ResolveLegendTextColor(theme) is { } textColor
            ? CreateFrozenBrush(ToMediaColor(textColor))
            : DirectChartTextBrush;
        var fontSize = GetDirectLegendFontSize(chart);
        if (flow == DirectLegendFlow.Horizontal)
            DrawDirectHorizontalLegend(dc, data, rect, fontSize, textBrush);
        else
            DrawDirectVerticalLegend(dc, data, rect, fontSize, textBrush);
    }

    private static void DrawDirectVerticalLegend(
        DrawingContext dc,
        DirectChartData data,
        Rect rect,
        double fontSize,
        Brush textBrush)
    {
        var rowHeight = GetDirectLegendRowHeight(fontSize);
        var y = rect.Top;
        foreach (var series in data.Series)
        {
            dc.DrawRectangle(series.Fill, series.Stroke, new Rect(rect.Left, y + 3, 10, 10));
            DrawText(dc, series.Name, rect.Left + 15, y, Math.Max(10, rect.Width - 15), fontSize, textBrush);
            y += rowHeight;
            if (y > rect.Bottom - rowHeight / 2)
                break;
        }
    }

    private static void DrawDirectHorizontalLegend(
        DrawingContext dc,
        DirectChartData data,
        Rect rect,
        double fontSize,
        Brush textBrush)
    {
        var rowHeight = GetDirectLegendRowHeight(fontSize);
        var x = rect.Left;
        var y = rect.Top;
        foreach (var series in data.Series)
        {
            var label = CreateFormattedText(series.Name, fontSize, textBrush, Math.Max(1, rect.Width));
            var itemWidth = Math.Min(rect.Width, Math.Max(44, label.WidthIncludingTrailingWhitespace + 24));
            if (x > rect.Left && x + itemWidth > rect.Right)
            {
                x = rect.Left;
                y += rowHeight;
                if (y > rect.Bottom - rowHeight / 2)
                    break;
            }

            dc.DrawRectangle(series.Fill, series.Stroke, new Rect(x, y + 3, 10, 10));
            DrawText(dc, series.Name, x + 15, y, Math.Max(10, itemWidth - 15), fontSize, textBrush);
            x += itemWidth + 12;
        }
    }

    private static double GetDirectLegendFontSize(ChartModel chart) =>
        Math.Clamp(chart.LegendFontSize, 8.0, 18.0);

    private static double GetDirectLegendRowHeight(double fontSize) =>
        Math.Max(16.0, fontSize + 8.0);

    private static (double Minimum, double Maximum) GetDirectValueRange(DirectChartData data, bool includeZero)
    {
        var values = data.Series.SelectMany(series => series.Values).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        if (values.Length == 0)
            return (0, 1);

        var minimum = values.Min();
        var maximum = values.Max();
        if (includeZero)
        {
            minimum = Math.Min(0, minimum);
            maximum = Math.Max(0, maximum);
        }

        if (Math.Abs(maximum - minimum) < 0.000001)
        {
            maximum += 1;
            minimum = Math.Min(0, minimum - 1);
        }

        return (minimum, maximum);
    }

    private static double MapY(double value, double minimum, double maximum, Rect plot) =>
        plot.Bottom - (value - minimum) / (maximum - minimum) * plot.Height;

    private static double MapX(double value, double minimum, double maximum, Rect plot) =>
        plot.Left + (value - minimum) / (maximum - minimum) * plot.Width;

    private static string FormatDirectAxisLabel(double value) =>
        Math.Abs(value) >= 100 || Math.Abs(value - Math.Round(value)) < 0.000001
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);

    private static void DrawCenteredText(DrawingContext dc, string text, double left, double top, double width, double fontSize, Brush brush)
    {
        var formatted = CreateFormattedText(text, fontSize, brush, Math.Max(1, width));
        dc.DrawText(formatted, new Point(left + (width - formatted.WidthIncludingTrailingWhitespace) / 2.0, top));
    }

    private static void DrawRightAlignedText(DrawingContext dc, string text, double left, double top, double width, double fontSize, Brush brush)
    {
        var formatted = CreateFormattedText(text, fontSize, brush, Math.Max(1, width));
        dc.DrawText(formatted, new Point(left + width - formatted.WidthIncludingTrailingWhitespace, top));
    }

    private static void DrawText(DrawingContext dc, string text, double left, double top, double width, double fontSize, Brush brush)
    {
        var formatted = CreateFormattedText(text, fontSize, brush, Math.Max(1, width));
        dc.DrawText(formatted, new Point(left, top));
    }

    private static FormattedText CreateFormattedText(string text, double fontSize, Brush brush, double maxWidth)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            DirectChartTypeface,
            fontSize,
            brush,
            1.0)
        {
            MaxTextWidth = maxWidth,
            Trimming = TextTrimming.CharacterEllipsis
        };
        return formatted;
    }

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreateFrozenPen(Color color, double thickness)
    {
        var pen = new Pen(CreateFrozenBrush(color), thickness);
        pen.Freeze();
        return pen;
    }

    private static Color ToMediaColor(CellColor color) =>
        Color.FromRgb(color.R, color.G, color.B);

    private static Color Darken(Color color, double factor) =>
        Color.FromRgb(
            (byte)Math.Clamp(color.R * factor, 0, 255),
            (byte)Math.Clamp(color.G * factor, 0, 255),
            (byte)Math.Clamp(color.B * factor, 0, 255));
}
