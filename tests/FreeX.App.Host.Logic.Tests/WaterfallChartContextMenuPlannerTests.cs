using FluentAssertions;
using FreeX.App.Host;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class WaterfallChartContextMenuPlannerTests
{
    [Fact]
    public void BuildCommands_ChecksSetAsTotalForImplicitLastWaterfallPoint()
    {
        var chart = CreateWaterfallChart();

        var firstPoint = WaterfallChartContextMenuPlanner.BuildCommands(chart, 0).Should().ContainSingle().Subject;
        var lastPoint = WaterfallChartContextMenuPlanner.BuildCommands(chart, 3).Should().ContainSingle().Subject;

        firstPoint.Header.Should().Be("Set as Total");
        firstPoint.IsChecked.Should().BeFalse();
        firstPoint.IsEnabled.Should().BeTrue();
        lastPoint.IsChecked.Should().BeTrue();
        lastPoint.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void BuildCommands_UsesAuthoredWaterfallTotalIndices()
    {
        var chart = CreateWaterfallChart();
        chart.WaterfallTotalPointIndices = [1];

        WaterfallChartContextMenuPlanner.BuildCommands(chart, 1)
            .Should().ContainSingle().Which.IsChecked.Should().BeTrue();
        WaterfallChartContextMenuPlanner.BuildCommands(chart, 3)
            .Should().ContainSingle().Which.IsChecked.Should().BeFalse();
    }

    [Fact]
    public void MainWindowWaterfallContextMenu_RoutesThroughUndoableCommand()
    {
        var constructorSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var contextMenuSource = DialogSourceTestSupport.ReadHostSources("MainWindow.WorksheetContextMenu.cs");

        constructorSource.Should().Contain("SheetGrid.WaterfallChartPointContextMenuRequested += OnWaterfallChartPointContextMenuRequested;");
        contextMenuSource.Should().Contain("private void OnWaterfallChartPointContextMenuRequested(ChartModel chart, int pointIndex, System.Windows.Point gridPos)");
        contextMenuSource.Should().Contain("MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>());");
        contextMenuSource.Should().Contain("new SetWaterfallTotalPointCommand(_currentSheetId, chart.Id, pointIndex, setAsTotal)");
        contextMenuSource.Should().Contain("WaterfallChartContextMenuPlanner.IsPointTotal(chart, pointIndex)");
    }

    private static ChartModel CreateWaterfallChart()
    {
        var sheetId = SheetId.New();
        return new ChartModel
        {
            Type = ChartType.Waterfall,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 5, 2))
        };
    }
}
