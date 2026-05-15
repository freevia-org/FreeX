using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

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
        var ctx = new SimpleCtx(targetWorkbook);

        var command = new ImportSheetCommand(targetSheet.Id, destination, sourceSheet);

        command.Apply(ctx).Success.Should().BeTrue();

        targetSheet.GetValue(3, 3).Should().Be(new NumberValue(10));
        targetSheet.GetValue(4, 4).Should().Be(new TextValue("hello"));

        command.Revert(ctx);

        targetSheet.GetValue(3, 3).Should().Be(new NumberValue(999));
        targetSheet.GetCell(4, 4).Should().BeNull();
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
        var ctx = new SimpleCtx(targetWorkbook);

        var command = new ImportSheetCommand(targetSheet.Id, new CellAddress(targetSheet.Id, 1, 1), sourceSheet);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        targetSheet.GetCell(1, 1).Should().BeNull();
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
