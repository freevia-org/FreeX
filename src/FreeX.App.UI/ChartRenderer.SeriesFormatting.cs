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

    /// <summary>
    /// The "Dot" marker style maps to the same <see cref="MarkerType.Circle"/> glyph as "Auto"/"Circle" --
    /// OxyPlot has no smaller dedicated dot marker type -- so it is distinguished by size instead, mirroring
    /// the Avalonia chart renderer's own Dot marker (<c>dotR = r * 0.45</c>, a filled circle at 45% of the
    /// full marker radius).
    /// </summary>
    private const double DotMarkerSizeScale = 0.45;

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
        ChartRenderPolicyPlanner.ResolveBarHalfWidth(chart);

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
        return ChartRenderPolicyPlanner.CountClusteredSourceSeries(chart, dataStartCol, endCol);
    }

    /// <summary>
    /// Returns the left/right x-offsets (relative to the category centre) for the bar of the
    /// <paramref name="clusterOrdinal"/>-th clustered series, given the full category half-width,
    /// the total clustered-series count, and Excel's Series Overlap percentage (-100..100, Format
    /// Data Series' "Overlap" slider; <see cref="ChartModel.BarOverlap"/>). With one series the bar
    /// fills the whole slot regardless of overlap (there's nothing to overlap/space against). With
    /// N series each bar has a fixed width <c>unitWidth</c> chosen so the whole cluster of N bars —
    /// spaced <c>unitWidth * (1 - overlap/100)</c> apart center-to-center — exactly fills
    /// <c>[-halfWidth, halfWidth]</c>: overlap=0 reproduces the previous disjoint side-by-side tiling,
    /// overlap=100 collapses every bar onto the same full-width position (Excel's fully-overlapping
    /// look), and overlap=-100 spreads the bars out with equal gaps between them.
    /// </summary>
    private static (double Left, double Right) ClusteredBarOffsets(
        double halfWidth,
        int clusterOrdinal,
        int clusterCount,
        int overlapPercent = 0)
    {
        return ChartRenderPolicyPlanner.ResolveClusteredBarOffsets(
            halfWidth,
            clusterOrdinal,
            clusterCount,
            overlapPercent);
    }

    private static bool ShouldSkipScatterXColumn(ChartModel chart, uint col, uint dataStartCol) =>
        ChartRenderPolicyPlanner.ShouldSkipSourceColumn(chart, col, dataStartCol);

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
        return ChartRenderPolicyPlanner.HasAuthoritativeSeriesColumns(chart, dataStartCol, endCol);
    }

    /// <summary>
    /// True when <paramref name="col"/> should be rendered as a data series. With an authoritative
    /// mapping only mapped value columns render; otherwise every column in the span renders (legacy).
    /// </summary>
    private static bool ShouldRenderColumnAsSeries(ChartModel chart, uint col, uint dataStartCol, uint endCol)
    {
        return ChartRenderPolicyPlanner.ShouldRenderSourceColumn(chart, col, dataStartCol, endCol);
    }

    private static int GetSeriesIndex(ChartModel chart, uint col, uint dataStartCol) =>
        ChartRenderPolicyPlanner.ResolveSeriesIndex(chart, col, dataStartCol);

    private static int GetSeriesIndex(ChartModel chart, uint col, uint dataStartCol, uint endCol)
    {
        return ChartRenderPolicyPlanner.ResolveSeriesIndex(chart, col, dataStartCol, endCol);
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
        return ChartRenderPolicyPlanner.IsLegendEntryDeleted(chart, seriesIndex);
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
            series.Color = ApplyFillAlpha(OxyColor.FromRgb(fill.R, fill.G, fill.B), format.FillAlpha);
        if (format.StrokeThickness is { } thickness)
            series.StrokeThickness = thickness;
        if (format.DashStyle is { } dashStyle)
            series.LineStyle = ToOxyLineStyle(dashStyle);
        if (format.MarkerStyle is { } markerStyle)
            series.MarkerType = ToOxyMarkerType(markerStyle);
        if (format.MarkerSize is { } markerSize)
            series.MarkerSize = Math.Clamp(markerSize, 1, 20);
        if (format.MarkerStyle == ChartMarkerStyle.Dot)
            series.MarkerSize *= DotMarkerSizeScale;
        if (format.ResolveFillColor(theme) is { } markerFill)
            series.MarkerFill = ApplyFillAlpha(OxyColor.FromRgb(markerFill.R, markerFill.G, markerFill.B), format.FillAlpha);
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
            series.FillColor = ApplyFillAlpha(OxyColor.FromRgb(fill.R, fill.G, fill.B), format.FillAlpha);
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
            series.FillColor = ApplyFillAlpha(OxyColor.FromRgb(fill.R, fill.G, fill.B), format.FillAlpha);
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
        // R91-render-chart-series-format-5-2: "Invert if negative" (<c:invertIfNegative>) was parsed
        // and re-serialized but never consumed at render time. BarSeries (the horizontal Bar/ThreeDBar
        // renderer) has a built-in NegativeFillColor OxyPlot honors per-item against BaseValue, so
        // wiring it here is the single choke point for both the cell-lookup and embedded-data render
        // paths that call ApplyBarFormat. No distinct "invert color" is modeled/round-tripped
        // separately from the flag, so this mirrors Excel's own default alternate fill (white) for a
        // freshly-checked "Invert if negative" box with no further customization.
        series.NegativeFillColor = format.InvertIfNegative == true && !format.NoFill
            ? OxyColors.White
            : OxyColors.Automatic;
    }

    /// <summary>
    /// Returns the fill color a single bar/column ITEM should use when Excel's "Invert if negative"
    /// (<see cref="ChartSeriesFormat.InvertIfNegative"/>) applies to it -- i.e. the item's own value
    /// is negative and the series has the flag set -- or null when the normal series/point fill
    /// should be used instead (value non-negative, flag unset/false, or the series is fully
    /// transparent via NoFill). Used for <see cref="RectangleBarSeries"/> (Column/ThreeDColumn),
    /// which -- unlike <see cref="BarSeries"/> -- has no built-in negative-fill property, so the
    /// inversion must be applied per <c>RectangleBarItem.Color</c> at the point the item is created.
    /// See R91-render-chart-series-format-5-2.
    /// </summary>
    internal static OxyColor? ResolveInvertIfNegativeItemColor(ChartSeriesFormat? format, double value) =>
        format?.InvertIfNegative == true && value < 0 && !format.NoFill
            ? OxyColors.White
            : null;

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
        // R88-render-chart-labels-legend-5-3: when the callout affordance is showing (a moved
        // label's border) and no explicit label border was set, fall back to the leader-line
        // color/thickness parsed from XLSX instead of a hardcoded gray/1pt -- otherwise those
        // parsed values are never actually consumed by rendering.
        var leaderLineColor = chart.ResolveDataLabelLeaderLineColor(theme);
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
                Stroke = ToOxyColor(borderColor)
                    ?? (chart.ShowDataLabelCallouts ? ToOxyColor(leaderLineColor) ?? OxyColors.Gray : OxyColors.Transparent),
                StrokeThickness = chart.DataLabelBorderThickness > 0
                    ? chart.DataLabelBorderThickness
                    : chart.ShowDataLabelCallouts ? chart.DataLabelLeaderLineThickness : 0,
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
        var radius = ChartRenderPolicyPlanner.ResolvePieLabelRadiusFraction(
            labelPosition,
            center: 0.48,
            insideEnd: 0.78,
            outsideEnd: 1.12);
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
            series.Fill = ApplyFillAlpha(OxyColor.FromRgb(fill.R, fill.G, fill.B), format.FillAlpha);
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
            series.MarkerFill = ApplyFillAlpha(OxyColor.FromRgb(fill.R, fill.G, fill.B), format.FillAlpha);
        if (format.ResolveStrokeColor(theme) is { } stroke)
            series.MarkerStroke = OxyColor.FromRgb(stroke.R, stroke.G, stroke.B);
        if (format.StrokeThickness is { } thickness)
            series.MarkerStrokeThickness = thickness;
        if (format.MarkerStyle is { } markerStyle)
            series.MarkerType = ToOxyMarkerType(markerStyle);
        if (format.MarkerSize is { } markerSize)
            series.MarkerSize = Math.Clamp(markerSize, 1, 30);
        if (format.MarkerStyle == ChartMarkerStyle.Dot)
            series.MarkerSize *= DotMarkerSizeScale;
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
        // R88-render-chart-labels-legend-5-3: same leader-line fallback as the pie annotation path
        // -- consume the parsed leader-line color/thickness instead of a hardcoded gray/1pt.
        var leaderLineColor = chart.ResolveDataLabelLeaderLineColor(theme);
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
            Stroke = ToOxyColor(borderColor)
                ?? (chart.ShowDataLabelCallouts ? ToOxyColor(leaderLineColor) ?? OxyColors.Gray : OxyColors.Transparent),
            StrokeThickness = pointFormat?.BorderThickness
                ?? (chart.DataLabelBorderThickness > 0 ? chart.DataLabelBorderThickness : chart.ShowDataLabelCallouts ? chart.DataLabelLeaderLineThickness : 0),
            Background = ToOxyColor(fillColor) ?? (chart.ShowDataLabelCallouts ? OxyColor.FromAColor(235, OxyColors.White) : OxyColors.Transparent),
            TextRotation = chart.DataLabelAngle,
            Padding = new OxyThickness(chart.ShowDataLabelCallouts ? 4 : 2)
        });
    }

    private static OxyColor? ToOxyColor(CellColor? color) =>
        color is { } value ? OxyColor.FromRgb(value.R, value.G, value.B) : null;

    /// <summary>
    /// Applies a series' authored fill transparency (<see cref="ChartSeriesFormat.FillAlpha"/> --
    /// the &lt;a:alpha&gt; child of the series fill's &lt;a:srgbClr&gt;/&lt;a:schemeClr&gt;, a 0..1
    /// opacity fraction) to an already-resolved fill <see cref="OxyColor"/>. R91 wired this value
    /// through the reader and writer so it round-trips on save, but no renderer ever consumed it --
    /// a semi-transparent series fill always drew fully opaque. Null (no authored &lt;a:alpha&gt;,
    /// the common case) leaves the color untouched.
    /// </summary>
    private static OxyColor ApplyFillAlpha(OxyColor color, double? fillAlpha) =>
        fillAlpha is { } alpha
            ? OxyColor.FromAColor((byte)Math.Clamp(Math.Round(alpha * 255.0), 0, 255), color)
            : color;

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
                // R131-render-chart-date-category-axis: look up the category by the LOOP index, not
                // by rounding point.X -- a date category axis's X is now a large proportional day
                // value (DateTimeAxis.ToDouble), not a small 0,1,2… index, so rounding it would look
                // up entirely the wrong (usually out-of-range, blank) category label.
                ChartDataLabelTextPlanner.GetCategory(categories, pointIndex),
                point.X,
                point.Y,
                point.Y);
        }
    }

    private static bool UsesSecondaryAxis(ChartModel chart, int seriesIndex)
    {
        // Secondary-axis membership is authoritative for ANY series, including index 0 — Excel's
        // Format Data Series > Secondary Axis works on the first series just as on any other
        // (R25-chart-axis-series-deep-1). Only the empty-list default legitimately excludes the first
        // series (below): an explicit assignment list that contains 0 must move series 0 to secondary.
        return ChartRenderPolicyPlanner.UsesSecondaryAxis(chart, seriesIndex);
    }

    private static bool IsComboLineSeries(ChartModel chart, int seriesIndex)
    {
        // Membership in ComboLineSeriesIndexes is authoritative (populated from the chart's
        // <c:lineChart> element), so honor it even at series index 0 — Excel commonly draws the
        // line series first over bar helper series (shaded target-band charts). An empty list still
        // means "no combo lines", so a plain stacked/clustered chart is unaffected.
        return ChartRenderPolicyPlanner.IsComboLineSeries(chart, seriesIndex);
    }

    private static bool IsComboScatterSeries(ChartModel chart, int seriesIndex)
    {
        return ChartRenderPolicyPlanner.IsComboScatterSeries(chart, seriesIndex);
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
        out List<DataPoint>? capturedTrendPoints,
        // R131-render-chart-date-category-axis: per-category date-proportional X positions (see
        // ChartRenderer.Axes.cs's TryBuildDateCategoryAxis), null for every plain (non-date)
        // category axis -- in which case x falls back to the plain 0,1,2… index exactly as before.
        double[]? xPositions = null)
    {
        var i = 0;
        for (uint r = dataStartRow; r <= endRow; r++, i++)
        {
            double x = xPositions is not null && i < xPositions.Length ? xPositions[i] : i;
            if (cellLookup.TryGetValue((r, col), out var cell)
                && TryGetChartNumericValue(cell, out var v))
            {
                var point = new DataPoint(x, v);
                series.Points.Add(point);
                trendPoints?.Add(point);
            }
            else if (cellLookup.TryGetValue((r, col), out cell) && IsChartBlank(cell))
            {
                if (chart.BlankDisplayMode == ChartBlankDisplayMode.Zero)
                {
                    var point = new DataPoint(x, 0);
                    series.Points.Add(point);
                    trendPoints?.Add(point);
                }
                else if (chart.BlankDisplayMode == ChartBlankDisplayMode.Gap)
                {
                    series.Points.Add(new DataPoint(x, double.NaN));
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
        return ChartRenderPolicyPlanner.HasAnySecondaryAxisSeries(
            chart,
            Enumerable.Range(0, Math.Max(0, seriesCount)));
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
            // Distinct from the plain-Right fallback below -- OxyPlot has a dedicated corner
            // placement so TopRight actually renders top-right instead of collapsing to Right.
            ChartLegendPosition.TopRight => OxyPlot.Legends.LegendPosition.TopRight,
            _ => overlay ? OxyPlot.Legends.LegendPosition.RightTop : OxyPlot.Legends.LegendPosition.RightMiddle
        };

    /// <summary>
    /// Draws Excel-style error-bar whiskers for every plotted series that supports them
    /// (column/bar/line/scatter/bubble/area — the same set <see cref="ChartTypeSupport.SupportsTrendlines"/>
    /// covers, mirroring <c>ChartErrorBarsPlanner.SupportsErrorBars</c>). Reads the already-built series
    /// out of <paramref name="model"/> (added earlier in the same render pass) rather than requiring a
    /// second pass over the worksheet. Each whisker is drawn as a disjoint line segment (plus optional
    /// end-cap ticks) in a single marker-less <see cref="LineSeries"/> per plotted series — rather than
    /// OxyPlot's built-in <see cref="ScatterErrorSeries"/>, whose <see cref="ScatterErrorPoint"/> can only
    /// express one symmetric magnitude per axis — so Excel's Plus-only/Minus-only directions and
    /// asymmetric Custom plus/minus amounts render correctly as one-sided or unequal whiskers.
    /// </summary>
    private static void AddErrorBarsIfRequested(PlotModel model, ChartModel chart, WorkbookTheme theme)
    {
        if (!chart.ShowErrorBars || !ChartTypeSupport.SupportsTrendlines(chart.Type))
            return;

        var barColor = chart.ErrorBarThemeColor?.Resolve(theme) ?? chart.ErrorBarColor;
        var oxyColor = barColor is { } color ? OxyColor.FromRgb(color.R, color.G, color.B) : OxyColors.Black;
        var customPlus = ChartRenderPolicyPlanner.ParseErrorBarRangeCache(chart.ErrorBarPlusRangeCacheXml);
        var customMinus = ChartRenderPolicyPlanner.ParseErrorBarRangeCache(chart.ErrorBarMinusRangeCacheXml) ?? customPlus;

        // ChartModel has no per-series error-bar list: the reader keeps only the FIRST <c:ser> that
        // carried <c:errBars> in the source file, chart-wide (XlsxChartTrendlineErrorBarReader.
        // ApplyErrorBars). For Standard Error/Percentage/Fixed Value that spec is either recomputed
        // from each series' own values or a single chart-wide constant, so annotating every supporting
        // series still matches Excel's "select the whole chart, add error bars" gesture. Custom-kind
        // amounts, however, are a specific cached plus/minus value PER POINT read off the one series
        // that owned the <c:errBars> element, so painting that exact cache onto unrelated series would
        // fabricate whiskers Excel never drew there. Since the series identity itself wasn't kept, the
        // best we can do here is restrict Custom to the single series whose own point count matches
        // the cached range's length (the only series the cache could possibly belong to) instead of
        // stamping it onto every series that happens to support error bars.
        var isCustomKind = chart.ErrorBarKind == ChartErrorBarKind.Custom;
        var customLength = customPlus?.Count ?? customMinus?.Count ?? 0;
        var customApplied = false;

        // Snapshot first: we're about to append whisker LineSeries entries to model.Series and must
        // not walk into those while iterating the series this pass is meant to annotate.
        var targets = model.Series.ToArray();
        foreach (var series in targets)
        {
            var points = GetErrorBarAnchorPoints(series, out var isBarOrientedHorizontal);
            if (points is null || points.Count == 0)
                continue;

            if (isCustomKind && (customApplied || points.Count != customLength))
                continue;

            // A horizontal Bar chart always whiskers along its value axis (X). Everything else
            // whiskers along Y unless the chart XML explicitly requested X-direction error bars
            // (errDir="x" — only meaningful, and only ever set by Excel, for Scatter/Bubble, whose
            // X axis carries real values rather than a category index).
            var isHorizontal = isBarOrientedHorizontal || chart.ErrorBarAxisDirection == ChartErrorBarAxisDirection.X;
            var values = new double[points.Count];
            for (var i = 0; i < points.Count; i++)
                values[i] = isHorizontal ? points[i].X : points[i].Y;

            // A small tick perpendicular to the whisker at half a category-slot wide; consistent with
            // the 0.05..0.5 half-width range ColumnBarHalfWidth uses for real bar geometry.
            const double endCapHalfWidth = 0.08;
            var whiskers = new LineSeries
            {
                LineStyle = ToOxyLineStyle(chart.ErrorBarDashStyle),
                StrokeThickness = chart.ErrorBarThickness,
                Color = oxyColor,
                MarkerType = MarkerType.None,
                YAxisKey = (series as XYAxisSeries)?.YAxisKey
            };

            var any = false;
            for (var i = 0; i < points.Count; i++)
            {
                var amounts = ChartRenderPolicyPlanner.ResolveErrorBarAmounts(
                    chart,
                    values,
                    i,
                    customPlus,
                    customMinus);
                var plus = amounts.Plus;
                var minus = amounts.Minus;
                if (plus <= 0 && minus <= 0)
                    continue;

                any = true;
                var point = points[i];
                AddWhisker(whiskers, point, plus, minus, isHorizontal, chart.ErrorBarEndCaps, endCapHalfWidth);
            }

            if (any)
            {
                model.Series.Add(whiskers);
                if (isCustomKind)
                    customApplied = true;
            }
        }
    }

    /// <summary>
    /// Appends one error-bar whisker (and its optional end-cap ticks) for a single data point to
    /// <paramref name="whiskers"/>, using <c>NaN</c> point separators so each whisker/cap renders as its
    /// own disjoint segment within the shared <see cref="LineSeries"/> (the same idiom
    /// <see cref="AddLinePoints"/> uses for gapped blank cells).
    /// </summary>
    private static void AddWhisker(
        LineSeries whiskers,
        DataPoint point,
        double plus,
        double minus,
        bool isHorizontal,
        bool endCaps,
        double endCapHalfWidth)
    {
        DataPoint AtOffset(double offset) =>
            isHorizontal ? new DataPoint(point.X + offset, point.Y) : new DataPoint(point.X, point.Y + offset);

        DataPoint CapPoint(double offset, double perpendicular) =>
            isHorizontal
                ? new DataPoint(point.X + offset, point.Y + perpendicular)
                : new DataPoint(point.X + perpendicular, point.Y + offset);

        if (whiskers.Points.Count > 0)
            whiskers.Points.Add(DataPoint.Undefined);

        whiskers.Points.Add(plus > 0 ? AtOffset(plus) : point);
        whiskers.Points.Add(minus > 0 ? AtOffset(-minus) : point);

        if (!endCaps)
            return;

        if (plus > 0)
        {
            whiskers.Points.Add(DataPoint.Undefined);
            whiskers.Points.Add(CapPoint(plus, -endCapHalfWidth));
            whiskers.Points.Add(CapPoint(plus, endCapHalfWidth));
        }

        if (minus > 0)
        {
            whiskers.Points.Add(DataPoint.Undefined);
            whiskers.Points.Add(CapPoint(-minus, -endCapHalfWidth));
            whiskers.Points.Add(CapPoint(-minus, endCapHalfWidth));
        }
    }

    /// <summary>
    /// Extracts the (category/value) anchor points to hang error-bar whiskers off, for each renderer
    /// series type that supports error bars. <paramref name="isHorizontal"/> reports true for the
    /// horizontal Bar chart orientation (value on the X axis), so the caller knows which axis the
    /// whisker error amount applies to.
    /// </summary>
    private static IReadOnlyList<DataPoint>? GetErrorBarAnchorPoints(OxyPlot.Series.Series series, out bool isHorizontal)
    {
        isHorizontal = false;
        switch (series)
        {
            case LineSeries lineSeries:
                return lineSeries.Points;
            case ScatterSeries scatterSeries:
                return scatterSeries.Points.Select(p => new DataPoint(p.X, p.Y)).ToArray();
            case RectangleBarSeries rectangleBarSeries:
                return rectangleBarSeries.Items
                    .Select(item => new DataPoint((item.X0 + item.X1) / 2.0, item.Y1 >= 0 ? item.Y1 : item.Y0))
                    .ToArray();
            case BarSeries barSeries:
                isHorizontal = true;
                return barSeries.Items
                    .Select((item, index) => new DataPoint(item.Value, index))
                    .ToArray();
            default:
                return null;
        }
    }

}
