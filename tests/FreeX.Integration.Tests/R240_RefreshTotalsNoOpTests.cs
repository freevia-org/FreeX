using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r240: RefreshStructuredTableTotalsCommand. A refresh re-derives every totals cell from the
/// current data, so refreshing a table whose data has not moved writes back exactly what is there --
/// and Refresh is a button the user can press at any time, twice in a row.
/// <para>
/// This one has a single undo snapshot, so per the r237 invariant consulting it is the whole of the
/// question. That made it the cheapest of the remaining cluster, and it is also the case that
/// exposed a flaw in the contract itself: see the round notes.
/// </para>
/// </summary>
public sealed class R240_RefreshTotalsNoOpTests
{
    private static (Sheet Sheet, StructuredTableModel Table, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Sales",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1)),
            TotalsRowShown = true,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Amount", TotalsRowFunction: "sum"));
        sheet.StructuredTables.Add(table);

        return (sheet, table, new TestCommandContext(workbook));
    }

    [Fact]
    public void RefreshingTotalsTwice_ReportsNoOpTheSecondTime()
    {
        var (sheet, table, ctx) = Fixture();

        var first = new RefreshStructuredTableTotalsCommand(sheet.Id, table.Id).Apply(ctx);
        first.Success.Should().BeTrue();

        new RefreshStructuredTableTotalsCommand(sheet.Id, table.Id).Apply(ctx)
            .IsNoOp.Should().BeTrue("the totals row already holds what the refresh would write");
    }

    [Fact]
    public void RefreshingAfterTheTotalsFunctionChanges_DoesNotReportNoOp()
    {
        // My first version of this test changed a DATA cell and expected the refresh to be a real
        // edit. It is not, and the guard was right: ResolveTotalsCell writes a SUBTOTAL FORMULA
        // derived from the column metadata and the table range, not a computed value, so the data
        // moving underneath it changes nothing this command writes -- the evaluator recalculates the
        // formula, not the refresh. Changing the totals FUNCTION does change the written formula.
        var (sheet, table, ctx) = Fixture();
        new RefreshStructuredTableTotalsCommand(sheet.Id, table.Id).Apply(ctx);

        sheet.StructuredTables[0].Columns[0] =
            sheet.StructuredTables[0].Columns[0] with { TotalsRowFunction = "average" };

        new RefreshStructuredTableTotalsCommand(sheet.Id, table.Id).Apply(ctx)
            .IsNoOp.Should().BeFalse("the totals formula the refresh writes is a different one now");
    }
}
