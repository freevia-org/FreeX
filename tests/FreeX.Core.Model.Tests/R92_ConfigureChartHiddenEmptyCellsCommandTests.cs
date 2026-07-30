using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R92-app-chart-data-edit-5-3: the Select Data dialog's "Hidden and Empty Cell Settings" button
/// used to only show a static informational MessageBox -- chart.BlankDisplayMode/
/// ShowDataInHiddenRowsAndColumns could never be changed for an ordinary (non-pivot) chart, because
/// the only command that touched them (<see cref="ConfigurePivotChartOptionsCommand"/>) rejected
/// every chart that wasn't a PivotChart. <see cref="ConfigureChartHiddenEmptyCellsCommand"/> is the
/// real, undoable fix, usable by ANY chart.
/// </summary>
public sealed class R92_ConfigureChartHiddenEmptyCellsCommandTests
{
    private static (Sheet Sheet, TestCommandContext Ctx, ChartModel Chart) CreateNonPivotChart()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3));
        new AddChartCommand(sheet.Id, range, ChartType.Line, "Sales").Apply(ctx);
        return (sheet, ctx, sheet.Charts[0]);
    }

    [Fact]
    public void ConfigureChartHiddenEmptyCellsCommand_SetsBlankDisplayModeAndShowHiddenDataOnNonPivotChart()
    {
        var (sheet, ctx, chart) = CreateNonPivotChart();
        chart.BlankDisplayMode.Should().Be(ChartBlankDisplayMode.Gap); // default
        chart.ShowDataInHiddenRowsAndColumns.Should().BeFalse();

        var outcome = new ConfigureChartHiddenEmptyCellsCommand(sheet.Id, chart.Id, ChartBlankDisplayMode.Zero, true).Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.BlankDisplayMode.Should().Be(ChartBlankDisplayMode.Zero);
        chart.ShowDataInHiddenRowsAndColumns.Should().BeTrue();
    }

    [Fact]
    public void ConfigureChartHiddenEmptyCellsCommand_SetsConnectDataPointsWithLine()
    {
        var (sheet, ctx, chart) = CreateNonPivotChart();

        var outcome = new ConfigureChartHiddenEmptyCellsCommand(sheet.Id, chart.Id, ChartBlankDisplayMode.Span, false).Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.BlankDisplayMode.Should().Be(ChartBlankDisplayMode.Span);
    }

    [Fact]
    public void ConfigureChartHiddenEmptyCellsCommand_IsUndoable()
    {
        var (sheet, ctx, chart) = CreateNonPivotChart();
        var command = new ConfigureChartHiddenEmptyCellsCommand(sheet.Id, chart.Id, ChartBlankDisplayMode.Zero, true);

        command.Apply(ctx).Success.Should().BeTrue();
        command.Revert(ctx);

        chart.BlankDisplayMode.Should().Be(ChartBlankDisplayMode.Gap);
        chart.ShowDataInHiddenRowsAndColumns.Should().BeFalse();
    }

    [Fact]
    public void ConfigureChartHiddenEmptyCellsCommand_RejectsProtectedSheetWithoutEditObjectsPermission()
    {
        var (sheet, ctx, chart) = CreateNonPivotChart();
        sheet.IsProtected = true;

        var outcome = new ConfigureChartHiddenEmptyCellsCommand(sheet.Id, chart.Id, ChartBlankDisplayMode.Zero, true).Apply(ctx);

        outcome.Success.Should().BeFalse();
        chart.BlankDisplayMode.Should().Be(ChartBlankDisplayMode.Gap);
    }
}
