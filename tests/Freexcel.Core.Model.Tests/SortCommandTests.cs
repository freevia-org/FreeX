using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class SortCommandTests
{
    [Fact]
    public void SortCommand_SupportsMultipleSortKeysAndUndoRestoresRows()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new SimpleCtx(workbook);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(15));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));

        var command = new SortCommand(sheet.Id, range, [new SortKey(0, true), new SortKey(1, false)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(1, 1).Should().Be(new TextValue("East"));
        sheet.GetValue(1, 2).Should().Be(new NumberValue(15));
        sheet.GetValue(2, 1).Should().Be(new TextValue("East"));
        sheet.GetValue(2, 2).Should().Be(new NumberValue(10));
        sheet.GetValue(3, 1).Should().Be(new TextValue("West"));
        sheet.GetValue(3, 2).Should().Be(new NumberValue(20));
        sheet.GetValue(4, 1).Should().Be(new TextValue("West"));
        sheet.GetValue(4, 2).Should().Be(new NumberValue(5));

        command.Revert(ctx);

        sheet.GetValue(1, 1).Should().Be(new TextValue("West"));
        sheet.GetValue(1, 2).Should().Be(new NumberValue(5));
        sheet.GetValue(4, 1).Should().Be(new TextValue("East"));
        sheet.GetValue(4, 2).Should().Be(new NumberValue(15));
    }

    private sealed class SimpleCtx(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;
        public Sheet GetSheet(SheetId sheetId) => Workbook.GetSheet(sheetId)!;
    }
}
