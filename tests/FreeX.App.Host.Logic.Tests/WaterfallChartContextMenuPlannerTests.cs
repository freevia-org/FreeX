using FluentAssertions;
using FreeX.App.Host;
using FreeX.Core.Commands;
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
    public void CreateToggleCommand_InvertsThePortableTotalPointState()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var chart = CreateWaterfallChart(sheet.Id);
        chart.WaterfallTotalPointIndices = [1];
        sheet.Charts.Add(chart);

        var command = WaterfallChartContextMenuPlanner.CreateToggleCommand(
            sheet.Id,
            chart,
            pointIndex: 1);

        command.Should().BeOfType<SetWaterfallTotalPointCommand>();
        command!.Apply(new WorkbookCommandContext(workbook))
            .Success.Should().BeTrue();
        chart.WaterfallTotalPointIndices.Should().NotContain(1);
    }

    [Fact]
    public void CreateToggleCommand_RejectsNonWaterfallOrOutOfRangePoints()
    {
        var chart = CreateWaterfallChart();
        var sheetId = chart.DataRange.Start.Sheet;

        WaterfallChartContextMenuPlanner.CreateToggleCommand(sheetId, chart, -1).Should().BeNull();
        WaterfallChartContextMenuPlanner.CreateToggleCommand(sheetId, chart, 4).Should().BeNull();

        chart.Type = ChartType.Column;
        WaterfallChartContextMenuPlanner.CreateToggleCommand(sheetId, chart, 0).Should().BeNull();
    }

    [Fact]
    public void MainWindowWaterfallContextMenu_RoutesThroughUndoableCommand()
    {
        var constructorSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var contextMenuSource = DialogSourceTestSupport.ReadHostSources("MainWindow.WorksheetContextMenu.cs");

        constructorSource.Should().Contain("SheetGrid.WaterfallChartPointContextMenuRequested += OnWaterfallChartPointContextMenuRequested;");
        contextMenuSource.Should().Contain("private void OnWaterfallChartPointContextMenuRequested(ChartModel chart, int pointIndex, System.Windows.Point gridPos)");
        contextMenuSource.Should().Contain("MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>());");
        contextMenuSource.Should().Contain("WaterfallChartContextMenuPlanner.CreateToggleCommand(");
        contextMenuSource.Should().NotContain("new SetWaterfallTotalPointCommand(");

        var avaloniaSource = WorkspaceFileLocator.ReadAllText(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.PivotChartContextMenus.cs");
        avaloniaSource.Should().Contain("WaterfallChartContextMenuPlanner.CreateToggleCommand(");
        avaloniaSource.Should().NotContain("new SetWaterfallTotalPointCommand(");
    }

    private static ChartModel CreateWaterfallChart(SheetId? sheetId = null)
    {
        var id = sheetId ?? SheetId.New();
        return new ChartModel
        {
            Type = ChartType.Waterfall,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(
                new CellAddress(id, 1, 1),
                new CellAddress(id, 5, 2))
        };
    }
}
