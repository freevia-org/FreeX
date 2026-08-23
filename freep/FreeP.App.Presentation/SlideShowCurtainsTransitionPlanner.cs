using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowCurtainsTransitionPlan(
    bool HorizontalAxis,
    bool Reverse,
    int PanelCount,
    double FoldDepth);

/// <summary>
/// Shared center-out panel geometry for the Curtains transition.
/// Panels begin as narrow pleats around the center and expand toward their
/// owning edge, keeping both hosts on the same deterministic clip surface.
/// </summary>
public static class SlideShowCurtainsTransitionPlanner
{
    public const int DefaultPanelCount = 10;
    public const double DefaultFoldDepth = 0.18;

    public static SlideShowCurtainsTransitionPlan Plan(SlideTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        var horizontal = transition.Direction is not (
            TransitionDirection.Up or
            TransitionDirection.Down or
            TransitionDirection.Vertical);
        var reverse = transition.Direction is
            TransitionDirection.Left or
            TransitionDirection.Up or
            TransitionDirection.LeftUp or
            TransitionDirection.LeftDown;

        return new(
            horizontal,
            reverse,
            DefaultPanelCount,
            DefaultFoldDepth);
    }

    public static IReadOnlyList<SlideShowMaskPolygon> BuildPolygons(
        double width,
        double height,
        double progress,
        SlideShowCurtainsTransitionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        width = Math.Max(0, width);
        height = Math.Max(0, height);
        progress = Math.Clamp(progress, 0, 1);
        if (width <= 0 || height <= 0 || progress <= 0)
            return Array.Empty<SlideShowMaskPolygon>();
        if (progress >= 1)
            return new[] { new SlideShowMaskPolygon(SlideShowTransitionGeometry.BuildRectangle(width, height)) };

        var panels = Math.Max(2, plan.PanelCount);
        var foldDepth = Math.Clamp(plan.FoldDepth, 0, 0.45);
        var polygons = new List<SlideShowMaskPolygon>(panels);

        if (plan.HorizontalAxis)
        {
            var panelWidth = width / panels;
            for (var panel = 0; panel < panels; panel++)
            {
                var normalized = (panel + 0.5) / panels;
                var distance = Math.Abs(normalized - 0.5) * 2;
                var local = SlideShowTransitionGeometry.SmoothStep(Math.Clamp((progress - distance * 0.45) / 0.55, 0, 1));
                if (local <= 0)
                    continue;

                var center = (panel + 0.5) * panelWidth;
                var fold = (plan.Reverse ? -1 : 1)
                    * panelWidth * foldDepth * (1 - local)
                    * (normalized < 0.5 ? -1 : 1);
                var halfWidth = panelWidth * 0.5 * local;
                var x0 = Math.Clamp(center - halfWidth + fold, 0, width);
                var x1 = Math.Clamp(center + halfWidth + fold, 0, width);
                polygons.Add(new(new[]
                {
                    new SlideShowMaskPoint(x0, 0),
                    new SlideShowMaskPoint(x1, 0),
                    new SlideShowMaskPoint(x1, height),
                    new SlideShowMaskPoint(x0, height)
                }));
            }
        }
        else
        {
            var panelHeight = height / panels;
            for (var panel = 0; panel < panels; panel++)
            {
                var normalized = (panel + 0.5) / panels;
                var distance = Math.Abs(normalized - 0.5) * 2;
                var local = SlideShowTransitionGeometry.SmoothStep(Math.Clamp((progress - distance * 0.45) / 0.55, 0, 1));
                if (local <= 0)
                    continue;

                var center = (panel + 0.5) * panelHeight;
                var fold = (plan.Reverse ? -1 : 1)
                    * panelHeight * foldDepth * (1 - local)
                    * (normalized < 0.5 ? -1 : 1);
                var halfHeight = panelHeight * 0.5 * local;
                var y0 = Math.Clamp(center - halfHeight + fold, 0, height);
                var y1 = Math.Clamp(center + halfHeight + fold, 0, height);
                polygons.Add(new(new[]
                {
                    new SlideShowMaskPoint(0, y0),
                    new SlideShowMaskPoint(width, y0),
                    new SlideShowMaskPoint(width, y1),
                    new SlideShowMaskPoint(0, y1)
                }));
            }
        }

        return polygons;
    }


}
