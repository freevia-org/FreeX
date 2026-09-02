using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r216: page breaks. Apply SORTS the incoming breaks before writing them, so the comparison is
/// against the sorted input -- the same breaks supplied in a different order really are no change,
/// and comparing against the raw input would have called that a real edit.
/// </summary>
public sealed class R216_PageBreaksNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (sheet, new TestCommandContext(workbook));
    }

    [Fact]
    public void ReApplyingTheSheetsOwnBreaks_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        sheet.RowPageBreaks.Add(10);
        sheet.RowPageBreaks.Add(20);
        sheet.ColumnPageBreaks.Add(5);

        new SetPageBreaksCommand(sheet.Id, [10, 20], [5]).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void SupplyingTheSameBreaksOutOfOrder_ReportsNoOp()
    {
        // Apply sorts before writing, so order in the request carries no meaning.
        var (sheet, ctx) = Fixture();
        sheet.RowPageBreaks.Add(10);
        sheet.RowPageBreaks.Add(20);

        new SetPageBreaksCommand(sheet.Id, [20, 10], []).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void AddingABreak_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        sheet.RowPageBreaks.Add(10);

        var outcome = new SetPageBreaksCommand(sheet.Id, [10, 30], []).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.RowPageBreaks.Should().Equal(10u, 30u);
    }

    [Fact]
    public void ClearingAllBreaksWhenThereAreNone_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();

        new SetPageBreaksCommand(sheet.Id, [], []).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ClearingExistingBreaks_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        sheet.ColumnPageBreaks.Add(5);

        var outcome = new SetPageBreaksCommand(sheet.Id, [], []).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.ColumnPageBreaks.Should().BeEmpty();
    }
}
