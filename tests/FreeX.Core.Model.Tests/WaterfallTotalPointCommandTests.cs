using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class WaterfallTotalPointCommandTests
{
    [Fact]
    public void SetWaterfallTotalPointCommand_SetsPointAsTotalAndUndoRestoresNullDefault()
    {
        var (workbook, sheet, chart) = CreateWaterfallChart();
        var ctx = new TestCommandContext(workbook);
        var command = new SetWaterfallTotalPointCommand(sheet.Id, chart.Id, pointIndex: 1, setAsTotal: true);

        command.Apply(ctx).Success.Should().BeTrue();

        chart.WaterfallTotalPointIndices.Should().Equal(1, 3);

        command.Revert(ctx);

        chart.WaterfallTotalPointIndices.Should().BeNull();
    }

    [Fact]
    public void SetWaterfallTotalPointCommand_ClearsImplicitLastTotalAndUndoRestoresIt()
    {
        var (workbook, sheet, chart) = CreateWaterfallChart();
        var ctx = new TestCommandContext(workbook);
        var command = new SetWaterfallTotalPointCommand(sheet.Id, chart.Id, pointIndex: 3, setAsTotal: false);

        command.Apply(ctx).Success.Should().BeTrue();

        chart.WaterfallTotalPointIndices.Should().BeEmpty();

        command.Revert(ctx);

        chart.WaterfallTotalPointIndices.Should().BeNull();
    }

    [Fact]
    public void SetWaterfallTotalPointCommand_RejectsNonWaterfallCharts()
    {
        var (workbook, sheet, chart) = CreateWaterfallChart();
        chart.Type = ChartType.Column;
        var command = new SetWaterfallTotalPointCommand(sheet.Id, chart.Id, pointIndex: 1, setAsTotal: true);

        var outcome = command.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("waterfall");
        chart.WaterfallTotalPointIndices.Should().BeNull();
    }

    private static (Workbook Workbook, Sheet Sheet, ChartModel Chart) CreateWaterfallChart()
    {
        var workbook = new Workbook("Waterfall");
        var sheet = workbook.AddSheet("Sheet1");
        var chart = new ChartModel
        {
            Type = ChartType.Waterfall,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 5, 2))
        };
        sheet.Charts.Add(chart);
        return (workbook, sheet, chart);
    }
}
