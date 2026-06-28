using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ConditionalFormatDedupSourceTests
{
    [Fact]
    public void ManageConditionalFormatsPlanner_HostFacadeIsRemovedAndDialogUsesPresentationPlannerDirectly()
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        var hostFacadePath = Path.Combine(repoRoot, "src", "FreeX.App.Host", "ManageConditionalFormatsPlanner.cs");
        var dialogSource = DialogSourceTestSupport.ReadHostSources(
            "ManageConditionalFormatsDialog.cs",
            "ManageConditionalFormatsDialog.Rules.cs");
        var presentationSource = DialogSourceTestSupport.ReadPresentationSources(
            "ConditionalFormatting",
            "ManageConditionalFormatsPlanner.cs");

        File.Exists(hostFacadePath)
            .Should().BeFalse("the WPF host should call the shared manage planner directly instead of keeping a pass-through facade");
        dialogSource.Should().Contain(
            "using ManageConditionalFormatsPlanner = FreeX.App.Presentation.ConditionalFormatting.ManageConditionalFormatsPlanner;");
        dialogSource.Should().Contain("ManageConditionalFormatsPlanner.BuildResultRules(");
        dialogSource.Should().Contain("ManageConditionalFormatsPlanner.DuplicateRule(");
        dialogSource.Should().Contain("ManageConditionalFormatsPlanner.RangesOverlap(");
        dialogSource.Should().Contain("ConditionalFormatRuleMoveDirection.Up");
        dialogSource.Should().Contain("ConditionalFormatRuleMoveDirection.Down");

        presentationSource.Should().Contain("matchingRuleCount--");
        presentationSource.Should().Contain("FindRuleIndex");
        presentationSource.Should().Contain("src.Clone(id)");
        presentationSource.Should().Contain("result.Insert(index + 1");
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

}
