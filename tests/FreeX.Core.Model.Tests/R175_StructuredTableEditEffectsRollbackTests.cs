using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R175-auditB-F1 regression: <see cref="StructuredTableEditEffects.Apply"/> (Commands.cs) drives
/// N33 auto-expand via <c>resizeCommand.Apply(ctx)</c> and N34 calculated-column propagation via
/// <c>propagateCommand.Apply(ctx)</c>, adding each to the caller's <c>applied</c> list only after a
/// SUCCESSFUL return. Before this round's fix, a child that instead THREW mid-mutation (not merely
/// returned a failed CommandOutcome) was never added to that list, so its own partial mutation was
/// never reverted -- the identical shape this round already fixed in CompositeWorkbookCommand and
/// ApplyStructuredTableStyleCommand. This test forces <c>ResizeStructuredTableCommand.Apply</c> to
/// throw AFTER it has already mutated <c>sheet.StructuredTables</c> (by rigging its own nested
/// totals-row refresh sub-command to throw), and asserts the table is restored to its exact
/// pre-edit range rather than left half-resized.
/// </summary>
public sealed class R175_StructuredTableEditEffectsRollbackTests
{
    /// <summary>
    /// Delegates every call to the real workbook except the Nth call to <see cref="GetSheet"/>
    /// (1-based, counting across the whole Apply call chain), which throws instead -- simulating a
    /// real nested command (here, <c>RefreshStructuredTableTotalsCommand.Apply</c>, invoked from
    /// deep inside <c>ResizeStructuredTableCommand.Apply</c>) throwing after an earlier step in the
    /// same chain already mutated the sheet.
    /// </summary>
    private sealed class ThrowOnNthGetSheetCommandContext(Workbook workbook, int throwOnCall) : ICommandContext
    {
        private int _calls;

        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId)
        {
            _calls++;
            if (_calls == throwOnCall)
                throw new InvalidOperationException($"boom mid-resize (GetSheet call #{_calls})");

            return Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
        }
    }

    private static (Workbook Wb, Sheet Sheet, StructuredTableModel Table) BuildTableWithTotalsRow()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Col1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Col2"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("a"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1));
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            // Header (row 1) + one data row (row 2) + a shown totals row (row 3).
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            HeaderRowCount = 1,
            TotalsRowShown = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Col1"),
                new StructuredTableColumnModel(2, "Col2")
            }
        };
        sheet.StructuredTables.Add(table);
        return (wb, sheet, table);
    }

    [Fact]
    public void Apply_RevertsPartiallyResizedTableWhenResizeCommandThrowsMidMutation()
    {
        // A shown totals row blocks N33's ROW auto-expand entirely ("Excel does not auto-expand
        // below one" -- StructuredTableDesignCommandHelpers.TryGetAutoExpandRange), but COLUMN
        // auto-expand is unaffected by it, and a resize that keeps TotalsRowShown true still runs
        // ResizeStructuredTableCommand's own nested RefreshStructuredTableTotalsCommand.Apply call
        // -- exactly the nested-throw opportunity this test needs. Editing C2 (one column right of
        // the table, inside its row span) triggers a column-only auto-expand.
        var (wb, sheet, table) = BuildTableWithTotalsRow();
        var originalRange = table.Range;

        // GetSheet call sequence for this Apply attempt:
        //   #1 EditCellsCommand.Apply's own sheet fetch
        //   #2 StructuredTableEditEffects.Apply's per-edit sheet fetch
        //   #3 ResizeStructuredTableCommand.Apply's own sheet fetch (succeeds; mutates
        //      sheet.StructuredTables to the grown 3-column table)
        //   #4 RefreshStructuredTableTotalsCommand.Apply's own sheet fetch (nested inside the
        //      resize, because the resized table keeps TotalsRowShown=true) -- THROW HERE, after
        //      the resize has already replaced sheet.StructuredTables[tableIndex].
        var ctx = new ThrowOnNthGetSheetCommandContext(wb, throwOnCall: 4);
        var editAddress = new CellAddress(sheet.Id, 2, 3);
        var command = EditCellsCommand.ForValue(sheet.Id, editAddress, new NumberValue(99));

        var act = () => command.Apply(ctx);

        act.Should().Throw<InvalidOperationException>().WithMessage("*boom mid-resize*");

        // The core regression assertion: ResizeStructuredTableCommand's own partial mutation
        // (growing the table to 3 columns) must already be rolled back by the time the exception
        // reaches the caller -- not left half-resized because it was never added to `applied`.
        sheet.StructuredTables.Should().ContainSingle();
        sheet.StructuredTables.Single().Range.Should().Be(
            originalRange,
            "the resize command's own partial mutation must be reverted when it throws mid-Apply, not just skipped from the applied-effects list");

        // Full-cycle check, mirroring what CommandBus.Execute does after an Apply throws: revert
        // the whole edit and confirm the model is back to its exact pre-Apply state, not just the
        // table shape.
        command.Revert(ctx);
        sheet.GetCell(editAddress).Should().BeNull("the base cell edit itself must also be undone");
        sheet.StructuredTables.Single().Range.Should().Be(originalRange);
    }

    [Fact]
    public void Apply_StillAutoExpandsColumnWhenNoChildThrows()
    {
        // Sibling no-regression: the ordinary, fully-successful column auto-expand next to a
        // totals-row table must still work -- the new try/catch must not interfere with the happy
        // path this round's fix sits inside of.
        var (wb, sheet, table) = BuildTableWithTotalsRow();
        var ctx = new TestCommandContext(wb);
        var editAddress = new CellAddress(sheet.Id, 2, 3);
        var command = EditCellsCommand.ForValue(sheet.Id, editAddress, new NumberValue(99));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        var grownTable = sheet.StructuredTables.Single(t => t.Id == table.Id);
        grownTable.Range.Should().Be(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)));
        sheet.GetValue(editAddress).Should().Be(new NumberValue(99));

        command.Revert(ctx);

        sheet.GetCell(editAddress).Should().BeNull();
        sheet.StructuredTables.Single(t => t.Id == table.Id).Range.Should().Be(table.Range);
    }
}
