using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R43-commands-paste-special-5-1/2: two Paste Special gaps in PasteSpecialCellsCommand.
///
/// paste-special-5-1: the edit loop in PasteSpecialCellsCommand.Apply had no guard against writing
/// into a non-anchor (covered) cell of an existing destination merged region, unlike plain Ctrl+V
/// paste (PasteCellsCommand.Apply), which explicitly skips such cells. Fixed by adding the identical
/// GetMergeRegion guard.
///
/// paste-special-5-2: PasteArithmetic.ApplyOperation (the shared Add/Subtract/Multiply/Divide
/// combine used by Paste Special's "Operation") treated an error-valued operand the same as
/// non-numeric text -- a no-op that leaves the destination cell entirely unchanged. Real Excel
/// propagates errors through arithmetic, so an error operand must make the result that same error.
/// Fixed by checking for ErrorValue up front and returning it directly.
/// </summary>
public sealed class R43_PasteSpecialMergeAndErrorTests
{
    // ---- paste-special-5-1 -------------------------------------------------------------------

    [Fact]
    public void ValuesAndNumberFormats_DestinationHasExistingMerge_DoesNotWriteIntoCoveredCell()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Source A1:B1, plain unmerged cells: A1=10, B1=20.
        var sourceA1 = new CellAddress(sheet.Id, 1, 1);
        var sourceB1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(sourceA1, Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(sourceB1, Cell.FromValue(new NumberValue(20)));
        var sourceRange = new GridRange(sourceA1, sourceB1);

        // Destination C1:D1 is an existing merged region (anchor C1, covered D1), currently blank.
        var destinationAnchor = new CellAddress(sheet.Id, 1, 3);
        var destinationCovered = new CellAddress(sheet.Id, 1, 4);
        sheet.AddMergedRegion(new GridRange(destinationAnchor, destinationCovered));

        var sourceCells = new List<(CellAddress Address, Cell Cell)>
        {
            (sourceA1, sheet.GetCell(sourceA1)!.Clone()),
            (sourceB1, sheet.GetCell(sourceB1)!.Clone())
        };

        // "Values and Number Formats" is one of the named modes from the finding, and reaches the
        // unguarded PasteSpecialCellsCommand.Apply path used by every special content kind.
        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            sourceRange,
            sourceCells,
            destinationAnchor,
            new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndNumberFormats));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destinationAnchor).Should().Be(new NumberValue(10), "the merge anchor is a valid paste target");
        sheet.GetCell(destinationCovered).Should().BeNull("a covered merge member must never carry a live value, matching plain Ctrl+V paste");
        outcome.AffectedCells.Should().NotContain(destinationCovered);
    }

    [Fact]
    public void ValuesAndNumberFormats_DestinationNotMerged_WritesBothCells_NoRegression()
    {
        // Sibling case: an ordinary (unmerged) two-cell destination must still get both cells
        // written, exactly as before the merge guard was added.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var sourceA1 = new CellAddress(sheet.Id, 1, 1);
        var sourceB1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(sourceA1, Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(sourceB1, Cell.FromValue(new NumberValue(20)));
        var sourceRange = new GridRange(sourceA1, sourceB1);

        var destinationStart = new CellAddress(sheet.Id, 3, 3);
        var destinationEnd = new CellAddress(sheet.Id, 3, 4);

        var sourceCells = new List<(CellAddress Address, Cell Cell)>
        {
            (sourceA1, sheet.GetCell(sourceA1)!.Clone()),
            (sourceB1, sheet.GetCell(sourceB1)!.Clone())
        };

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            sourceRange,
            sourceCells,
            destinationStart,
            new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndNumberFormats));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destinationStart).Should().Be(new NumberValue(10));
        sheet.GetValue(destinationEnd).Should().Be(new NumberValue(20));
        outcome.AffectedCells.Should().Contain([destinationStart, destinationEnd]);
    }

    // ---- paste-special-5-2 -------------------------------------------------------------------

    [Fact]
    public void Add_SourceIsError_PropagatesErrorToDestination()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // A1 = formula =1/0 whose cached value is #DIV/0!.
        var source = new CellAddress(sheet.Id, 1, 1);
        var sourceCell = Cell.FromValue(ErrorValue.DivByZero);
        sourceCell.FormulaText = "1/0";
        sheet.SetCell(source, sourceCell);

        // B1 = plain number 5.
        var destination = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(destination, Cell.FromValue(new NumberValue(5)));

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destination).Should().Be(ErrorValue.DivByZero, "an error operand must propagate through Paste Special arithmetic like it does everywhere else in Excel");
    }

    [Fact]
    public void Add_DestinationIsError_PropagatesErrorToDestination()
    {
        // Sibling case: the error can also come from the pre-existing destination cell rather than
        // the pasted source -- that must propagate too (destination/left operand checked first).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, Cell.FromValue(new NumberValue(5)));

        var destination = new CellAddress(sheet.Id, 1, 2);
        var destinationCell = Cell.FromValue(ErrorValue.Ref);
        destinationCell.FormulaText = "#REF!";
        sheet.SetCell(destination, destinationCell);

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(source, source),
            [(source, sheet.GetCell(source)!.Clone())],
            destination,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destination).Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Add_SourceIsText_DestinationUnchanged_NoRegression()
    {
        // Sibling case: non-numeric TEXT (as opposed to an error) is still a genuine no-op that
        // leaves the destination entirely untouched -- only error semantics changed.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, Cell.FromValue(new TextValue("hello")));

        var destination = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(destination, Cell.FromValue(new NumberValue(5)));

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(source, source),
            [(source, sheet.GetCell(source)!.Clone())],
            destination,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetValue(destination).Should().Be(new NumberValue(5), "text operands stay a no-op, unlike errors");
    }
}
