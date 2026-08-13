using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class AvaloniaContextualCommandContractTests
{
    [Fact]
    public void ContextualCommandMap_DescribesAndRoutesImplementedSharedWorkflows()
    {
        var contextual = Read("src", "FreeX.App.Avalonia", "MainWindow.ContextualTabs.cs");
        var charts = Read("src", "FreeX.App.Avalonia", "MainWindow.ChartTabs.cs");
        var pivots = Read("src", "FreeX.App.Avalonia", "MainWindow.PivotTabs.cs");
        var pictureShape = Read("src", "FreeX.App.Avalonia", "MainWindow.PictureShapeTabs.cs");
        var proofing = Read("src", "FreeX.App.Avalonia", "MainWindow.Proofing.cs");

        contextual.Should().Contain("[\"Move Chart\"] = () => RunGuarded(ShowMoveChartDialog)");
        contextual.Should().Contain("[\"Format Stock Chart\"] = () => RunGuarded(ShowChartStockFormatDialog)");
        contextual.Should().Contain("[FreeXRibbonCommandIds.PivotChartChangeType]");
        contextual.Should().Contain("[\"Move PivotTable\"] = OpenPivotMove");
        contextual.Should().Contain("BuildPictureShapeTabCommands()");

        contextual.Should().NotContain("Phase-1");
        contextual.Should().NotContain("ReportContextualNotYetAvailable");
        charts.Should().NotContain("ReportChartCommandNotYetAvailable");
        pivots.Should().NotContain("ReportPivotNotYetAvailable");
        pictureShape.Should().NotContain("Phase-1");
        pictureShape.Should().NotContain("it reports an honest, clearly-labeled status");
        proofing.Should().Contain("Insert Object is implemented separately in MainWindow.InsertObjects");
        proofing.Should().NotContain("object embedding is unsupported");
    }

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine([RepositoryRoot(), .. path]));

    private static string RepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
