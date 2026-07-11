using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R25-remove-duplicates-consolidate-1: <see cref="RemoveDuplicateRowsCommand"/> built its row key
/// from <c>ScalarValue.ToString()</c>, whose compiler-generated record ToString embeds the type
/// name — so a date-typed cell (<see cref="DateTimeValue"/>) never compared equal to a plain-number
/// cell (<see cref="NumberValue"/>) holding the identical serial value, even though Excel's Remove
/// Duplicates treats them as the same underlying value (a date IS a number; only its display format
/// differs). Verifies the fix, plus sibling cases that must keep working unchanged: two equal plain
/// numbers still dedup, two different dates are not merged, and a number is never merged with a
/// text cell that merely looks like the same number (no over-correction of the type boundary).
///
/// R25-remove-duplicates-consolidate-3: Remove Duplicates run over a structured table's full data
/// body never shrank the table's own <see cref="StructuredTableModel.Range"/>, leaving it pointing
/// at rows that were just vacated by the compaction. Verifies the fix (table shrinks, and Undo
/// restores its original extent), plus sibling cases that must keep working unchanged: a no-op
/// dedup (nothing removed) never touches the table, and a dedup range that doesn't match a table's
/// exact column span/tail never touches an unrelated table either.
/// </summary>
public sealed class R25_RemoveDuplicatesConsolidateTests
{
    // ── R25-remove-duplicates-consolidate-1: Number/DateTime key equivalence ──────────────

    [Fact]
    public void RemoveDuplicateRows_DateAndEqualNumberSerial_AreTreatedAsDuplicates()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // A1 entered as a date literal ("1/1/2024" -> serial 45292); A2 entered as the plain
        // number 45292. Same underlying value in real Excel -> row 2 is a duplicate of row 1.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new DateTimeValue(45292));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(45292));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        var command = new RemoveDuplicateRowsCommand(sheet.Id, range);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        command.RemovedRowCount.Should().Be(1, "a date and the equal-valued plain number are the same underlying value in Excel");
        sheet.GetValue(1, 1).Should().Be(new DateTimeValue(45292), "first row is kept as-is");
        sheet.GetValue(2, 1).Should().BeOfType<BlankValue>("the duplicate row is cleared");

        command.Revert(ctx);

        sheet.GetValue(1, 1).Should().Be(new DateTimeValue(45292));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(45292));
    }

    [Fact]
    public void RemoveDuplicateRows_EqualPlainNumbers_StillDedupAsBefore()
    {
        // Sibling/already-working case: two plain (non-date) numbers with the same value must
        // keep deduping exactly as before this fix.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(5));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        var command = new RemoveDuplicateRowsCommand(sheet.Id, range);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        command.RemovedRowCount.Should().Be(1);
        sheet.GetValue(1, 1).Should().Be(new NumberValue(5));
        sheet.GetValue(2, 1).Should().BeOfType<BlankValue>();
    }

    [Fact]
    public void RemoveDuplicateRows_DifferentDateSerials_AreNotDuplicates()
    {
        // Sibling case: distinct date serials must never be merged together.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new DateTimeValue(45292)); // 2024-01-01
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new DateTimeValue(45293)); // 2024-01-02

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        var command = new RemoveDuplicateRowsCommand(sheet.Id, range);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        command.RemovedRowCount.Should().Be(0, "different serial values must not be treated as duplicates");
    }

    [Fact]
    public void RemoveDuplicateRows_NumberAndLookAlikeText_AreNotMergedAcrossTypes()
    {
        // Regression guard against over-correction: the Number/DateTime merge must not spill over
        // into merging a number with a text cell that happens to render the same digits — Excel
        // still treats a numeric 45292 and the text "45292" as different values/types.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(45292));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("45292"));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        var command = new RemoveDuplicateRowsCommand(sheet.Id, range);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        command.RemovedRowCount.Should().Be(0, "a number and a text cell with the same digits are different values/types");
    }

    // ── R25-remove-duplicates-consolidate-3: structured table Range shrinks with the data ──

    [Fact]
    public void RemoveDuplicateRows_OverFullTableBody_ShrinksTableRangeAndUndoRestoresIt()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Header row 1; data rows 2-6. Row 4 duplicates row 2 on all 3 key columns.
        SetRow(sheet, 1, "Cat", "Sub", "Amt");
        SetRow(sheet, 2, "X", "Y", "1");
        SetRow(sheet, 3, "Z", "W", "2");
        SetRow(sheet, 4, "X", "Y", "1"); // duplicate of row 2
        SetRow(sheet, 5, "P", "Q", "3");
        SetRow(sheet, 6, "M", "N", "4");

        var tableRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 3));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = tableRange,
        });

        // Selection excludes the header row, exactly as RemoveDuplicatesPlanner.ExcludeHeaderRow
        // trims it before constructing the command.
        var dedupRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 6, 3));
        var command = new RemoveDuplicateRowsCommand(sheet.Id, dedupRange);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        command.RemovedRowCount.Should().Be(1);

        // Table shrinks from A1:C6 to A1:C5 — the freed trailing row (old row 6, now blank)
        // must no longer be part of the table.
        sheet.StructuredTables.Should().ContainSingle();
        sheet.StructuredTables[0].Range.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 3)));

        // Data compacted correctly: row 5's data ("P","Q","3") moved to row 4, row 6's data
        // ("M","N","4") moved to row 5, and the vacated old row 6 is blank.
        sheet.GetValue(4, 1).Should().Be(new TextValue("P"));
        sheet.GetValue(5, 1).Should().Be(new TextValue("M"));
        sheet.GetValue(6, 1).Should().BeOfType<BlankValue>();

        command.Revert(ctx);

        sheet.StructuredTables.Should().ContainSingle();
        sheet.StructuredTables[0].Range.Should().Be(tableRange, "Undo must restore the table's original extent");
        sheet.GetValue(4, 1).Should().Be(new TextValue("X"));
        sheet.GetValue(5, 1).Should().Be(new TextValue("P"));
        sheet.GetValue(6, 1).Should().Be(new TextValue("M"));
    }

    [Fact]
    public void RemoveDuplicateRows_NoDuplicatesFoundOverTableBody_LeavesTableRangeUntouched()
    {
        // Sibling/already-working case: when nothing is actually removed, the table's Range must
        // stay exactly as it was (the early no-op return path must not be short-circuited by the
        // new shrink logic).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        SetRow(sheet, 1, "Cat", "Sub", "Amt");
        SetRow(sheet, 2, "X", "Y", "1");
        SetRow(sheet, 3, "Z", "W", "2");

        var tableRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = tableRange,
        });

        var dedupRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 3, 3));
        var command = new RemoveDuplicateRowsCommand(sheet.Id, dedupRange);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        command.RemovedRowCount.Should().Be(0);
        sheet.StructuredTables[0].Range.Should().Be(tableRange);

        command.Revert(ctx);
        sheet.StructuredTables[0].Range.Should().Be(tableRange);
    }

    [Fact]
    public void RemoveDuplicateRows_RangeNotMatchingTableColumnSpan_LeavesUnrelatedTableUntouched()
    {
        // Sibling/regression guard: a dedup range that only covers PART of a table's columns (not
        // an exact column-span match) must never touch that table's Range — proving the new logic
        // doesn't over-fire on a partial/unrelated overlap.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        SetRow(sheet, 1, "Cat", "Sub", "Amt");
        SetRow(sheet, 2, "X", "Y", "1");
        SetRow(sheet, 3, "Z", "W", "2");
        SetRow(sheet, 4, "X", "Y", "1"); // duplicate of row 2 on column A only
        SetRow(sheet, 5, "P", "Q", "3");

        var tableRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)); // A1:C5
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = tableRange,
        });

        // Dedup only column A (A2:A5), not the table's full B:C span — must not touch the table.
        var dedupRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 5, 1));
        var command = new RemoveDuplicateRowsCommand(sheet.Id, dedupRange);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        command.RemovedRowCount.Should().Be(1);
        sheet.StructuredTables[0].Range.Should().Be(tableRange, "a partial-column dedup must never resize an unrelated table");
    }

    private static void SetRow(Sheet sheet, uint row, string a, string b, string c)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(a));
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(b));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), new TextValue(c));
    }
}
