using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

public class FreezePanesCommandTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new SimpleCtx(wb));
    }

    [Fact]
    public void SetFreezePanesCommand_SetsFrozenRowsAndColumnsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.FrozenRows = 1;
        sheet.FrozenCols = 0;

        var cmd = new SetFreezePanesCommand(sheet.Id, frozenRows: 3, frozenCols: 2);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.FrozenRows.Should().Be(3);
        sheet.FrozenCols.Should().Be(2);

        cmd.Revert(ctx);

        sheet.FrozenRows.Should().Be(1);
        sheet.FrozenCols.Should().Be(0);
    }

    [Fact]
    public void SetFreezePanesCommand_RejectsOutOfBoundsFreezeCounts()
    {
        var (_, sheet, ctx) = Setup();

        var outcome = new SetFreezePanesCommand(
            sheet.Id,
            frozenRows: CellAddress.MaxRow,
            frozenCols: 0).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("outside");
        sheet.FrozenRows.Should().Be(0);
        sheet.FrozenCols.Should().Be(0);
    }

    [Fact]
    public void SetSplitPanesCommand_SetsSplitClearsFreezeAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.FrozenRows = 1;
        sheet.FrozenCols = 2;
        var ctx = new SimpleCtx(wb);

        var command = new SetSplitPanesCommand(sheet.Id, splitRow: 5, splitColumn: 3);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.SplitRow.Should().Be(5);
        sheet.SplitColumn.Should().Be(3);
        sheet.FrozenRows.Should().Be(0);
        sheet.FrozenCols.Should().Be(0);

        command.Revert(ctx);

        sheet.SplitRow.Should().BeNull();
        sheet.SplitColumn.Should().BeNull();
        sheet.FrozenRows.Should().Be(1);
        sheet.FrozenCols.Should().Be(2);
    }

    [Fact]
    public void SetSplitPanesCommand_RejectsOutOfBoundsSplit()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);

        var outcome = new SetSplitPanesCommand(sheet.Id, 0, CellAddress.MaxCol + 1).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.SplitRow.Should().BeNull();
        sheet.SplitColumn.Should().BeNull();
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
