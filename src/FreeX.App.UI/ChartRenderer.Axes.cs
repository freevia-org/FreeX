using OxyPlot;
using OxyPlot.Axes;

using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public static partial class ChartRenderer
{
    private static void ApplyAxisBounds(PlotModel model, ChartModel chart, WorkbookTheme theme)
    {
        // Added before the axis bound pass below so autoscale (when no explicit Min/Max is set)
        // accounts for the whiskers' extent, same as any other plotted series.
        AddErrorBarsIfRequested(model, chart, theme);

        for (var index = 0; index < model.Axes.Count; index++)
        {
            var axis = model.Axes[index];
            if (axis is not LinearAxis linearAxis)
                continue;

            if (ShouldUseLogAxis(chart, linearAxis))
            {
                var logAxis = new LogarithmicAxis
                {
                    Position = linearAxis.Position,
                    Title = linearAxis.Title,
                    Key = linearAxis.Key,
                    Minimum = GetPositiveAxisValue(linearAxis.Minimum),
                    Maximum = GetPositiveAxisValue(linearAxis.Maximum),
                    MajorStep = GetPositiveAxisValue(linearAxis.MajorStep),
                    MinorStep = GetPositiveAxisValue(linearAxis.MinorStep),
                    LabelFormatter = linearAxis.LabelFormatter
                };
                model.Axes[index] = logAxis;
                axis = logAxis;
            }

            if (axis.Position is AxisPosition.Bottom or AxisPosition.Top)
            {
                // R131-render-chart-axis-crosses: XAxisCrosses/YAxisCrosses are parsed and
                // round-tripped (XlsxChartAxisReader/XlsxChartXmlWriter.Axes.cs) but were never
                // consulted here, so a "crosses at maximum" axis (Excel's Format Axis > Axis
                // crosses > Maximum category, e.g. a value axis moved to the top or a category
                // axis moved to the right) always drew at its default edge instead. Flipping the
                // physical Position to the opposite edge only for the explicit Maximum case keeps
                // every other chart (AutoZero is the default for the overwhelming majority, and
                // Minimum is already the default edge) rendering exactly as before.
                ApplyAxisCrossesPosition(axis, chart.XAxisCrosses);
                ApplyAxisTitleStyle(axis, chart, theme);
                ApplyAxisReverseOrder(axis, chart.XAxisReverseOrder);
                if (ChartTypeSupport.SupportsXAxisBounds(chart.Type))
                {
                    if (chart.XAxisMinimum is { } minimum)
                        axis.Minimum = ShouldUseLogAxis(chart, axis) ? Math.Max(double.Epsilon, minimum) : minimum;
                    if (chart.XAxisMaximum is { } maximum)
                        axis.Maximum = ShouldUseLogAxis(chart, axis) ? Math.Max(double.Epsilon, maximum) : maximum;
                    if (chart.XAxisMajorUnit is { } majorUnit)
                        axis.MajorStep = majorUnit;
                    if (chart.XAxisMinorUnit is { } minorUnit)
                        axis.MinorStep = minorUnit;
                }
                var xDisplayUnitDivisor = GetAxisDisplayUnitDivisor(chart.XAxisDisplayUnit, chart.XAxisCustomDisplayUnit);
                if (ChartTypeSupport.SupportsXAxisBounds(chart.Type) &&
                    chart.XAxisNumberFormat != ChartDataLabelNumberFormat.General &&
                    axis.LabelFormatter is null)
                    axis.LabelFormatter = value => ChartDataLabelTextPlanner.FormatAxisValue(chart.XAxisNumberFormat, value);
                ApplyAxisDisplayUnit(axis, xDisplayUnitDivisor, chart.XAxisDisplayUnit, chart.XAxisCustomDisplayUnit);
                ApplyGridlineStyle(
                    axis,
                    chart.ShowXAxisMajorGridlines,
                    chart.ShowXAxisMinorGridlines,
                    chart.XAxisMajorGridlineColor,
                    chart.XAxisMinorGridlineColor,
                    chart.XAxisGridlineThickness);
                ApplyTickAndLabelStyle(axis, chart.XAxisMajorTickStyle, chart.XAxisMinorTickStyle, chart.ShowXAxisLabels);
                ApplyAxisLabelStyle(axis, chart.ResolveXAxisLabelTextColor(theme), chart.XAxisLabelFontSize, chart.XAxisLabelAngle);
                ApplyAxisLineStyle(axis, chart.XAxisLineColor, chart.XAxisLineThickness);
            }
            else if (axis.Position is AxisPosition.Left or AxisPosition.Right)
            {
                ApplyAxisCrossesPosition(axis, chart.YAxisCrosses);
                ApplyAxisTitleStyle(axis, chart, theme);
                ApplyAxisReverseOrder(axis, chart.YAxisReverseOrder);
                if (ChartTypeSupport.SupportsYAxisBounds(chart.Type))
                {
                    if (chart.YAxisMinimum is { } minimum)
                        axis.Minimum = ShouldUseLogAxis(chart, axis) ? Math.Max(double.Epsilon, minimum) : minimum;
                    if (chart.YAxisMaximum is { } maximum)
                        axis.Maximum = ShouldUseLogAxis(chart, axis) ? Math.Max(double.Epsilon, maximum) : maximum;
                    if (chart.YAxisMajorUnit is { } majorUnit)
                        axis.MajorStep = majorUnit;
                    if (chart.YAxisMinorUnit is { } minorUnit)
                        axis.MinorStep = minorUnit;
                }
                var yDisplayUnitDivisor = GetAxisDisplayUnitDivisor(chart.YAxisDisplayUnit, chart.YAxisCustomDisplayUnit);
                if (ChartTypeSupport.SupportsYAxisBounds(chart.Type) &&
                    chart.YAxisNumberFormat != ChartDataLabelNumberFormat.General &&
                    axis.LabelFormatter is null)
                    axis.LabelFormatter = value => ChartDataLabelTextPlanner.FormatAxisValue(chart.YAxisNumberFormat, value);
                ApplyAxisDisplayUnit(axis, yDisplayUnitDivisor, chart.YAxisDisplayUnit, chart.YAxisCustomDisplayUnit);
                ApplyGridlineStyle(
                    axis,
                    chart.ShowYAxisMajorGridlines,
                    chart.ShowYAxisMinorGridlines,
                    chart.YAxisMajorGridlineColor,
                    chart.YAxisMinorGridlineColor,
                    chart.YAxisGridlineThickness);
                ApplyTickAndLabelStyle(axis, chart.YAxisMajorTickStyle, chart.YAxisMinorTickStyle, chart.ShowYAxisLabels);
                ApplyAxisLabelStyle(axis, chart.ResolveYAxisLabelTextColor(theme), chart.YAxisLabelFontSize, chart.YAxisLabelAngle);
                ApplyAxisLineStyle(axis, chart.YAxisLineColor, chart.YAxisLineThickness);
            }
        }

        // R90-render-chart-axis-titles-5-2: runs after the loop above so it sees the final axis set
        // (a category axis is never log-converted, but the value axis in the same model may be).
        ApplyCategoryAxisSkip(model, chart);
    }

    private static LinearAxis CreateCenteredIndexedCategoryAxis(
        AxisPosition position,
        string? title,
        IReadOnlyList<string> labels,
        int? effectiveCount = null)
    {
        var count = effectiveCount ?? labels.Count;
        return CreateIndexedCategoryAxis(position, title, labels, -0.5, Math.Max(0.5, count - 0.5));
    }

    private static LinearAxis CreateZeroBasedIndexedCategoryAxis(
        AxisPosition position,
        string? title,
        IReadOnlyList<string> labels) =>
        CreateIndexedCategoryAxis(position, title, labels, 0, Math.Max(1, labels.Count - 1));

    private static LinearAxis CreateIndexedCategoryAxis(
        AxisPosition position,
        string? title,
        IReadOnlyList<string> labels,
        double minimum,
        double maximum) =>
        new SkipAwareIndexedCategoryAxis
        {
            Position = position,
            Title = title,
            Minimum = minimum,
            Maximum = maximum,
            MajorStep = 1,
            MinorStep = 1,
            LabelFormatter = value => GetIndexedCategoryAxisLabel(labels, value)
        };

    private static CategoryAxis CreateCategoryAxis(
        AxisPosition position,
        string? title,
        IReadOnlyList<string> labels)
    {
        var axis = new SkipAwareCategoryAxis { Position = position, Title = title };
        axis.Labels.AddRange(labels);
        return axis;
    }

    private static string GetIndexedCategoryAxisLabel(IReadOnlyList<string> labels, double value)
    {
        var index = (int)Math.Round(value);
        return index >= 0 && index < labels.Count ? labels[index] : "";
    }

    /// <summary>
    /// R131-render-chart-date-category-axis: when the category axis is marked as a date axis
    /// (<see cref="ChartModel.XAxisIsDateAxis"/>, OOXML's <c>&lt;c:dateAx&gt;</c>) and every category
    /// label parses as a date, builds a real <see cref="DateTimeAxis"/> plus one X position per
    /// category proportional to its actual date (the same day-based scale <see cref="DateTimeAxis.ToDouble"/>
    /// uses, mirroring the existing Stock-chart date axis in <c>ChartRenderer.Stock.cs</c>'s
    /// GetStockXValues) instead of the plain 0,1,2… index Column/Area/Line charts used unconditionally
    /// before this fix -- so unevenly spaced dates (e.g. Jan 1, Jan 2, Jan 10) plot with proportional
    /// gaps instead of collapsing to equal intervals. Returns false -- and leaves both out parameters
    /// at their default -- whenever the chart isn't marked as a date axis, has no categories, or any
    /// single category fails to parse as a date, so callers fall back to exactly the previous
    /// evenly-spaced indexed category axis; this also means a plain (non-date) text category axis is
    /// completely unaffected by this method ever being called.
    /// </summary>
    private static bool TryBuildDateCategoryAxis(
        ChartModel chart,
        IReadOnlyList<string> categories,
        out DateTimeAxis? dateAxis,
        out double[] positions)
    {
        dateAxis = null;
        positions = [];
        if (!chart.XAxisIsDateAxis || categories.Count == 0)
            return false;

        var values = new double[categories.Count];
        var minValue = double.PositiveInfinity;
        var maxValue = double.NegativeInfinity;
        for (var index = 0; index < categories.Count; index++)
        {
            if (!TryParseStockDateCategory(categories[index], out var parsed))
                return false;

            var value = DateTimeAxis.ToDouble(parsed.Date);
            values[index] = value;
            if (value < minValue) minValue = value;
            if (value > maxValue) maxValue = value;
        }

        positions = values;
        dateAxis = new DateTimeAxis
        {
            Position = AxisPosition.Bottom,
            Title = chart.XAxisTitle,
            StringFormat = "d",
            IntervalType = DateTimeIntervalType.Days,
            Minimum = minValue - 0.5,
            Maximum = maxValue + 0.5
        };
        return true;
    }

    /// <summary>
    /// R131-render-chart-axis-crosses: applies Excel's Format Axis &gt; Axis crosses &gt; Maximum
    /// category (<see cref="ChartAxisCrosses.Maximum"/>, OOXML's <c>&lt;c:crosses val="max"/&gt;</c>,
    /// round-tripped via <see cref="ChartModel.XAxisCrosses"/>/<see cref="ChartModel.YAxisCrosses"/>)
    /// to where the axis line and its labels are actually drawn -- flipping the physical
    /// <see cref="Axis.Position"/> to the opposite edge (Bottom&lt;-&gt;Top, Left&lt;-&gt;Right) of
    /// this axis's plot-area side. Every other value is a deliberate no-op: <see cref="ChartAxisCrosses.Minimum"/>
    /// already matches the default edge every axis already renders at, and <see cref="ChartAxisCrosses.AutoZero"/>
    /// -- the <see cref="ChartModel"/> default for the overwhelming majority of charts that never
    /// touch this setting -- deliberately keeps the pre-existing edge rendering rather than attempting
    /// a true zero-crossing repositioning, so this fix cannot regress any chart that didn't explicitly
    /// opt into "crosses at maximum". <see cref="ChartAxisCrosses.Custom"/> (crosses at a specific
    /// authored value) is also left as a no-op for the same reason -- it has no single edge to flip to.
    /// </summary>
    private static void ApplyAxisCrossesPosition(Axis axis, ChartAxisCrosses crosses)
    {
        if (crosses != ChartAxisCrosses.Maximum)
            return;

        axis.Position = axis.Position switch
        {
            AxisPosition.Bottom => AxisPosition.Top,
            AxisPosition.Top => AxisPosition.Bottom,
            AxisPosition.Left => AxisPosition.Right,
            AxisPosition.Right => AxisPosition.Left,
            _ => axis.Position
        };
    }

    private static void ApplyAreaStyle(PlotModel model, ChartModel chart, WorkbookTheme theme)
    {
        // R44-meta-1: "No Fill"/"No Line" are explicit user choices distinct from "nothing set" --
        // force the OxyPlot model to render transparent instead of leaving its own opaque default.
        if (chart.IsChartAreaFillSuppressed)
            model.Background = OxyColors.Transparent;
        else if (chart.ResolveChartAreaFillColor(theme) is { } chartFill)
            model.Background = OxyColor.FromRgb(chartFill.R, chartFill.G, chartFill.B);

        if (chart.IsPlotAreaFillSuppressed)
            model.PlotAreaBackground = OxyColors.Transparent;
        else if (chart.ResolvePlotAreaFillColor(theme) is { } plotFill)
            model.PlotAreaBackground = OxyColor.FromRgb(plotFill.R, plotFill.G, plotFill.B);

        if (chart.IsPlotAreaLineSuppressed)
        {
            model.PlotAreaBorderColor = OxyColors.Transparent;
            model.PlotAreaBorderThickness = new OxyThickness(0);
        }
        else
        {
            if (chart.ResolvePlotAreaBorderColor(theme) is { } plotBorder)
                model.PlotAreaBorderColor = OxyColor.FromRgb(plotBorder.R, plotBorder.G, plotBorder.B);
            model.PlotAreaBorderThickness = new OxyThickness(chart.PlotAreaBorderThickness);
        }

        if (chart.ResolveChartDefaultTextColor(theme) is { } defaultText)
            model.TextColor = OxyColor.FromRgb(defaultText.R, defaultText.G, defaultText.B);
    }

    private static void ApplyTitleStyle(PlotModel model, ChartModel chart, WorkbookTheme theme)
    {
        model.TitleFontSize = chart.ChartTitleFontSize;
        if (chart.ResolveChartTitleTextColor(theme) is { } titleColor)
            model.TitleColor = OxyColor.FromRgb(titleColor.R, titleColor.G, titleColor.B);
    }

    private static void ApplyAxisTitleStyle(Axis axis, ChartModel chart, WorkbookTheme theme)
    {
        axis.TitleFontSize = chart.AxisTitleFontSize;
        if (chart.ResolveAxisTitleTextColor(theme) is { } titleColor)
            axis.TitleColor = OxyColor.FromRgb(titleColor.R, titleColor.G, titleColor.B);
    }

    private static void ApplyGridlineStyle(
        Axis axis,
        bool showMajor,
        bool showMinor,
        CellColor? majorColor,
        CellColor? minorColor,
        double thickness)
    {
        axis.MajorGridlineStyle = showMajor ? LineStyle.Solid : LineStyle.None;
        axis.MajorGridlineColor = ToOxyColor(majorColor) ?? OxyColor.FromRgb(220, 220, 220);
        axis.MajorGridlineThickness = thickness;
        axis.MinorGridlineStyle = showMinor ? LineStyle.Dot : LineStyle.None;
        axis.MinorGridlineColor = ToOxyColor(minorColor) ?? OxyColor.FromRgb(235, 235, 235);
        axis.MinorGridlineThickness = Math.Max(0.25, thickness * 0.75);
    }

    private static void ApplyTickAndLabelStyle(
        Axis axis,
        ChartAxisTickStyle majorTickStyle,
        ChartAxisTickStyle minorTickStyle,
        bool showLabels)
    {
        axis.TickStyle = ToOxyTickStyle(majorTickStyle);
        axis.MajorTickSize = GetTickSize(majorTickStyle);
        axis.MinorTickSize = GetTickSize(minorTickStyle);
        if (!showLabels)
            axis.TextColor = OxyColors.Transparent;
    }

    private static void ApplyAxisLabelStyle(Axis axis, CellColor? textColor, double fontSize, double angle)
    {
        axis.FontSize = fontSize;
        axis.Angle = angle;
        if (textColor is { } color && axis.TextColor != OxyColors.Transparent)
            axis.TextColor = OxyColor.FromRgb(color.R, color.G, color.B);
    }

    private static void ApplyAxisLineStyle(Axis axis, CellColor? color, double thickness)
    {
        axis.AxislineStyle = LineStyle.Solid;
        axis.AxislineThickness = thickness;
        if (color is { } lineColor)
            axis.AxislineColor = OxyColor.FromRgb(lineColor.R, lineColor.G, lineColor.B);
    }

    private static double GetTickSize(ChartAxisTickStyle tickStyle) =>
        tickStyle switch
        {
            ChartAxisTickStyle.None => 0,
            ChartAxisTickStyle.Inside => 4,
            ChartAxisTickStyle.Cross => 8,
            _ => 6
        };

    private static TickStyle ToOxyTickStyle(ChartAxisTickStyle tickStyle) =>
        tickStyle switch
        {
            ChartAxisTickStyle.None => TickStyle.None,
            ChartAxisTickStyle.Inside => TickStyle.Inside,
            ChartAxisTickStyle.Cross => TickStyle.Crossing,
            _ => TickStyle.Outside
        };

    /// <summary>
    /// Applies Excel's "Values in reverse order" axis option (<see cref="ChartModel.XAxisReverseOrder"/>/
    /// <see cref="ChartModel.YAxisReverseOrder"/>, OOXML scaling <c>orientation="maxMin"</c>) to the
    /// actual plotted geometry by swapping <see cref="Axis.StartPosition"/>/<see cref="Axis.EndPosition"/>
    /// (OxyPlot's own reversed-axis mechanism — <c>0</c> is bottom/left, <c>1</c> is top/right, so
    /// swapping them flips every series/gridline/tick drawn against this axis). Previously only the
    /// printed axis *label text* was repositioned to fake a reversal (<c>PrintChartTextOverlayPlanner</c>),
    /// which left the plotted bars/lines pointing the un-reversed way under reversed labels; driving the
    /// axis itself keeps the interactive chart, the print label overlay, and the underlying series all
    /// consistent.
    /// </summary>
    private static void ApplyAxisReverseOrder(Axis axis, bool reverseOrder)
    {
        if (!reverseOrder)
            return;

        (axis.StartPosition, axis.EndPosition) = (1, 0);
    }

    private static bool ShouldUseLogAxis(ChartModel chart, Axis axis) =>
        axis.Position is AxisPosition.Bottom or AxisPosition.Top
            ? chart.XAxisLogScale && ChartTypeSupport.SupportsXAxisLogScale(chart.Type)
            : chart.YAxisLogScale && ChartTypeSupport.SupportsYAxisLogScale(chart.Type);

    private static double GetPositiveAxisValue(double value) =>
        double.IsNaN(value) || value <= 0 ? double.NaN : value;

    /// <summary>
    /// Resolves Excel's Format Axis &gt; Display Units (<c>&lt;c:dispUnits&gt;</c>, round-tripped via
    /// <see cref="ChartModel.XAxisDisplayUnit"/>/<see cref="ChartModel.YAxisDisplayUnit"/> and their
    /// custom-unit overrides) to the numeric divisor Excel scales tick labels by. A custom unit
    /// (<c>&lt;c:custUnit&gt;</c>) always wins when present and positive-finite, matching the writer's
    /// own precedence (XlsxChartXmlWriter.Axes.cs ToAxisDisplayUnitXml). Returns null when no display
    /// unit is set, so callers can distinguish "no scaling" from "scale by 1".
    /// </summary>
    private static double? GetAxisDisplayUnitDivisor(ChartAxisDisplayUnit? unit, double? customUnit)
    {
        if (customUnit is { } custom && double.IsFinite(custom) && custom > 0)
            return custom;

        return unit switch
        {
            ChartAxisDisplayUnit.Hundreds => 1e2,
            ChartAxisDisplayUnit.Thousands => 1e3,
            ChartAxisDisplayUnit.TenThousands => 1e4,
            ChartAxisDisplayUnit.HundredThousands => 1e5,
            ChartAxisDisplayUnit.Millions => 1e6,
            ChartAxisDisplayUnit.TenMillions => 1e7,
            ChartAxisDisplayUnit.HundredMillions => 1e8,
            ChartAxisDisplayUnit.Billions => 1e9,
            ChartAxisDisplayUnit.Trillions => 1e12,
            _ => null
        };
    }

    /// <summary>
    /// Applies Excel's axis Display Unit to <paramref name="axis"/>: tick labels are divided by
    /// <paramref name="divisor"/> (so an axis maximum of 3,000,000 with unit=Millions reads "3"), and
    /// the axis title gains a "(Millions)"-style suffix so the scale is still communicated even though
    /// the raw values are no longer shown (Excel draws this as a separate rotated display-units label;
    /// appending it to the axis title is the closest single-line equivalent this renderer already has
    /// a slot for). No-ops when no display unit is set, so unaffected charts render unchanged.
    /// </summary>
    private static void ApplyAxisDisplayUnit(Axis axis, double? divisor, ChartAxisDisplayUnit? unit, double? customUnit)
    {
        if (divisor is not { } scale || scale <= 0 || !double.IsFinite(scale))
            return;

        var innerFormatter = axis.LabelFormatter;
        axis.LabelFormatter = value =>
        {
            var scaled = value / scale;
            return innerFormatter is not null
                ? innerFormatter(scaled)
                : scaled.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        };

        var unitLabel = GetAxisDisplayUnitLabel(unit, customUnit);
        if (string.IsNullOrEmpty(unitLabel))
            return;

        axis.Title = string.IsNullOrEmpty(axis.Title) ? unitLabel : $"{axis.Title} ({unitLabel})";
    }

    private static string GetAxisDisplayUnitLabel(ChartAxisDisplayUnit? unit, double? customUnit)
    {
        if (customUnit is { } custom && double.IsFinite(custom) && custom > 0)
            return custom.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        return unit switch
        {
            ChartAxisDisplayUnit.Hundreds => "Hundreds",
            ChartAxisDisplayUnit.Thousands => "Thousands",
            ChartAxisDisplayUnit.TenThousands => "Ten Thousands",
            ChartAxisDisplayUnit.HundredThousands => "Hundred Thousands",
            ChartAxisDisplayUnit.Millions => "Millions",
            ChartAxisDisplayUnit.TenMillions => "Ten Millions",
            ChartAxisDisplayUnit.HundredMillions => "Hundred Millions",
            ChartAxisDisplayUnit.Billions => "Billions",
            ChartAxisDisplayUnit.Trillions => "Trillions",
            _ => ""
        };
    }
}
