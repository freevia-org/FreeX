using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R175-auditB-F1 regression: <see cref="SubtotalCommand.Apply"/> loops over its detected groups
/// (<c>ApplyInsertAndEdit</c>, called from <c>ApplyInsertions</c>) calling <c>insert.Apply(ctx)</c>
/// then <c>edit.Apply(ctx)</c> for each one, adding each to <c>_appliedCommands</c> only after a
/// SUCCESSFUL return -- the identical shape this round already fixed in CompositeWorkbookCommand,
/// ApplyStructuredTableStyleCommand, and StructuredTableEditEffects. Before this round's fix, an
/// <c>edit</c> (an <see cref="EditCellsCommand"/>) that had already written its label/formula cells
/// directly to the sheet and THEN threw (rather than merely returning a failed CommandOutcome) was
/// never added to <c>_appliedCommands</c>, so its own already-written cells were never reverted.
/// This test forces the first group's <c>edit.Apply(ctx)</c> to throw AFTER it has already written
/// those cells (by rigging a harmless nested sheet-fetch inside its own N33/N34 effects pass to
/// throw), and asserts the row is back to blank rather than left holding "East Total"/the SUBTOTAL
/// formula with no undo entry to remove it.
/// </summary>
public sealed class R175_SubtotalCommandChildThrowRollbackTests
{
    /// <summary>
    /// Delegates every call to the real workbook except the Nth call to <see cref="GetSheet"/>
    /// (1-based, counting across the whole Apply call chain), which throws instead -- simulating a
    /// real nested command throwing after an earlier step in the same chain already mutated the
    /// sheet.
    /// </summary>
    private sealed class ThrowOnNthGetSheetCommandContext(Workbook workbook, int throwOnCall) : ICommandContext
    {
        private int _calls;

        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId)
        {
            _calls++;
            if (_calls == throwOnCall)
                throw new InvalidOperationException($"boom mid-subtotal (GetSheet call #{_calls})");

            return Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
        }
    }

    private static (Workbook Wb, Sheet Sheet, GridRange Range) BuildEastWestSalesData()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(25));
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));
        return (wb, sheet, range);
    }

    [Fact]
    public void Apply_RevertsFirstGroupEditWhenItThrowsAfterWritingItsCells()
    {
        // Same East/West dataset SubtotalCommandTests.SubtotalCommand_InsertsGroupAndGrandTotalRows
        // uses, so the happy-path shape (2 groups + grand total) is already well established.
        var (wb, sheet, range) = BuildEastWestSalesData();

        // GetSheet call sequence for this Apply attempt (first group = "East", the first row
        // SubtotalPlanBuilder's plan processes) -- determined empirically by sweeping throwOnCall
        // and observing when row 4's content transitions (see the diagnostic method this regression
        // was authored against): SubtotalCommand.Apply's own top fetch is call #1; InsertRowsCommand
        // itself performs SEVERAL internal sheet fetches while shifting named ranges/hyperlinks/
        // tables/filters down (calls #2-#8); call #9 is SubtotalCommand's own "mark this row as a
        // real subtotal row" fetch; call #10 is EditCellsCommand.Apply's own top fetch, immediately
        // after which it writes the "East Total" label and SUBTOTAL formula DIRECTLY to row 4; call
        // #11 is StructuredTableEditEffects.Apply's per-edit fetch for the label address, called
        // from INSIDE that same EditCellsCommand.Apply AFTER its direct cell writes already
        // completed -- THROW HERE, so `edit` has already mutated the sheet by the time it "fails".
        var ctx = new ThrowOnNthGetSheetCommandContext(wb, throwOnCall: 11);
        var command = new SubtotalCommand(sheet.Id, range, groupByColumnOffset: 0, subtotalColumnOffset: 1);

        var act = () => command.Apply(ctx);

        act.Should().Throw<InvalidOperationException>().WithMessage("*boom mid-subtotal*");

        // The core regression assertion: the East-total EditCellsCommand's own partial mutation
        // (the "East Total" label and its SUBTOTAL formula, written directly to row 4 before the
        // throw) must already be rolled back to the row's state right after InsertRowsCommand
        // inserted it (blank) -- not left permanently in the sheet because `edit` was never added
        // to `_appliedCommands`. The insert itself is untouched at this point (undoing IT is
        // SubtotalCommand.Revert's job below, not this fix's) -- row 4 is the still-blank inserted
        // row, not yet shifted back to "West".
        sheet.GetValue(4, 1).Should().Be(
            BlankValue.Instance,
            "the East-total edit's own partial mutation (the label) must be reverted when it throws mid-Apply, leaving the freshly-inserted row blank rather than half-written");
        sheet.GetCell(4, 2).Should().BeNull("the East-total SUBTOTAL formula must be reverted, not left half-applied");

        // Full-cycle check, mirroring what CommandBus.Execute does after an Apply throws: revert
        // the whole SubtotalCommand and confirm the model is back to its EXACT pre-Apply state
        // (not just the one edit this test targeted).
        command.Revert(ctx);

        sheet.GetValue(2, 1).Should().Be(new TextValue("East"));
        sheet.GetValue(2, 2).Should().Be(new NumberValue(10));
        sheet.GetValue(3, 1).Should().Be(new TextValue("East"));
        sheet.GetValue(3, 2).Should().Be(new NumberValue(15));
        sheet.GetValue(4, 1).Should().Be(new TextValue("West"));
        sheet.GetValue(4, 2).Should().Be(new NumberValue(20));
        sheet.GetValue(5, 1).Should().Be(new TextValue("West"));
        sheet.GetValue(5, 2).Should().Be(new NumberValue(25));
        sheet.GetCell(6, 1).Should().BeNull("no extra row must survive a fully-reverted Subtotal");
        sheet.GetCell(7, 1).Should().BeNull();
        sheet.GetCell(8, 1).Should().BeNull();
    }

    [Fact]
    public void Apply_StillInsertsBothGroupsAndGrandTotalWhenNoChildThrows()
    {
        // Sibling no-regression: the ordinary, fully-successful multi-group Subtotal must still
        // apply completely -- the new try/catch must not interfere with the happy path this
        // round's fix sits inside of. Mirrors SubtotalCommandTests' own happy-path assertions.
        var (wb, sheet, range) = BuildEastWestSalesData();
        var ctx = new TestCommandContext(wb);
        var command = new SubtotalCommand(sheet.Id, range, groupByColumnOffset: 0, subtotalColumnOffset: 1);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(4, 1).Should().Be(new TextValue("East Total"));
        sheet.GetCell(4, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B2:B3)");
        sheet.GetValue(7, 1).Should().Be(new TextValue("West Total"));
        sheet.GetCell(7, 2)!.FormulaText.Should().Be("SUBTOTAL(9,B5:B6)");
        sheet.GetValue(8, 1).Should().Be(new TextValue("Grand Total"));

        command.Revert(ctx);

        sheet.GetValue(4, 1).Should().Be(new TextValue("West"));
        sheet.GetCell(6, 1).Should().BeNull();
    }
}
