using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class WorksheetViewCommandTests
{
    [Fact]
    public void SetWorksheetOutlineSymbolsCommand_SetsValueAndUndoRestoresNullDefault()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var command = new SetWorksheetOutlineSymbolsCommand(sheet.Id, showOutlineSymbols: false);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.ShowOutlineSymbols.Should().BeFalse();

        command.Revert(ctx);

        sheet.ShowOutlineSymbols.Should().BeNull();
    }

    [Fact]
    public void SetWorksheetOutlineSymbolsCommand_UndoRestoresExplicitPreviousValue()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.ShowOutlineSymbols = false;

        var command = new SetWorksheetOutlineSymbolsCommand(sheet.Id, showOutlineSymbols: true);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.ShowOutlineSymbols.Should().BeTrue();

        command.Revert(ctx);

        sheet.ShowOutlineSymbols.Should().BeFalse();
    }
}
