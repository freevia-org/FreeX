using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PasteSpecialCommandTests
{
    [Fact]
    public void PasteSpecialCellsCommand_CrossSheetAddOperationWritesHiddenDestinationRow()
    {
        var wb = new Workbook("test");
        var sourceSheet = wb.AddSheet("Source");
        var targetSheet = wb.AddSheet("Target");
        var ctx = new TestCommandContext(wb);
        var sourceRange = new GridRange(
            new CellAddress(sourceSheet.Id, 1, 1),
            new CellAddress(sourceSheet.Id, 2, 1));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 1), new NumberValue(5));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 2, 1), new NumberValue(7));
        var destinationStart = new CellAddress(targetSheet.Id, 5, 2);
        targetSheet.SetCell(destinationStart, new NumberValue(10));
        targetSheet.SetCell(new CellAddress(targetSheet.Id, 6, 2), new NumberValue(20));
        targetSheet.HiddenRows.Add(6);

        var command = new PasteSpecialCellsCommand(
            targetSheet.Id,
            sourceRange,
            CaptureCells(sourceSheet, sourceRange),
            destinationStart,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        command.Apply(ctx).Success.Should().BeTrue();

        targetSheet.GetValue(destinationStart).Should().Be(new NumberValue(15));
        targetSheet.GetValue(new CellAddress(targetSheet.Id, 6, 2)).Should().Be(new NumberValue(27));
        targetSheet.HiddenRows.Should().Contain(6);
    }

    [Fact]
    public void PasteCommandFactory_CrossSheetFormatsModeAppliesStyleToHiddenDestinationColumn()
    {
        var wb = new Workbook("test");
        var sourceSheet = wb.AddSheet("Source");
        var targetSheet = wb.AddSheet("Target");
        var ctx = new TestCommandContext(wb);
        var sourceStyle = wb.RegisterStyle(new CellStyle { Bold = true, NumberFormat = "$#,##0" });
        var targetStyle = wb.RegisterStyle(new CellStyle { Italic = true });
        var source = new CellAddress(sourceSheet.Id, 1, 1);
        var destination = new CellAddress(targetSheet.Id, 4, 4);
        var sourceCell = Cell.FromValue(new NumberValue(42));
        sourceCell.StyleId = sourceStyle;
        sourceSheet.SetCell(source, sourceCell);
        var destinationCell = Cell.FromValue(new TextValue("keep"));
        destinationCell.StyleId = targetStyle;
        targetSheet.SetCell(destination, destinationCell);
        targetSheet.HiddenCols.Add(4);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            targetSheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.Formats,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = targetSheet.GetCell(destination)!;
        pasted.Value.Should().Be(new TextValue("keep"));
        pasted.StyleId.Should().Be(sourceStyle);
        targetSheet.HiddenCols.Should().Contain(4);
    }

    private static List<(CellAddress Address, Cell Cell)> CaptureCells(Sheet sheet, GridRange range) =>
        range
            .AllCells()
            .Select(address => (address, sheet.GetCell(address)?.Clone() ?? Cell.FromValue(BlankValue.Instance)))
            .ToList();
}
