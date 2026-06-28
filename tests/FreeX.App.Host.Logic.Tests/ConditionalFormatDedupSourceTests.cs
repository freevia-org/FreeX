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

    [Fact]
    public void ConditionalFormatPresetGalleryPlanner_StaysHostLocalizationFacade()
    {
        var hostSource = DialogSourceTestSupport.ReadHostSourceFile("ConditionalFormatPresetGalleryPlanner.cs");
        var presentationSource = DialogSourceTestSupport.ReadPresentationSources(
            "ConditionalFormatting",
            "ConditionalFormatPresetGalleryPlanner.cs");

        hostSource.Should().Contain(
            "using PresentationPlanner = FreeX.App.Presentation.ConditionalFormatting.ConditionalFormatPresetGalleryPlanner;");
        hostSource.Should().Contain("UiText.Get(option.LabelKey)");
        hostSource.Should().Contain("PresentationPlanner.CreateDataBarRule(style, range)");
        hostSource.Should().Contain("PresentationPlanner.CreateColorScaleRule(style, range)");

        hostSource.Should().NotContain("DataBar(\"GradientBlue\"");
        hostSource.Should().NotContain("ColorScale(\"GreenYellowRed\"");
        presentationSource.Should().Contain("DataBar(\"GradientBlue\"");
        presentationSource.Should().Contain("ColorScale(\"GreenYellowRed\"");
        presentationSource.Should().NotContain("UiText.Get(");
    }

    [Fact]
    public void ConditionalFormatDialogPlanner_StaysHostFacadeOverPresentationPlanner()
    {
        var hostSource = DialogSourceTestSupport.ReadHostSourceFile("ConditionalFormatDialogPlanner.cs");
        var presentationSource = DialogSourceTestSupport.ReadPresentationSources(
            "ConditionalFormatting",
            "ConditionalFormatDialogPlanner.cs");

        hostSource.Should().Contain(
            "using PresentationPlanner = FreeX.App.Presentation.ConditionalFormatting.ConditionalFormatDialogPlanner;");
        hostSource.Should().Contain("PresentationPlanner.CloneRule(source)");
        hostSource.Should().Contain("PresentationPlanner.RuleTypeLabel(cf)");

        hostSource.Should().NotContain("CfRuleType.Formula =>");
        presentationSource.Should().Contain("CfRuleType.Formula =>");
        presentationSource.Should().Contain("ManageConditionalFormatsPlanner.CloneWithPriority");
    }
}
