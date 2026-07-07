using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for cleanup batch MED11 finding P42: Paste Special arithmetic operations
/// (Add/Subtract/Multiply/Divide) must match Excel by leaving non-numeric destination cells (text)
/// completely unchanged instead of overwriting them with #VALUE!, and must leave a destination cell
/// that is blank in both source and destination untouched instead of materializing a literal 0.
/// </summary>
public sealed class FreeXCleanupMED11Tests
{
    [Fact]
    public void PasteSpecialCellsCommand_MultiplyOperation_LeavesTextDestinationCellUnchanged()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var textDest = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(textDest, new TextValue("Header"));

        var source = new[]
        {
            (new CellAddress(sheet.Id, 5, 5), Cell.FromValue(new NumberValue(2)))
        };

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 5, 5), new CellAddress(sheet.Id, 5, 5)),
            source,
            textDest,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Multiply));

        command.Apply(ctx).Success.Should().BeTrue();

        // Excel leaves a text cell untouched by an arithmetic Paste Special operation; it must not
        // become #VALUE!.
        sheet.GetValue(textDest).Should().Be(new TextValue("Header"));
    }

    [Fact]
    public void PasteSpecialCellsCommand_AddOperation_LeavesBlankDestinationBlankWhenSourceAlsoBlank()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var blankDest = new CellAddress(sheet.Id, 2, 2);

        var source = new[]
        {
            (new CellAddress(sheet.Id, 9, 9), Cell.FromValue(BlankValue.Instance))
        };

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 9, 9), new CellAddress(sheet.Id, 9, 9)),
            source,
            blankDest,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add, SkipBlanks: false));

        command.Apply(ctx).Success.Should().BeTrue();

        // Excel leaves the destination blank rather than writing a literal 0 when both source and
        // destination are blank.
        sheet.GetCell(blankDest).Should().BeNull();
    }

    [Fact]
    public void PasteSpecialCellsCommand_AddOperation_StillCombinesBlankDestinationWithNumericSource()
    {
        // Guard against an over-broad fix: a blank destination combined with a *numeric* source
        // must still behave like Excel (blank treated as 0), producing the source's value.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var blankDest = new CellAddress(sheet.Id, 3, 3);

        var source = new[]
        {
            (new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(7)))
        };

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            source,
            blankDest,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(blankDest).Should().Be(new NumberValue(7));
    }
}
