using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R23-meta-2: the round-22 uniform-merge relaxation in SortCommand (see
/// R22_SortUniformMergedRowsTests) only checked that every overlapping merge was contained in the
/// range and identically sized — it never restricted the merges to a single row. The actual sort
/// swaps individual GRID ROWS (MergedRegions are never touched), which is only safe for horizontal
/// (RowCount==1) merges spanning columns. A vertical (RowCount>1) merge "record" has its non-anchor
/// rows blank in the sort key column, so the anchor row and its partner row(s) get reordered
/// independently of each other, scrambling which data rows belong to which merged record while the
/// merge geometry itself stays fixed. This must still be rejected exactly like non-uniform merges.
/// </summary>
public sealed class R23_SortVerticalMergeRegressionTests
{
    [Fact]
    public void Sort_OverUniformVerticalTwoRowMerges_IsRejectedNotSilentlyCorrupted()
    {
        // A2:B5 — two vertical 2-row merges in column A (each "record" spans 2 grid rows), with
        // column B holding per-row line-item data that must stay paired with its own record.
        // A2:A3 = "Bob" (B2="item1a", B3="item1b"); A4:A5 = "Alice" (B4="item2a", B5="item2b").
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Bob"));
        sheet.SetCell(new CellAddress(sid, 2, 2), new TextValue("item1a"));
        sheet.SetCell(new CellAddress(sid, 3, 2), new TextValue("item1b"));
        sheet.AddMergedRegion(new GridRange(new CellAddress(sid, 2, 1), new CellAddress(sid, 3, 1)));

        sheet.SetCell(new CellAddress(sid, 4, 1), new TextValue("Alice"));
        sheet.SetCell(new CellAddress(sid, 4, 2), new TextValue("item2a"));
        sheet.SetCell(new CellAddress(sid, 5, 2), new TextValue("item2b"));
        sheet.AddMergedRegion(new GridRange(new CellAddress(sid, 4, 1), new CellAddress(sid, 5, 1)));

        var range = new GridRange(new CellAddress(sid, 2, 1), new CellAddress(sid, 5, 2));
        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);

        var outcome = cmd.Apply(ctx);

        // Excel refuses this sort outright rather than silently scrambling the row-to-row
        // association within a multi-row merged record.
        outcome.Success.Should().BeFalse("a vertical (RowCount>1) merge cannot be safely sorted by the per-grid-row swap");
        outcome.ErrorMessage.Should().Be("Cannot sort a range that contains merged cells.");

        // Nothing must have moved — the merged records' line items must still be exactly as
        // authored, still paired under their own record.
        sheet.GetValue(2, 1).Should().Be(new TextValue("Bob"));
        sheet.GetValue(2, 2).Should().Be(new TextValue("item1a"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("item1b"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Alice"));
        sheet.GetValue(4, 2).Should().Be(new TextValue("item2a"));
        sheet.GetValue(5, 2).Should().Be(new TextValue("item2b"));
    }

    [Fact]
    public void Sort_OverUniformSingleRowMerges_StillSucceeds()
    {
        // The horizontal (RowCount==1) case the R22 relaxation was meant for must still work after
        // restricting the relaxation to single-row merges: B2:C6, one identically-sized 1x2 merge
        // per row.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        var keys = new[] { 50d, 10d, 30d, 20d, 40d };
        for (uint i = 0; i < 5; i++)
        {
            var row = 2 + i;
            sheet.SetCell(new CellAddress(sid, row, 2), new NumberValue(keys[i]));
            sheet.SetCell(new CellAddress(sid, row, 3), new TextValue($"tag{keys[i]}"));
            sheet.AddMergedRegion(new GridRange(
                new CellAddress(sid, row, 2),
                new CellAddress(sid, row, 3)));
        }

        var range = new GridRange(new CellAddress(sid, 2, 2), new CellAddress(sid, 6, 3));
        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue("uniform single-row (horizontal) merges are still the safe, supported relaxation");

        var expectedKeys = new[] { 10d, 20d, 30d, 40d, 50d };
        for (uint i = 0; i < 5; i++)
        {
            var row = 2 + i;
            sheet.GetValue(row, 2).Should().Be(new NumberValue(expectedKeys[i]));
            sheet.GetValue(row, 3).Should().Be(new TextValue($"tag{expectedKeys[i]}"));
        }
    }
}
