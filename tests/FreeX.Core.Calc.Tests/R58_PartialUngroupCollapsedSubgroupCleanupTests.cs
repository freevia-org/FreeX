using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R58-commands-outline-group-6-1: a partial Ungroup that decrements a nested subgroup down to a
/// NONZERO level (e.g. level 2 -> 1) previously skipped the GroupHiddenRows/CollapsedAnchorRows
/// cleanup entirely -- that cleanup was gated exclusively on `_level == 0` (a full ungroup).
/// Rows that were hidden by collapsing the now-removed inner subgroup stayed hidden forever, and
/// the stale collapsed anchor below them was never cleared. GroupRowsCommand.Apply now runs the
/// same cleanup regardless of the target level, un-hiding a row whenever its own outline level
/// actually decreased. Mirrored in GroupColumnsCommand for column groups.
/// </summary>
public sealed class R58_PartialUngroupCollapsedSubgroupCleanupTests
{
    private static (Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (sheet, new TestCommandContext(wb));
    }

    [Fact]
    public void Ungroup_PartialDecrementOfCollapsedNestedSubgroup_UnhidesRowsAndClearsStaleAnchor()
    {
        var (sheet, ctx) = Setup();

        // Outer group rows 5-15 @ level 1; inner nested subgroup rows 8-12 @ level 2.
        new GroupRowsCommand(sheet.Id, 5, 15, 1, preserveExistingHierarchy: true).Apply(ctx);
        new GroupRowsCommand(sheet.Id, 8, 12, 2, preserveExistingHierarchy: true).Apply(ctx);
        sheet.RowOutlineLevels[10].Should().Be(2, "sanity: the inner selection nested to level 2");

        // Collapse just the inner level-2 subgroup: rows 8-12 hidden, anchor stamped on row 13
        // (end+1, default summaryBelow).
        new CollapseRowGroupCommand(sheet.Id, 2, selectionStart: 10).Apply(ctx);
        for (uint r = 8; r <= 12; r++)
            sheet.GroupHiddenRows.Should().Contain(r, "sanity: the inner collapse hid rows 8-12");
        sheet.CollapsedAnchorRows.Should().Contain(13u, "sanity: the inner collapse anchored on row 13");

        // Partial Ungroup on rows 8-12: decrements level 2 -> 1 (nonzero), exactly as
        // CreateUngroupCommand computes for a single-level Ungroup on the innermost nested group.
        var outcome = new GroupRowsCommand(sheet.Id, 8, 12, 1).Apply(ctx);
        outcome.Success.Should().BeTrue();

        for (uint r = 8; r <= 12; r++)
            sheet.RowOutlineLevels[r].Should().Be(1, "rows 8-12 merge into the surviving outer level-1 group");

        // The bug: these rows stayed in GroupHiddenRows forever with no subgroup left to justify
        // hiding them, and row 13 kept a stale collapsed-anchor flag even though it is now a plain
        // detail row, not a group boundary.
        for (uint r = 8; r <= 12; r++)
            sheet.GroupHiddenRows.Should().NotContain(r, "the subgroup that justified hiding this row no longer exists");
        sheet.CollapsedAnchorRows.Should().NotContain(13u, "row 13 is no longer a group boundary after the inner subgroup was ungrouped");
    }

    [Fact]
    public void Ungroup_PartialDecrementOfCollapsedNestedSubgroup_Columns_UnhidesColumnsAndClearsStaleAnchor()
    {
        var (sheet, ctx) = Setup();

        new GroupColumnsCommand(sheet.Id, 5, 15, 1, preserveExistingHierarchy: true).Apply(ctx);
        new GroupColumnsCommand(sheet.Id, 8, 12, 2, preserveExistingHierarchy: true).Apply(ctx);
        sheet.ColOutlineLevels[10].Should().Be(2);

        new CollapseColGroupCommand(sheet.Id, 2, selectionStart: 10).Apply(ctx);
        for (uint c = 8; c <= 12; c++)
            sheet.GroupHiddenCols.Should().Contain(c);
        sheet.CollapsedAnchorCols.Should().Contain(13u);

        var outcome = new GroupColumnsCommand(sheet.Id, 8, 12, 1).Apply(ctx);
        outcome.Success.Should().BeTrue();

        for (uint c = 8; c <= 12; c++)
            sheet.ColOutlineLevels[c].Should().Be(1);
        for (uint c = 8; c <= 12; c++)
            sheet.GroupHiddenCols.Should().NotContain(c);
        sheet.CollapsedAnchorCols.Should().NotContain(13u);
    }

    // Sibling no-regression: a plain single-level (non-nested) Ungroup collapsed group must still
    // fully un-hide its rows and clear its anchor, exactly as before this fix (this already worked
    // via the _level == 0 path).
    [Fact]
    public void Ungroup_FullDecrementToZeroOfCollapsedGroup_StillUnhidesRowsAndClearsAnchor()
    {
        var (sheet, ctx) = Setup();

        new GroupRowsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        new CollapseRowGroupCommand(sheet.Id, 1).Apply(ctx);
        sheet.IsRowEffectivelyHidden(3).Should().BeTrue();

        var outcome = new GroupRowsCommand(sheet.Id, 2, 4, 0).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.RowOutlineLevels.Should().NotContainKey(3);
        sheet.IsRowEffectivelyHidden(3).Should().BeFalse();
        sheet.CollapsedAnchorRows.Should().BeEmpty();
    }

    // Sibling no-regression: a Group (increase) that nests a still-visible selection deeper must
    // not disturb an unrelated, currently-collapsed group elsewhere on the sheet -- the fix only
    // un-hides rows whose OWN level decreased, never as a side effect of a Group call elsewhere.
    [Fact]
    public void Group_NestingDeeperElsewhere_DoesNotDisturbUnrelatedCollapsedGroup()
    {
        var (sheet, ctx) = Setup();

        new GroupRowsCommand(sheet.Id, 20, 25, 1).Apply(ctx);
        new CollapseRowGroupCommand(sheet.Id, 1, selectionStart: 20).Apply(ctx);
        for (uint r = 20; r <= 25; r++)
            sheet.GroupHiddenRows.Should().Contain(r, "sanity: the unrelated group is collapsed before the Group call under test");

        // A completely separate Group action (increase, not decrease) on rows 2-6.
        new GroupRowsCommand(sheet.Id, 2, 6, 1, preserveExistingHierarchy: true).Apply(ctx);
        new GroupRowsCommand(sheet.Id, 3, 4, 2, preserveExistingHierarchy: true).Apply(ctx);

        for (uint r = 20; r <= 25; r++)
            sheet.GroupHiddenRows.Should().Contain(r, "the unrelated collapsed group must remain hidden -- Group elsewhere must not un-hide it");
    }
}
