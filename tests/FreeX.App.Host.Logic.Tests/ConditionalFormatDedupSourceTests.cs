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
        var hostRulesFacadePath = Path.Combine(repoRoot, "src", "FreeX.App.Host", "ManageConditionalFormatsDialog.Rules.cs");
        var dialogSource = DialogSourceTestSupport.ReadHostSourceFile("ManageConditionalFormatsDialog.cs");
        var presentationSource = DialogSourceTestSupport.ReadPresentationSources(
            "ConditionalFormatting",
            "ManageConditionalFormatsPlanner.cs");

        File.Exists(hostFacadePath)
            .Should().BeFalse("the WPF host should call the shared manage planner directly instead of keeping a pass-through facade");
        File.Exists(hostRulesFacadePath)
            .Should().BeFalse("the WPF dialog should not retain test-only pass-throughs to the shared planner");
        dialogSource.Should().Contain(
            "using ManageConditionalFormatsPlanner = FreeX.App.Presentation.ConditionalFormatting.ManageConditionalFormatsPlanner;");
        dialogSource.Should().Contain("new ManageConditionalFormatsSession(");
        dialogSource.Should().Contain("_manageSession.BuildResultRules(");
        dialogSource.Should().Contain("ConditionalFormatRuleMoveDirection.Up");
        dialogSource.Should().Contain("ConditionalFormatRuleMoveDirection.Down");

        presentationSource.Should().Contain("matchingRuleCount--");
        presentationSource.Should().Contain("FindRuleIndex");
        presentationSource.Should().Contain("src.Clone(id)");
        presentationSource.Should().Contain("result.Insert(index + 1");
        presentationSource.Should().Contain("public static IReadOnlyList<ConditionalFormat> AddRule(");
    }

    [Fact]
    public void ManageConditionalFormatsLifecycle_IsSharedByWpfAndAvalonia()
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        var avaloniaFacadePath = Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Avalonia",
            "Dialogs",
            "ConditionalFormatManageModel.cs");
        var wpfSource = DialogSourceTestSupport.ReadHostSourceFile("ManageConditionalFormatsDialog.cs");
        var avaloniaSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.ConditionalFormat.cs"));
        var sessionSource = DialogSourceTestSupport.ReadPresentationSources(
            "ConditionalFormatting",
            "ManageConditionalFormatsSession.cs");

        File.Exists(avaloniaFacadePath).Should().BeFalse(
            "the Avalonia-only working-copy model was replaced by the portable manager session");
        wpfSource.Should().Contain("new ManageConditionalFormatsSession(");
        wpfSource.Should().Contain("_manageSession.BuildResultRules(");
        avaloniaSource.Should().Contain("new ManageConditionalFormatsSession(");
        avaloniaSource.Should().Contain("manageSession.BuildProjection()");
        avaloniaSource.Should().Contain("manageSession.CreateApplyPlan(");
        avaloniaSource.Should().NotContain("ConditionalFormatManageModel");

        sessionSource.Should().Contain("public sealed class ManageConditionalFormatsSession");
        sessionSource.Should().Contain("ManageConditionalFormatsPlanner.AddRule(");
        sessionSource.Should().Contain("ManageConditionalFormatsPlanner.MoveRule(");
        sessionSource.Should().Contain("ManageConditionalFormatsPlanner.ApplyRuleRange(");
    }

    [Fact]
    public void ManageConditionalFormatsDescriptionLocalization_IsOwnedByPresentation()
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        var wpfSource = DialogSourceTestSupport.ReadHostSourceFile("ManageConditionalFormatsDialog.Helpers.cs");
        var avaloniaSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.ConditionalFormat.cs"));
        var presentationSource = DialogSourceTestSupport.ReadPresentationSources(
            "ConditionalFormatting",
            "ManageConditionalFormatsPlanner.cs");

        wpfSource.Should().Contain("ManageConditionalFormatsPlanner.ResolveDescription(");
        wpfSource.Should().Contain("WpfResourceKeyTextResolver.Instance");
        avaloniaSource.Should().Contain("ManageConditionalFormatsPlanner.ResolveDescription(item.Description, ManageConditionalFormatsText)");
        presentationSource.Should().Contain("ResourceKeyTextResolver text");
        presentationSource.Should().Contain("ResourceListDescriptionArgument resourceList => string.Join(");

        wpfSource.Should().NotContain("private static string ResolveDescription(");
        avaloniaSource.Should().NotContain("ResolveManageConditionalFormatDescription");
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
        var ruleBuilderSource = DialogSourceTestSupport.ReadPresentationSources(
            "ConditionalFormatting",
            "ConditionalFormatRuleBuilder.cs");

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
        dialogSource.Should().Contain("ConditionalFormatDialogCatalog.ColorPresets");
        dialogSource.Should().Contain("ConditionalFormatRuleBuilder.Build(");
        dialogSource.Should().Contain("UiText.Get(option.LabelKey)");
        ruleBuilderSource.Should().Contain("ConditionalFormatIconSetCatalog.CreateThresholds(cf.IconSetStyle)");
        ruleBuilderSource.Should().Contain("ApplyIconOverrides(cf, input.IconOverrides)");

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
