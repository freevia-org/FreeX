using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// freex-subtotals-outline F1 (round 153): a nested two-level Data &gt; Subtotal report (Data &gt;
/// Subtotal "at each change in Region", then Data &gt; Subtotal again "at each change in Person"
/// with "Replace current subtotals" unchecked -- Excel's own documented recipe) must keep the
/// FIRST pass's own group-total rows ("East Total", "West Total") and its grand total at outline
/// level 0, exactly where the first pass left them. <see cref="SubtotalCommand"/>'s
/// ApplyGroupOutline used to stamp EVERY pre-existing subtotal/grand-total row it found in the
/// range -- including those left behind by the FIRST pass -- with the same intermediate level as
/// the second pass's own brand-new total rows, erasing the level-0 boundary that used to separate
/// one outer group ("East") from its sibling ("West"). <see cref="RowOutlineGroupScope.Resolve"/>
/// (via <see cref="GroupRowsCommand"/>, exercised here through <see cref="CollapseRowGroupCommand"/>
/// exactly as the ribbon's Collapse Group button and the row-header gutter "-" button use it) walks
/// outward through any contiguous run of rows whose level is &gt;= the anchor's level, so once that
/// level-0 gap is gone it can no longer tell "still inside East's group" from "spilled into West's
/// unrelated sibling group".
/// </summary>
public sealed class R153_SubtotalNestedOutlineSiblingMergeTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) BuildRegionPersonSheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Person"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Ann"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Ann"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new TextValue("Ann"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new TextValue("Carol"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new NumberValue(40));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 2), new TextValue("Carol"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 3), new NumberValue(50));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 2), new TextValue("Carol"));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 3), new NumberValue(60));
        return (wb, sheet, new TestCommandContext(wb));
    }

    // R153-subtotal-nested-outline-sibling-merge -----------------------------------------------

    [Fact]
    public void NestedSubtotal_PreservesFirstPassTotalRowsAtLevelZero_SoCollapseStaysWithinEastGroup()
    {
        var (_, sheet, ctx) = BuildRegionPersonSheet();

        // Pass 1: Data > Subtotal "at each change in Region" (offset 0), Sum of Sales, summary
        // below data. Produces East's block, then West's block, then a grand total.
        var range1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 7, 3));
        new SubtotalCommand(sheet.Id, range1, groupByColumnOffset: 0, subtotalColumnOffset: 2, summaryBelowData: true)
            .Apply(ctx).Success.Should().BeTrue();

        // Sanity: confirm pass 1's physical layout before nesting a second pass on top of it.
        sheet.GetValue(5, 1).Should().Be(new TextValue("East Total"), "sanity: pass 1's East subtotal");
        sheet.GetValue(9, 1).Should().Be(new TextValue("West Total"), "sanity: pass 1's West subtotal");
        sheet.GetValue(10, 1).Should().Be(new TextValue("Grand Total"), "sanity: pass 1's grand total");
        sheet.RowOutlineLevels.GetValueOrDefault(5u).Should().Be(0, "sanity: first pass keeps its own totals at level 0");
        sheet.RowOutlineLevels.GetValueOrDefault(9u).Should().Be(0);

        // Pass 2 (nested): Data > Subtotal "at each change in Person" (offset 1), Sum of Sales,
        // still summary below data, "Replace current subtotals" UNCHECKED -- i.e. simply run again
        // over the now-enlarged range that still contains pass 1's own total rows, exactly Excel's
        // documented two-level subtotal recipe.
        var range2 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 3));
        new SubtotalCommand(sheet.Id, range2, groupByColumnOffset: 1, subtotalColumnOffset: 2, summaryBelowData: true)
            .Apply(ctx).Success.Should().BeTrue();

        // Sanity: confirm the resulting physical layout (East's block absorbs pass 1's East Total
        // as the tail of its own "Ann" span, per SubtotalPlanBuilder's existing, documented
        // handling of leftover subtotal rows -- not part of this finding) before asserting on the
        // outline levels the finding is actually about.
        sheet.GetValue(2, 2).Should().Be(new TextValue("Ann"), "sanity: East's raw rows are untouched by pass 2");
        sheet.GetValue(5, 1).Should().Be(new TextValue("East Total"), "sanity: pass 1's East Total is still at row 5, unmoved by pass 2's own inserts (all of which land at or after it)");
        sheet.GetValue(6, 2).Should().Be(new TextValue("Ann Total"), "sanity: pass 2's own Person subtotal for East lands right after pass 1's East Total");
        sheet.GetValue(10, 1).Should().Be(new TextValue("West Total"), "sanity: pass 1's West Total, shifted down by pass 2's East-side insert");
        sheet.GetValue(11, 2).Should().Be(new TextValue("Carol Total"), "sanity: pass 2's own Person subtotal for West");
        sheet.GetValue(12, 1).Should().Be(new TextValue("Grand Total"), "sanity: pass 1's preserved grand total, shifted down intact");
        sheet.GetValue(13, 2).Should().Be(new TextValue("Grand Total"), "sanity: pass 2's own new grand total, appended at the very end");
        sheet.GetCell(14, 1).Should().BeNull("sanity: nothing past pass 2's own grand total");

        // THE FIX: pass 1's own total/grand-total rows must stay at level 0 -- untouched by pass
        // 2 -- so they keep acting as the boundary that separates East's group from West's.
        sheet.RowOutlineLevels.GetValueOrDefault(5u).Should().Be(0, "East Total (pass 1's own) must stay the level-0 boundary between East and West, not be promoted to an intermediate level");
        sheet.RowOutlineLevels.GetValueOrDefault(10u).Should().Be(0, "West Total (pass 1's own) must likewise stay level 0");
        sheet.RowOutlineLevels.GetValueOrDefault(12u).Should().Be(0, "pass 1's own preserved Grand Total must stay level 0, not be swept into the same run as the region totals");

        // Pass 2's own brand-new total rows correctly DO become an intermediate level (one level
        // shallower than the freshly-marked detail rows), so they remain collapsible in their own
        // right without being confused with the level-0 region boundaries.
        sheet.RowOutlineLevels.GetValueOrDefault(6u).Should().Be(1, "Ann Total is pass 2's own new total row, so it becomes the intermediate level");
        sheet.RowOutlineLevels.GetValueOrDefault(11u).Should().Be(1, "Carol Total is likewise pass 2's own new total row");
        sheet.RowOutlineLevels.GetValueOrDefault(2u).Should().Be(2, "raw detail rows get promoted to the new, deeper detail level");

        // THE USER GESTURE: put the cursor on "East Total" (row 5) and collapse -- exactly what
        // the ribbon's Collapse Group button and the row-outline gutter's "-" button both do.
        new CollapseRowGroupCommand(sheet.Id, level: 0, selectionStart: 5)
            .Apply(ctx).Success.Should().BeTrue();

        // Only East's own three raw detail rows collapse.
        sheet.GroupHiddenRows.Should().BeEquivalentTo([2u, 3u, 4u],
            "collapsing at East Total must hide exactly East's own detail rows, matching the single-level " +
            "behavior -- not spill into West's sibling group, West's own total, Ann Total, or either grand total");
    }

    // Sibling no-regression: a first (non-nested) pass must behave exactly as before -- every
    // total row in a first pass has no pre-existing RowOutlineLevels entry, so the new
    // "already at level 0 from a prior pass" check is a no-op and the totals still land at 0.
    [Fact]
    public void FirstPassSubtotal_StillKeepsOwnTotalsAtLevelZero_AndCollapseStaysWithinEastGroup()
    {
        var (_, sheet, ctx) = BuildRegionPersonSheet();

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 7, 3));
        new SubtotalCommand(sheet.Id, range, groupByColumnOffset: 0, subtotalColumnOffset: 2, summaryBelowData: true)
            .Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(5, 1).Should().Be(new TextValue("East Total"));
        sheet.GetValue(9, 1).Should().Be(new TextValue("West Total"));
        sheet.GetValue(10, 1).Should().Be(new TextValue("Grand Total"));

        sheet.RowOutlineLevels.GetValueOrDefault(5u).Should().Be(0);
        sheet.RowOutlineLevels.GetValueOrDefault(9u).Should().Be(0);
        sheet.RowOutlineLevels.GetValueOrDefault(10u).Should().Be(0);
        sheet.RowOutlineLevels.GetValueOrDefault(2u).Should().Be(1);

        new CollapseRowGroupCommand(sheet.Id, level: 0, selectionStart: 5)
            .Apply(ctx).Success.Should().BeTrue();

        sheet.GroupHiddenRows.Should().BeEquivalentTo([2u, 3u, 4u],
            "a plain, non-nested Subtotal must still only collapse East's own detail rows");
    }
}
