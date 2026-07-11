using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R22-merged-cells-view-state-3: SortCommand blanket-rejected any sort range overlapping ANY
/// merged region, even when every overlapping merge was identically sized (a common "each record
/// spans N cosmetic columns" layout, e.g. B2:C2, B3:C3, ... each a uniform 1x2 merge). Real Excel
/// allows this: merged cells of a uniform size are treated as one sortable unit per row/column, and
/// the sort succeeds, moving each merged row's data together. Excel only refuses with "This
/// operation requires the merged cells to be identically sized" when the overlapping merges differ
/// in size/shape, or one only partially overlaps the sort range.
/// </summary>
public sealed class R22_sort_uniform_merged_rows_Tests
{
    [Fact]
    public void Sort_OverUniformFullWidthRowMerges_SucceedsAndMovesEachMergedRowTogether()
    {
        // B2:C6 with every row merged across B:C (five identically-sized 1x2 merges) — the exact
        // failure scenario. Column B holds the sort key; column C carries a tag identifying which
        // original row the pair came from, so we can confirm the merged unit moved as a whole.
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

        outcome.Success.Should().BeTrue("Excel allows sorting a range whose overlapping merges are all identically sized");

        // Column B is now ascending 10,20,30,40,50, and each row's tag (column C) must have moved
        // together with its key — proving the merged unit was kept intact during the sort.
        var expectedKeys = new[] { 10d, 20d, 30d, 40d, 50d };
        for (uint i = 0; i < 5; i++)
        {
            var row = 2 + i;
            sheet.GetValue(row, 2).Should().Be(new NumberValue(expectedKeys[i]));
            sheet.GetValue(row, 3).Should().Be(new TextValue($"tag{expectedKeys[i]}"));
        }

        // The merge geometry itself never needed to move (every row already carried an identical
        // full-width merge before the sort), so each row 2..6 must still be merged across B:C.
        for (uint row = 2; row <= 6; row++)
        {
            sheet.GetMergeRegion(new CellAddress(sid, row, 2)).Should().Be(
                new GridRange(new CellAddress(sid, row, 2), new CellAddress(sid, row, 3)),
                $"row {row} must remain merged across B:C after the sort");
        }
    }

    [Fact]
    public void Sort_OverNonUniformMerges_StillRejected()
    {
        // Same layout as above, except one merge (row 4) is a different shape (spans B:D instead
        // of B:C) — Excel's "identically sized" restriction must still block this sort.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        for (uint row = 2; row <= 6; row++)
        {
            sheet.SetCell(new CellAddress(sid, row, 2), new NumberValue(6 - row));
            var endCol = row == 4 ? 4u : 3u; // row 4's merge is wider than the others
            sheet.AddMergedRegion(new GridRange(
                new CellAddress(sid, row, 2),
                new CellAddress(sid, row, endCol)));
        }

        var range = new GridRange(new CellAddress(sid, 2, 2), new CellAddress(sid, 6, 3));
        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeFalse("non-uniform merge shapes must still be rejected");
        outcome.ErrorMessage.Should().Be("Cannot sort a range that contains merged cells.");
    }

    [Fact]
    public void Sort_OverMergeThatOnlyPartiallyOverlapsTheRange_StillRejected()
    {
        // The merge B4:D4 sticks out past the sort range's right edge (C) into column D, which is
        // outside the selected range — this can't be treated as one uniform per-row unit and must
        // still be rejected exactly as before.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        for (uint row = 2; row <= 6; row++)
            sheet.SetCell(new CellAddress(sid, row, 2), new NumberValue(6 - row));

        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sid, 4, 2),
            new CellAddress(sid, 4, 4))); // B4:D4 — extends beyond the C-column range boundary

        var range = new GridRange(new CellAddress(sid, 2, 2), new CellAddress(sid, 6, 3)); // B2:C6
        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeFalse("a merge that only partially overlaps the sort range must still be rejected");
        outcome.ErrorMessage.Should().Be("Cannot sort a range that contains merged cells.");
    }
}
