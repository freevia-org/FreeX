using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class InsertCopiedCellsPlannerTests
{
    [Fact]
    public void CreateCommand_ShiftDownInsertsRoomAndPastesCopiedCells()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new SimpleCtx(workbook);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(source, Cell.FromValue(new TextValue("copied")));
        sheet.SetCell(destination, Cell.FromValue(new TextValue("old")));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromValue(new TextValue("below")));

        var command = InsertCopiedCellsPlanner.CreateCommand(
            workbook,
            sheet.Id,
            new GridRange(source, source),
            [(source, sheet.GetCell(source)!.Clone())],
            new GridRange(destination, destination),
            KeyboardInsertDeleteDialogChoice.ShiftDown);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(destination).Should().Be(new TextValue("copied"));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 2)).Should().Be(new TextValue("old"));
        sheet.GetValue(new CellAddress(sheet.Id, 4, 2)).Should().Be(new TextValue("below"));
    }

    [Fact]
    public void CreateCommand_ShiftRightInsertsRoomAndPastesCopiedCells()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new SimpleCtx(workbook);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(source, Cell.FromValue(new TextValue("copied")));
        sheet.SetCell(destination, Cell.FromValue(new TextValue("old")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), Cell.FromValue(new TextValue("right")));

        var command = InsertCopiedCellsPlanner.CreateCommand(
            workbook,
            sheet.Id,
            new GridRange(source, source),
            [(source, sheet.GetCell(source)!.Clone())],
            new GridRange(destination, destination),
            KeyboardInsertDeleteDialogChoice.ShiftRight);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(destination).Should().Be(new TextValue("copied"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 3)).Should().Be(new TextValue("old"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 4)).Should().Be(new TextValue("right"));
    }

    private sealed class SimpleCtx(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook => workbook;

        public Sheet GetSheet(SheetId id) => workbook.GetSheet(id) ?? throw new InvalidOperationException();
    }
}
