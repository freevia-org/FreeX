using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R127: ResizeStructuredTableCommand.Apply only relocated/refreshed a shown totals row when the
/// table GREW (both RelocateTotalsRowIfNeeded and the RefreshStructuredTableTotalsCommand trigger
/// were gated on <c>resizedTable.Range.End.Row &gt; table.Range.End.Row</c>), while CopyTable
/// carries TotalsRowShown through unchanged on every resize including a shrink. So shrinking a
/// table that already had a shown totals row left whatever ordinary data used to sit in the new
/// last row completely untouched -- the sheet cell still displayed real, uncalculated user data --
/// while the table model now marked that same row as the totals row, so
/// StructuredReferenceResolver.DataBodyRange/IsDataBodyRow (driven purely off Range/TotalsRowShown,
/// not cell content) silently excluded it from every structured reference against the table.
/// Fixed by dropping the grow-only gate on the totals refresh: it now runs whenever the resized
/// table has TotalsRowShown=true, regardless of resize direction.
/// </summary>
public sealed class R127_ResizeStructuredTableShrinkTotalsRowTests
{
    private static void SeedTotalsShrinkTable(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        // Row 4 is ordinary data before the shrink -- it is the row that becomes the NEW last row
        // (the totals row) once the table is resized down to rows 1..4.
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(5));
        // Row 6 is the ORIGINAL totals row, pre-populated as if a previous refresh already ran.
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue("Total"));
        sheet.SetFormula(new CellAddress(sheet.Id, 6, 2), "SUBTOTAL(109,[Amount])");
    }

    // ── fail-before / pass-after: the exact defect scenario ─────────────────────

    [Fact]
    public void ResizeStructuredTableCommand_ShrinkingTableWithShownTotalsRow_RegeneratesTotalsAtNewLastRowAndUndoRestoresStaleData()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTotalsShrinkTable(sheet);
        var table = new StructuredTableModel
        {
            Id = 9,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 2)),
            TotalsRowShown = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region", TotalsRowLabel: "Total"),
                new StructuredTableColumnModel(2, "Amount", TotalsRowFunction: "sum")
            }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);
        // Shrink from rows 1..6 (header + 4 data rows + totals) down to rows 1..4 (header + 2 data
        // rows + totals) -- Resize Table's own dialog pre-fills the full current range including the
        // totals row (TableResizePlanner), so a user simply reducing the row count reaches exactly
        // this range.
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));
        var command = new ResizeStructuredTableCommand(sheet.Id, table.Id, newRange);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var resized = sheet.StructuredTables.Should().ContainSingle().Subject;
        resized.Range.End.Row.Should().Be(4);
        resized.TotalsRowShown.Should().BeTrue();

        // THE assertion that fails before the fix: row 4 (the new last row) must now hold live
        // totals content, not the "North" / 15 data that was sitting there before the shrink.
        var labelCell = sheet.GetCell(4, 1);
        labelCell.Should().NotBeNull();
        labelCell!.Value.Should().Be(new TextValue("Total"));
        var totalsCell = sheet.GetCell(4, 2);
        totalsCell.Should().NotBeNull();
        totalsCell!.FormulaText.Should().Be("SUBTOTAL(109,[Amount])");

        // Row 5 (dropped by the shrink, ordinary data) and row 6 (the OLD totals row, also dropped)
        // are both now outside the table's range entirely -- like any other row/column a shrink
        // drops from a table, they are left exactly as they were, not cleared.
        sheet.GetValue(5, 1).Should().Be(new TextValue("West"));
        sheet.GetValue(5, 2).Should().Be(new NumberValue(5));
        sheet.GetCell(6, 1)!.Value.Should().Be(new TextValue("Total"));
        sheet.GetCell(6, 2)!.FormulaText.Should().Be("SUBTOTAL(109,[Amount])");

        command.Revert(ctx);

        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(table);
        // Undo must restore row 4's original stale-looking (but, pre-resize, perfectly ordinary)
        // data -- not leave the regenerated totals content behind.
        sheet.GetValue(4, 1).Should().Be(new TextValue("North"));
        sheet.GetValue(4, 2).Should().Be(new NumberValue(15));
        sheet.GetCell(4, 2)!.FormulaText.Should().BeNull();
    }

    // ── no-regression: a shrink with NO totals row shown must not write any totals-style content
    // ── into the new last row -- the unconditional refresh must stay scoped to TotalsRowShown ──

    [Fact]
    public void ResizeStructuredTableCommand_ShrinkingTableWithoutTotalsRow_LeavesNewLastRowDataUntouched()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        SeedTotalsShrinkTable(sheet);
        var table = new StructuredTableModel
        {
            Id = 9,
            Name = "Sales",
            DisplayName = "Sales",
            // No totals row: the table's own range stops at row 5 (header + 4 data rows).
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            TotalsRowShown = false,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region"),
                new StructuredTableColumnModel(2, "Amount")
            }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));
        var command = new ResizeStructuredTableCommand(sheet.Id, table.Id, newRange);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var resized = sheet.StructuredTables.Should().ContainSingle().Subject;
        resized.TotalsRowShown.Should().BeFalse();
        // Row 4 is ordinary data body all the way through -- no totals refresh should have touched it.
        sheet.GetValue(4, 1).Should().Be(new TextValue("North"));
        sheet.GetValue(4, 2).Should().Be(new NumberValue(15));
        sheet.GetCell(4, 2)!.FormulaText.Should().BeNull();
    }

    // ── extra coverage: the same unconditional-refresh fix also reaches a WIDTH-only resize (no
    // ── row-count change at all), which the old row-comparison gate skipped entirely ──

    [Fact]
    public void ResizeStructuredTableCommand_WideningColumnsOnlyWithShownTotalsRow_ClearsStaleDataInNewColumnsTotalsCell()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Notes"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        // Row 4 is the totals row; column 3 (about to join the table) already carries stray leftover
        // text at the totals row that was never part of the table.
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Total"));
        sheet.SetFormula(new CellAddress(sheet.Id, 4, 2), "SUBTOTAL(109,[Amount])");
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new TextValue("Leftover"));
        var table = new StructuredTableModel
        {
            Id = 9,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            TotalsRowShown = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region", TotalsRowLabel: "Total"),
                new StructuredTableColumnModel(2, "Amount", TotalsRowFunction: "sum")
            }
        };
        sheet.StructuredTables.Add(table);
        var ctx = new TestCommandContext(wb);
        // Widen to include column 3 -- row count is unchanged (still rows 1..4).
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3));
        var command = new ResizeStructuredTableCommand(sheet.Id, table.Id, newRange);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var resized = sheet.StructuredTables.Should().ContainSingle().Subject;
        resized.Range.End.Col.Should().Be(3);
        // The new column has no totalsRowFunction/label/formula of its own, so its totals cell must
        // be cleared to blank -- not left holding the stray "Leftover" text that was never a total.
        sheet.GetValue(4, 3).Should().Be(BlankValue.Instance);
        // The pre-existing columns' totals content is unaffected (idempotent regeneration).
        sheet.GetCell(4, 1)!.Value.Should().Be(new TextValue("Total"));
        sheet.GetCell(4, 2)!.FormulaText.Should().Be("SUBTOTAL(109,[Amount])");
    }
}
