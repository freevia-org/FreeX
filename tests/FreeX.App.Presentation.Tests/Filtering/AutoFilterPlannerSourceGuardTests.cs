using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Filtering;

public sealed class AutoFilterPlannerSourceGuardTests
{
    [Fact]
    public void AutoFilterRangePlanning_LivesBelowPresentation()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var coreCommandsRoot = Path.Combine(repoRoot, "src", "FreeX.Core.Commands");

        File.Exists(Path.Combine(presentationRoot, "Filtering", "AutoFilterHeaderButtonPlanner.cs"))
            .Should()
            .BeTrue("filter header targeting should be shared by WPF, Avalonia, and sister apps");
        File.Exists(Path.Combine(coreCommandsRoot, "AutoFilterRangeResolver.cs"))
            .Should()
            .BeTrue("effective AutoFilter range resolution is model/command logic shared below app renderers");
        File.Exists(Path.Combine(coreCommandsRoot, "AutoFilterToggleRangePlanner.cs"))
            .Should()
            .BeTrue("filter command range selection should be shared by Presentation and Services");
        File.Exists(Path.Combine(presentationRoot, "AutoFilter", "AutoFilterRangeResolver.cs"))
            .Should()
            .BeFalse("Presentation should not duplicate Core AutoFilter range resolution");
        File.Exists(Path.Combine(presentationRoot, "Filtering", "AutoFilterToggleRangePlanner.cs"))
            .Should()
            .BeFalse("Presentation should use the Core range planner instead of owning a copy");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Avalonia", "AutoFilterHeaderPlanner.cs"))
            .Should()
            .BeFalse("Avalonia should render shared filter-header plans instead of owning the planner");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "AutoFilterToggleRangePlanner.cs"))
            .Should()
            .BeFalse("WPF Host should use the shared range planner instead of carrying a local copy");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "AutoFilterDropdownPlanner.cs"))
            .Should()
            .BeFalse("WPF Host should call the shared dropdown planner directly and keep only UI text resources local");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "ClearFilterRangePlanner.cs"))
            .Should()
            .BeFalse("WPF Host should call the shared AutoFilter range and active-filter planners directly");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "FilterPromptPlanner.cs"))
            .Should()
            .BeFalse("filter prompt parsing should live below Host, with WPF owning only localized error text");
    }

    [Fact]
    public void RendererLayers_DelegateToSharedFilteringPlanners()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var avaloniaSource = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.AutoFilter.cs"));
        var hostDropdownSource = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Host", "MainWindow.EditingDropdowns.cs"));
        var hostResourcesSource = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Host", "AutoFilterMenuResources.cs"));
        var hostDataFilterSource = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Host", "MainWindow.DataFilterCommands.cs"));
        var presentationPromptSource = File.ReadAllText(Path.Combine(presentationRoot, "Filtering", "FilterPromptPlanner.cs"));
        var presentationMenuSource = File.ReadAllText(Path.Combine(presentationRoot, "Filtering", "AutoFilterMenuPlanner.cs"));
        var presentationMessageSource = File.ReadAllText(Path.Combine(presentationRoot, "Filtering", "WorksheetFilterMessagePlanner.cs"));

        avaloniaSource.Should().Contain("AutoFilterHeaderButtonPlanner.IsFilterButtonCell");
        avaloniaSource.Should().Contain("AutoFilterHeaderButtonPlanner.IsColumnActive(sheet, range, address.Col)");
        avaloniaSource.Should().NotContain("sheet.AutoFilter?.FilterColumns");
        avaloniaSource.Should().NotContain("table.FilterColumns.Any");
        avaloniaSource.Should().Contain("AutoFilterDropdownMenuPlanner.CreateMenuPlan");
        avaloniaSource.Should().Contain("AutoFilterMenuPlanner.Build(");
        avaloniaSource.Should().Contain("WorksheetFilterMessagePlanner.GetPlanErrorResourceKey(plan)");
        avaloniaSource.Should().Contain("WorksheetFilterMessagePlanner.GetCommandFailureResourceKey(plan.Kind)");
        avaloniaSource.Should().Contain("WorksheetFilterMessagePlanner.GetSuccessResourceKey(plan.Kind)");
        avaloniaSource.Should().Contain("InvariantAutoFilterMenuTextProvider.Instance");
        avaloniaSource.Should().NotContain("RangeHasActiveFilter(");
        avaloniaSource.Should().NotContain("private static string FormatFilterPromptPlanError(");
        hostDropdownSource.Should().Contain("AutoFilterDropdownMenuPlanner.TryGetAutoFilterRange");
        hostDropdownSource.Should().Contain("AutoFilterDropdownMenuPlanner.CreateMenuPlan");
        hostDropdownSource.Should().Contain("AutoFilterMenuResources.TextProvider");
        hostDropdownSource.Should().NotContain("AutoFilterDropdownPlanner.");
        hostResourcesSource.Should().Contain("IAutoFilterMenuTextProvider");
        hostResourcesSource.Should().NotContain("AutoFilterDropdownMenuPlanner.");
        hostDataFilterSource.Should().Contain("AutoFilterToggleRangePlanner.Create(sheet, selectedRange)");
        hostDataFilterSource.Should().Contain("AutoFilterDropdownMenuPlanner.HasActiveFilter(sheet, range)");
        hostDataFilterSource.Should().Contain("_filterWorkflowSession.PlanDialogResult(");
        hostDataFilterSource.Should().Contain("WorksheetFilterMessagePlanner.GetPlanErrorResourceKey(plan)");
        hostDataFilterSource.Should().NotContain("private static string FormatFilterPromptPlanError(");
        hostDataFilterSource.Should().NotContain("FilterPromptPlanner.TryPlan(");
        presentationPromptSource.Should().Contain("FilterInputParser.TryParseTopBottom");
        presentationPromptSource.Should().Contain("FilterInputParser.TryParseCriterion");
        presentationPromptSource.Should().NotContain("UiText.Get(");
        presentationMenuSource.Should().Contain("public static class AutoFilterMenuPlanner");
        presentationMenuSource.Should().Contain("CreateCriteriaOptions(");
        presentationMessageSource.Should().Contain("GetPromptErrorResourceKey(");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Avalonia", "AutoFilterMenuPlanner.cs"))
            .Should().BeFalse("renderer-neutral menu projection belongs in Presentation");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "AutoFilterCriteriaLabels.cs"))
            .Should().BeFalse("localized criteria projection should use the shared menu planner");
        hostDataFilterSource.Should().NotContain("SelectionRangeService.GetCurrentRegion");

        var hostViewportSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Host",
            "MainWindow.Viewport.cs"));
        hostViewportSource.Should().Contain("AutoFilterHeaderButtonPlanner.GetActiveColumnOffsets(");
        hostViewportSource.Should().NotContain("private static IReadOnlySet<uint>? BuildActiveAutoFilterColumns(");
    }

    [Fact]
    public void ServicesLayer_DelegatesAutoFilterToggleRangeToCorePlanner()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var sessionSource = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Services", "WorkbookSession.cs"));

        sessionSource.Should().Contain("AutoFilterToggleRangePlanner.Create(ActiveSheet, SelectedRange)");
        sessionSource.Should().NotContain("ResolveAutoFilterToggleRange");
        sessionSource.Should().NotContain("Mirrors the desktop host");
    }
}
