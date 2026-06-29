using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class QuickAnalysisPreviewIconFactoryTests
{
    [Fact]
    public void Factory_RendersSharedPreviewIconPlan()
    {
        var source = DialogSourceTestSupport.ReadHostSources("QuickAnalysisPreviewIconFactory.cs");

        source.Should().Contain("QuickAnalysisPreviewIconPlanner.Plan(visual)");
        source.Should().Contain("foreach (var element in plan.Elements)");
        source.Should().Contain("switch (element)");
        source.Should().NotContain("switch (visual.Kind)");
        source.Should().NotContain("QuickAnalysisPreviewVisualKind.");
        source.Should().NotContain("QuickAnalysisPreviewIconGlyph.");
    }
}
