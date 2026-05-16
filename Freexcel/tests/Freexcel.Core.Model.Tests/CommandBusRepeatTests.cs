using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class CommandBusRepeatTests
{
    [Fact]
    public void RepeatLast_ReplaysLastRepeatableCommandWithFreshCommandInstance()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var bus = new CommandBus(_ => new SimpleCommandContext(workbook));
        var target = new CellAddress(sheet.Id, 1, 1);

        bus.ExecuteRepeatable(workbook.Id, () => new ApplyStyleCommand(
            sheet.Id,
            new GridRange(target, target),
            new StyleDiff(Bold: true))).Success.Should().BeTrue();

        target = new CellAddress(sheet.Id, 2, 1);

        bus.RepeatLast(workbook.Id).Success.Should().BeTrue();

        // Both cells are empty but styled — they live in the style-only dictionary, not the cell dictionary
        workbook.GetStyle(sheet.GetStyleOnly(1, 1)!.Value).Bold.Should().BeTrue();
        workbook.GetStyle(sheet.GetStyleOnly(2, 1)!.Value).Bold.Should().BeTrue();

        bus.Undo(workbook.Id).Success.Should().BeTrue();

        // After undoing only the repeated command, (1,1) still has its style-only entry
        workbook.GetStyle(sheet.GetStyleOnly(1, 1)!.Value).Bold.Should().BeTrue();
        // (2,1) was reverted entirely — no cell and no style-only entry
        sheet.GetCell(new CellAddress(sheet.Id, 2, 1)).Should().BeNull();
        sheet.GetStyleOnly(2, 1).Should().BeNull();
    }

    [Fact]
    public void RepeatLast_ReturnsFailureWhenThereIsNoRepeatableCommand()
    {
        var workbook = new Workbook("test");
        var bus = new CommandBus(_ => new SimpleCommandContext(workbook));

        bus.CanRepeat(workbook.Id).Should().BeFalse();
        bus.RepeatLast(workbook.Id).Should().Be(new CommandOutcome(false, "Nothing to repeat"));
    }

    [Fact]
    public void RepeatLast_RepeatsStructuralCommandAndUndoOnlyRevertsRepeatedInstance()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var bus = new CommandBus(_ => new SimpleCommandContext(workbook));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("top")));

        bus.ExecuteRepeatable(workbook.Id, () => new InsertRowsCommand(sheet.Id, 1)).Success.Should().BeTrue();
        bus.RepeatLast(workbook.Id).Success.Should().BeTrue();

        sheet.GetCell(new CellAddress(sheet.Id, 3, 1))!.Value.Should().Be(new TextValue("top"));

        bus.Undo(workbook.Id).Success.Should().BeTrue();

        sheet.GetCell(new CellAddress(sheet.Id, 2, 1))!.Value.Should().Be(new TextValue("top"));
        sheet.GetCell(new CellAddress(sheet.Id, 3, 1)).Should().BeNull();
    }

    private sealed class SimpleCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook => workbook;
        public Sheet GetSheet(SheetId sheetId) => workbook.GetSheet(sheetId)!;
    }
}
