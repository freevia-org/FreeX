using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Series;

using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public static partial class ChartRenderer
{
    private static void AddTrendlineIfRequested(
        PlotModel model,
        ChartModel chart,
        WorkbookTheme theme,
        IReadOnlyList<DataPoint>? points,
        bool swapTrendlineAxes = false)
    {
        if (points is null)
            return;

        var sourcePoints = new TrendPoint[points.Count];
        for (var i = 0; i < points.Count; i++)
            sourcePoints[i] = new TrendPoint(points[i].X, points[i].Y);

        var plan = TrendlineProjectionPlanner.Plan(chart, sourcePoints, swapTrendlineAxes);
        if (plan is null)
            return;

        var trendline = new LineSeries
        {
            Title = plan.Title,
            LineStyle = ToOxyLineStyle(chart.TrendlineDashStyle),
            StrokeThickness = chart.TrendlineThickness,
            Color = chart.ResolveTrendlineColor(theme) is { } color
                ? OxyColor.FromRgb(color.R, color.G, color.B)
                : OxyColors.Gray,
        };
        foreach (var point in plan.Points)
            trendline.Points.Add(new DataPoint(point.X, point.Y));

        model.Series.Add(trendline);
        AddTrendlineAnnotation(model, plan);
    }

    private static LineStyle ToOxyLineStyle(ChartLineDashStyle dashStyle) =>
        dashStyle switch
        {
            ChartLineDashStyle.Solid => LineStyle.Solid,
            ChartLineDashStyle.Dot => LineStyle.Dot,
            _ => LineStyle.Dash,
        };

    /// <summary>
    /// Maps a chart marker style to its OxyPlot marker type. OxyPlot has no dedicated "smaller
    /// dot" marker type, so <see cref="ChartMarkerStyle.Dot"/> shares <see cref="MarkerType.Circle"/>
    /// with <see cref="ChartMarkerStyle.Auto"/>/<see cref="ChartMarkerStyle.Circle"/> here; callers
    /// distinguish it by scaling the series marker size down by <see cref="DotMarkerSizeScale"/>.
    /// </summary>
    private static MarkerType ToOxyMarkerType(ChartMarkerStyle markerStyle) =>
        markerStyle switch
        {
            ChartMarkerStyle.None => MarkerType.None,
            ChartMarkerStyle.Square => MarkerType.Square,
            ChartMarkerStyle.Diamond => MarkerType.Diamond,
            ChartMarkerStyle.Triangle => MarkerType.Triangle,
            ChartMarkerStyle.X => MarkerType.Cross,
            ChartMarkerStyle.Star => MarkerType.Star,
            ChartMarkerStyle.Plus => MarkerType.Plus,
            ChartMarkerStyle.Dot => MarkerType.Circle,
            ChartMarkerStyle.Dash => MarkerType.Square,
            ChartMarkerStyle.Auto => MarkerType.Circle,
            _ => MarkerType.Circle,
        };

    private static void AddTrendlineAnnotation(PlotModel model, TrendlineProjectionPlan plan)
    {
        if (plan.AnnotationLines.Count == 0 || plan.AnnotationAnchor is not { } anchor)
            return;

        model.Annotations.Add(new TextAnnotation
        {
            Text = string.Join(Environment.NewLine, plan.AnnotationLines),
            TextPosition = new DataPoint(anchor.X, anchor.Y),
            TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left,
            TextVerticalAlignment = OxyPlot.VerticalAlignment.Top,
            Background = OxyColor.FromAColor(220, OxyColors.White),
            Stroke = OxyColors.LightGray,
            StrokeThickness = 1,
            Padding = new OxyThickness(4),
        });
    }
}
