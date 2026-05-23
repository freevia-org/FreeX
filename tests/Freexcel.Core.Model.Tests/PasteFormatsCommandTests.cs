using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using Xunit;

namespace Freexcel.Core.Model.Tests;

public sealed class PasteFormatsCommandTests
{
    [Fact]
    public void PasteFormatsCommand_ReplacesOnlyStyleAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var addr = new CellAddress(sheet.Id, 1, 1);

        var oldStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        var newStyle = wb.RegisterStyle(new CellStyle { Italic = true });
        var cell = Cell.FromFormula("A1+B1");
        cell.Value = new NumberValue(12);
        cell.StyleId = oldStyle;
        sheet.SetCell(addr, cell);

        var command = new PasteFormatsCommand(sheet.Id, [(addr, newStyle)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(addr)!.StyleId.Should().Be(newStyle);
        sheet.GetCell(addr)!.FormulaText.Should().Be("A1+B1");
        sheet.GetCell(addr)!.Value.Should().Be(new NumberValue(12));

        command.Revert(ctx);

        sheet.GetCell(addr)!.StyleId.Should().Be(oldStyle);
        sheet.GetCell(addr)!.FormulaText.Should().Be("A1+B1");
        sheet.GetCell(addr)!.Value.Should().Be(new NumberValue(12));
    }

    [Fact]
    public void PasteFormatsCommand_RejectsProtectedSheetWithoutFormatCellsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var addr = new CellAddress(sheet.Id, 1, 1);
        var style = wb.RegisterStyle(new CellStyle { Italic = true });
        sheet.IsProtected = true;

        var outcome = new PasteFormatsCommand(sheet.Id, [(addr, style)]).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.GetCell(addr).Should().BeNull();
    }

    [Fact]
    public void PasteFormatsCommand_AllowsProtectedSheetWithFormatCellsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var addr = new CellAddress(sheet.Id, 1, 1);
        var style = wb.RegisterStyle(new CellStyle { Italic = true });
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatCells);

        var outcome = new PasteFormatsCommand(sheet.Id, [(addr, style)]).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(addr)!.StyleId.Should().Be(style);
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
