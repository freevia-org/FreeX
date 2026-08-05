using System.Windows.Controls;
using System.Windows.Shapes;
using FluentAssertions;
using FreeX.App.Presentation.QuickAnalysis;

namespace FreeX.App.Host.Tests;

public sealed class QuickAnalysisPreviewIconFactoryTests
{
    [Fact]
    public void Factory_RendersSharedPreviewIconPlan()
    {
        var source = DialogSourceTestSupport.ReadHostSources("QuickAnalysisPreviewIconFactory.cs");

        source.Should().Contain("QuickAnalysisPreviewIconRenderAdapter<Canvas, UIElement>.Render(");
        source.Should().Contain("QuickAnalysisPreviewIconPlan plan");
        source.Should().Contain("private sealed class WpfQuickAnalysisPreviewIconRenderPrimitives");
        source.Should().NotContain("IQuickAnalysisPreviewIconRenderSink");
        source.Should().NotContain("RootCanvas");
        source.Should().NotContain("QuickAnalysisPreviewVisual visual");
        source.Should().NotContain("QuickAnalysisPreviewIconRenderPlanner.Render(");
        source.Should().NotContain("QuickAnalysisPreviewIconPlanner.Plan(visual)");
        source.Should().NotContain("foreach (var element in plan.Elements)");
        source.Should().NotContain("switch (element)");
        source.Should().NotContain("switch (visual.Kind)");
        source.Should().NotContain("QuickAnalysisPreviewVisualKind.");
        source.Should().NotContain("QuickAnalysisPreviewIconGlyph.");
    }

    [Fact]
    public void Create_DataBarsRendersPreplannedHorizontalBarGlyph()
    {
        StaTestRunner.Run(() =>
        {
            var icon = QuickAnalysisPreviewIconFactory.Create(
                QuickAnalysisPreviewIconPlanner.Plan(QuickAnalysisPreviewVisualKind.DataBars));

            var canvas = icon.Should().BeOfType<Canvas>().Subject;
            canvas.Width.Should().Be(34);
            canvas.Height.Should().Be(22);
            canvas.Children.OfType<Rectangle>().Should().HaveCount(3);
            canvas.Children.OfType<Line>().Should().BeEmpty();
        });
    }

    [Fact]
    public void Create_ClearFormatRendersPreplannedGridWithSlash()
    {
        StaTestRunner.Run(() =>
        {
            var icon = QuickAnalysisPreviewIconFactory.Create(
                QuickAnalysisPreviewIconPlanner.Plan(QuickAnalysisPreviewVisualKind.ClearFormat));

            var canvas = icon.Should().BeOfType<Canvas>().Subject;
            canvas.Children.OfType<Rectangle>().Should().HaveCount(6);
            canvas.Children.OfType<Line>().Should().ContainSingle();
        });
    }
}
