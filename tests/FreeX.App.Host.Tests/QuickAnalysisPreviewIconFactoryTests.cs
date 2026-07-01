using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class QuickAnalysisPreviewIconFactoryTests
{
    [Fact]
    public void Factory_RendersSharedPreviewIconPlan()
    {
        var source = DialogSourceTestSupport.ReadHostSources("QuickAnalysisPreviewIconFactory.cs");

        source.Should().Contain("QuickAnalysisPreviewIconRenderPlanner.Render(visual, renderer)");
        source.Should().Contain("QuickAnalysisPreviewIconRenderAdapter<Canvas, UIElement>");
        source.Should().Contain("private sealed class WpfQuickAnalysisPreviewIconRenderPrimitives");
        source.Should().NotContain("IQuickAnalysisPreviewIconRenderSink");
        source.Should().NotContain("RootCanvas");
        source.Should().NotContain("QuickAnalysisPreviewIconPlanner.Plan(visual)");
        source.Should().NotContain("foreach (var element in plan.Elements)");
        source.Should().NotContain("switch (element)");
        source.Should().NotContain("switch (visual.Kind)");
        source.Should().NotContain("QuickAnalysisPreviewVisualKind.");
        source.Should().NotContain("QuickAnalysisPreviewIconGlyph.");
    }
}
