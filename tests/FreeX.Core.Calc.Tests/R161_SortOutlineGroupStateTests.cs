using FreeX.Core.Model;
using FreeX.Core.Commands;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

// R161-commands-sort-outline-1: SortCommand permutes RowHeights/RowStyles/HiddenRows/
// FilterHiddenRows/ValueFilterHiddenRows/ColumnFilterOwnedRows so per-row formatting/visibility
// "belongs to the row's content" and follows it to its new post-sort position -- but it never
// touched sheet.RowOutlineLevels/GroupHiddenRows/CollapsedAnchorRows, three more per-row
// collections describing the same kind of state (outline nesting level, and whether a
// Data>Group collapse hides this row or marks it as the group's visible "+/-" anchor). Left
// unpermuted, those markers stayed pinned to the physical row number while the row's content
// moved elsewhere, so a sort silently hid/revealed the wrong rows.
public class R161_SortOutlineGroupStateTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) MakeContext() =>
        TestWorkbookFixture.CreateContext("Test");

    [Fact]
    public void Sort_MovesOutlineLevelAndGroupHiddenStateWithRowsAndUndoRestores()
    {
        var (_, sheet, ctx) = MakeContext();
        var sid = sheet.Id;

        // A1=3, A2=1, A3=2 -- row 2 is a collapsed group's hidden detail row (outline level 1,
        // GroupHiddenRows), row 3 is a visible detail row at the same outline level.
        sheet.SetCell(new CellAddress(sid, 1, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(2));
        sheet.RowOutlineLevels[2] = 1;
        sheet.RowOutlineLevels[3] = 1;
        sheet.GroupHiddenRows.Add(2);

        var range = new GridRange(
            new CellAddress(sid, 1, 1),
            new CellAddress(sid, 3, 1));
        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);

        var outcome = cmd.Apply(ctx);
        outcome.Success.Should().BeTrue();

        // Ascending sort of [3,1,2] lands as [1,2,3]: new row1 gets old row2's content (the
        // hidden value 1), new row2 gets old row3's content (value 2), new row3 gets old row1's
        // content (value 3, never grouped). The outline level and collapsed-hidden marker must
        // follow their row's content to its new position: row1 becomes the hidden row, row2
        // becomes a visible detail row at the same outline level, row3 has no marker at all.
        sheet.RowOutlineLevels.Should().ContainKey(1).WhoseValue.Should().Be(1);
        sheet.GroupHiddenRows.Contains(1).Should().BeTrue("row 1 now holds the content that was the collapsed group's hidden detail row");

        sheet.RowOutlineLevels.Should().ContainKey(2).WhoseValue.Should().Be(1);
        sheet.GroupHiddenRows.Contains(2).Should().BeFalse("row 2 now holds the content that was the visible detail row");

        sheet.RowOutlineLevels.TryGetValue(3, out _).Should().BeFalse("row 3 now holds content that was never grouped");
        sheet.GroupHiddenRows.Contains(3).Should().BeFalse();

        cmd.Revert(ctx);

        sheet.RowOutlineLevels.Should().ContainKey(2).WhoseValue.Should().Be(1);
        sheet.RowOutlineLevels.Should().ContainKey(3).WhoseValue.Should().Be(1);
        sheet.RowOutlineLevels.TryGetValue(1, out _).Should().BeFalse();
        sheet.GroupHiddenRows.Should().BeEquivalentTo(new[] { 2u });
    }

    [Fact]
    public void Sort_MovesCollapsedAnchorRowMarkerWithItsRowAndUndoRestores()
    {
        var (_, sheet, ctx) = MakeContext();
        var sid = sheet.Id;

        // A1=3, A2=1, A3=2 -- row 2 is the visible collapsed-group anchor ("+/-" toggle row),
        // which is distinct from (and independent of) GroupHiddenRows: it stays VISIBLE while
        // carrying the collapsed="1" marker.
        sheet.SetCell(new CellAddress(sid, 1, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(2));
        sheet.CollapsedAnchorRows.Add(2);

        var range = new GridRange(
            new CellAddress(sid, 1, 1),
            new CellAddress(sid, 3, 1));
        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);

        cmd.Apply(ctx);

        // Post-sort order is 1 (was row2), 2 (was row3), 3 (was row1): the anchor marker must
        // follow the row-2 content to its new position (row 1), not stay pinned to physical row 2.
        sheet.CollapsedAnchorRows.Should().BeEquivalentTo(new[] { 1u });

        cmd.Revert(ctx);

        sheet.CollapsedAnchorRows.Should().BeEquivalentTo(new[] { 2u });
    }

    // ── Sibling no-regression: filter-hidden rows are excluded from the sort worklist entirely
    // (Excel never sorts a row an active AutoFilter is hiding), so their outline/group-collapse
    // state must stay completely untouched -- same as their content/HiddenRows/FilterHiddenRows.
    [Fact]
    public void Sort_LeavesFilterHiddenRowsOutlineAndGroupStateUntouched()
    {
        var (_, sheet, ctx) = MakeContext();
        var sid = sheet.Id;

        sheet.SetCell(new CellAddress(sid, 1, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sid, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sid, 3, 1), new NumberValue(2));
        sheet.FilterHiddenRows.Add(2);
        sheet.RowOutlineLevels[2] = 1;
        sheet.GroupHiddenRows.Add(2);

        var range = new GridRange(
            new CellAddress(sid, 1, 1),
            new CellAddress(sid, 3, 1));
        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);

        var outcome = cmd.Apply(ctx);
        outcome.Success.Should().BeTrue();

        // Row 2 stayed pinned (filter-hidden rows are never sorted), so its own outline/group
        // markers must remain exactly where they were, untouched by the row-1/row-3 permutation.
        sheet.GetValue(new CellAddress(sid, 2, 1)).Should().Be(new NumberValue(1));
        sheet.RowOutlineLevels.Should().ContainKey(2).WhoseValue.Should().Be(1);
        sheet.GroupHiddenRows.Contains(2).Should().BeTrue();
    }
}
