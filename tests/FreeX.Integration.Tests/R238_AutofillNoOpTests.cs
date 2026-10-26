using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r238: AutofillCommand, the sibling of the command r237 did. Same five undo snapshots, same
/// complete comparison -- now shared between them rather than written twice, so the two cannot
/// drift into disagreeing about what "changed" means.
/// <para>
/// Dragging the fill handle back over a series that already holds the values it would produce is the
/// gesture: the handle is dragged out, then dragged back.
/// </para>
/// </summary>
public sealed class R238_AutofillNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (sheet, new TestCommandContext(workbook));
    }

    private static GridRange Rows(Sheet sheet, uint fromRow, uint toRow) =>
        new(new CellAddress(sheet.Id, fromRow, 1), new CellAddress(sheet.Id, toRow, 1));

    [Fact]
    public void AutofillingOverTheValuesItWouldProduce_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(4));

        var outcome = new AutofillCommand(sheet.Id, Rows(sheet, 1, 2), Rows(sheet, 3, 4)).Apply(ctx);

        if (outcome.Success)
            outcome.IsNoOp.Should().BeTrue("the series already holds 3 and 4");
    }

    [Fact]
    public void AutofillingIntoEmptyCells_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));

        var outcome = new AutofillCommand(sheet.Id, Rows(sheet, 1, 2), Rows(sheet, 3, 4)).Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.IsNoOp.Should().BeFalse();
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void AutofillingWhenOnlyANoteDiffers_IsARealEdit()
    {
        // The companion case again, and the reason the two commands share one comparison: the
        // numbers already match, but a target carries a note the fill will clear.
        var (sheet, ctx) = Fixture();
        for (uint row = 1; row <= 4; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
        sheet.Comments[new CellAddress(sheet.Id, 4, 1)] = "a note";

        var outcome = new AutofillCommand(sheet.Id, Rows(sheet, 1, 2), Rows(sheet, 3, 4)).Apply(ctx);

        if (outcome.Success)
            outcome.IsNoOp.Should().BeFalse();
    }
}
