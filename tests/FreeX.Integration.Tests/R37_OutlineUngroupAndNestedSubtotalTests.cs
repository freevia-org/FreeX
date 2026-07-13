using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// Round 37 outline/subtotal fixes:
///
/// R37-commands-outline-subtotal-2-1: the Avalonia shell's Ungroup entry points (ribbon
/// "data.ungroup", the "Ungroup" submenu item, and the grid context-menu Ungroup action) all
/// routed to ClearWorksheetOutlineCommand, which unconditionally wipes RowOutlineLevels,
/// ColOutlineLevels, GroupHiddenRows and GroupHiddenCols for the WHOLE sheet regardless of what
/// is selected. Real Excel (and the WPF host) scope Ungroup to the current row/column selection.
/// The fix (MainWindow.Outline.cs) now scopes to the selection via GroupRowsCommand /
/// GroupColumnsCommand — the same command classes exercised directly below — falling back to the
/// legacy whole-sheet ClearWorksheetOutlineCommand only for the trivial single-cell selection that
/// the separate, unconditional "Clear Outline" menu item shares the same entry point with.
///
/// R37-commands-outline-subtotal-2-3: the WPF host's Ungroup handler
/// (MainWindow.OutlineCommands.cs) always built `new GroupRowsCommand(..., level: 0)`, wiping a
/// selection's outline nesting straight to zero in one click instead of decrementing exactly one
/// level (matching Excel and leaving the selection part of any wider, still-nested outer group).
/// Both shell fixes compute the new level the same way: the deepest existing outline level found
/// across the selected range, minus one, floored at zero — validated here directly against
/// GroupRowsCommand/GroupColumnsCommand, the command classes the shells now call with that level.
/// </summary>
public sealed class R37_OutlineUngroupAndNestedSubtotalTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    private static int GetUngroupedOutlineLevel(IReadOnlyDictionary<uint, int> levels, uint start, uint end)
    {
        var maxLevel = 0;
        for (var i = start; i <= end; i++)
        {
            if (levels.TryGetValue(i, out var level) && level > maxLevel)
                maxLevel = level;
        }

        return Math.Max(maxLevel - 1, 0);
    }

    // R37-commands-outline-subtotal-2-1 -----------------------------------------------------

    [Fact]
    public void Ungroup_ScopedToSelection_LeavesUnrelatedGroupElsewhereOnSheetUntouched()
    {
        var (_, sheet, ctx) = Setup();

        // Two independent row groups, e.g. one from Data > Subtotal and one from manual Group Rows.
        new GroupRowsCommand(sheet.Id, 3, 6, 1).Apply(ctx);
        new GroupRowsCommand(sheet.Id, 20, 25, 1).Apply(ctx);

        // Select rows 3-6 only and Ungroup: the fixed shell code scopes to this selection.
        var newLevel = GetUngroupedOutlineLevel(sheet.RowOutlineLevels, 3, 6);
        var outcome = new GroupRowsCommand(sheet.Id, 3, 6, newLevel).Apply(ctx);

        outcome.Success.Should().BeTrue();
        for (uint r = 3; r <= 6; r++)
            sheet.RowOutlineLevels.Should().NotContainKey(r, "the selected rows were fully ungrouped (single-level group)");
        for (uint r = 20; r <= 25; r++)
            sheet.RowOutlineLevels.Should().ContainKey(r).WhoseValue.Should().Be(1, "the unrelated group must be untouched by a selection-scoped Ungroup");
    }

    [Fact]
    public void Ungroup_ScopedToColumnSelection_LeavesUnrelatedColumnGroupUntouched()
    {
        var (_, sheet, ctx) = Setup();

        new GroupColumnsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        new GroupColumnsCommand(sheet.Id, 10, 12, 1).Apply(ctx);

        var newLevel = GetUngroupedOutlineLevel(sheet.ColOutlineLevels, 2, 4);
        var outcome = new GroupColumnsCommand(sheet.Id, 2, 4, newLevel).Apply(ctx);

        outcome.Success.Should().BeTrue();
        for (uint c = 2; c <= 4; c++)
            sheet.ColOutlineLevels.Should().NotContainKey(c);
        for (uint c = 10; c <= 12; c++)
            sheet.ColOutlineLevels.Should().ContainKey(c).WhoseValue.Should().Be(1);
    }

    // Sibling no-regression: the legacy sheet-wide "Clear Outline" behavior (invoked with a
    // trivial, non-scoping single-cell selection) must still wipe every group on the sheet.
    [Fact]
    public void ClearWorksheetOutlineCommand_StillWipesEveryGroupOnTheSheet()
    {
        var (_, sheet, ctx) = Setup();

        new GroupRowsCommand(sheet.Id, 3, 6, 1).Apply(ctx);
        new GroupRowsCommand(sheet.Id, 20, 25, 1).Apply(ctx);
        new GroupColumnsCommand(sheet.Id, 2, 4, 1).Apply(ctx);

        var outcome = new ClearWorksheetOutlineCommand(sheet.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.RowOutlineLevels.Should().BeEmpty();
        sheet.ColOutlineLevels.Should().BeEmpty();
    }

    // R37-commands-outline-subtotal-2-3 -----------------------------------------------------

    [Fact]
    public void Ungroup_OnNestedSubgroup_DecrementsOneLevel_StaysPartOfOuterGroup()
    {
        var (_, sheet, ctx) = Setup();

        // Outer group rows 2-19 at level 1; inner subgroup rows 5-10 nests to level 2.
        new GroupRowsCommand(sheet.Id, 2, 19, 1, preserveExistingHierarchy: true).Apply(ctx);
        new GroupRowsCommand(sheet.Id, 5, 10, 2, preserveExistingHierarchy: true).Apply(ctx);
        sheet.RowOutlineLevels[7].Should().Be(2, "sanity: the inner selection nested to level 2");

        // Select rows 5-10 (still level 2) and Ungroup.
        var newLevel = GetUngroupedOutlineLevel(sheet.RowOutlineLevels, 5, 10);
        newLevel.Should().Be(1, "Ungroup must decrement by exactly one level, not straight to 0");
        var outcome = new GroupRowsCommand(sheet.Id, 5, 10, newLevel).Apply(ctx);

        outcome.Success.Should().BeTrue();
        for (uint r = 5; r <= 10; r++)
            sheet.RowOutlineLevels[r].Should().Be(1, "rows 5-10 drop one level but remain part of the outer level-1 group");
        // Rows in the outer group outside the inner selection are untouched.
        sheet.RowOutlineLevels[2].Should().Be(1);
        sheet.RowOutlineLevels[19].Should().Be(1);

        // Collapsing the outer group (level 1) still hides rows 5-10: they never left level 1.
        var collapseOutcome = new CollapseRowGroupCommand(sheet.Id, 1, selectionStart: 3, selectionEnd: 3).Apply(ctx);
        collapseOutcome.Success.Should().BeTrue();
        for (uint r = 5; r <= 10; r++)
            sheet.GroupHiddenRows.Should().Contain(r, "rows 5-10 are visually inside the outer group's range and must still collapse with it");
    }

    // Sibling no-regression: ungrouping a simple, non-nested single-level group must still fully
    // remove it (decrementing level 1 by one reaches 0, matching the pre-existing simple case).
    [Fact]
    public void Ungroup_OnSimpleSingleLevelGroup_StillFullyRemovesTheGroup()
    {
        var (_, sheet, ctx) = Setup();

        new GroupRowsCommand(sheet.Id, 3, 6, 1).Apply(ctx);

        var newLevel = GetUngroupedOutlineLevel(sheet.RowOutlineLevels, 3, 6);
        newLevel.Should().Be(0);
        var outcome = new GroupRowsCommand(sheet.Id, 3, 6, newLevel).Apply(ctx);

        outcome.Success.Should().BeTrue();
        for (uint r = 3; r <= 6; r++)
            sheet.RowOutlineLevels.Should().NotContainKey(r);
    }

    // R37-commands-outline-subtotal-2-2 -----------------------------------------------------

    private static (Workbook wb, Sheet sheet) BuildRegionCitySheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("City"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Boston"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Boston"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new TextValue("Springfield"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new TextValue("Denver"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new NumberValue(25));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 2), new TextValue("Denver"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 3), new NumberValue(30));
        return (wb, sheet);
    }

    [Fact]
    public void NestedSubtotal_SecondPassDifferentGroupBy_ReplaceUnchecked_InsertsExactlyTwoRegionTotals()
    {
        var (wb, sheet) = BuildRegionCitySheet();
        var ctx = new TestCommandContext(wb);

        // Pass 1: subtotal "at each change in" City (offset 1), sum Amount (offset 2).
        var range1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 3));
        new SubtotalCommand(sheet.Id, range1, groupByColumnOffset: 1, subtotalColumnOffset: 2)
            .Apply(ctx).Success.Should().BeTrue();

        // Pass 1 inserted 3 city-total rows + 1 grand total = 4 rows; table is now 10 rows.
        sheet.GetValue(4, 2).Should().Be(new TextValue("Boston Total"));
        sheet.GetValue(6, 2).Should().Be(new TextValue("Springfield Total"));
        sheet.GetValue(9, 2).Should().Be(new TextValue("Denver Total"));
        sheet.GetValue(10, 2).Should().Be(new TextValue("Grand Total"));

        // Pass 2: subtotal "at each change in" Region (offset 0), sum Amount, over the now-expanded
        // range, "Replace current subtotals" UNCHECKED (i.e. just apply again, nothing removed).
        var range2 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 3));
        new SubtotalCommand(sheet.Id, range2, groupByColumnOffset: 0, subtotalColumnOffset: 2)
            .Apply(ctx).Success.Should().BeTrue();

        // Correct nested result: exactly 2 new Region subtotal rows, each placed right after the
        // LAST City-total row in that region (not one after every pre-existing subtotal/grand-total
        // row, which is what the bug produced -- 6 group spans instead of 2).
        sheet.GetValue(7, 1).Should().Be(new TextValue("East Total"), "placed right after the last East-region City subtotal (Springfield Total)");
        sheet.GetCell(7, 3)!.FormulaText.Should().Be("SUBTOTAL(9,C2:C6)");
        sheet.GetValue(11, 1).Should().Be(new TextValue("West Total"), "placed right after the last West-region City subtotal (Denver Total)");
        sheet.GetCell(11, 3)!.FormulaText.Should().Be("SUBTOTAL(9,C8:C10)");

        // No spurious extra subtotal rows were inserted after every prior subtotal/grand-total row:
        // the table grew by exactly 3 rows this pass (2 region totals + this pass's own grand total),
        // not 7 (one after each of the 4 pre-existing subtotal/grand-total rows, plus a grand total).
        sheet.GetValue(13, 1).Should().Be(new TextValue("Grand Total"));
        sheet.GetValue(14, 1).Should().BeOfType<BlankValue>("nothing should be inserted past this pass's own grand total");
        sheet.GetCell(14, 1).Should().BeNull();
    }

    // Sibling no-regression: a first (non-nested) Subtotal pass must still produce the original,
    // simple single-level grouping (unaffected by the SubtotalRowFinder-based absorption logic,
    // since there are no pre-existing subtotal rows for it to find).
    [Fact]
    public void Subtotal_FirstPass_StillProducesSimpleSingleLevelGroups()
    {
        var (wb, sheet) = BuildRegionCitySheet();
        var ctx = new TestCommandContext(wb);

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 3));
        var outcome = new SubtotalCommand(sheet.Id, range, groupByColumnOffset: 0, subtotalColumnOffset: 2).Apply(ctx);

        outcome.Success.Should().BeTrue();
        // Region groups: East (rows 2-4), West (rows 5-6) -- exactly 2 groups, matching a plain,
        // non-nested Subtotal pass (no pre-existing subtotal rows for the fix's absorption logic
        // to find, so GetGroups behaves exactly as before this change).
        sheet.GetValue(5, 1).Should().Be(new TextValue("East Total"));
        sheet.GetCell(5, 3)!.FormulaText.Should().Be("SUBTOTAL(9,C2:C4)");
        sheet.GetValue(8, 1).Should().Be(new TextValue("West Total"));
        sheet.GetValue(9, 1).Should().Be(new TextValue("Grand Total"));
        sheet.GetValue(10, 1).Should().BeOfType<BlankValue>("nothing should be inserted past the grand total");
        sheet.GetCell(10, 1).Should().BeNull();
    }
}
