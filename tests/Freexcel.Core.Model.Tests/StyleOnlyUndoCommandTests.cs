using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class StyleOnlyUndoCommandTests
{
    [Fact]
    public void EditCellsCommand_UndoRestoresStyleOnlyFormattingOnEmptyCell()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var address = new CellAddress(sheet.Id, 2, 3);
        var style = wb.RegisterStyle(new CellStyle { Bold = true });
        sheet.SetStyleOnly(address.Row, address.Col, style);

        var command = EditCellsCommand.ForValue(sheet.Id, address, new TextValue("typed"));

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetCell(address).Should().NotBeNull();
        sheet.GetStyleOnly(address.Row, address.Col).Should().BeNull();

        command.Revert(ctx);

        sheet.GetCell(address).Should().BeNull();
        sheet.GetStyleOnly(address.Row, address.Col).Should().Be(style);
    }

    [Fact]
    public void PasteCellsCommand_UndoRestoresStyleOnlyFormattingOnEmptyCell()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var address = new CellAddress(sheet.Id, 4, 2);
        var style = wb.RegisterStyle(new CellStyle { Italic = true });
        sheet.SetStyleOnly(address.Row, address.Col, style);

        var command = new PasteCellsCommand(sheet.Id, [(address, Cell.FromValue(new NumberValue(42)))]);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetValue(address).Should().Be(new NumberValue(42));
        sheet.GetStyleOnly(address.Row, address.Col).Should().BeNull();

        command.Revert(ctx);

        sheet.GetCell(address).Should().BeNull();
        sheet.GetStyleOnly(address.Row, address.Col).Should().Be(style);
    }

    [Fact]
    public void PasteFormatsCommand_UndoRestoresPreviousStyleOnlyFormatting()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var address = new CellAddress(sheet.Id, 6, 1);
        var oldStyle = wb.RegisterStyle(new CellStyle { Underline = true });
        var newStyle = wb.RegisterStyle(new CellStyle { Strikethrough = true });
        sheet.SetStyleOnly(address.Row, address.Col, oldStyle);

        var command = new PasteFormatsCommand(sheet.Id, [(address, newStyle)]);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetCell(address)!.StyleId.Should().Be(newStyle);
        sheet.GetStyleOnly(address.Row, address.Col).Should().BeNull();

        command.Revert(ctx);

        sheet.GetCell(address).Should().BeNull();
        sheet.GetStyleOnly(address.Row, address.Col).Should().Be(oldStyle);
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
