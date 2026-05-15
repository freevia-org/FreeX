using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class GroupedEditCellsCommandTests
{
    [Fact]
    public void Apply_WritesSameAddressesAcrossGroupedSheetsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var sheet3 = wb.AddSheet("Sheet3");
        var ctx = new SimpleCtx(wb);

        var a1 = new CellAddress(sheet1.Id, 1, 1);
        sheet1.SetCell(a1, Cell.FromValue(new TextValue("old1")));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), Cell.FromValue(new TextValue("old2")));

        var command = new GroupedEditCellsCommand(
            [sheet1.Id, sheet2.Id, sheet3.Id],
            sheet1.Id,
            [(a1, Cell.FromValue(new TextValue("new")))]);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet1.GetValue(new CellAddress(sheet1.Id, 1, 1)).Should().Be(new TextValue("new"));
        sheet2.GetValue(new CellAddress(sheet2.Id, 1, 1)).Should().Be(new TextValue("new"));
        sheet3.GetValue(new CellAddress(sheet3.Id, 1, 1)).Should().Be(new TextValue("new"));
        outcome.AffectedCells.Should().BeEquivalentTo([
            new CellAddress(sheet1.Id, 1, 1),
            new CellAddress(sheet2.Id, 1, 1),
            new CellAddress(sheet3.Id, 1, 1)
        ]);

        command.Revert(ctx);

        sheet1.GetValue(new CellAddress(sheet1.Id, 1, 1)).Should().Be(new TextValue("old1"));
        sheet2.GetValue(new CellAddress(sheet2.Id, 1, 1)).Should().Be(new TextValue("old2"));
        sheet3.GetCell(new CellAddress(sheet3.Id, 1, 1)).Should().BeNull();
    }

    [Fact]
    public void Apply_PreservesExistingStyleOnEachGroupedSheet()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new SimpleCtx(wb);
        var bold = wb.RegisterStyle(new CellStyle { Bold = true });
        var italic = wb.RegisterStyle(new CellStyle { Italic = true });
        var source = new CellAddress(sheet1.Id, 2, 3);
        var target = new CellAddress(sheet2.Id, 2, 3);
        var sourceOld = WithStyle(Cell.FromValue(new NumberValue(1)), bold);
        var targetOld = WithStyle(Cell.FromValue(new NumberValue(2)), italic);
        sheet1.SetCell(source, sourceOld);
        sheet2.SetCell(target, targetOld);

        var command = new GroupedEditCellsCommand(
            [sheet1.Id, sheet2.Id],
            sheet1.Id,
            [(source, Cell.FromValue(new NumberValue(42)))]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet1.GetCell(source)!.StyleId.Should().Be(bold);
        sheet2.GetCell(target)!.StyleId.Should().Be(italic);
        sheet1.GetValue(source).Should().Be(new NumberValue(42));
        sheet2.GetValue(target).Should().Be(new NumberValue(42));

        static Cell WithStyle(Cell cell, StyleId style)
        {
            cell.StyleId = style;
            return cell;
        }
    }

    [Fact]
    public void Apply_RejectsProtectedGroupedTargetBeforeChangingAnySheet()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new SimpleCtx(wb);
        var source = new CellAddress(sheet1.Id, 1, 1);
        var target = new CellAddress(sheet2.Id, 1, 1);
        sheet1.SetCell(source, Cell.FromValue(new TextValue("old1")));
        sheet2.SetCell(target, Cell.FromValue(new TextValue("old2")));
        sheet2.IsProtected = true;

        var command = new GroupedEditCellsCommand(
            [sheet1.Id, sheet2.Id],
            sheet1.Id,
            [(source, Cell.FromValue(new TextValue("new")))]);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet1.GetValue(source).Should().Be(new TextValue("old1"));
        sheet2.GetValue(target).Should().Be(new TextValue("old2"));
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
