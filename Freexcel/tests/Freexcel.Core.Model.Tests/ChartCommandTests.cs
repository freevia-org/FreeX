using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class ChartCommandTests
{
    [Theory]
    [InlineData(ChartType.Column)]
    [InlineData(ChartType.Line)]
    [InlineData(ChartType.Pie)]
    [InlineData(ChartType.Bar)]
    public void AddChartCommand_AddsRequestedChartTypeAndUndoRemovesIt(ChartType type)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));

        var command = new AddChartCommand(sheet.Id, range, type, "Sales");

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts.Should().ContainSingle();
        sheet.Charts[0].Type.Should().Be(type);
        sheet.Charts[0].DataRange.Should().Be(range);
        sheet.Charts[0].Title.Should().Be("Sales");

        command.Revert(ctx);

        sheet.Charts.Should().BeEmpty();
    }

    [Fact]
    public void AddChartCommand_RejectsDataRangeOnDifferentSheet()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet2.Id, 1, 1),
            new CellAddress(sheet2.Id, 3, 2));

        var command = new AddChartCommand(sheet1.Id, range, ChartType.Column);

        command.Apply(ctx).Success.Should().BeFalse();
        sheet1.Charts.Should().BeEmpty();
    }

    [Fact]
    public void SetChartLayoutCommand_UpdatesTitleAxesLegendAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Old").Apply(ctx);
        var chartId = sheet.Charts[0].Id;

        var command = new SetChartLayoutCommand(
            sheet.Id,
            chartId,
            new ChartLayoutOptions(
                Title: "Revenue",
                XAxisTitle: "Quarter",
                YAxisTitle: "Amount",
                LegendPosition: ChartLegendPosition.Bottom,
                ShowLegend: true));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].Title.Should().Be("Revenue");
        sheet.Charts[0].XAxisTitle.Should().Be("Quarter");
        sheet.Charts[0].YAxisTitle.Should().Be("Amount");
        sheet.Charts[0].LegendPosition.Should().Be(ChartLegendPosition.Bottom);
        sheet.Charts[0].ShowLegend.Should().BeTrue();

        command.Revert(ctx);

        sheet.Charts[0].Title.Should().Be("Old");
        sheet.Charts[0].XAxisTitle.Should().BeNull();
        sheet.Charts[0].YAxisTitle.Should().BeNull();
        sheet.Charts[0].LegendPosition.Should().Be(ChartLegendPosition.Right);
        sheet.Charts[0].ShowLegend.Should().BeTrue();
    }

    [Fact]
    public void SetChartLayoutCommand_RejectsMissingChart()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            Guid.NewGuid(),
            new ChartLayoutOptions(Title: "Revenue"));

        command.Apply(ctx).Success.Should().BeFalse();
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
