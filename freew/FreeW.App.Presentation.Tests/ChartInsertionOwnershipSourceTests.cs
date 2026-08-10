namespace FreeW.App.Presentation.Tests;

public sealed class ChartInsertionOwnershipSourceTests
{
    [Fact]
    public void NativeDocumentViewsDelegateChartDefaultsToThePortableCoordinator()
    {
        var coordinator = ReadSource(
            "freew", "FreeW.App.Presentation", "Editing", "DocumentObjectEditingCoordinator.cs");
        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        coordinator.Should().Contain("public static Chart PlanChartInsertion(Chart? chart = null)");
        coordinator.Should().Contain("InsertChartDialogPlanner.BuildInitialState(null");
        coordinator.Should().Contain("InsertChartDialogPlanner.TryBuildResult(");

        wpf.Should().Contain("DocumentObjectEditingCoordinator.PlanChartInsertion(chart)");
        avalonia.Should().Contain("DocumentObjectEditingCoordinator.PlanChartInsertion(chart)");
        wpf.Should().NotContain("public void InsertChart(Chart chart)");
        avalonia.Should().NotContain("chart ?? Chart.Create(");
        avalonia.Should().NotContain("[\"Q1\", \"Q2\", \"Q3\", \"Q4\"]");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }
}
