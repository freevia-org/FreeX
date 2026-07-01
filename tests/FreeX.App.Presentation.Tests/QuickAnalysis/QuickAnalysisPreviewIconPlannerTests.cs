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

    [Fact]
    public void Plan_BuildsSharedDataBarsGeometry()
    {
        var plan = QuickAnalysisPreviewIconPlanner.Plan(QuickAnalysisPreviewVisualKind.DataBars);

        plan.Width.Should().Be(34);
        plan.Height.Should().Be(22);
        var bars = plan.Elements.OfType<QuickAnalysisPreviewIconRectangle>().ToArray();
        bars.Should().HaveCount(3);
        bars.Select(bar => bar.Width).Should().Equal(14, 22, 18);
        bars.Select(bar => bar.Fill).Should().OnlyContain(color => color == QuickAnalysisPreviewIconColor.SteelBlue);
    }

    [Fact]
    public void Plan_BuildsSharedClearFormatGeometry()
    {
        var plan = QuickAnalysisPreviewIconPlanner.Plan(QuickAnalysisPreviewVisualKind.ClearFormat);

        plan.Elements.OfType<QuickAnalysisPreviewIconRectangle>().Should().HaveCount(6);
        var slash = plan.Elements.OfType<QuickAnalysisPreviewIconLine>().Should().ContainSingle().Subject;
        slash.X1.Should().Be(6);
        slash.Y1.Should().Be(17);
        slash.X2.Should().Be(28);
        slash.Y2.Should().Be(5);
        slash.Stroke.Should().Be(QuickAnalysisPreviewIconColor.Firebrick);
        slash.StrokeThickness.Should().Be(1.5);
    }

    [Fact]
    public void Plan_BuildsSharedFormulaTextDescriptor()
    {
        var plan = QuickAnalysisPreviewIconPlanner.Plan(QuickAnalysisPreviewVisualKind.TotalFormula);

        var text = plan.Elements.OfType<QuickAnalysisPreviewIconText>().Should().ContainSingle().Subject;
        text.Text.Should().Be("fx");
        text.FontWeight.Should().Be(QuickAnalysisPreviewIconFontWeight.SemiBold);
        text.Foreground.Should().Be(QuickAnalysisPreviewIconColor.SteelBlue);
    }
}
