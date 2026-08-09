using FluentAssertions;

namespace FreeX.App.Presentation.Tests;

public sealed class NavigationOutlinePlannerDedupSourceGuardTests
{
    [Fact]
    public void NavigationOutlineAndGroupedRangePlanners_HaveSharedPresentationOwners()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var hostRoot = Path.Combine(repoRoot, "src", "FreeX.App.Host");

        foreach (var sharedPlanner in new[]
        {
            "ExcelSelectionModePlanner.cs",
            "ExcelWorksheetNavigationPlanner.cs",
            "GroupedSheetRangePlanner.cs",
            "OutlineGroupingPlanner.cs"
        })
        {
            File.Exists(Path.Combine(presentationRoot, sharedPlanner))
                .Should()
                .BeTrue($"{sharedPlanner} should expose portable planning from FreeX.App.Presentation");
        }

        foreach (var removedHostPlanner in new[]
        {
            "GroupedSheetRangePlanner.cs",
            "OutlineGroupingPlanner.cs"
        })
        {
            File.Exists(Path.Combine(hostRoot, removedHostPlanner))
                .Should()
                .BeFalse($"{removedHostPlanner} should not remain as a pure Host facade");
        }

        var selectionAdapter = File.ReadAllText(Path.Combine(hostRoot, "ExcelSelectionModePlanner.cs"));
        selectionAdapter.Should().Contain("FreeX.App.Presentation.ExcelSelectionModePlanner.TryToggle(");
        selectionAdapter.Should().NotContain("current == ExcelSelectionMode.Extend");

        var navigationAdapter = File.ReadAllText(Path.Combine(hostRoot, "ExcelWorksheetNavigationPlanner.cs"));
        navigationAdapter.Should().Contain("FreeX.App.Presentation.ExcelWorksheetNavigationPlanner.TryToggleEndMode(");
        navigationAdapter.Should().Contain("MapModifiers");
        navigationAdapter.Should().NotContain("EnumerateValueBearingCells(");
    }
}
