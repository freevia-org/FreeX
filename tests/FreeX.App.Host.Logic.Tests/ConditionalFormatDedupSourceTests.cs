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
    public void ConditionalFormatGalleryPlanners_AreSharedAndWpfLocalizesAtBindingEdges()
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        var presetFacadePath = Path.Combine(repoRoot, "src", "FreeX.App.Host", "ConditionalFormatPresetGalleryPlanner.cs");
        var iconSetFacadePath = Path.Combine(repoRoot, "src", "FreeX.App.Host", "ConditionalFormatIconSetPlanner.cs");
        var mainWindowSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.HomeFormatting.cs");
        var dialogSource = DialogSourceTestSupport.ReadHostSources(
            "ConditionalFormatDialog.Catalog.cs",
            "ConditionalFormatDialog.IconSets.cs",
            "ConditionalFormatDialog.Result.cs");
        var runtimeCatalogSource = DialogSourceTestSupport.ReadPresentationSources(
            "Ribbon",
            "RibbonRuntimeCatalogPlanner.cs");
        var presetPresentationSource = DialogSourceTestSupport.ReadPresentationSources(
            "ConditionalFormatting",
            "ConditionalFormatPresetGalleryPlanner.cs");
        var iconSetPresentationSource = DialogSourceTestSupport.ReadPresentationSources(
            "ConditionalFormatting",
            "ConditionalFormatIconSetCatalog.cs");

        File.Exists(presetFacadePath)
            .Should()
            .BeFalse("WPF should bind the shared preset gallery planner directly instead of carrying a host facade");
        File.Exists(iconSetFacadePath)
            .Should()
            .BeFalse("WPF should bind the shared icon-set gallery planner directly instead of carrying a host facade");

        mainWindowSource.Should().Contain("using FreeX.App.Presentation.ConditionalFormatting;");
        mainWindowSource.Should().Contain("ConditionalFormatPresetGalleryPlanner.CreateDataBarRule(style, range)");
        mainWindowSource.Should().Contain("ConditionalFormatPresetGalleryPlanner.CreateColorScaleRule(style, range)");
        mainWindowSource.Should().Contain("ConditionalFormatIconSetCatalog.CreateRule(style, range)");
        mainWindowSource.Should().Contain("UiText.Get(group.CategoryKey)");
        mainWindowSource.Should().Contain("UiText.Get(option.LabelKey)");

        dialogSource.Should().Contain("ConditionalFormatIconSetCatalog.GalleryOptions");
        dialogSource.Should().Contain("ConditionalFormatIconSetCatalog.CreateThresholds(cf.IconSetStyle)");
        dialogSource.Should().Contain("UiText.Get(option.LabelKey)");

        runtimeCatalogSource.Should().Contain("nameof(ConditionalFormatIconSetCatalog)");
        runtimeCatalogSource.Should().Contain("textProvider(group.CategoryKey)");
        runtimeCatalogSource.Should().Contain("textProvider(option.LabelKey)");

        presetPresentationSource.Should().Contain("DataBar(\"GradientBlue\"");
        presetPresentationSource.Should().Contain("ColorScale(\"GreenYellowRed\"");
        presetPresentationSource.Should().NotContain("UiText.Get(");

        iconSetPresentationSource.Should().Contain("GalleryOption(\"3Arrows\"");
        iconSetPresentationSource.Should().Contain("CreateRule(string? style, GridRange range)");
        iconSetPresentationSource.Should().NotContain("UiText.Get(");
    }
}
