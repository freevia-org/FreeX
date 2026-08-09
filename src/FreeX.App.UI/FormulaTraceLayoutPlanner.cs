using FreeX.App.Presentation.FormulaAuditing;
using FreeX.Core.Model;
using System.Windows;

namespace FreeX.App.UI;

/// <summary>WPF coordinate adapter for the portable formula trace overlay planner.</summary>
public static class FormulaTraceLayoutPlanner
{
    public static IReadOnlyList<FormulaTraceArrowLayout> CalculateLayouts(
        ViewportModel viewport,
        IReadOnlyList<FormulaTraceArrow> arrows,
        SheetId sheetId) =>
        FormulaTraceOverlayPlanner.CalculateLayouts(
            viewport,
            arrows,
            sheetId,
            CreateProjection(viewport),
            FormulaTraceOverlayProfiles.Wpf);

    public static void VisitLayouts<TConsumer>(
        ViewportModel viewport,
        IReadOnlyList<FormulaTraceArrow> arrows,
        SheetId sheetId,
        ref TConsumer consumer)
        where TConsumer : struct, IFormulaTraceArrowLayoutConsumer =>
        FormulaTraceOverlayPlanner.VisitLayouts(
            viewport,
            arrows,
            sheetId,
            CreateProjection(viewport),
            FormulaTraceOverlayProfiles.Wpf,
            ref consumer);

    public static CellAddress? HitTestMarker(
        ViewportModel viewport,
        IReadOnlyList<FormulaTraceArrow> arrows,
        SheetId sheetId,
        Point position) =>
        FormulaTraceOverlayPlanner.HitTestMarker(
            viewport,
            arrows,
            sheetId,
            CreateProjection(viewport),
            FormulaTraceOverlayProfiles.Wpf,
            new LayoutPoint(position.X, position.Y));

    private static FormulaTraceViewportProjection CreateProjection(ViewportModel viewport) =>
        FormulaTraceViewportProjection.FromMetricOffsets(
            GridView.CalculateRowHeaderWidth(viewport),
            GridView.ColHeaderHeight);
}
