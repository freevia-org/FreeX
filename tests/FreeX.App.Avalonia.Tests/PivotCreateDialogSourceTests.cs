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
        sharedSource.Should().Contain("public sealed class PivotApplicationSession");
        sharedSource.Should().Contain("public PivotApplicationOutcome Execute(PivotApplicationPlan plan)");
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
