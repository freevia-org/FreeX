using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r232: the last four fixes of the FreeX sweep -- the round that empties the never-examined list.
/// <para>
/// AllowEditRangeCommand is the fifth instance of the shape r226 went looking for: it already knew,
/// via its own <c>Contains</c> check, that it had nothing to add, and returned a plain success.
/// The two Group commands follow r223's technique in the same files -- every mutation is captured
/// for Revert, so comparing the live outline state against those snapshots says exactly whether the
/// group moved.
/// </para>
/// </summary>
public sealed class R232_LastSweepNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (sheet, new TestCommandContext(workbook));
    }

    private static GridRange Range(Sheet sheet) =>
        new(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3));

    [Fact]
    public void AddingAnAllowEditRangeThatIsAlreadyThere_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        new AllowEditRangeCommand(sheet.Id, Range(sheet)).Apply(ctx)
            .IsNoOp.Should().BeFalse("the first add is a real edit");

        new AllowEditRangeCommand(sheet.Id, Range(sheet)).Apply(ctx)
            .IsNoOp.Should().BeTrue();
        sheet.AllowEditRanges.Should().HaveCount(1);
    }

    [Fact]
    public void UngroupingRowsThatCarryNoOutlineLevel_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();

        new GroupRowsCommand(sheet.Id, 2, 4, level: 0).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void GroupingRowsThatAreNotGrouped_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();

        var outcome = new GroupRowsCommand(sheet.Id, 2, 4, level: 1).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.RowOutlineLevels[3].Should().Be(1);
    }

    [Fact]
    public void UngroupingColumnsThatCarryNoOutlineLevel_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();

        new GroupColumnsCommand(sheet.Id, 2, 4, level: 0).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void GoalSeekLandingOnTheValueTheCellAlreadyHolds_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(address, new NumberValue(42));

        new GoalSeekCommand(address, 42).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void GoalSeekWritingADifferentValue_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(address, new NumberValue(42));

        var outcome = new GoalSeekCommand(address, 43).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.GetValue(2, 2).Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(43);
    }

    [Fact]
    public void GoalSeekOverATextCell_IsARealEdit()
    {
        // The clause that keeps the guard from over-reporting: the cell holds text, not a number,
        // so writing the number is a change even though no number comparison can be made.
        var (sheet, ctx) = Fixture();
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(address, new TextValue("42"));

        new GoalSeekCommand(address, 42).Apply(ctx).IsNoOp.Should().BeFalse();
    }
}
