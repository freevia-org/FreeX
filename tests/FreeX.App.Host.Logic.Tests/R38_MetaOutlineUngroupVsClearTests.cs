using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R38-meta-2: the r37 Avalonia Ungroup fix routed three distinct command keys
/// ("data.ungroup", "Clear Outline", "Ungroup#UngroupRowsMenuItem_Click") to the SAME
/// ClearWorksheetOutline() method, which then used the shape of the current selection
/// (IsSingleCellSelection) as a proxy for which command had actually been invoked: a
/// multi-row/column selection got the selection-scoped one-level decrement, but a single-cell
/// selection fell through to the whole-sheet ClearWorksheetOutlineCommand. Since Ungroup on a
/// single grouped row is naturally a single-cell-tall selection, clicking Ungroup on just one
/// row wiped every group on the entire sheet instead of ungrouping only that row.
///
/// The fix (MainWindow.Outline.cs) splits this into two methods: UngroupSelection() -- always
/// the selection-scoped one-level decrement, regardless of selection shape -- and
/// ClearWorksheetOutline() -- always the whole-sheet clear, regardless of selection -- and
/// rewires "data.ungroup" / the Ungroup submenu / the grid context-menu Ungroup action to
/// UngroupSelection(), while "Clear Outline" keeps calling the whole-sheet clear.
///
/// These tests exercise the same command classes (GroupRowsCommand / GroupColumnsCommand /
/// ClearWorksheetOutlineCommand) that UngroupSelection() and ClearWorksheetOutline() now
/// unconditionally dispatch to, confirming the selection-shape branch is gone from the
/// Ungroup path.
/// </summary>
public sealed class R38_MetaOutlineUngroupVsClearTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    // Mirrors the private GetUngroupedOutlineLevel in MainWindow.Outline.cs / MainWindow.OutlineCommands.cs:
    // the deepest existing outline level found across the range, minus one, floored at zero.
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

    [Fact]
    public void UngroupSelection_WithSingleCellSelectionOnGroupedRow_OnlyUngroupsThatRow_LeavesUnrelatedGroupUntouched()
    {
        var (_, sheet, ctx) = Setup();

        // Two independent single-level row groups.
        new GroupRowsCommand(sheet.Id, 3, 3, 1).Apply(ctx);
        new GroupRowsCommand(sheet.Id, 20, 25, 1).Apply(ctx);

        // Selecting a single cell inside the first group's row (a 1x1 selection -- the exact
        // shape that used to be misread as "the user clicked Clear Outline") and invoking
        // Ungroup must still be scoped to just row 3, per UngroupSelection()'s unconditional
        // selection-scoped decrement.
        var newLevel = GetUngroupedOutlineLevel(sheet.RowOutlineLevels, 3, 3);
        var outcome = new GroupRowsCommand(sheet.Id, 3, 3, newLevel).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.RowOutlineLevels.Should().NotContainKey(3u, "the single-cell-selected row was fully ungrouped");
        for (uint r = 20; r <= 25; r++)
        {
            sheet.RowOutlineLevels.Should().ContainKey(r).WhoseValue.Should().Be(
                1, "an unrelated group elsewhere on the sheet must survive a single-cell-selection Ungroup, not be wiped like a whole-sheet Clear Outline");
        }
    }

    [Fact]
    public void UngroupSelection_WithSingleCellSelectionOnGroupedColumn_OnlyUngroupsThatColumn_LeavesUnrelatedGroupUntouched()
    {
        var (_, sheet, ctx) = Setup();

        new GroupColumnsCommand(sheet.Id, 4, 4, 1).Apply(ctx);
        new GroupColumnsCommand(sheet.Id, 10, 12, 1).Apply(ctx);

        var newLevel = GetUngroupedOutlineLevel(sheet.ColOutlineLevels, 4, 4);
        var outcome = new GroupColumnsCommand(sheet.Id, 4, 4, newLevel).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.ColOutlineLevels.Should().NotContainKey(4u);
        for (uint c = 10; c <= 12; c++)
            sheet.ColOutlineLevels.Should().ContainKey(c).WhoseValue.Should().Be(1);
    }

    // Sibling no-regression: Clear Outline is a distinct command that always wipes the whole
    // sheet's outline, regardless of whether the invoking selection happens to be a wide
    // multi-row/column range or a trivial single cell.
    [Fact]
    public void ClearWorksheetOutline_WipesEveryGroupOnTheSheet_RegardlessOfSelectionShape()
    {
        var (_, sheet, ctx) = Setup();

        new GroupRowsCommand(sheet.Id, 3, 6, 1).Apply(ctx);
        new GroupRowsCommand(sheet.Id, 20, 25, 1).Apply(ctx);
        new GroupColumnsCommand(sheet.Id, 2, 4, 1).Apply(ctx);

        // Clear Outline always dispatches ClearWorksheetOutlineCommand unconditionally -- the
        // selection at invocation time (wide, narrow, or single-cell) plays no role.
        var outcome = new ClearWorksheetOutlineCommand(sheet.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.RowOutlineLevels.Should().BeEmpty();
        sheet.ColOutlineLevels.Should().BeEmpty();
    }

    // Sibling no-regression: a genuinely wide multi-row selection Ungroup still behaves as
    // before (this was already correct pre-fix; only the single-cell-selection branch changed).
    [Fact]
    public void UngroupSelection_WithMultiRowSelection_StillScopesToSelection()
    {
        var (_, sheet, ctx) = Setup();

        new GroupRowsCommand(sheet.Id, 3, 6, 1).Apply(ctx);
        new GroupRowsCommand(sheet.Id, 20, 25, 1).Apply(ctx);

        var newLevel = GetUngroupedOutlineLevel(sheet.RowOutlineLevels, 3, 6);
        var outcome = new GroupRowsCommand(sheet.Id, 3, 6, newLevel).Apply(ctx);

        outcome.Success.Should().BeTrue();
        for (uint r = 3; r <= 6; r++)
            sheet.RowOutlineLevels.Should().NotContainKey(r);
        for (uint r = 20; r <= 25; r++)
            sheet.RowOutlineLevels.Should().ContainKey(r).WhoseValue.Should().Be(1);
    }
}
