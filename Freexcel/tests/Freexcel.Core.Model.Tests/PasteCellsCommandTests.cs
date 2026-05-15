using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using Xunit;

namespace Freexcel.Core.Model.Tests;

public sealed class PasteCellsCommandTests
{
    [Fact]
    public void PasteCellsCommand_ReplacesValueAndStyleAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var addr = new CellAddress(sheet.Id, 1, 1);

        var oldStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        var newStyle = wb.RegisterStyle(new CellStyle { Italic = true });
        var oldCell = Cell.FromValue(new TextValue("old"));
        oldCell.StyleId = oldStyle;
        sheet.SetCell(addr, oldCell);

        var pastedCell = Cell.FromValue(new TextValue("new"));
        pastedCell.StyleId = newStyle;

        var command = new PasteCellsCommand(sheet.Id, [(addr, pastedCell)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(addr).Should().Be(new TextValue("new"));
        sheet.GetCell(addr)!.StyleId.Should().Be(newStyle);

        command.Revert(ctx);

        sheet.GetValue(addr).Should().Be(new TextValue("old"));
        sheet.GetCell(addr)!.StyleId.Should().Be(oldStyle);
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
