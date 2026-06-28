using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ConditionalFormatDedupSourceTests
{
    [Fact]
    public void ManageConditionalFormatsPlanner_StaysHostFacadeOverPresentationPlanner()
    {
        var hostSource = DialogSourceTestSupport.ReadHostSourceFile("ManageConditionalFormatsPlanner.cs");
        var presentationSource = DialogSourceTestSupport.ReadPresentationSources(
            "ConditionalFormatting",
            "ManageConditionalFormatsPlanner.cs");

        hostSource.Should().Contain(
            "using PresentationPlanner = FreeX.App.Presentation.ConditionalFormatting.ManageConditionalFormatsPlanner;");
        hostSource.Should().Contain("PresentationPlanner.BuildResultRules(");
        hostSource.Should().Contain("PresentationPlanner.DuplicateRule(");
        hostSource.Should().Contain("PresentationPlanner.RangesOverlap(");

        hostSource.Should().NotContain("matchingRuleCount--");
        hostSource.Should().NotContain("FindRuleIndex");
        hostSource.Should().NotContain("src.Clone(id)");
        hostSource.Should().NotContain("result.Insert(index + 1");

        presentationSource.Should().Contain("matchingRuleCount--");
        presentationSource.Should().Contain("FindRuleIndex");
        presentationSource.Should().Contain("src.Clone(id)");
    }
}
