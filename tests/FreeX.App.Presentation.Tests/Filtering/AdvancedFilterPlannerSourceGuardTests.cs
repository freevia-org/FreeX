using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Filtering;

public sealed class AdvancedFilterPlannerSourceGuardTests
{
    [Fact]
    public void AdvancedFilterPlanning_LivesBelowPresentation()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var advancedFilterPlannerPath = Path.Combine(presentationRoot, "Filtering", "AdvancedFilterPlanner.cs");
        var servicesPlannerPath = Path.Combine(repoRoot, "src", "FreeX.App.Services", "AdvancedFilterPlanner.cs");
        var workbookSessionSource = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Services", "WorkbookSession.cs"));
        var avaloniaMainWindowSource = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var hostInputParserPath = Path.Combine(repoRoot, "src", "FreeX.App.Host", "AdvancedFilterInputParser.cs");
        var compatibilityFacadePath = Path.Combine(repoRoot, "src", "FreeX.App.Host", "AdvancedFilterDialog.Planning.cs");
        var hostDialogSource = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Host", "AdvancedFilterDialog.cs"));
        var hostDataCommandsSource = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Host", "MainWindow.DataCommands.cs"));

        File.Exists(advancedFilterPlannerPath)
            .Should()
            .BeTrue("Advanced Filter parsing and dialog planning should be shared by renderers");
        File.Exists(hostInputParserPath)
            .Should()
            .BeFalse("WPF Host should use the shared Advanced Filter parser instead of carrying a local facade");
        File.Exists(servicesPlannerPath)
            .Should()
            .BeFalse("App Services should consume the canonical Presentation types instead of duplicating them");
        File.Exists(compatibilityFacadePath)
            .Should()
            .BeFalse("WPF should consume the Presentation planner without a compatibility facade");

        workbookSessionSource.Should().Contain("using FreeX.App.Presentation.Filtering;");
        avaloniaMainWindowSource.Should().Contain("using FreeX.App.Presentation.Filtering;");
        hostDialogSource.Should().Contain("AdvancedFilterPlanner.CreatePlan(");
        hostDialogSource.Should().Contain("AdvancedFilterPlanner.CreateRangeSelectionRequest(");
        hostDialogSource.Should().Contain("AdvancedFilterPlanner.TryCreateDialogResult(");
        hostDialogSource.Should().NotContain("WorkbookReferenceNavigator");
        hostDialogSource.Should().NotContain("WorkbookRangeTextCodec.TryParse");
        hostDataCommandsSource.Should().Contain("AdvancedFilterPlanner.TryParseRange(");
        hostDataCommandsSource.Should().NotContain("AdvancedFilterInputParser.");
    }
}
