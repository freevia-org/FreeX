using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

public sealed class WorksheetStructureSplitTargetTests
{
    [Fact]
    public void ResolveSplitTarget_UsesActiveCell()
    {
        var (splitRow, splitColumn) = WorksheetStructureCommandPlanner.ResolveSplitTarget(
            activeRow: 5,
            activeColumn: 3,
            wasSplit: false);

        splitRow.Should().Be(5u);
        splitColumn.Should().Be(3u);
    }

    [Fact]
    public void ResolveSplitTarget_AtA1FallsBackToViewportMidpoint()
    {
        var rowMetrics = BuildRowMetrics(20);
        var columnMetrics = BuildColumnMetrics(8);

        var (splitRow, splitColumn) = WorksheetStructureCommandPlanner.ResolveSplitTarget(
            activeRow: 1,
            activeColumn: 1,
            wasSplit: false,
            viewportRows: rowMetrics,
            viewportColumns: columnMetrics);

        splitRow.Should().Be(rowMetrics[rowMetrics.Count / 2].Row);
        splitColumn.Should().Be(columnMetrics[columnMetrics.Count / 2].Col);
    }

    [Fact]
    public void ResolveSplitTarget_AtA1WithoutViewportReturnsNoSplit()
    {
        var (splitRow, splitColumn) = WorksheetStructureCommandPlanner.ResolveSplitTarget(
            1,
            1,
            wasSplit: false);

        splitRow.Should().BeNull();
        splitColumn.Should().BeNull();
    }

    [Fact]
    public void ResolveSplitTarget_WhenAlreadySplitClearsBothAxes()
    {
        var (splitRow, splitColumn) = WorksheetStructureCommandPlanner.ResolveSplitTarget(
            5,
            3,
            wasSplit: true,
            BuildRowMetrics(20),
            BuildColumnMetrics(8));

        splitRow.Should().BeNull();
        splitColumn.Should().BeNull();
    }

    [Fact]
    public void ResolveSplitTarget_OnFirstRowUsesColumnOnly()
    {
        var (splitRow, splitColumn) = WorksheetStructureCommandPlanner.ResolveSplitTarget(
            activeRow: 1,
            activeColumn: 4,
            wasSplit: false,
            viewportRows: BuildRowMetrics(20),
            viewportColumns: BuildColumnMetrics(8));

        splitRow.Should().BeNull();
        splitColumn.Should().Be(4u);
    }

    [Fact]
    public void ResolveSplitTarget_ViewportFallbackAxesAreIndependent()
    {
        var rowMetrics = BuildRowMetrics(20);

        var (splitRow, splitColumn) = WorksheetStructureCommandPlanner.ResolveSplitTarget(
            activeRow: 1,
            activeColumn: 1,
            wasSplit: false,
            viewportRows: rowMetrics,
            viewportColumns: null);

        splitRow.Should().Be(rowMetrics[rowMetrics.Count / 2].Row);
        splitColumn.Should().BeNull();
    }

    [Fact]
    public void ResolveSplitTarget_SingleVisibleMetricDoesNotCreateFallback()
    {
        var (splitRow, splitColumn) = WorksheetStructureCommandPlanner.ResolveSplitTarget(
            activeRow: 1,
            activeColumn: 1,
            wasSplit: false,
            viewportRows: BuildRowMetrics(1),
            viewportColumns: BuildColumnMetrics(1));

        splitRow.Should().BeNull();
        splitColumn.Should().BeNull();
    }

    [Fact]
    public void RendererFacades_AreAbsentAndHostsUseTheSharedSession()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var avaloniaFacade = Path.Combine(repoRoot, "src", "FreeX.App.Avalonia", "SplitAnchorResolver.cs");
        var avaloniaSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.ParityWires.cs"));
        var wpfSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Host",
            "MainWindow.ViewCommands.cs"));

        File.Exists(avaloniaFacade).Should().BeFalse("split-target decisions belong to WorksheetStructureCommandPlanner");
        avaloniaSource.Should().Contain("_session.ToggleSplitPanesAtActiveCell()");
        wpfSource.Should().Contain("_session.ToggleSplitPanesAtActiveCell(viewportRows, viewportColumns)");
        avaloniaSource.Should().NotContain("SplitAnchorResolver");
        wpfSource.Should().NotContain("SplitAnchorResolver");
    }

    private static List<RowMetric> BuildRowMetrics(int count) =>
        Enumerable.Range(1, count)
            .Select(index => new RowMetric((uint)index, 20.0, (index - 1) * 20.0))
            .ToList();

    private static List<ColMetric> BuildColumnMetrics(int count) =>
        Enumerable.Range(1, count)
            .Select(index => new ColMetric((uint)index, 64.0, (index - 1) * 64.0))
            .ToList();
}
