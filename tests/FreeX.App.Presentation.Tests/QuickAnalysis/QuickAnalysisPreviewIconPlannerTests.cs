using FluentAssertions;
using FreeX.App.Presentation.QuickAnalysis;

namespace FreeX.App.Presentation.Tests.QuickAnalysis;

public sealed class QuickAnalysisPreviewIconPlannerTests
{
    [Theory]
    [InlineData(QuickAnalysisPreviewVisualKind.None, QuickAnalysisPreviewIconGlyph.EmptyGrid)]
    [InlineData(QuickAnalysisPreviewVisualKind.DataBars, QuickAnalysisPreviewIconGlyph.HorizontalBars)]
    [InlineData(QuickAnalysisPreviewVisualKind.ColorScale, QuickAnalysisPreviewIconGlyph.ColorScale)]
    [InlineData(QuickAnalysisPreviewVisualKind.IconSet, QuickAnalysisPreviewIconGlyph.IconSet)]
    [InlineData(QuickAnalysisPreviewVisualKind.Highlight, QuickAnalysisPreviewIconGlyph.HighlightGrid)]
    [InlineData(QuickAnalysisPreviewVisualKind.ClearFormat, QuickAnalysisPreviewIconGlyph.ClearFormat)]
    [InlineData(QuickAnalysisPreviewVisualKind.ColumnChart, QuickAnalysisPreviewIconGlyph.VerticalBars)]
    [InlineData(QuickAnalysisPreviewVisualKind.ColumnSparkline, QuickAnalysisPreviewIconGlyph.VerticalBars)]
    [InlineData(QuickAnalysisPreviewVisualKind.StackedColumnChart, QuickAnalysisPreviewIconGlyph.StackedVerticalBars)]
    [InlineData(QuickAnalysisPreviewVisualKind.LineChart, QuickAnalysisPreviewIconGlyph.LineChart)]
    [InlineData(QuickAnalysisPreviewVisualKind.LineSparkline, QuickAnalysisPreviewIconGlyph.LineChart)]
    [InlineData(QuickAnalysisPreviewVisualKind.PieChart, QuickAnalysisPreviewIconGlyph.Pie)]
    [InlineData(QuickAnalysisPreviewVisualKind.BarChart, QuickAnalysisPreviewIconGlyph.HorizontalBars)]
    [InlineData(QuickAnalysisPreviewVisualKind.AreaChart, QuickAnalysisPreviewIconGlyph.Area)]
    [InlineData(QuickAnalysisPreviewVisualKind.ScatterChart, QuickAnalysisPreviewIconGlyph.Scatter)]
    [InlineData(QuickAnalysisPreviewVisualKind.TotalFormula, QuickAnalysisPreviewIconGlyph.Formula)]
    [InlineData(QuickAnalysisPreviewVisualKind.Table, QuickAnalysisPreviewIconGlyph.Table)]
    [InlineData(QuickAnalysisPreviewVisualKind.WinLossSparkline, QuickAnalysisPreviewIconGlyph.WinLoss)]
    public void Plan_MapsPreviewVisualsToSharedIconGlyphs(
        QuickAnalysisPreviewVisualKind visualKind,
        QuickAnalysisPreviewIconGlyph expectedGlyph)
    {
        QuickAnalysisPreviewIconPlanner.Plan(visualKind).Glyph.Should().Be(expectedGlyph);
    }

    [Fact]
    public void Plan_AcceptsPreviewVisualDescriptor()
    {
        var plan = QuickAnalysisPreviewIconPlanner.Plan(
            new QuickAnalysisPreviewVisual(QuickAnalysisPreviewVisualKind.ColumnSparkline));

        plan.Glyph.Should().Be(QuickAnalysisPreviewIconGlyph.VerticalBars);
    }
}
