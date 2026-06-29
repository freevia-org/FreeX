namespace FreeX.App.Presentation.QuickAnalysis;

public enum QuickAnalysisPreviewIconGlyph
{
    EmptyGrid,
    HorizontalBars,
    ColorScale,
    IconSet,
    HighlightGrid,
    ClearFormat,
    VerticalBars,
    StackedVerticalBars,
    LineChart,
    Pie,
    Area,
    Scatter,
    Formula,
    Table,
    WinLoss
}

public sealed record QuickAnalysisPreviewIconPlan(QuickAnalysisPreviewIconGlyph Glyph);

/// <summary>
/// Shared Quick Analysis menu-icon shape planning. Renderers still draw native controls and brushes;
/// this keeps preview visual grouping out of platform glue.
/// </summary>
public static class QuickAnalysisPreviewIconPlanner
{
    public static QuickAnalysisPreviewIconPlan Plan(QuickAnalysisPreviewVisual visual) =>
        Plan(visual.Kind);

    public static QuickAnalysisPreviewIconPlan Plan(QuickAnalysisPreviewVisualKind kind) =>
        new(kind switch
        {
            QuickAnalysisPreviewVisualKind.DataBars => QuickAnalysisPreviewIconGlyph.HorizontalBars,
            QuickAnalysisPreviewVisualKind.ColorScale => QuickAnalysisPreviewIconGlyph.ColorScale,
            QuickAnalysisPreviewVisualKind.IconSet => QuickAnalysisPreviewIconGlyph.IconSet,
            QuickAnalysisPreviewVisualKind.Highlight => QuickAnalysisPreviewIconGlyph.HighlightGrid,
            QuickAnalysisPreviewVisualKind.ClearFormat => QuickAnalysisPreviewIconGlyph.ClearFormat,
            QuickAnalysisPreviewVisualKind.ColumnChart => QuickAnalysisPreviewIconGlyph.VerticalBars,
            QuickAnalysisPreviewVisualKind.ColumnSparkline => QuickAnalysisPreviewIconGlyph.VerticalBars,
            QuickAnalysisPreviewVisualKind.StackedColumnChart => QuickAnalysisPreviewIconGlyph.StackedVerticalBars,
            QuickAnalysisPreviewVisualKind.LineChart => QuickAnalysisPreviewIconGlyph.LineChart,
            QuickAnalysisPreviewVisualKind.LineSparkline => QuickAnalysisPreviewIconGlyph.LineChart,
            QuickAnalysisPreviewVisualKind.PieChart => QuickAnalysisPreviewIconGlyph.Pie,
            QuickAnalysisPreviewVisualKind.BarChart => QuickAnalysisPreviewIconGlyph.HorizontalBars,
            QuickAnalysisPreviewVisualKind.AreaChart => QuickAnalysisPreviewIconGlyph.Area,
            QuickAnalysisPreviewVisualKind.ScatterChart => QuickAnalysisPreviewIconGlyph.Scatter,
            QuickAnalysisPreviewVisualKind.TotalFormula => QuickAnalysisPreviewIconGlyph.Formula,
            QuickAnalysisPreviewVisualKind.Table => QuickAnalysisPreviewIconGlyph.Table,
            QuickAnalysisPreviewVisualKind.WinLossSparkline => QuickAnalysisPreviewIconGlyph.WinLoss,
            _ => QuickAnalysisPreviewIconGlyph.EmptyGrid
        });
}
