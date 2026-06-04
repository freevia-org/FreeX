using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class ImportSheetCommandTests
{
    [Fact]
    public void ImportSheetCommand_CopiesUsedCellsToDestinationAndUndoRestores()
    {
        var targetWorkbook = new Workbook("target");
        var targetSheet = targetWorkbook.AddSheet("Sheet1");
        var sourceWorkbook = new Workbook("source");
        var sourceSheet = sourceWorkbook.AddSheet("Imported");
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 1), new NumberValue(10));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 2, 2), new TextValue("hello"));
        var destination = new CellAddress(targetSheet.Id, 3, 3);
        targetSheet.SetCell(destination, new NumberValue(999));
        var ctx = new TestCommandContext(targetWorkbook);

        var command = new ImportSheetCommand(targetSheet.Id, destination, sourceSheet);

        command.Apply(ctx).Success.Should().BeTrue();

        targetSheet.GetValue(3, 3).Should().Be(new NumberValue(10));
        targetSheet.GetValue(4, 4).Should().Be(new TextValue("hello"));

        command.Revert(ctx);

        targetSheet.GetValue(3, 3).Should().Be(new NumberValue(999));
        targetSheet.GetCell(4, 4).Should().BeNull();
    }

    [Fact]
    public void ImportSheetCommand_RejectsDestinationExtentPastWorksheetEdge()
    {
        var targetWorkbook = new Workbook("target");
        var targetSheet = targetWorkbook.AddSheet("Sheet1");
        var sourceWorkbook = new Workbook("source");
        var sourceSheet = sourceWorkbook.AddSheet("Imported");
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 1), new NumberValue(10));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 2), new NumberValue(20));
        var destination = new CellAddress(targetSheet.Id, 1, CellAddress.MaxCol);
        targetSheet.SetCell(destination, new TextValue("keep"));
        var ctx = new TestCommandContext(targetWorkbook);

        var command = new ImportSheetCommand(targetSheet.Id, destination, sourceSheet);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("bounds");
        targetSheet.GetValue(destination).Should().Be(new TextValue("keep"));
    }

    [Fact]
    public void ImportSheetCommand_UndoRestoresStyleOnlyDestination()
    {
        var targetWorkbook = new Workbook("target");
        var targetSheet = targetWorkbook.AddSheet("Sheet1");
        var styleId = targetWorkbook.RegisterStyle(new CellStyle { Italic = true });
        var destination = new CellAddress(targetSheet.Id, 3, 3);
        targetSheet.SetStyleOnly(destination.Row, destination.Col, styleId);
        var sourceWorkbook = new Workbook("source");
        var sourceSheet = sourceWorkbook.AddSheet("Imported");
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 1), new NumberValue(10));
        var ctx = new TestCommandContext(targetWorkbook);

        var command = new ImportSheetCommand(targetSheet.Id, destination, sourceSheet);

        command.Apply(ctx).Success.Should().BeTrue();
        targetSheet.GetCell(destination)!.StyleId.Should().Be(styleId);

        command.Revert(ctx);

        targetSheet.GetCell(destination).Should().BeNull();
        targetSheet.GetStyleOnly(destination.Row, destination.Col).Should().Be(styleId);
    }

    [Fact]
    public void ImportSheetCommand_RejectsImportIntoProtectedLockedCells()
    {
        var targetWorkbook = new Workbook("target");
        var targetSheet = targetWorkbook.AddSheet("Sheet1");
        var sourceWorkbook = new Workbook("source");
        var sourceSheet = sourceWorkbook.AddSheet("Imported");
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 1), new NumberValue(10));
        targetSheet.IsProtected = true;
        var ctx = new TestCommandContext(targetWorkbook);

        var command = new ImportSheetCommand(targetSheet.Id, new CellAddress(targetSheet.Id, 1, 1), sourceSheet);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        targetSheet.GetCell(1, 1).Should().BeNull();
    }

}
