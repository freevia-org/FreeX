using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Filtering;

public sealed class AutoFilterPlannerSourceGuardTests
{
    [Fact]
    public void AutoFilterRangeAndHeaderPlanning_LiveInPresentation()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = Directory.GetParent(presentationRoot)?.Parent?.FullName
            ?? throw new DirectoryNotFoundException("Could not resolve repository root.");

        File.Exists(Path.Combine(presentationRoot, "Filtering", "AutoFilterHeaderButtonPlanner.cs"))
            .Should()
            .BeTrue("filter header targeting should be shared by WPF, Avalonia, and sister apps");
        File.Exists(Path.Combine(presentationRoot, "Filtering", "AutoFilterToggleRangePlanner.cs"))
            .Should()
            .BeTrue("filter command range selection should be shared by renderers");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Avalonia", "AutoFilterHeaderPlanner.cs"))
            .Should()
            .BeFalse("Avalonia should render shared filter-header plans instead of owning the planner");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "AutoFilterToggleRangePlanner.cs"))
            .Should()
            .BeFalse("WPF Host should use the shared range planner instead of carrying a local copy");
    }

    [Fact]
    public void RendererLayers_DelegateToSharedFilteringPlanners()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = Directory.GetParent(presentationRoot)?.Parent?.FullName
            ?? throw new DirectoryNotFoundException("Could not resolve repository root.");
        var avaloniaSource = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.AutoFilter.cs"));
        var hostClearSource = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Host", "ClearFilterRangePlanner.cs"));

        avaloniaSource.Should().Contain("AutoFilterHeaderButtonPlanner.IsFilterButtonCell");
        avaloniaSource.Should().Contain("AutoFilterDropdownMenuPlanner.CreateMenuPlan");
        avaloniaSource.Should().Contain("InvariantAutoFilterMenuTextProvider.Instance");
        avaloniaSource.Should().NotContain("RangeHasActiveFilter(");
        hostClearSource.Should().Contain("AutoFilterToggleRangePlanner.Create");
        hostClearSource.Should().Contain("AutoFilterDropdownMenuPlanner.HasActiveFilter");
        hostClearSource.Should().NotContain("SelectionRangeService.GetCurrentRegion");
    }
}
