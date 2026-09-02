using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r225: two commands that saturate rather than move. The Nudge family's chart member clamps with
/// <c>Math.Max(0, ...)</c> where its three siblings add to an unclamped anchor offset, so a chart
/// already at the left edge absorbs every further arrow-key press; and Move Chart has an explicit
/// same-sheet early return that was already right and simply never said so.
/// <para>
/// Both matter more than one wasted undo entry. Holding an arrow key against the edge pushes one
/// entry per repeat, and every push clears the redo stack.
/// </para>
/// </summary>
public sealed class R225_SaturatingMoveNoOpTests
{
    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new TestCommandContext(workbook));
    }

    private static ChartModel Chart(Sheet sheet, double left, double top)
    {
        var chart = new ChartModel
        {
            Left = left,
            Top = top,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
        };
        sheet.Charts.Add(chart);
        return chart;
    }

    [Fact]
    public void NudgingAChartThatIsAlreadyAtTheLeftEdge_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        var chart = Chart(sheet, left: 0, top: 40);

        new NudgeChartCommand(sheet.Id, chart.Id, -5, 0).Apply(ctx).IsNoOp.Should().BeTrue();

        chart.Left.Should().Be(0);
    }

    [Fact]
    public void NudgingAChartAtTheCornerInBothAxes_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        var chart = Chart(sheet, left: 0, top: 0);

        new NudgeChartCommand(sheet.Id, chart.Id, -5, -5).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void NudgingAChartAgainstOneEdgeButFreeOnTheOther_IsARealMove()
    {
        // The clause that stops this from over-reporting: the horizontal move saturates, the
        // vertical one does not, so the chart really did move and the entry belongs on the stack.
        var (_, sheet, ctx) = Fixture();
        var chart = Chart(sheet, left: 0, top: 40);

        var outcome = new NudgeChartCommand(sheet.Id, chart.Id, -5, 5).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        chart.Left.Should().Be(0);
        chart.Top.Should().Be(45);
    }

    [Fact]
    public void NudgingAChartWithRoomToMove_DoesNotReportNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        var chart = Chart(sheet, left: 30, top: 40);

        var outcome = new NudgeChartCommand(sheet.Id, chart.Id, -5, 0).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        chart.Left.Should().Be(25);
    }

    [Fact]
    public void MovingAChartToTheSheetItIsAlreadyOn_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        var chart = Chart(sheet, left: 10, top: 10);

        new MoveChartCommand(sheet.Id, chart.Id, sheet.Id).Apply(ctx).IsNoOp.Should().BeTrue();

        sheet.Charts.Should().HaveCount(1);
    }

    [Fact]
    public void MovingAChartToAnotherSheet_DoesNotReportNoOp()
    {
        var (workbook, sheet, ctx) = Fixture();
        var chart = Chart(sheet, left: 10, top: 10);
        var other = workbook.AddSheet("Sheet2");

        var outcome = new MoveChartCommand(sheet.Id, chart.Id, other.Id).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.Charts.Should().BeEmpty();
        other.Charts.Should().HaveCount(1);
    }
}
