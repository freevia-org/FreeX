using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Editing;

/// <summary>
/// R54-meta-1: CutMoveFollowUpCommand (the last command in the "Insert Cut Cells" composite) must
/// match live workbook state -- other formulas' current text, CF/DV rule ranges, merges, hyperlinks --
/// against wherever the PRECEDING insert command actually left the cut source range, not against the
/// stale pre-insert coordinates captured when the composite was built. An EntireRow/EntireColumn (or
/// band-scoped Shift Down/Right) insert whose shift band overlaps the cut source rewrites every
/// workbook formula referencing that region as an ordinary side effect of the insert, so by the time
/// this follow-up runs, any external formula that pointed at the cut cell already reads the
/// insert-shifted address -- and must be matched/repointed against THAT address, not the original one.
/// </summary>
public sealed class InsertCopiedCellsPlannerPostInsertShiftTests
{
    [Fact]
    public void CreateCommand_Cut_EntireRowInsertShiftsSourceBand_ExternalReferenceStillFollowsTheMove()
    {
        // A20 = 99 (the cell being cut); C1 = "=A20" (an external reference TO the cut cell).
        // Insert Cut Cells > Entire Row at A5: the insert shifts every row >= 5 (including row 20,
        // where the cut cells still physically sit until this follow-up runs) down by 1 row, so the
        // insert itself rewrites C1's formula from "A20" to "A21" as an ordinary side effect -- before
        // this follow-up ever runs.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var a20 = new CellAddress(sheet.Id, 20, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a20, Cell.FromValue(new NumberValue(99)));
        var c1Formula = Cell.FromFormula("A20");
        c1Formula.Value = new NumberValue(99);
        sheet.SetCell(c1, c1Formula);

        var source = new GridRange(a20, a20);
        var cells = new[] { (a20, sheet.GetCell(a20)!.Clone()) };

        var a5 = new CellAddress(sheet.Id, 5, 1);
        var destination = new GridRange(a5, a5);

        var command = InsertCopiedCellsPlanner.CreateCommand(
            workbook, sheet.Id, source, cells, destination,
            KeyboardInsertDeleteDialogChoice.EntireRow, isCut: true);

        command.Apply(ctx).Success.Should().BeTrue();

        // The cut value itself must land at the chosen destination.
        sheet.GetValue(a5).Should().Be(new NumberValue(99));

        // C1's reference must follow the cut cell all the way to A5 -- the bug: matching against the
        // STALE pre-insert A20:A20 rectangle misses C1 entirely (its live text already reads "A21" by
        // the time this follow-up runs), leaving it stuck on the now-blank "A21" instead.
        sheet.GetCell(c1)!.FormulaText.Should().Be("A5");
    }

    [Fact]
    public void CreateCommand_Cut_EntireRowInsertBelowSource_ExternalReferenceStillFollowsTheMove_NoRegression()
    {
        // Sibling no-regression case: the insertion point (row 20) is BELOW the cut source (row 5), so
        // the insert's shift band does not overlap the source at all -- the source's own address is
        // never touched by the insert, exactly like the pre-existing (same-row Shift Right/Down)
        // regression tests in InsertCopiedCellsPlannerCutMoveSemanticsTests.cs. This must keep working
        // unchanged now that the follow-up computes an "effective" (possibly shifted) source range.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var a5 = new CellAddress(sheet.Id, 5, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a5, Cell.FromValue(new NumberValue(7)));
        var c1Formula = Cell.FromFormula("A5");
        c1Formula.Value = new NumberValue(7);
        sheet.SetCell(c1, c1Formula);

        var source = new GridRange(a5, a5);
        var cells = new[] { (a5, sheet.GetCell(a5)!.Clone()) };

        var a20 = new CellAddress(sheet.Id, 20, 1);
        var destination = new GridRange(a20, a20);

        var command = InsertCopiedCellsPlanner.CreateCommand(
            workbook, sheet.Id, source, cells, destination,
            KeyboardInsertDeleteDialogChoice.EntireRow, isCut: true);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(a20).Should().Be(new NumberValue(7));
        sheet.GetCell(c1)!.FormulaText.Should().Be("A20");
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
