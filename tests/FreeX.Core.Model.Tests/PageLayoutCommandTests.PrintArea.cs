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

    [Fact]
    public void SetPrintAreaCommand_UndoRestoresBothAreasOfMultiAreaPrintArea()
    {
        // Regression: undo used to collapse the multi-area list to the first area only.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var area1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3));
        var area2 = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 5, 7));
        sheet.SetPrintAreas([area1, area2]);

        var newArea = new GridRange(new CellAddress(sheet.Id, 10, 1), new CellAddress(sheet.Id, 12, 3));
        var command = new SetPrintAreaCommand(sheet.Id, newArea);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PrintAreas.Should().HaveCount(1);

        command.Revert(ctx);

        sheet.PrintAreas.Should().HaveCount(2);
        sheet.PrintAreas.Should().ContainInOrder(area1, area2);
    }

    [Fact]
    public void ClearPrintAreaCommand_UndoRestoresBothAreasOfMultiAreaPrintArea()
    {
        // Regression: undo used to collapse the multi-area list to the first area only.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var area1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3));
        var area2 = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 5, 7));
        sheet.SetPrintAreas([area1, area2]);

        var command = new ClearPrintAreaCommand(sheet.Id);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PrintAreas.Should().BeEmpty();

        command.Revert(ctx);

        sheet.PrintAreas.Should().HaveCount(2);
        sheet.PrintAreas.Should().ContainInOrder(area1, area2);
    }
}
