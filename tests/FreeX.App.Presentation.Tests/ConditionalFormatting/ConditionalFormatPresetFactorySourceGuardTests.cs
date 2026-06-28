using FluentAssertions;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

public sealed class ConditionalFormatPresetFactorySourceGuardTests
{
    [Fact]
    public void ConditionalFormatRuleFactories_LiveInPresentationNotAvalonia()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = Directory.GetParent(presentationRoot)?.Parent?.FullName
            ?? throw new DirectoryNotFoundException("Could not resolve repository root.");

        var sharedFiles = new[]
        {
            "ConditionalFormatRulePlanner.cs",
            "ConditionalFormatRuleBuilder.cs",
            "ConditionalFormatPresetFactory.cs",
            "ConditionalFormatPresetGalleryPlanner.cs",
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
    public void HostConditionalFormatGallery_StaysLocalizationFacade()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = Directory.GetParent(presentationRoot)?.Parent?.FullName
            ?? throw new DirectoryNotFoundException("Could not resolve repository root.");

        var hostSource = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Host", "ConditionalFormatPresetGalleryPlanner.cs"));
        var presentationSource = File.ReadAllText(Path.Combine(presentationRoot, "ConditionalFormatting", "ConditionalFormatPresetGalleryPlanner.cs"));

        hostSource.Should().Contain("using PresentationPlanner = FreeX.App.Presentation.ConditionalFormatting.ConditionalFormatPresetGalleryPlanner;");
        hostSource.Should().Contain("UiText.Get(option.LabelKey)");
        hostSource.Should().Contain("PresentationPlanner.CreateDataBarRule(style, range)");
        hostSource.Should().Contain("PresentationPlanner.CreateColorScaleRule(style, range)");

        presentationSource.Should().NotContain("UiText.Get(");
        presentationSource.Should().Contain("ConditionalFormatDataBar_Category_GradientFill");
        presentationSource.Should().Contain("CreateDataBarRule");
    }
}
