using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r230: the Change* family -- a third verb hiding the equal-value setter shape, after r218's
/// Reposition/Resize/Rotate and r225's Move. The Change Chart Type gallery highlights the chart's
/// current type and Select Data pre-fills the current range and checkboxes, so re-confirming either
/// is an ordinary gesture.
/// <para>
/// Each guard has exactly as many clauses as Apply has writes, and the extra clauses are not
/// decoration. ChangeChartType writes Type AND FirstColIsCategories, the latter derived from the
/// requested type -- so a chart whose flag was set by hand can differ from what the type implies,
/// and correcting it is a real edit even when the type already matches. ChangeChartSource writes
/// four fields, and its long per-series clear block is already gated on the range or orientation
/// having changed, so all four matching means every remaining line is a self-assignment.
/// </para>
/// </summary>
public sealed class R230_ChangeChartNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"r{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }
        return (sheet, new TestCommandContext(workbook));
    }

    private static ChartModel Chart(Sheet sheet)
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
        };
        sheet.Charts.Add(chart);
        return chart;
    }

    [Fact]
    public void ReSelectingTheChartsCurrentType_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        var chart = Chart(sheet);

        new ChangeChartTypeCommand(sheet.Id, chart.Id, ChartType.Column).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ChangingTheChartType_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        var chart = Chart(sheet);

        var outcome = new ChangeChartTypeCommand(sheet.Id, chart.Id, ChartType.Line).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        chart.Type.Should().Be(ChartType.Line);
    }

    [Fact]
    public void ReSelectingTheTypeWhenTheCategoryFlagDisagreesWithIt_IsARealEdit()
    {
        // The clause that is easy to leave out. The type already matches, but FirstColIsCategories
        // was set by hand to something the type does not imply, and this command corrects it.
        var (sheet, ctx) = Fixture();
        var chart = Chart(sheet);
        chart.FirstColIsCategories = false;

        var outcome = new ChangeChartTypeCommand(sheet.Id, chart.Id, ChartType.Column).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        chart.FirstColIsCategories.Should().BeTrue();
    }

    [Fact]
    public void ReSubmittingTheChartsOwnSourceRange_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        var chart = Chart(sheet);

        new ChangeChartSourceCommand(sheet.Id, chart.Id, chart.DataRange).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ChangingTheChartsSourceRange_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        var chart = Chart(sheet);
        var wider = new GridRange(
            new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));

        var outcome = new ChangeChartSourceCommand(sheet.Id, chart.Id, wider).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        chart.DataRange.Should().Be(wider);
    }

    [Fact]
    public void ReSubmittingTheSameRangeWithADifferentHeaderFlag_IsARealEdit()
    {
        var (sheet, ctx) = Fixture();
        var chart = Chart(sheet);

        var outcome = new ChangeChartSourceCommand(
            sheet.Id, chart.Id, chart.DataRange, firstRowIsHeader: false).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        chart.FirstRowIsHeader.Should().BeFalse();
    }
}
