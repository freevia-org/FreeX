using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class PivotCreateDialogSourceTests
{
    [Fact]
    public void InsertPivotTableDialog_UsesPresentationPlanner()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotCreate.cs"));

        source.Should().Contain("using FreeX.App.Presentation.PivotUI;");
        source.Should().Contain("PivotApplication.PrepareCreate(");
        source.Should().Contain("PivotApplication.PlanCreate(");
        source.Should().Contain("new PivotCreateSubmission(");
        source.Should().NotContain("PivotCreatePlanner.BuildCommand(");
        source.Should().NotContain("using FreeX.App.Avalonia.Pivot;");
        File.Exists(RepoFileAllowMissing("src", "FreeX.App.Avalonia", "Pivot", "PivotCreatePlanner.cs")).Should().BeFalse();
    }

    [Fact]
    public void StandardPivotActions_RouteThroughSharedApplicationSession()
    {
        var rendererSource = string.Join(
            "\n",
            File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotCreate.cs")),
            File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotName.cs")),
            File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotMove.cs")),
            File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotDataSource.cs")),
            File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotAnalyzeActions.cs")),
            File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotTabs.cs")));
        var adapterSource = File.ReadAllText(
            RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotApplicationSession.cs"));
        var sharedSource = File.ReadAllText(
            RepoFile("src", "FreeX.App.Presentation", "PivotUI", "PivotApplicationSession.cs"));

        rendererSource.Should().Contain("PivotApplication.PlanCreate(");
        rendererSource.Should().Contain("PivotApplication.PlanRefresh(target)");
        rendererSource.Should().Contain("PivotApplication.PlanRename(target");
        rendererSource.Should().Contain("PivotApplication.PlanMove(target");
        rendererSource.Should().Contain("PivotApplication.PlanChangeDataSource(target");
        rendererSource.Should().Contain("PivotApplication.PlanClear(target)");
        rendererSource.Should().Contain("PivotApplication.PlanSelect(target)");
        rendererSource.Should().Contain("PivotApplication.PlanShowDetails(");
        rendererSource.Should().NotContain("new RefreshPivotTableCommand(");
        rendererSource.Should().NotContain("new RenamePivotTableCommand(");
        rendererSource.Should().NotContain("new MovePivotTableCommand(");
        rendererSource.Should().NotContain("new ChangePivotTableSourceCommand(");
        rendererSource.Should().NotContain("new ClearPivotTableViewCommand(");
        rendererSource.Should().NotContain("new DrillDownPivotTableCommand(");

        adapterSource.Should().Contain("_session.ExecuteReviewCommand(command)");
        sharedSource.Should().Contain("public sealed partial class PivotApplicationSession");
        sharedSource.Should().Contain("public PivotApplicationOutcome Execute(PivotApplicationPlan plan)");
    }

    [Fact]
    public void PivotEditorsAndSlicerTimelineAdapters_DoNotConstructPortableCommands()
    {
        var rendererSource = string.Join(
            "\n",
            File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotFilters.cs")),
            File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotFieldSettings.cs")),
            File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotCalculatedField.cs")),
            File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotCalculatedItem.cs")),
            File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotGroupField.cs")),
            File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotOptions.cs")),
            File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotTabs.cs")),
            File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotStyleGallery.cs")),
            File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.InsertSlicer.cs")),
            File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.SlicerTimeline.cs")),
            File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.SlicerTimelinePane.cs")));

        rendererSource.Should().NotContain("new ConfigurePivotTableFieldFiltersCommand(");
        rendererSource.Should().NotContain("new ConfigurePivotTableViewCommand(");
        rendererSource.Should().NotContain("new ConfigurePivotTableCalculatedItemsCommand(");
        rendererSource.Should().NotContain("new ConfigurePivotTableOptionsCommand(");
        rendererSource.Should().NotContain("new AddSlicerCommand(");
        rendererSource.Should().NotContain("new AddTimelineCommand(");
        rendererSource.Should().NotContain("new SetSlicerSelectionCommand(");
        rendererSource.Should().NotContain("new SetTimelineRangeCommand(");
        rendererSource.Should().NotContain("new SetTimelineGranularityCommand(");
        rendererSource.Should().Contain("PivotApplication.ReadSourceHeaders(");
        rendererSource.Should().Contain("PivotApplication.ReadSourceItems(");
        rendererSource.Should().Contain("PivotApplication.PlanFieldItemSelection(");
        rendererSource.Should().Contain("PivotApplication.PlanCalculatedConfiguration(");
        rendererSource.Should().Contain("PivotApplication.PlanDialogOptions(");
        rendererSource.Should().Contain("PivotApplication.PlanSlicerSelection(");
        rendererSource.Should().Contain("PivotApplication.PlanTimelineRange(");
    }

    private static string RepoFile(params string[] parts)
    {
        var path = RepoFileAllowMissing(parts);
        if (File.Exists(path))
            return path;

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }

    private static string RepoFileAllowMissing(params string[] parts)
    {
        return Path.Combine(new[] { FindRepositoryRoot() }.Concat(parts).ToArray());
    }

    private static string FindRepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
