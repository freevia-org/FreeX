using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

public sealed class ConditionalFormatPresetFactorySourceGuardTests
{
    [Fact]
    public void ConditionalFormatRuleFactories_LiveInPresentationNotAvalonia()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

        var sharedFiles = new[]
        {
            "ConditionalFormatRulePlanner.cs",
            "ConditionalFormatRuleBuilder.cs",
            "ConditionalFormatPresetFactory.cs",
            "ConditionalFormatPresetGalleryPlanner.cs",
            "ConditionalFormatIconSetCatalog.cs",
            "ConditionalFormatDialogPlanner.cs"
        };

        foreach (var fileName in sharedFiles)
        {
            File.Exists(Path.Combine(presentationRoot, "ConditionalFormatting", fileName))
                .Should()
                .BeTrue($"{fileName} should be owned by the shared conditional-format presentation layer");
        }

        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Avalonia", "ConditionalFormatRulePlanner.cs"))
            .Should()
            .BeFalse("rule ordering is portable presentation logic, not renderer logic");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Avalonia", "Dialogs", "ConditionalFormatRuleBuilder.cs"))
            .Should()
            .BeFalse("rule construction is portable presentation logic, not renderer dialog logic");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Avalonia", "Dialogs", "ConditionalFormatPresetFactory.cs"))
            .Should()
            .BeFalse("quick preset factories are portable presentation logic, not renderer dialog logic");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "ConditionalFormatDialogPlanner.cs"))
            .Should()
            .BeFalse("WPF host should use the shared conditional-format dialog planner instead of carrying a pass-through facade");
    }

    [Fact]
    public void HostConditionalFormatGallery_UsesSharedPlannersAndLocalizesAtBindingEdges()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

        var hostSource = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));
        var presetPresentationSource = File.ReadAllText(Path.Combine(presentationRoot, "ConditionalFormatting", "ConditionalFormatPresetGalleryPlanner.cs"));
        var iconSetPresentationSource = File.ReadAllText(Path.Combine(presentationRoot, "ConditionalFormatting", "ConditionalFormatIconSetCatalog.cs"));

        hostSource.Should().Contain("UiText.Get(option.LabelKey)");
        hostSource.Should().Contain("UiText.Get(group.CategoryKey)");
        hostSource.Should().Contain("ConditionalFormatPresetGalleryPlanner.CreateDataBarRule(style, range)");
        hostSource.Should().Contain("ConditionalFormatPresetGalleryPlanner.CreateColorScaleRule(style, range)");
        hostSource.Should().Contain("ConditionalFormatIconSetCatalog.CreateRule(style, range)");

        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "ConditionalFormatPresetGalleryPlanner.cs"))
            .Should()
            .BeFalse("the WPF host should bind shared preset gallery metadata directly instead of keeping a facade");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "ConditionalFormatIconSetPlanner.cs"))
            .Should()
            .BeFalse("the WPF host should bind shared icon-set gallery metadata directly instead of keeping a facade");

        presetPresentationSource.Should().NotContain("UiText.Get(");
        presetPresentationSource.Should().Contain("ConditionalFormatDataBar_Category_GradientFill");
        presetPresentationSource.Should().Contain("CreateDataBarRule");

        iconSetPresentationSource.Should().NotContain("UiText.Get(");
        iconSetPresentationSource.Should().Contain("ConditionalFormatIconSet_Category_Directional");
        iconSetPresentationSource.Should().Contain("CreateRule");
    }

    [Fact]
    public void ConditionalFormatPopupPseudoRows_AreBackedBySharedCatalogAndPairedHostSurfaces()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

        var plannerSource = File.ReadAllText(Path.Combine(presentationRoot, "ConditionalFormatting", "ConditionalFormatPresetGalleryPlanner.cs"));
        var runtimeCatalogSource = File.ReadAllText(Path.Combine(presentationRoot, "Ribbon", "RibbonRuntimeCatalogPlanner.cs"));
        var hostSource = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));
        var avaloniaMainSource = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var avaloniaRawIdsSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Presentation",
            "Ribbon",
            "FreeXRibbonCommandIdentityCatalog.RawCanonical.cs"));
        var homeRibbonMenuSource = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.Ribbon.Definitions", "HomeRibbonMenus.g.cs"));

        plannerSource.Should().Contain("public static readonly IReadOnlyList<ConditionalFormatPopupCatalogGroup> PopupGroups");
        runtimeCatalogSource.Should().Contain("CreateConditionalFormattingPopupSurface()");
        runtimeCatalogSource.Should().Contain("ConditionalFormatPresetGalleryPlanner.PopupGroups");

        hostSource.Should().Contain("ConditionalFormatPresetGalleryPlanner.DataBarGroups");
        hostSource.Should().Contain("ConditionalFormatPresetGalleryPlanner.ColorScaleGroups");
        hostSource.Should().Contain("ConditionalFormatIconSetCatalog.CreateRule(style, range)");

        foreach (var item in ConditionalFormatPresetGalleryPlanner.PopupItems)
        {
            homeRibbonMenuSource.Should().Contain($"\"{item.CommandId}\"");
            avaloniaRawIdsSource.Should().Contain($"\"{item.CommandId}\"");
            avaloniaMainSource.Should().Contain($"[\"{item.CommandId}\"]");
        }
    }
}
