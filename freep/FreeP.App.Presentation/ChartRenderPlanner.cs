using System.Globalization;
using System.Text;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public readonly record struct ChartPlanPoint(double X, double Y);

public readonly record struct ChartPlanRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool HasPositiveArea => Width > 0 && Height > 0;
}

public enum ChartRenderFamily
{
    Cartesian,
    HorizontalBar,
    Pie,
    ScatterLike,
    Radar
}

public enum ChartPlanTextAlignment
{
    Left,
    Center,
    Right
}

public readonly record struct ChartFramePlan(
    ChartPlanRect Bounds,
    ChartPlanRect Plot,
    ChartPlanRect? TitleBounds,
    bool HasLegend,
    bool LegendRight,
    double LegendAreaWidth,
    double LegendAreaHeight,
    ChartRenderFamily Family)
{
    public bool HasPlot => Plot.HasPositiveArea;
    public bool IsPie => Family == ChartRenderFamily.Pie;
    public bool IsBar => Family == ChartRenderFamily.HorizontalBar;
    public bool IsScatterLike => Family == ChartRenderFamily.ScatterLike;
    public bool IsRadar => Family == ChartRenderFamily.Radar;
}

public readonly record struct ChartGridLinePlan(ChartPlanPoint Start, ChartPlanPoint End);

public readonly record struct ChartTextPlan(
    string Text,
    ChartPlanRect Bounds,
    bool IsBold,
    double FontSize,
    ChartPlanTextAlignment Alignment);

public readonly record struct ChartRectPrimitive(
    int SeriesIndex,
    int CategoryIndex,
    ChartPlanRect Bounds);

public readonly record struct ChartLineSeriesPrimitive(
    int SeriesIndex,
    bool WithMarkers,
    IReadOnlyList<ChartPlanPoint?> Points);

public readonly record struct ChartPieSlicePrimitive(
    int SeriesIndex,
    int PointIndex,
    ChartPlanPoint Center,
    double InnerRadius,
    double OuterRadius,
    double StartAngle,
    double EndAngle)
{
    public double SweepAngle => EndAngle - StartAngle;
    public bool IsLargeArc => SweepAngle > Math.PI;
    public ChartPlanPoint OuterStart => PointOnCircle(OuterRadius, StartAngle);
    public ChartPlanPoint OuterEnd => PointOnCircle(OuterRadius, EndAngle);
    public ChartPlanPoint InnerEnd => PointOnCircle(InnerRadius, EndAngle);
    public ChartPlanPoint InnerStart => PointOnCircle(InnerRadius, StartAngle);

    private ChartPlanPoint PointOnCircle(double radius, double angle) =>
        new(Center.X + radius * Math.Cos(angle), Center.Y + radius * Math.Sin(angle));
}

public readonly record struct ChartDataLabelPlan(
    int SeriesIndex,
    int CategoryIndex,
    string Text,
    ChartPlanRect Bounds,
    bool IsBold,
    double FontSize,
    ChartPlanTextAlignment Alignment);

/// <summary>
/// Renderer-neutral chart planning helpers shared by the WPF and Avalonia slide canvases.
/// </summary>
public static class ChartRenderPlanner
{
    public const double Margin = 8.0;
    public const double TitleHeight = 18.0;
    public const double LegendHeight = 14.0;
    public const double AxisLabelWidth = 40.0;
    public const double CategoryLabelHeight = 16.0;
    public const double BarCategoryLabelWidth = 44.0;
    public const double GridlinePad = 2.0;

    public static ChartFramePlan BuildFramePlan(ChartShape chart, ChartPlanRect bounds)
    {
        double titleAreaHeight = chart.Title is not null ? TitleHeight + Margin : 0;
        bool hasLegend = chart.Legend.HasValue;
        bool legendRight = chart.Legend is LegendPosition.Right or LegendPosition.Left;
        double legendAreaWidth = hasLegend && legendRight
            ? Math.Min(90, bounds.Width * 0.20)
            : 0;
        double legendAreaHeight = hasLegend && !legendRight
            ? LegendHeight + Margin
            : 0;

        var family = GetRenderFamily(chart.ChartType);
        bool reservesAxes = family is not (ChartRenderFamily.Pie
            or ChartRenderFamily.ScatterLike
            or ChartRenderFamily.Radar);
        bool isBar = family == ChartRenderFamily.HorizontalBar;

        double plotLeft = bounds.X + Margin
            + (reservesAxes ? (isBar ? BarCategoryLabelWidth : AxisLabelWidth) : 0);
        double plotTop = bounds.Y + Margin + titleAreaHeight;
        double plotRight = bounds.X + bounds.Width - Margin - legendAreaWidth;
        double plotBottom = bounds.Y + bounds.Height - Margin - legendAreaHeight
            - (reservesAxes ? (isBar ? AxisLabelWidth : CategoryLabelHeight) : 0);

        var plot = new ChartPlanRect(
            plotLeft,
            plotTop,
            plotRight - plotLeft,
            plotBottom - plotTop);

        ChartPlanRect? titleBounds = chart.Title is not null
            ? new ChartPlanRect(
                bounds.X + Margin,
                bounds.Y + Margin,
                bounds.Width - 2 * Margin,
                TitleHeight)
            : null;

        return new ChartFramePlan(
            bounds,
            plot,
            titleBounds,
            hasLegend,
            legendRight,
            legendAreaWidth,
            legendAreaHeight,
            family);
    }

    public static ChartRenderFamily GetRenderFamily(ChartType chartType) =>
        chartType switch
        {
            ChartType.Pie or ChartType.Doughnut => ChartRenderFamily.Pie,
            ChartType.BarClustered or ChartType.BarStacked or ChartType.BarStacked100 => ChartRenderFamily.HorizontalBar,
            ChartType.Scatter or ChartType.Bubble => ChartRenderFamily.ScatterLike,
            ChartType.Radar => ChartRenderFamily.Radar,
            _ => ChartRenderFamily.Cartesian
        };

    public static bool IsLineOrArea(ChartType chartType) =>
        chartType is ChartType.Line
            or ChartType.LineMarkers
            or ChartType.Area
            or ChartType.AreaStacked;

    public static IReadOnlyList<ChartGridLinePlan> BuildMajorGridLinePlans(
        ChartShape chart,
        ChartFramePlan frame)
    {
        if (!frame.HasPlot || frame.IsPie || frame.IsRadar || frame.IsScatterLike || !chart.ValueAxis.HasMajorGridlines)
            return Array.Empty<ChartGridLinePlan>();

        var (minValue, maxValue, majorUnit) = ComputePrimaryValueAxisRange(chart);
        double steps = (maxValue - minValue) / majorUnit;
        if (steps <= 0)
            return Array.Empty<ChartGridLinePlan>();

        var plot = frame.Plot;
        int tickCount = (int)Math.Round(steps);
        var lines = new List<ChartGridLinePlan>(tickCount + 1);
        for (int index = 0; index <= tickCount; index++)
        {
            if (frame.IsBar)
            {
                double x = plot.X + plot.Width * index / steps;
                lines.Add(new ChartGridLinePlan(
                    new ChartPlanPoint(x, plot.Y),
                    new ChartPlanPoint(x, plot.Bottom)));
            }
            else
            {
                double y = plot.Bottom - plot.Height * index / steps;
                lines.Add(new ChartGridLinePlan(
                    new ChartPlanPoint(plot.X, y),
                    new ChartPlanPoint(plot.Right, y)));
            }
        }

        return lines;
    }

    public static IReadOnlyList<ChartTextPlan> BuildCategoryAxisLabelPlans(
        ChartShape chart,
        ChartFramePlan frame)
    {
        if (!frame.HasPlot || frame.IsPie || frame.IsRadar || frame.IsScatterLike || chart.Categories.Count == 0)
            return Array.Empty<ChartTextPlan>();

        var labels = new List<ChartTextPlan>(chart.Categories.Count);
        var plot = frame.Plot;
        if (frame.IsBar)
        {
            int categoryCount = chart.Categories.Count;
            double categoryStep = plot.Height / Math.Max(1, categoryCount);
            for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
            {
                int renderRow = categoryCount - 1 - categoryIndex;
                double y = plot.Y + renderRow * categoryStep;
                labels.Add(new ChartTextPlan(
                    chart.Categories[categoryIndex],
                    new ChartPlanRect(frame.Bounds.X + Margin, y, BarCategoryLabelWidth - 4, categoryStep),
                    IsBold: false,
                    FontSize: 6.5,
                    Alignment: ChartPlanTextAlignment.Right));
            }
        }
        else
        {
            double categoryStep = plot.Width / Math.Max(1, chart.Categories.Count);
            for (int categoryIndex = 0; categoryIndex < chart.Categories.Count; categoryIndex++)
            {
                double x = plot.X + categoryIndex * categoryStep;
                labels.Add(new ChartTextPlan(
                    chart.Categories[categoryIndex],
                    new ChartPlanRect(x, plot.Bottom + 2, categoryStep, CategoryLabelHeight),
                    IsBold: false,
                    FontSize: 7.0,
                    Alignment: ChartPlanTextAlignment.Center));
            }
        }

        return labels;
    }

    public static IReadOnlyList<ChartTextPlan> BuildValueAxisLabelPlans(
        ChartShape chart,
        ChartFramePlan frame)
    {
        if (!frame.HasPlot || frame.IsPie || frame.IsRadar || frame.IsScatterLike)
            return Array.Empty<ChartTextPlan>();

        var (minValue, maxValue, majorUnit) = ComputePrimaryValueAxisRange(chart);
        double steps = (maxValue - minValue) / majorUnit;
        if (steps <= 0)
            return Array.Empty<ChartTextPlan>();

        int tickCount = (int)Math.Round(steps);
        var labels = new List<ChartTextPlan>(tickCount + 1);
        var plot = frame.Plot;
        for (int tickIndex = 0; tickIndex <= tickCount; tickIndex++)
        {
            double value = minValue + majorUnit * tickIndex;
            if (frame.IsBar)
            {
                double x = plot.X + plot.Width * tickIndex / steps;
                labels.Add(new ChartTextPlan(
                    FormatAxisValue(value),
                    new ChartPlanRect(x - AxisLabelWidth / 2, plot.Bottom + 2, AxisLabelWidth, CategoryLabelHeight),
                    IsBold: false,
                    FontSize: 6.5,
                    Alignment: ChartPlanTextAlignment.Center));
            }
            else
            {
                double y = plot.Bottom - plot.Height * tickIndex / steps;
                labels.Add(new ChartTextPlan(
                    FormatAxisValue(value),
                    new ChartPlanRect(frame.Bounds.X + Margin, y - 6, AxisLabelWidth - GridlinePad, 12),
                    IsBold: false,
                    FontSize: 6.5,
                    Alignment: ChartPlanTextAlignment.Right));
            }
        }

        return labels;
    }

    public static IReadOnlyList<ChartTextPlan> BuildSecondaryValueAxisLabelPlans(
        ChartShape chart,
        ChartPlanRect plot,
        double boundsRight)
    {
        if (chart.SecondaryValueAxis is null || !plot.HasPositiveArea)
            return Array.Empty<ChartTextPlan>();

        double secondaryMin = 0;
        double secondaryMax = 0;
        foreach (var series in chart.Series)
        {
            if (!series.OnSecondaryAxis)
                continue;

            foreach (var value in series.Values)
            {
                if (!value.HasValue)
                    continue;

                secondaryMin = Math.Min(secondaryMin, value.Value);
                secondaryMax = Math.Max(secondaryMax, value.Value);
            }
        }

        double axisMin = chart.SecondaryValueAxis.Min ?? (secondaryMin >= 0 ? 0 : secondaryMin);
        double axisMax = chart.SecondaryValueAxis.Max ?? secondaryMax;
        if (axisMax <= axisMin)
            axisMax = axisMin + 1;

        double range = axisMax - axisMin;
        double rawUnit = range / 4.0;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(rawUnit, 1e-9))));
        double normalized = rawUnit / magnitude;
        double niceMultiplier = normalized < 1.5
            ? 1.0
            : normalized < 2.25
                ? 2.0
                : normalized < 3.75
                    ? 2.5
                    : normalized < 7.5
                        ? 5.0
                        : 10.0;
        double majorUnit = niceMultiplier * magnitude;
        double niceMax = Math.Ceiling(axisMax / majorUnit) * majorUnit;
        double niceMin = axisMin >= 0 ? 0 : Math.Floor(axisMin / majorUnit) * majorUnit;
        double steps = (niceMax - niceMin) / majorUnit;
        if (steps <= 0)
            return Array.Empty<ChartTextPlan>();

        int tickCount = (int)Math.Round(steps);
        var labels = new List<ChartTextPlan>(tickCount + 1);
        for (int tickIndex = 0; tickIndex <= tickCount; tickIndex++)
        {
            double value = niceMin + majorUnit * tickIndex;
            double y = plot.Bottom - plot.Height * tickIndex / steps;
            labels.Add(new ChartTextPlan(
                FormatAxisValue(value),
                new ChartPlanRect(boundsRight + 2, y - 6, AxisLabelWidth, 12),
                IsBold: false,
                FontSize: 6.5,
                Alignment: ChartPlanTextAlignment.Left));
        }

        return labels;
    }

    public static IReadOnlyList<ChartRectPrimitive> BuildColumnPrimitives(
        ChartShape chart,
        ChartPlanRect plot)
    {
        int categoryCount = Math.Max(1, chart.Categories.Count);
        if (chart.Series.Count == 0 || !plot.HasPositiveArea)
            return Array.Empty<ChartRectPrimitive>();

        var (primaryMin, primaryMax, _) = ComputePrimaryValueAxisRange(chart);
        double primaryRange = primaryMax - primaryMin;
        if (primaryRange <= 0)
            return Array.Empty<ChartRectPrimitive>();

        var (secondaryMin, secondaryMax, _) = ComputeSecondaryValueAxisRange(chart);
        double secondaryRange = secondaryMax - secondaryMin;
        bool stacked = chart.ChartType is ChartType.ColumnStacked or ChartType.ColumnStacked100;
        const double gapRatio = 1.5;
        double categoryWidth = plot.Width / categoryCount;
        double clusterWidth = categoryWidth / (1.0 + gapRatio);
        double halfGap = (categoryWidth - clusterWidth) / 2.0;
        int seriesCount = Math.Max(1, chart.Series.Count);
        double seriesWidth = stacked ? clusterWidth : clusterWidth / seriesCount;

        var primitives = new List<ChartRectPrimitive>();
        for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            double categoryLeft = plot.X + categoryIndex * categoryWidth + halfGap;
            double stackedY = plot.Bottom;

            for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
            {
                var series = chart.Series[seriesIndex];
                if (series.OverrideChartType.HasValue &&
                    series.OverrideChartType.Value is ChartType.Line
                        or ChartType.LineMarkers
                        or ChartType.Scatter
                        or ChartType.Bubble)
                {
                    continue;
                }

                double? rawValue = categoryIndex < series.Values.Count
                    ? series.Values[categoryIndex]
                    : null;
                if (rawValue is null)
                    continue;

                double effectiveMin = series.OnSecondaryAxis ? secondaryMin : primaryMin;
                double effectiveRange = series.OnSecondaryAxis ? secondaryRange : primaryRange;
                if (effectiveRange <= 0)
                    continue;

                double x = stacked ? categoryLeft : categoryLeft + seriesIndex * seriesWidth;
                double drawWidth = Math.Max(1, stacked ? seriesWidth : seriesWidth - 1);
                if (stacked)
                {
                    double height = Math.Max(0.5, Math.Abs(rawValue.Value / effectiveRange) * plot.Height);
                    primitives.Add(new ChartRectPrimitive(
                        seriesIndex,
                        categoryIndex,
                        new ChartPlanRect(x, stackedY - height, drawWidth, height)));
                    stackedY -= height;
                }
                else
                {
                    double height = Math.Max(0.5, Math.Abs((rawValue.Value - effectiveMin) / effectiveRange * plot.Height));
                    double y = plot.Bottom - (rawValue.Value - effectiveMin) / effectiveRange * plot.Height;
                    primitives.Add(new ChartRectPrimitive(
                        seriesIndex,
                        categoryIndex,
                        new ChartPlanRect(x, y, drawWidth, height)));
                }
            }
        }

        return primitives;
    }

    public static IReadOnlyList<ChartRectPrimitive> BuildBarPrimitives(
        ChartShape chart,
        ChartPlanRect plot)
    {
        int categoryCount = Math.Max(1, chart.Categories.Count);
        if (chart.Series.Count == 0 || !plot.HasPositiveArea)
            return Array.Empty<ChartRectPrimitive>();

        var (primaryMin, primaryMax, _) = ComputePrimaryValueAxisRange(chart);
        double primaryRange = primaryMax - primaryMin;
        if (primaryRange <= 0)
            return Array.Empty<ChartRectPrimitive>();

        var (secondaryMin, secondaryMax, _) = ComputeSecondaryValueAxisRange(chart);
        double secondaryRange = secondaryMax - secondaryMin;
        bool stacked = chart.ChartType is ChartType.BarStacked or ChartType.BarStacked100;
        const double gapRatio = 1.5;
        double categoryHeight = plot.Height / categoryCount;
        double clusterHeight = categoryHeight / (1.0 + gapRatio);
        double halfGap = (categoryHeight - clusterHeight) / 2.0;
        int seriesCount = Math.Max(1, chart.Series.Count);
        double seriesHeight = stacked ? clusterHeight : clusterHeight / seriesCount;

        var primitives = new List<ChartRectPrimitive>();
        for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            int renderRow = categoryCount - 1 - categoryIndex;
            double categoryTop = plot.Y + renderRow * categoryHeight + halfGap;
            double stackedX = plot.X;

            for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
            {
                var series = chart.Series[seriesIndex];
                double? rawValue = categoryIndex < series.Values.Count
                    ? series.Values[categoryIndex]
                    : null;
                if (rawValue is null)
                    continue;

                double effectiveMin = series.OnSecondaryAxis ? secondaryMin : primaryMin;
                double effectiveRange = series.OnSecondaryAxis ? secondaryRange : primaryRange;
                if (effectiveRange <= 0)
                    continue;

                double width = Math.Max(0.5, Math.Abs((rawValue.Value - effectiveMin) / effectiveRange * plot.Width));
                int renderSeries = stacked ? seriesIndex : seriesCount - 1 - seriesIndex;
                double y = stacked ? categoryTop : categoryTop + renderSeries * seriesHeight;
                double x = stacked ? stackedX : plot.X;
                double height = Math.Max(1, stacked ? seriesHeight : seriesHeight - 1);

                primitives.Add(new ChartRectPrimitive(
                    seriesIndex,
                    categoryIndex,
                    new ChartPlanRect(x, y, width, height)));

                if (stacked)
                    stackedX += width;
            }
        }

        return primitives;
    }

    public static IReadOnlyList<ChartLineSeriesPrimitive> BuildLineSeriesPrimitives(
        ChartShape chart,
        ChartPlanRect plot,
        bool withMarkers)
    {
        int categoryCount = Math.Max(1, chart.Categories.Count);
        if (chart.Series.Count == 0 || !plot.HasPositiveArea)
            return Array.Empty<ChartLineSeriesPrimitive>();

        var (primaryMin, primaryMax, _) = ComputePrimaryValueAxisRange(chart);
        double primaryRange = primaryMax - primaryMin;
        if (primaryRange <= 0)
            return Array.Empty<ChartLineSeriesPrimitive>();

        var (secondaryMin, secondaryMax, _) = ComputeSecondaryValueAxisRange(chart);
        double secondaryRange = secondaryMax - secondaryMin;
        double stepX = plot.Width / Math.Max(1, categoryCount - 1);
        var primitives = new List<ChartLineSeriesPrimitive>();

        for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
        {
            var series = chart.Series[seriesIndex];
            double effectiveMin = series.OnSecondaryAxis ? secondaryMin : primaryMin;
            double effectiveRange = series.OnSecondaryAxis ? secondaryRange : primaryRange;
            if (effectiveRange <= 0)
                continue;

            var points = new ChartPlanPoint?[categoryCount];
            for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
            {
                double? rawValue = categoryIndex < series.Values.Count
                    ? series.Values[categoryIndex]
                    : null;
                if (rawValue is null)
                    continue;

                double x = plot.X + categoryIndex * stepX;
                double y = plot.Bottom - (rawValue.Value - effectiveMin) / effectiveRange * plot.Height;
                points[categoryIndex] = new ChartPlanPoint(x, y);
            }

            primitives.Add(new ChartLineSeriesPrimitive(seriesIndex, withMarkers, points));
        }

        return primitives;
    }

    public static IReadOnlyList<ChartLineSeriesPrimitive> BuildComboOverrideLineSeriesPrimitives(
        ChartShape chart,
        ChartPlanRect plot)
    {
        int categoryCount = Math.Max(1, chart.Categories.Count);
        if (chart.Series.Count == 0 || !plot.HasPositiveArea)
            return Array.Empty<ChartLineSeriesPrimitive>();

        var (secondaryMin, secondaryMax, _) = ComputeSecondaryValueAxisRange(chart);
        double secondaryRange = secondaryMax - secondaryMin;
        var (primaryMin, primaryMax, _) = ComputePrimaryValueAxisRange(chart);
        double primaryRange = primaryMax - primaryMin;
        double stepX = categoryCount > 1 ? plot.Width / (categoryCount - 1) : plot.Width / 2;
        var primitives = new List<ChartLineSeriesPrimitive>();

        for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
        {
            var series = chart.Series[seriesIndex];
            var overrideType = series.OverrideChartType;
            if (overrideType is not (ChartType.Line or ChartType.LineMarkers))
                continue;

            double effectiveMin = series.OnSecondaryAxis ? secondaryMin : primaryMin;
            double effectiveRange = series.OnSecondaryAxis ? secondaryRange : primaryRange;
            if (effectiveRange <= 0)
                continue;

            var points = new ChartPlanPoint?[categoryCount];
            for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
            {
                double? rawValue = categoryIndex < series.Values.Count
                    ? series.Values[categoryIndex]
                    : null;
                if (rawValue is null)
                    continue;

                double x = categoryCount == 1
                    ? plot.X + plot.Width / 2
                    : plot.X + categoryIndex * stepX;
                double y = plot.Bottom - (rawValue.Value - effectiveMin) / effectiveRange * plot.Height;
                points[categoryIndex] = new ChartPlanPoint(x, y);
            }

            primitives.Add(new ChartLineSeriesPrimitive(
                seriesIndex,
                overrideType == ChartType.LineMarkers,
                points));
        }

        return primitives;
    }

    public static IReadOnlyList<ChartPieSlicePrimitive> BuildPieSlicePrimitives(
        ChartShape chart,
        ChartPlanRect plot)
    {
        if (chart.Series.Count == 0 || !plot.HasPositiveArea)
            return Array.Empty<ChartPieSlicePrimitive>();

        return BuildSlicePrimitivesForSeries(
            chart.Series[0],
            seriesIndex: 0,
            plot,
            innerRadius: 0,
            outerRadius: Math.Min(plot.Width, plot.Height) / 2 * 0.85);
    }

    public static IReadOnlyList<ChartPieSlicePrimitive> BuildDoughnutSlicePrimitives(
        ChartShape chart,
        ChartPlanRect plot)
    {
        if (chart.Series.Count == 0 || !plot.HasPositiveArea)
            return Array.Empty<ChartPieSlicePrimitive>();

        double outerRadius = Math.Min(plot.Width, plot.Height) / 2 * 0.85;
        double innerHoleRadius = outerRadius * Math.Clamp(chart.DoughnutHolePercent, 0, 90) / 100.0;
        int seriesCount = chart.Series.Count;
        double ringGap = seriesCount > 1 ? outerRadius * 0.04 : 0;
        double ringWidth = seriesCount > 1
            ? (outerRadius - innerHoleRadius - (seriesCount - 1) * ringGap) / seriesCount
            : outerRadius - innerHoleRadius;

        var primitives = new List<ChartPieSlicePrimitive>();
        for (int seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
        {
            double innerRadius = innerHoleRadius + seriesIndex * (ringWidth + ringGap);
            double seriesOuterRadius = innerRadius + ringWidth;
            if (seriesOuterRadius <= 0 || innerRadius < 0)
                innerRadius = 0;

            primitives.AddRange(BuildSlicePrimitivesForSeries(
                chart.Series[seriesIndex],
                seriesIndex,
                plot,
                innerRadius,
                seriesOuterRadius));
        }

        return primitives;
    }

    public static IReadOnlyList<ChartDataLabelPlan> BuildDataLabelPlans(
        ChartShape chart,
        ChartPlanRect plot)
    {
        var family = GetRenderFamily(chart.ChartType);
        if (family is ChartRenderFamily.Radar or ChartRenderFamily.ScatterLike || !plot.HasPositiveArea)
            return Array.Empty<ChartDataLabelPlan>();

        if (family == ChartRenderFamily.Pie)
            return BuildPieDataLabelPlans(chart, plot);

        var plans = new List<ChartDataLabelPlan>();
        bool isLineOrArea = IsLineOrArea(chart.ChartType);
        bool isBar = family == ChartRenderFamily.HorizontalBar;
        for (int seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
        {
            var overrideType = chart.Series[seriesIndex].OverrideChartType;
            bool seriesIsLineOrArea = overrideType.HasValue
                ? IsLineOrArea(overrideType.Value)
                : isLineOrArea;
            bool seriesIsBar = overrideType.HasValue
                ? overrideType.Value is ChartType.BarClustered
                    or ChartType.BarStacked
                    or ChartType.BarStacked100
                : isBar;

            IReadOnlyList<ChartDataLabelPlan> seriesPlans = seriesIsLineOrArea
                ? BuildLineDataLabelPlans(chart, seriesIndex, plot)
                : seriesIsBar
                    ? BuildBarDataLabelPlans(chart, seriesIndex, plot)
                    : BuildColumnDataLabelPlans(chart, seriesIndex, plot);

            plans.AddRange(seriesPlans);
        }

        return plans;
    }

    public static (double min, double max, double majorUnit) ComputePrimaryValueAxisRange(
        ChartShape chart)
    {
        double dataMin = 0;
        double dataMax = 0;

        foreach (var series in chart.Series)
        {
            if (series.OnSecondaryAxis)
                continue;

            AccumulateValues(series.Values, ref dataMin, ref dataMax);
        }

        double min = chart.ValueAxis.Min ?? (dataMin >= 0 ? 0 : dataMin);
        double max = chart.ValueAxis.Max ?? dataMax;
        return ComputeNiceRange(min, max);
    }

    public static (double min, double max, double majorUnit) ComputeSecondaryValueAxisRange(
        ChartShape chart)
    {
        double dataMin = 0;
        double dataMax = 0;
        bool any = false;

        foreach (var series in chart.Series)
        {
            if (!series.OnSecondaryAxis)
                continue;

            AccumulateValues(series.Values, ref dataMin, ref dataMax, ref any);
        }

        if (!any)
            return (0, 1, 1);

        double min = chart.SecondaryValueAxis?.Min ?? (dataMin >= 0 ? 0 : dataMin);
        double max = chart.SecondaryValueAxis?.Max ?? dataMax;
        return ComputeNiceRange(min, max);
    }

    public static (double min, double max, double majorUnit) ComputeScatterAxisRange(
        ChartShape chart,
        bool useX)
    {
        double dataMin = 0;
        double dataMax = 0;

        foreach (var series in chart.Series)
        {
            var values = useX ? series.XValues : series.Values;
            AccumulateValues(values, ref dataMin, ref dataMax);
        }

        double min = dataMin >= 0 ? 0 : dataMin;
        double max = dataMax;
        return ComputeNiceRange(min, max);
    }

    public static string FormatAxisValue(double value) =>
        Math.Abs(value) >= 1000
            ? $"{value / 1000:G4}K"
            : value == Math.Floor(value)
                ? ((long)value).ToString(CultureInfo.InvariantCulture)
                : value.ToString("G3", CultureInfo.InvariantCulture);

    public static ChartDataLabels? ResolveEffectiveLabels(ChartShape chart, int seriesIndex)
    {
        var series = seriesIndex < chart.Series.Count ? chart.Series[seriesIndex] : null;
        var labels = series?.DataLabels ?? chart.DataLabels;
        return labels is not null && labels.HasAny ? labels : null;
    }

    public static string FormatDataLabel(
        ChartDataLabels labels,
        double value,
        double total,
        string? categoryName,
        string? seriesName)
    {
        string formattedValue = string.IsNullOrEmpty(labels.NumberFormat)
            ? FormatAxisValue(value)
            : FormatWithCode(value, labels.NumberFormat!);

        string percent = total > 0
            ? $"{value / total * 100:0}%"
            : "0%";

        var parts = new StringBuilder();
        if (labels.ShowSeriesName && !string.IsNullOrEmpty(seriesName))
            parts.Append(seriesName).Append(' ');
        if (labels.ShowCategoryName && !string.IsNullOrEmpty(categoryName))
            parts.Append(categoryName).Append(' ');
        if (labels.ShowValue)
            parts.Append(formattedValue).Append(' ');
        if (labels.ShowPercent)
            parts.Append(percent).Append(' ');

        return parts.ToString().Trim();
    }

    public static string FormatWithCode(double value, string code)
    {
        if (code.Contains('%'))
        {
            double percent = value * 100.0;
            int dotPosition = code.IndexOf('.');
            int decimals = dotPosition >= 0 ? code.LastIndexOf('%') - dotPosition - 1 : 0;
            string format = decimals > 0 ? $"F{decimals}" : "F0";
            return percent.ToString(format, CultureInfo.InvariantCulture) + "%";
        }

        if (code.Contains(','))
            return value.ToString("N0", CultureInfo.InvariantCulture);

        int dotIndex = code.IndexOf('.');
        if (dotIndex >= 0)
        {
            int decimals = code.Length - dotIndex - 1;
            return value.ToString($"F{decimals}", CultureInfo.InvariantCulture);
        }

        return FormatAxisValue(value);
    }

    private static IReadOnlyList<ChartPieSlicePrimitive> BuildSlicePrimitivesForSeries(
        ChartSeries series,
        int seriesIndex,
        ChartPlanRect plot,
        double innerRadius,
        double outerRadius)
    {
        var values = series.Values
            .Where(value => value.HasValue && value.Value > 0)
            .Select(value => value!.Value)
            .ToList();
        if (values.Count == 0)
            return Array.Empty<ChartPieSlicePrimitive>();

        double total = values.Sum();
        if (total <= 0)
            return Array.Empty<ChartPieSlicePrimitive>();

        var center = new ChartPlanPoint(
            plot.X + plot.Width / 2,
            plot.Y + plot.Height / 2);
        double startAngle = -Math.PI / 2;
        var primitives = new List<ChartPieSlicePrimitive>(values.Count);
        for (int pointIndex = 0; pointIndex < values.Count; pointIndex++)
        {
            double sweepAngle = values[pointIndex] / total * 2 * Math.PI;
            double endAngle = startAngle + sweepAngle;
            primitives.Add(new ChartPieSlicePrimitive(
                seriesIndex,
                pointIndex,
                center,
                innerRadius,
                outerRadius,
                startAngle,
                endAngle));
            startAngle = endAngle;
        }

        return primitives;
    }

    private static IReadOnlyList<ChartDataLabelPlan> BuildColumnDataLabelPlans(
        ChartShape chart,
        int seriesIndex,
        ChartPlanRect plot)
    {
        var labels = ResolveEffectiveLabels(chart, seriesIndex);
        if (labels is null || seriesIndex < 0 || seriesIndex >= chart.Series.Count)
            return Array.Empty<ChartDataLabelPlan>();

        var series = chart.Series[seriesIndex];
        int categoryCount = Math.Max(1, chart.Categories.Count);
        var (primaryMin, primaryMax, _) = ComputePrimaryValueAxisRange(chart);
        double primaryRange = primaryMax - primaryMin;
        if (primaryRange <= 0)
            return Array.Empty<ChartDataLabelPlan>();

        var (secondaryMin, secondaryMax, _) = ComputeSecondaryValueAxisRange(chart);
        double secondaryRange = secondaryMax - secondaryMin;
        double effectiveMin = series.OnSecondaryAxis ? secondaryMin : primaryMin;
        double effectiveRange = series.OnSecondaryAxis ? secondaryRange : primaryRange;
        if (effectiveRange <= 0)
            return Array.Empty<ChartDataLabelPlan>();

        bool stacked = chart.ChartType is ChartType.ColumnStacked or ChartType.ColumnStacked100;
        const double gapRatio = 1.5;
        double categoryWidth = plot.Width / categoryCount;
        double clusterWidth = categoryWidth / (1.0 + gapRatio);
        double halfGap = (categoryWidth - clusterWidth) / 2.0;
        int seriesCount = Math.Max(1, chart.Series.Count);
        double seriesWidth = stacked ? clusterWidth : clusterWidth / seriesCount;
        var position = labels.Position ?? DataLabelPosition.OutsideEnd;
        var plans = new List<ChartDataLabelPlan>();

        for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            double? rawValue = categoryIndex < series.Values.Count
                ? series.Values[categoryIndex]
                : null;
            if (rawValue is null)
                continue;

            double value = rawValue.Value;
            double barX = stacked
                ? plot.X + categoryIndex * categoryWidth + halfGap
                : plot.X + categoryIndex * categoryWidth + halfGap + seriesIndex * seriesWidth;

            double barHeight;
            double barY;
            if (stacked)
            {
                double stackedY = plot.Bottom;
                for (int previousSeriesIndex = 0; previousSeriesIndex < seriesIndex; previousSeriesIndex++)
                {
                    double? previousValue = categoryIndex < chart.Series[previousSeriesIndex].Values.Count
                        ? chart.Series[previousSeriesIndex].Values[categoryIndex]
                        : null;
                    if (previousValue is null)
                        continue;

                    double height = Math.Max(0.5, Math.Abs(previousValue.Value / effectiveRange) * plot.Height);
                    stackedY -= height;
                }

                barHeight = Math.Max(0.5, Math.Abs(value / effectiveRange) * plot.Height);
                barY = stackedY - barHeight;
            }
            else
            {
                barHeight = Math.Abs((value - effectiveMin) / effectiveRange * plot.Height);
                barY = plot.Bottom - (value - effectiveMin) / effectiveRange * plot.Height;
            }

            double total = ComputeDataLabelTotal(chart, series, categoryIndex, stacked, labels);
            string categoryName = categoryIndex < chart.Categories.Count
                ? chart.Categories[categoryIndex]
                : string.Empty;
            string text = FormatDataLabel(labels, value, total, categoryName, series.Name);
            if (string.IsNullOrEmpty(text))
                continue;

            const double labelHeight = 11.0;
            double labelY = value < 0
                ? position switch
                {
                    DataLabelPosition.InsideEnd => barY + barHeight - labelHeight - 2,
                    DataLabelPosition.Center => barY + barHeight / 2 - labelHeight / 2,
                    DataLabelPosition.InsideBase => barY + 2,
                    _ => barY + barHeight + 1
                }
                : position switch
                {
                    DataLabelPosition.InsideEnd => barY + 2,
                    DataLabelPosition.Center => barY + barHeight / 2 - labelHeight / 2,
                    DataLabelPosition.InsideBase => barY + barHeight - labelHeight - 2,
                    _ => barY - labelHeight - 1
                };

            plans.Add(new ChartDataLabelPlan(
                seriesIndex,
                categoryIndex,
                text,
                new ChartPlanRect(barX, labelY, seriesWidth, labelHeight),
                IsBold: false,
                FontSize: 6.5,
                Alignment: ChartPlanTextAlignment.Center));
        }

        return plans;
    }

    private static IReadOnlyList<ChartDataLabelPlan> BuildLineDataLabelPlans(
        ChartShape chart,
        int seriesIndex,
        ChartPlanRect plot)
    {
        var labels = ResolveEffectiveLabels(chart, seriesIndex);
        if (labels is null || seriesIndex < 0 || seriesIndex >= chart.Series.Count)
            return Array.Empty<ChartDataLabelPlan>();

        var series = chart.Series[seriesIndex];
        int categoryCount = Math.Max(1, chart.Categories.Count);
        var (primaryMin, primaryMax, _) = ComputePrimaryValueAxisRange(chart);
        double primaryRange = primaryMax - primaryMin;
        if (primaryRange <= 0)
            return Array.Empty<ChartDataLabelPlan>();

        var (secondaryMin, secondaryMax, _) = ComputeSecondaryValueAxisRange(chart);
        double secondaryRange = secondaryMax - secondaryMin;
        double effectiveMin = series.OnSecondaryAxis ? secondaryMin : primaryMin;
        double effectiveRange = series.OnSecondaryAxis ? secondaryRange : primaryRange;
        if (effectiveRange <= 0)
            return Array.Empty<ChartDataLabelPlan>();

        double stepX = plot.Width / Math.Max(1, categoryCount - 1);
        double total = ComputeDataLabelTotal(chart, series, categoryIndex: 0, stacked: false, labels);
        var plans = new List<ChartDataLabelPlan>();

        for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            double? rawValue = categoryIndex < series.Values.Count
                ? series.Values[categoryIndex]
                : null;
            if (rawValue is null)
                continue;

            double x = plot.X + categoryIndex * stepX;
            double y = plot.Bottom - (rawValue.Value - effectiveMin) / effectiveRange * plot.Height;
            string categoryName = categoryIndex < chart.Categories.Count
                ? chart.Categories[categoryIndex]
                : string.Empty;
            string text = FormatDataLabel(labels, rawValue.Value, total, categoryName, series.Name);
            if (string.IsNullOrEmpty(text))
                continue;

            plans.Add(new ChartDataLabelPlan(
                seriesIndex,
                categoryIndex,
                text,
                new ChartPlanRect(x - 20, y - 14, 40, 11),
                IsBold: false,
                FontSize: 6.5,
                Alignment: ChartPlanTextAlignment.Center));
        }

        return plans;
    }

    private static IReadOnlyList<ChartDataLabelPlan> BuildBarDataLabelPlans(
        ChartShape chart,
        int seriesIndex,
        ChartPlanRect plot)
    {
        var labels = ResolveEffectiveLabels(chart, seriesIndex);
        if (labels is null || seriesIndex < 0 || seriesIndex >= chart.Series.Count)
            return Array.Empty<ChartDataLabelPlan>();

        var series = chart.Series[seriesIndex];
        int categoryCount = Math.Max(1, chart.Categories.Count);
        var (primaryMin, primaryMax, _) = ComputePrimaryValueAxisRange(chart);
        double primaryRange = primaryMax - primaryMin;
        if (primaryRange <= 0)
            return Array.Empty<ChartDataLabelPlan>();

        var (secondaryMin, secondaryMax, _) = ComputeSecondaryValueAxisRange(chart);
        double secondaryRange = secondaryMax - secondaryMin;
        double effectiveMin = series.OnSecondaryAxis ? secondaryMin : primaryMin;
        double effectiveRange = series.OnSecondaryAxis ? secondaryRange : primaryRange;
        if (effectiveRange <= 0)
            return Array.Empty<ChartDataLabelPlan>();

        bool stacked = chart.ChartType is ChartType.BarStacked or ChartType.BarStacked100;
        const double gapRatio = 1.5;
        double categoryHeight = plot.Height / categoryCount;
        double clusterHeight = categoryHeight / (1.0 + gapRatio);
        double halfGap = (categoryHeight - clusterHeight) / 2.0;
        int seriesCount = Math.Max(1, chart.Series.Count);
        double seriesHeight = stacked ? clusterHeight : clusterHeight / seriesCount;
        var position = labels.Position ?? DataLabelPosition.OutsideEnd;
        var plans = new List<ChartDataLabelPlan>();

        for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
        {
            double? rawValue = categoryIndex < series.Values.Count
                ? series.Values[categoryIndex]
                : null;
            if (rawValue is null)
                continue;

            double value = rawValue.Value;
            int renderRow = categoryCount - 1 - categoryIndex;
            double categoryTop = plot.Y + renderRow * categoryHeight + halfGap;

            double barWidth;
            double barX;
            double barY;
            if (stacked)
            {
                double stackedX = plot.X;
                for (int previousSeriesIndex = 0; previousSeriesIndex < seriesIndex; previousSeriesIndex++)
                {
                    double? previousValue = categoryIndex < chart.Series[previousSeriesIndex].Values.Count
                        ? chart.Series[previousSeriesIndex].Values[categoryIndex]
                        : null;
                    if (previousValue is null)
                        continue;

                    stackedX += Math.Max(0.5, Math.Abs((previousValue.Value - effectiveMin) / effectiveRange * plot.Width));
                }

                barWidth = Math.Max(0.5, Math.Abs((value - effectiveMin) / effectiveRange * plot.Width));
                barX = stackedX;
                barY = categoryTop;
            }
            else
            {
                int renderSeries = seriesCount - 1 - seriesIndex;
                barWidth = Math.Abs((value - effectiveMin) / effectiveRange * plot.Width);
                barX = plot.X;
                barY = categoryTop + renderSeries * seriesHeight;
            }

            double total = ComputeDataLabelTotal(chart, series, categoryIndex, stacked, labels);
            string categoryName = categoryIndex < chart.Categories.Count
                ? chart.Categories[categoryIndex]
                : string.Empty;
            string text = FormatDataLabel(labels, value, total, categoryName, series.Name);
            if (string.IsNullOrEmpty(text))
                continue;

            const double labelHeight = 11.0;
            double labelX = position switch
            {
                DataLabelPosition.InsideEnd => barX + barWidth - 22 - 2,
                DataLabelPosition.Center => barX + barWidth / 2 - 22,
                DataLabelPosition.InsideBase => barX + 2,
                _ => barX + barWidth + 2
            };
            double labelY = barY + seriesHeight / 2 - labelHeight / 2;

            plans.Add(new ChartDataLabelPlan(
                seriesIndex,
                categoryIndex,
                text,
                new ChartPlanRect(labelX, labelY, 44, labelHeight),
                IsBold: false,
                FontSize: 6.5,
                Alignment: ChartPlanTextAlignment.Center));
        }

        return plans;
    }

    private static IReadOnlyList<ChartDataLabelPlan> BuildPieDataLabelPlans(
        ChartShape chart,
        ChartPlanRect plot)
    {
        var labels = ResolveEffectiveLabels(chart, 0);
        if (labels is null || chart.Series.Count == 0)
            return Array.Empty<ChartDataLabelPlan>();

        var firstSeries = chart.Series[0];
        var values = firstSeries.Values
            .Where(value => value.HasValue && value.Value > 0)
            .Select(value => value!.Value)
            .ToList();
        if (values.Count == 0)
            return Array.Empty<ChartDataLabelPlan>();

        double total = values.Sum();
        if (total <= 0)
            return Array.Empty<ChartDataLabelPlan>();

        double centerX = plot.X + plot.Width / 2;
        double centerY = plot.Y + plot.Height / 2;
        double radius = Math.Min(plot.Width, plot.Height) / 2 * 0.85;
        double startAngle = -Math.PI / 2;
        var position = labels.Position ?? DataLabelPosition.BestFit;
        double labelRadius = position == DataLabelPosition.InsideEnd
            ? radius * 0.65
            : radius * 1.15;
        var plans = new List<ChartDataLabelPlan>();

        for (int valueIndex = 0; valueIndex < values.Count; valueIndex++)
        {
            double sweepAngle = values[valueIndex] / total * 2 * Math.PI;
            double midAngle = startAngle + sweepAngle / 2;
            string categoryName = valueIndex < chart.Categories.Count
                ? chart.Categories[valueIndex]
                : string.Empty;
            string text = FormatDataLabel(labels, values[valueIndex], total, categoryName, firstSeries.Name);
            if (!string.IsNullOrEmpty(text))
            {
                double labelX = centerX + labelRadius * Math.Cos(midAngle);
                double labelY = centerY + labelRadius * Math.Sin(midAngle);
                plans.Add(new ChartDataLabelPlan(
                    SeriesIndex: 0,
                    CategoryIndex: valueIndex,
                    Text: text,
                    Bounds: new ChartPlanRect(labelX - 22, labelY - 6, 44, 12),
                    IsBold: false,
                    FontSize: 6.5,
                    Alignment: ChartPlanTextAlignment.Center));
            }

            startAngle += sweepAngle;
        }

        return plans;
    }

    private static double ComputeDataLabelTotal(
        ChartShape chart,
        ChartSeries series,
        int categoryIndex,
        bool stacked,
        ChartDataLabels labels)
    {
        if (!labels.ShowPercent)
            return 0;

        double total = 0;
        if (stacked)
        {
            foreach (var chartSeries in chart.Series)
            {
                if (categoryIndex < chartSeries.Values.Count && chartSeries.Values[categoryIndex].HasValue)
                    total += Math.Abs(chartSeries.Values[categoryIndex]!.Value);
            }
        }
        else
        {
            foreach (var value in series.Values)
            {
                if (value.HasValue)
                    total += Math.Abs(value.Value);
            }
        }

        return total;
    }

    private static (double min, double max, double majorUnit) ComputeNiceRange(
        double min,
        double max)
    {
        if (max <= min)
            max = min + 1;

        double range = max - min;
        double rawUnit = range / 4.0;
        if (rawUnit <= 0)
            rawUnit = 1;

        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawUnit)));
        double normalized = rawUnit / magnitude;
        double niceMultiplier = normalized switch
        {
            < 1.5 => 1.0,
            < 2.25 => 2.0,
            < 3.75 => 2.5,
            < 7.5 => 5.0,
            _ => 10.0
        };

        double majorUnit = niceMultiplier * magnitude;
        double niceMax = Math.Ceiling(max / majorUnit) * majorUnit;
        double niceMin = min >= 0 ? 0 : Math.Floor(min / majorUnit) * majorUnit;

        if (Math.Abs(niceMax - max) < majorUnit * 1e-9)
            niceMax += majorUnit;

        return (niceMin, niceMax, majorUnit);
    }

    private static void AccumulateValues(
        IEnumerable<double?> values,
        ref double dataMin,
        ref double dataMax)
    {
        bool ignored = false;
        AccumulateValues(values, ref dataMin, ref dataMax, ref ignored);
    }

    private static void AccumulateValues(
        IEnumerable<double?> values,
        ref double dataMin,
        ref double dataMax,
        ref bool any)
    {
        foreach (var value in values)
        {
            if (!value.HasValue)
                continue;

            dataMin = Math.Min(dataMin, value.Value);
            dataMax = Math.Max(dataMax, value.Value);
            any = true;
        }
    }
}
