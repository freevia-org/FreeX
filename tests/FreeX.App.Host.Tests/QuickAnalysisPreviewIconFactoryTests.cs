using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class QuickAnalysisPreviewIconFactoryTests
{
    [Fact]
    public void Factory_RendersSharedPreviewIconPlan()
    {
        var source = DialogSourceTestSupport.ReadHostSources("QuickAnalysisPreviewIconFactory.cs");

        source.Should().Contain("QuickAnalysisPreviewIconPlanner.Plan(visual)");
        source.Should().Contain("switch (plan.Glyph)");
        source.Should().NotContain("switch (visual.Kind)");
        source.Should().NotContain("QuickAnalysisPreviewVisualKind.");
    }
}
