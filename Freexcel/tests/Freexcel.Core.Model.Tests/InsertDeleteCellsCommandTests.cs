using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class InsertDeleteCellsCommandTests
{
    [Fact]
    public void InsertCellsShiftRight_ShiftsCellsInSelectedRowsOnlyAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("B2"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));

        var command = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetValue(1, 1).Should().BeOfType<BlankValue>();
        sheet.GetValue(1, 2).Should().Be(new TextValue("A1"));
        sheet.GetValue(1, 3).Should().Be(new TextValue("B1"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("B2"));

        command.Revert(ctx);

        sheet.GetValue(1, 1).Should().Be(new TextValue("A1"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("B1"));
        sheet.GetCell(1, 3).Should().BeNull();
    }

    [Fact]
    public void InsertCellsShiftDown_ShiftsCellsInSelectedColumnsOnlyAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A2"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B1"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));

        var command = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Down);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetValue(1, 1).Should().BeOfType<BlankValue>();
        sheet.GetValue(2, 1).Should().Be(new TextValue("A1"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("A2"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("B1"));

        command.Revert(ctx);

        sheet.GetValue(1, 1).Should().Be(new TextValue("A1"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("A2"));
        sheet.GetCell(3, 1).Should().BeNull();
    }

    [Fact]
    public void InsertCellsCommand_RejectsInvalidShiftDirection()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));

        var outcome = new InsertCellsCommand(sheet.Id, range, (InsertCellsShiftDirection)99).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.GetValue(1, 1).Should().Be(new TextValue("A1"));
        sheet.GetCell(2, 1).Should().BeNull();
    }

    [Fact]
    public void InsertCellsCommand_RejectsProtectedSheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.IsProtected = true;
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));

        var outcome = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.GetValue(1, 1).Should().Be(new TextValue("A1"));
        sheet.GetCell(1, 2).Should().BeNull();
    }

    [Fact]
    public void DeleteCellsShiftLeft_ShiftsCellsInSelectedRowsOnlyAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("C1"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 1, 2));

        var command = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Left);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetValue(1, 1).Should().Be(new TextValue("A1"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("C1"));
        sheet.GetCell(1, 3).Should().BeNull();

        command.Revert(ctx);

        sheet.GetValue(1, 2).Should().Be(new TextValue("B1"));
        sheet.GetValue(1, 3).Should().Be(new TextValue("C1"));
    }

    [Fact]
    public void DeleteCellsShiftUp_ShiftsCellsInSelectedColumnsOnlyAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("A3"));
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));

        var command = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Up);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetValue(1, 1).Should().Be(new TextValue("A1"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("A3"));
        sheet.GetCell(3, 1).Should().BeNull();

        command.Revert(ctx);

        sheet.GetValue(2, 1).Should().Be(new TextValue("A2"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("A3"));
    }

    [Fact]
    public void DeleteCellsCommand_RejectsInvalidShiftDirection()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A2"));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));

        var outcome = new DeleteCellsCommand(sheet.Id, range, (DeleteCellsShiftDirection)99).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.GetValue(1, 1).Should().Be(new TextValue("A1"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("A2"));
    }

    [Fact]
    public void DeleteCellsCommand_RejectsProtectedSheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B1"));
        sheet.IsProtected = true;
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));

        var outcome = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Left).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.GetValue(1, 1).Should().Be(new TextValue("A1"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("B1"));
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
