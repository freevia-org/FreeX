using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R34-commands-sort-custom-deep-1: the merge-uniformity guard (SortCommand.cs ~147-163) ran
/// BEFORE the LeftToRight branch and never checked SortOptions.LeftToRight, so it accepted a
/// row-uniform merge layout (safe only for a top-to-bottom sort) for a left-to-right (byRows)
/// sort too. ApplyLeftToRight swaps whole grid COLUMNS and never touches MergedRegions, so
/// approving a row-uniform merge for a column swap desyncs the merge geometry from the data.
/// The fix: when LeftToRight, the guard must require a column-uniform merge layout (each merge
/// exactly one column wide, identical row-span, one such merge per column of the range) instead.
///
/// R34-commands-sort-custom-deep-3: BuildSortState (SortCommand.cs ~436-475) destructured each
/// sort key dropping CustomOrder, so a custom-list ("First key sort order") sort's persisted
/// &lt;sortCondition&gt; never got a customList attribute, even though the data itself was
/// correctly rearranged. The fix: BuildSortState must populate
/// WorksheetSortConditionModel.CustomList from the key's CustomOrder.
/// </summary>
public sealed class R34_SortCustomDeepTests
{
    [Fact]
    public void LeftToRightSort_OverRowUniformMerges_IsRejected()
    {
        // A1:D2 with two one-row-tall merges spanning A:B (A1:B1 and A2:B2) — safe for a
        // top-to-bottom sort, but NOT for a left-to-right (byRows) sort, which swaps whole
        // columns instead of whole rows. Real Excel refuses a column-swap sort over a merge
        // layout that doesn't uniformly span full columns; FreeX must do the same rather than
        // silently desyncing the merge geometry from the data.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("X")); // A1:B1 merged
        sheet.SetCell(new CellAddress(sid, 1, 3), new NumberValue(3)); // C1
        sheet.SetCell(new CellAddress(sid, 1, 4), new NumberValue(1)); // D1
        sheet.AddMergedRegion(new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 1, 2)));

        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Y")); // A2:B2 merged
        sheet.SetCell(new CellAddress(sid, 2, 3), new NumberValue(30)); // C2
        sheet.SetCell(new CellAddress(sid, 2, 4), new NumberValue(10)); // D2
        sheet.AddMergedRegion(new GridRange(new CellAddress(sid, 2, 1), new CellAddress(sid, 2, 2)));

        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 2, 4));
        // Sort by row index 0 (row 1), left-to-right — swaps whole columns by row-1 value.
        var cmd = new SortCommand(sid, range, [new SortKey(0, true)], new SortOptions(LeftToRight: true));

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeFalse(
            "a row-uniform merge layout is only safe for a top-to-bottom sort, not a column-swapping left-to-right sort");
        outcome.ErrorMessage.Should().Be("Cannot sort a range that contains merged cells.");

        // The merge geometry and data must be completely untouched by the rejected sort.
        sheet.GetMergeRegion(new CellAddress(sid, 1, 1)).Should().Be(
            new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 1, 2)));
        sheet.GetValue(1, 3).Should().Be(new NumberValue(3));
        sheet.GetValue(1, 4).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void LeftToRightSort_OverColumnUniformMerges_SucceedsAndMovesEachMergedColumnTogether()
    {
        // The transposed, genuinely-safe layout for a left-to-right sort: every COLUMN of the
        // range carries an identical two-row-tall merge (B1:B2, C1:C2, D1:D2), so swapping whole
        // columns keeps each merge intact — mirrors the already-working top-to-bottom uniform
        // case (R22_SortUniformMergedRowsTests), transposed.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        var keys = new[] { 30d, 10d, 20d };
        for (uint i = 0; i < 3; i++)
        {
            var col = 2 + i; // B, C, D
            sheet.SetCell(new CellAddress(sid, 1, col), new NumberValue(keys[i]));
            sheet.SetCell(new CellAddress(sid, 2, col), new TextValue($"tag{keys[i]}"));
            sheet.AddMergedRegion(new GridRange(
                new CellAddress(sid, 1, col),
                new CellAddress(sid, 2, col)));
        }

        var range = new GridRange(new CellAddress(sid, 1, 2), new CellAddress(sid, 2, 4));
        var cmd = new SortCommand(sid, range, [new SortKey(0, true)], new SortOptions(LeftToRight: true));

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue("a column-uniform merge layout is safe for a left-to-right sort");

        var expectedKeys = new[] { 10d, 20d, 30d };
        for (uint i = 0; i < 3; i++)
        {
            var col = 2 + i;
            sheet.GetValue(1, col).Should().Be(new NumberValue(expectedKeys[i]));
            sheet.GetValue(2, col).Should().Be(new TextValue($"tag{expectedKeys[i]}"));
            sheet.GetMergeRegion(new CellAddress(sid, 1, col)).Should().Be(
                new GridRange(new CellAddress(sid, 1, col), new CellAddress(sid, 2, col)),
                $"column {col} must remain merged across rows 1-2 after the sort");
        }
    }

    [Fact]
    public void TopToBottomSort_OverRowUniformMerges_StillSucceeds()
    {
        // Sibling regression guard: the existing (already-working) top-to-bottom uniform-merge
        // case from R22_SortUniformMergedRowsTests must keep working unchanged now that the
        // guard branches on _options.LeftToRight.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        var keys = new[] { 50d, 10d, 30d };
        for (uint i = 0; i < 3; i++)
        {
            var row = 2 + i;
            sheet.SetCell(new CellAddress(sid, row, 2), new NumberValue(keys[i]));
            sheet.SetCell(new CellAddress(sid, row, 3), new TextValue($"tag{keys[i]}"));
            sheet.AddMergedRegion(new GridRange(
                new CellAddress(sid, row, 2),
                new CellAddress(sid, row, 3)));
        }

        var range = new GridRange(new CellAddress(sid, 2, 2), new CellAddress(sid, 4, 3));
        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue("top-to-bottom uniform row merges must keep sorting successfully");
        var expectedKeys = new[] { 10d, 30d, 50d };
        for (uint i = 0; i < 3; i++)
        {
            var row = 2 + i;
            sheet.GetValue(row, 2).Should().Be(new NumberValue(expectedKeys[i]));
        }
    }

    [Fact]
    public void BuildSortState_ForCustomListSort_PersistsCustomListOnCondition()
    {
        // A first-key "Custom List" sort (Jan,Feb,...) must round-trip its list through the
        // persisted sortState's customList attribute, or reopening the saved file shows "Normal"
        // instead of the custom order that was actually applied.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Feb"));

        CustomSortOrder.TryParse("Jan,Feb,Mar", out var customOrder).Should().BeTrue();

        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 3, 1));
        var cmd = new SortCommand(sid, range, [new SortKey(0, true, CustomOrder: customOrder)]);

        var outcome = cmd.Apply(ctx);
        outcome.Success.Should().BeTrue();

        // Sanity-check the data itself was ordered by the custom list.
        sheet.GetValue(1, 1).Should().Be(new TextValue("Jan"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Feb"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Mar"));

        sheet.SortState.Should().NotBeNull();
        sheet.SortState!.Conditions.Should().HaveCount(1);
        sheet.SortState.Conditions[0].CustomList.Should().Be(
            "Jan,Feb,Mar", "the custom list that drove the sort must be persisted, not dropped");
    }

    [Fact]
    public void BuildSortState_ForPlainValueSort_LeavesCustomListNull()
    {
        // Sibling regression guard: an ordinary (no custom list) sort must not have a customList
        // attribute conjured up out of nothing.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        for (uint row = 1; row <= 3; row++)
            sheet.SetCell(new CellAddress(sid, row, 1), new NumberValue(4 - row));

        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 3, 1));
        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.SortState.Should().NotBeNull();
        sheet.SortState!.Conditions.Should().HaveCount(1);
        sheet.SortState.Conditions[0].CustomList.Should().BeNull();
    }
}
