using System.Globalization;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public static partial class ChartRenderer
{
    private const string PieAnnotationXAxisKey = "PieAnnotationX";
    private const string PieAnnotationYAxisKey = "PieAnnotationY";
    private const int PointDataLabelFormatLookupThreshold = 16;

    private sealed record PieDataLabelPoint(string CategoryName, double Value);

    private readonly struct ChartPointDataLabelFormatLookup
    {
        private readonly IReadOnlyList<ChartPointDataLabelFormat>? _formats;
        private readonly Dictionary<(int SeriesIndex, int PointIndex), ChartPointDataLabelFormat>? _indexedFormats;

        public ChartPointDataLabelFormatLookup(IReadOnlyList<ChartPointDataLabelFormat> formats)
        {
            _formats = formats;
            if (formats.Count <= PointDataLabelFormatLookupThreshold)
            {
                _indexedFormats = null;
                return;
            }

            _indexedFormats = new Dictionary<(int SeriesIndex, int PointIndex), ChartPointDataLabelFormat>(formats.Count);
            for (var i = 0; i < formats.Count; i++)
            {
                var format = formats[i];
                _indexedFormats[(format.SeriesIndex, format.PointIndex)] = format;
            }
        }

        public ChartPointDataLabelFormat? Get(int seriesIndex, int pointIndex)
        {
            if (_indexedFormats is not null)
                return _indexedFormats.TryGetValue((seriesIndex, pointIndex), out var format) ? format : null;

            if (_formats is null)
                return null;

            for (var i = _formats.Count - 1; i >= 0; i--)
            {
                var format = _formats[i];
                if (format.SeriesIndex == seriesIndex && format.PointIndex == pointIndex)
                    return format;
            }

            return null;
        }
    }

    private static double ColumnBarHalfWidth(ChartModel chart) =>
        chart.BarGapWidth is int gapWidth
            // gapWidth=0 ⇒ half-width 0.5 so adjacent category bars touch (Excel's continuous look,
            // e.g. a shaded target band). Larger gapWidth narrows the bar toward the category centre.
            ? Math.Clamp(0.5 * 100.0 / (100.0 + gapWidth), 0.05, 0.5)
            : 0.35;

    /// <summary>
    /// Counts the clustered (grouped) bar/column series that share a single category slot —
    /// i.e. the Column/Bar series that are NOT promoted to a combo line or combo scatter overlay.
    /// A clustered chart with N such series draws N bars SIDE BY SIDE within each category.
    /// Stacked variants are handled separately and never reach this path.
    /// </summary>
    private static int CountClusteredBarSeries(
        ChartModel chart,
        uint dataStartCol,
        uint endCol)
    {
        var count = 0;
        for (var col = dataStartCol; col <= endCol; col++)
        {
            if (ShouldSkipScatterXColumn(chart, col, dataStartCol))
                continue;

            if (!ShouldRenderColumnAsSeries(chart, col, dataStartCol, endCol))
                continue;

            var seriesIndex = GetSeriesIndex(chart, col, dataStartCol, endCol);
            if (IsComboLineSeries(chart, seriesIndex) || IsComboScatterSeries(chart, seriesIndex))
                continue;

            count++;
        }

        return count;
    }

    /// <summary>
    /// Returns the left/right x-offsets (relative to the category centre) for the bar of the
    /// <paramref name="clusterOrdinal"/>-th clustered series, given the full category half-width
    /// and the total clustered-series count. With one series the bar fills the whole slot; with N
    /// series each occupies a disjoint 1/N sub-slot so the bars sit side by side (Excel's clustered
    /// layout).
    /// </summary>
    private static (double Left, double Right) ClusteredBarOffsets(
        double halfWidth,
        int clusterOrdinal,
        int clusterCount)
    {
        if (clusterCount <= 1)
            return (-halfWidth, halfWidth);

        var slotWidth = 2.0 * halfWidth / clusterCount;
        var left = -halfWidth + clusterOrdinal * slotWidth;
        return (left, left + slotWidth);
    }

    private static bool ShouldSkipScatterXColumn(ChartModel chart, uint col, uint dataStartCol) =>
        chart.Type == ChartType.Scatter
            && !chart.FirstColIsCategories
            && col == dataStartCol;

    /// <summary>
    /// True when the chart carries an authoritative series-to-column mapping (every series'
    /// value column is known and lies within the rendered data-column span). When this holds the
    /// renderer plots exactly those columns — using each series' chart-XML idx for format / combo /
    /// legend lookups — and skips any column that is not a mapped series (e.g. a worksheet column
    /// that falls inside the union data range but is not actually plotted, such as a "Target" helper
    /// column the chart does not reference).
    /// </summary>
    private static bool HasAuthoritativeSeriesColumns(ChartModel chart, uint dataStartCol, uint endCol)
    {
        // Column-based mappings cannot describe row-major series, and under Switch Row/Column the
        // renderer works in transposed (virtual) coordinates the mapped sheet columns don't match.
        if (chart.SeriesInRows)
            return false;

        var mappings = chart.SeriesColumnMappings;
        if (mappings.Count == 0)
            return false;

        for (var i = 0; i < mappings.Count; i++)
        {
            var column = mappings[i].ValueColumn;
            if (column < dataStartCol || column > endCol)
                return false;
        }

        return true;
    }

    /// <summary>
    /// True when <paramref name="col"/> should be rendered as a data series. With an authoritative
    /// mapping only mapped value columns render; otherwise every column in the span renders (legacy).
    /// </summary>
    private static bool ShouldRenderColumnAsSeries(ChartModel chart, uint col, uint dataStartCol, uint endCol)
    {
        if (!HasAuthoritativeSeriesColumns(chart, dataStartCol, endCol))
            return true;

        var mappings = chart.SeriesColumnMappings;
        for (var i = 0; i < mappings.Count; i++)
        {
            if (mappings[i].ValueColumn == col)
                return true;
        }

        return false;
    }

    private static int GetSeriesIndex(ChartModel chart, uint col, uint dataStartCol) =>
        GetSeriesIndex(chart, col, dataStartCol, uint.MaxValue);

    private static int GetSeriesIndex(ChartModel chart, uint col, uint dataStartCol, uint endCol)
    {
        if (HasAuthoritativeSeriesColumns(chart, dataStartCol, endCol))
        {
            var mappings = chart.SeriesColumnMappings;
            for (var i = 0; i < mappings.Count; i++)
            {
                if (mappings[i].ValueColumn == col)
                    return mappings[i].SeriesXmlIndex;
            }
        }

        return (int)(col - dataStartCol - (chart.Type == ChartType.Scatter && !chart.FirstColIsCategories ? 1 : 0));
    }

    private static ChartSeriesFormat? GetSeriesFormat(ChartModel chart, int seriesIndex) =>
        ChartStylePlanner.FindSeriesFormat(chart, seriesIndex);

    /// <summary>
    /// Returns true when the series with chart-XML index <paramref name="seriesIndex"/> has its
    /// legend entry marked deleted (Excel's way to hide helper series from the legend).
    /// <para>
    /// The OOXML <c>&lt;c:legendEntry&gt;&lt;c:idx&gt;</c> is a <em>legend-position</em> index — the
    /// order the series are DECLARED in the chart XML — NOT the series' own <c>&lt;c:idx&gt;</c>.
    /// Excel can declare series out of idx order (e.g. a "shaded target band" combo chart whose
    /// line series has idx 0 but is declared last, so legend position 0 is actually the first
    /// declared helper series). When <see cref="ChartModel.SeriesPlotOrder"/> is populated the
    /// legend-entry idx is resolved through it to the matching series idx; when empty (the legacy
    /// single-plot-group case where declaration order equals idx order, e.g. bullet-chart helper
    /// series) the legend-entry idx is matched against the series idx directly.
    /// </para>
    /// </summary>
    private static bool IsLegendEntryDeleted(ChartModel chart, int seriesIndex)
    {
        var entries = chart.LegendEntries;
        var plotOrder = chart.SeriesPlotOrder;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            // Resolve the legend-position index to the series chart-XML idx via the declaration
            // order when available; otherwise treat the entry idx as the series idx (legacy).
            var resolvedSeriesIndex = plotOrder.Count > 0 && entry.Index >= 0 && entry.Index < plotOrder.Count
                ? plotOrder[entry.Index]
                : entry.Index;
            if (resolvedSeriesIndex == seriesIndex)
                return entry.IsDeleted == true;
        }

        return false;
    }

    /// <summary>
    /// Returns the per-point fill color for a given series/point pair,
    /// resolved against the workbook theme. Returns null when no per-point
    /// override exists (caller should fall back to series-level or palette color).
    /// </summary>
    private static CellColor? GetPointFillColor(ChartModel chart, int seriesIndex, int pointIndex, WorkbookTheme theme) =>
        ChartStylePlanner.ResolvePointFillColor(chart, seriesIndex, pointIndex, theme);

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
        if (format.NoFill)
        {
            // Explicit <a:noFill/> — render the bar body fully transparent so helper
            // series (e.g. "Max Invisible" bullet-chart scaffolding) do not paint over
            // the real data series beneath them.
            series.FillColor = OxyColors.Transparent;
        }
        else if (format.ResolveFillColor(theme) is { } fill)
        {
            series.FillColor = OxyColor.FromRgb(fill.R, fill.G, fill.B);
        }
        if (format.NoLine)
        {
            // Explicit <a:ln><a:noFill/> — suppress the bar outline entirely (transparent spacer).
            series.StrokeColor = OxyColors.Transparent;
            series.StrokeThickness = 0;
        }
        else if (format.ResolveStrokeColor(theme) is { } stroke)
        {
            series.StrokeColor = OxyColor.FromRgb(stroke.R, stroke.G, stroke.B);
        }
        if (!format.NoLine && format.StrokeThickness is { } thickness)
            series.StrokeThickness = thickness;
        // Note: RectangleBarSeries does not support dash-style strokes; the dash
        // pattern on outline-only helper series (e.g. "Max Outline") is a best-effort
        // approximation via stroke color + thickness only.
    }

    private static void ApplyBarFormat(BarSeries series, ChartSeriesFormat? format, WorkbookTheme theme)
    {
        if (format is null)
            return;
        if (format.NoFill)
        {
            // Explicit <a:noFill/> — render the bar body fully transparent.
            series.FillColor = OxyColors.Transparent;
        }
        else if (format.ResolveFillColor(theme) is { } fill)
        {
            series.FillColor = OxyColor.FromRgb(fill.R, fill.G, fill.B);
        }
        if (format.NoLine)
        {
            series.StrokeColor = OxyColors.Transparent;
            series.StrokeThickness = 0;
        }
        else if (format.ResolveStrokeColor(theme) is { } stroke)
        {
            series.StrokeColor = OxyColor.FromRgb(stroke.R, stroke.G, stroke.B);
        }
        if (!format.NoLine && format.StrokeThickness is { } thickness)
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

    private static bool ShouldUseNativePieLabels(ChartModel chart) =>
        chart.ShowDataLabels
            && chart.DataLabelFillColor is null
            && chart.DataLabelFillThemeColor is null
            && chart.DataLabelBorderColor is null
            && chart.DataLabelBorderThemeColor is null
            && chart.DataLabelBorderThickness <= 0
            && !chart.ShowDataLabelCallouts
            && Math.Abs(chart.DataLabelAngle) <= 0.5;

    private static void AddPieDataLabelAnnotations(
        PlotModel model,
        ChartModel chart,
        WorkbookTheme theme,
        string seriesName,
        IReadOnlyList<PieDataLabelPoint> points)
    {
        if (ShouldUseNativePieLabels(chart) || !chart.ShowDataLabels || points.Count == 0)
            return;

        var total = 0d;
        for (var i = 0; i < points.Count; i++)
            total += Math.Max(0, points[i].Value);

        if (total <= 0)
            return;

        AddPieAnnotationAxes(model);

        var textColor = chart.ResolveDataLabelTextColor(theme);
        var borderColor = chart.ResolveDataLabelBorderColor(theme);
        var fillColor = chart.ResolveDataLabelFillColor(theme);
        var accumulatedAngle = chart.FirstSliceAngle;
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var positiveValue = Math.Max(0, point.Value);
            var sweep = positiveValue / total * 360.0;
            var midAngle = accumulatedAngle + sweep / 2.0;
            accumulatedAngle += sweep;

            var value = ChartDataLabelTextPlanner.ShouldRenderPercentageLabels(chart)
                ? positiveValue / total
                : point.Value;
            var position = GetPieDataLabelPosition(chart.DataLabelPosition, midAngle);
            model.Annotations.Add(new TextAnnotation
            {
                XAxisKey = PieAnnotationXAxisKey,
                YAxisKey = PieAnnotationYAxisKey,
                Text = ChartDataLabelTextPlanner.FormatDataLabel(chart, seriesName, point.CategoryName, value),
                TextPosition = position,
                TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                TextVerticalAlignment = OxyPlot.VerticalAlignment.Middle,
                TextColor = ToOxyColor(textColor) ?? OxyColors.Automatic,
                FontSize = chart.DataLabelFontSize,
                Stroke = ToOxyColor(borderColor) ?? (chart.ShowDataLabelCallouts ? OxyColors.Gray : OxyColors.Transparent),
                StrokeThickness = chart.DataLabelBorderThickness > 0 ? chart.DataLabelBorderThickness : chart.ShowDataLabelCallouts ? 1 : 0,
                Background = ToOxyColor(fillColor) ?? (chart.ShowDataLabelCallouts ? OxyColor.FromAColor(235, OxyColors.White) : OxyColors.Transparent),
                TextRotation = chart.DataLabelAngle,
                Padding = new OxyThickness(chart.ShowDataLabelCallouts ? 4 : 2)
            });
        }
    }

    private const string PieLegendXAxisKey = "PieLegendX";
    private const string PieLegendYAxisKey = "PieLegendY";

    /// <summary>
    /// Draws a custom pie/doughnut legend (a colored swatch + category label per slice). OxyPlot's
    /// PieSeries does not contribute per-slice entries to the built-in legend, so this annotation-based
    /// legend mirrors Excel's behaviour (e.g. Completed / Remaining swatches). Honors the chart's
    /// legend position for the corner placement and skips slices whose value is non-positive.
    /// </summary>
    private static void AddPieLegendAnnotations(PlotModel model, ChartModel chart, WorkbookTheme theme, PieSeries pieSeries)
    {
        if (!chart.ShowLegend || chart.LegendPosition == ChartLegendPosition.None || pieSeries.Slices.Count == 0)
            return;

        // A dedicated 0..1 normalized axis pair keeps legend placement independent of the pie's own
        // annotation axes; it is invisible and does not affect the pie geometry.
        if (!model.Axes.Any(axis => axis.Key == PieLegendXAxisKey))
        {
            model.Axes.Add(new LinearAxis
            {
                Key = PieLegendXAxisKey,
                Position = AxisPosition.Bottom,
                IsAxisVisible = false,
                Minimum = 0,
                Maximum = 1
            });
            model.Axes.Add(new LinearAxis
            {
                Key = PieLegendYAxisKey,
                Position = AxisPosition.Left,
                IsAxisVisible = false,
                Minimum = 0,
                Maximum = 1
            });
        }

        var onRight = chart.LegendPosition is ChartLegendPosition.Right;
        var onTop = chart.LegendPosition is ChartLegendPosition.Top;
        var onBottom = chart.LegendPosition is ChartLegendPosition.Bottom;
        // Default (Right / tr-style) places the legend column near the top-right corner.
        var swatchX = chart.LegendPosition is ChartLegendPosition.Left ? 0.02 : 0.86;
        var labelX = swatchX + 0.035;
        var startY = onBottom ? 0.12 : 0.96;
        var step = 0.06;

        var legendTextColor = ToOxyColor(chart.ResolveLegendTextColor(theme)) ?? OxyColor.FromRgb(89, 89, 89);

        for (var i = 0; i < pieSeries.Slices.Count; i++)
        {
            var slice = pieSeries.Slices[i];
            var y = onTop || onBottom
                ? (onTop ? 0.96 : 0.12)
                : startY - i * step;

            // For top/bottom legends lay swatches out horizontally instead of vertically.
            var sx = onTop || onBottom ? 0.30 + i * 0.22 : swatchX;
            var lx = onTop || onBottom ? sx + 0.035 : labelX;

            model.Annotations.Add(new RectangleAnnotation
            {
                XAxisKey = PieLegendXAxisKey,
                YAxisKey = PieLegendYAxisKey,
                MinimumX = sx,
                MaximumX = sx + 0.028,
                MinimumY = y - 0.018,
                MaximumY = y + 0.018,
                Fill = slice.Fill,
                Stroke = OxyColors.Transparent,
                StrokeThickness = 0
            });
            model.Annotations.Add(new TextAnnotation
            {
                XAxisKey = PieLegendXAxisKey,
                YAxisKey = PieLegendYAxisKey,
                Text = slice.Label,
                TextPosition = new DataPoint(lx, y),
                TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left,
                TextVerticalAlignment = OxyPlot.VerticalAlignment.Middle,
                TextColor = legendTextColor,
                FontSize = chart.LegendFontSize,
                Stroke = OxyColors.Transparent,
                StrokeThickness = 0,
                Background = OxyColors.Transparent
            });
        }

        // The custom annotation legend replaces the (empty) built-in legend for pies.
        model.Legends.Clear();
    }

    private static void AddPieAnnotationAxes(PlotModel model)
    {
        if (model.Axes.Any(axis => axis.Key == PieAnnotationXAxisKey))
            return;

        model.Axes.Add(new LinearAxis
        {
            Key = PieAnnotationXAxisKey,
            Position = AxisPosition.Bottom,
            IsAxisVisible = false,
            Minimum = -1.3,
            Maximum = 1.3
        });
        model.Axes.Add(new LinearAxis
        {
            Key = PieAnnotationYAxisKey,
            Position = AxisPosition.Left,
            IsAxisVisible = false,
            Minimum = -1.3,
            Maximum = 1.3
        });
    }

    private static DataPoint GetPieDataLabelPosition(ChartDataLabelPosition labelPosition, double angle)
    {
        var radius = labelPosition switch
        {
            ChartDataLabelPosition.Center => 0.48,
            ChartDataLabelPosition.InsideEnd => 0.78,
            ChartDataLabelPosition.OutsideEnd => 1.12,
            _ => 0.78
        };
        var radians = Math.PI * angle / 180.0;
        return new DataPoint(Math.Cos(radians) * radius, Math.Sin(radians) * radius);
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
        ChartPointDataLabelFormatLookup pointDataLabelFormats,
        string seriesName,
        int seriesIndex,
        int pointIndex,
        string categoryName,
        double x,
        double y,
        double value)
    {
        var pointFormat = pointDataLabelFormats.Get(seriesIndex, pointIndex);
        var textColor = pointFormat?.ResolveTextColor(theme) ?? chart.ResolveDataLabelTextColor(theme);
        var borderColor = pointFormat?.ResolveBorderColor(theme) ?? chart.ResolveDataLabelBorderColor(theme);
        var fillColor = pointFormat?.ResolveFillColor(theme) ?? chart.ResolveDataLabelFillColor(theme);
        model.Annotations.Add(new TextAnnotation
        {
            Text = ChartDataLabelTextPlanner.FormatDataLabel(chart, seriesName, categoryName, value),
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
        ChartPointDataLabelFormatLookup pointDataLabelFormats,
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
                pointDataLabelFormats,
                seriesName,
                seriesIndex,
                pointIndex,
                ChartDataLabelTextPlanner.GetCategory(categories, (int)Math.Round(point.X)),
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
        // Membership in ComboLineSeriesIndexes is authoritative (populated from the chart's
        // <c:lineChart> element), so honor it even at series index 0 — Excel commonly draws the
        // line series first over bar helper series (shaded target-band charts). An empty list still
        // means "no combo lines", so a plain stacked/clustered chart is unaffected.
        if (!ChartTypeSupport.SupportsComboLineOverlay(chart.Type) || !chart.UseComboLineForSecondarySeries || seriesIndex < 0)
            return false;

        return chart.ComboLineSeriesIndexes.Contains(seriesIndex);
    }

    private static bool IsComboScatterSeries(ChartModel chart, int seriesIndex)
    {
        if (!ChartTypeSupport.SupportsComboLineOverlay(chart.Type) || seriesIndex < 0)
            return false;

        return chart.ComboScatterSeriesIndexes.Contains(seriesIndex);
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
        ChartModel chart,
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
                && TryGetChartNumericValue(cell, out var v))
            {
                var point = new DataPoint(i, v);
                series.Points.Add(point);
                trendPoints?.Add(point);
            }
            else if (cellLookup.TryGetValue((r, col), out cell) && IsChartBlank(cell))
            {
                if (chart.BlankDisplayMode == ChartBlankDisplayMode.Zero)
                {
                    var point = new DataPoint(i, 0);
                    series.Points.Add(point);
                    trendPoints?.Add(point);
                }
                else if (chart.BlankDisplayMode == ChartBlankDisplayMode.Gap)
                {
                    series.Points.Add(new DataPoint(i, double.NaN));
                }
            }
        }

        capturedTrendPoints = trendPoints;
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
