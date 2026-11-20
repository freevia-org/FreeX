using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Editing;

public sealed class ShiftDragInsertPlannerTests
{
    [Fact]
    public void VerticalDrag_InsertsMovedCellsAndShiftsExistingCellsDown()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new TextValue("old")));

        var source = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var target = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 3, 1));
        var command = ShiftDragInsertPlanner.CreateCommand(workbook, sheet.Id, source, target)!;

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(new CellAddress(sheet.Id, 3, 1)).Should().Be(new NumberValue(1));
        sheet.GetValue(new CellAddress(sheet.Id, 4, 1)).Should().Be(new TextValue("old"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void HorizontalDrag_ShiftsExistingCellsRight()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new TextValue("old")));

        var source = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var target = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 1, 3));
        ShiftDragInsertPlanner.ChooseShift(source, target).Should().Be(KeyboardInsertDeleteDialogChoice.ShiftRight);

        ShiftDragInsertPlanner.CreateCommand(workbook, sheet.Id, source, target)!.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(new NumberValue(1));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 4)).Should().Be(new TextValue("old"));
    }

    [Fact]
    public void SameRange_ReturnsNoCommand()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        ShiftDragInsertPlanner.CreateCommand(workbook, sheet.Id, range, range).Should().BeNull();
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
