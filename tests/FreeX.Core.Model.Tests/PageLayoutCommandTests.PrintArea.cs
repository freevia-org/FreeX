using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

public sealed partial class PageLayoutCommandTests
{
    [Fact]
    public void SetPrintAreaCommand_SetsPrintAreaAndUndoRestoresPreviousArea()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var previous = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));
        var next = new GridRange(
            new CellAddress(sheet.Id, 3, 3),
            new CellAddress(sheet.Id, 4, 4));
        sheet.PrintArea = previous;

        var command = new SetPrintAreaCommand(sheet.Id, next);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PrintArea.Should().Be(next);

        command.Revert(ctx);

        sheet.PrintArea.Should().Be(previous);
    }

    [Fact]
    public void ClearPrintAreaCommand_ClearsPrintAreaAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var previous = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));
        sheet.PrintArea = previous;

        var command = new ClearPrintAreaCommand(sheet.Id);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PrintArea.Should().BeNull();

        command.Revert(ctx);

        sheet.PrintArea.Should().Be(previous);
    }
}
