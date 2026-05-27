using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using Xunit;

namespace Freexcel.Core.Model.Tests;

public sealed class RemoveDuplicateRowsCommandTests
{
    [Fact]
    public void RemoveDuplicateRowsCommand_RemovesDuplicateRowsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("C"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(3));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 2));
        var command = new RemoveDuplicateRowsCommand(sheet.Id, range);

        command.Apply(ctx).Success.Should().BeTrue();

        command.RemovedRowCount.Should().Be(1);
        sheet.GetValue(1, 1).Should().Be(new TextValue("A"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("B"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("C"));

        command.Revert(ctx);

        sheet.GetValue(1, 1).Should().Be(new TextValue("A"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("B"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("A"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("C"));
    }

    [Fact]
    public void RemoveDuplicateRowsCommand_UsesSelectedColumnOffsetsOnly()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Ada"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Ben"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Ada"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(10));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));
        var command = new RemoveDuplicateRowsCommand(sheet.Id, range, [0u, 2u]);

        command.Apply(ctx).Success.Should().BeTrue();

        command.RemovedRowCount.Should().Be(1);
        sheet.GetValue(1, 1).Should().Be(new TextValue("North"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("Ada"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("South"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("Ada"));
        sheet.GetValue(3, 1).Should().BeOfType<BlankValue>();

        command.Revert(ctx);

        sheet.GetValue(1, 2).Should().Be(new TextValue("Ada"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("Ben"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("Ada"));
    }

    [Fact]
    public void RemoveDuplicateRowsCommand_RejectsProtectedSheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.IsProtected = true;
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));

        var outcome = new RemoveDuplicateRowsCommand(sheet.Id, range).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.GetValue(1, 1).Should().Be(new TextValue("A"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("A"));
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
