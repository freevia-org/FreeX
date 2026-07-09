using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R19-sort-state-persistence-3: SortCommand.Apply reorders data but never created, updated, or
/// invalidated sheet.SortState, so a saved file's persisted sort metadata was either missing (a
/// brand-new sort left no &lt;sortState&gt; at all) or — worse — stale (a sort performed over a
/// range that already carried a &lt;sortState&gt; from a prior Excel sort kept describing the OLD
/// sort key/direction even though the data was physically reordered by the NEW one). Excel writes
/// a fresh sortState after every Data &gt; Sort; FreeX must do the same, and Revert (undo) must put
/// back exactly whatever was there before.
/// </summary>
public sealed class R19_sortcommand_Tests
{
    [Fact]
    public void Apply_OnSheetWithNoPriorSortState_CreatesSortStateMatchingAppliedSort()
    {
        // Case A from the finding: a brand-new sheet with no persisted sortState at all.
        // Data A1:B10, sort ascending by column A (offset 0). After Apply, sheet.SortState must
        // exist and describe exactly that sort — not be left null.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        for (uint row = 1; row <= 10; row++)
        {
            sheet.SetCell(new CellAddress(sid, row, 1), new NumberValue(11 - row));
            sheet.SetCell(new CellAddress(sid, row, 2), new NumberValue(row));
        }

        sheet.SortState.Should().BeNull("no sort has ever been performed or loaded on this sheet");

        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 10, 2));
        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.SortState.Should().NotBeNull("Apply must record the sort it just performed");
        sheet.SortState!.Reference.Should().Be("A1:B10");
        sheet.SortState.Conditions.Should().HaveCount(1);
        sheet.SortState.Conditions[0].Reference.Should().Be("A1:A10");
        sheet.SortState.Conditions[0].Descending.Should().NotBe(true, "the sort was ascending");
    }

    [Fact]
    public void Apply_OverStaleSortState_ReplacesItWithTheSortJustPerformed()
    {
        // Case B from the finding: a sheet whose SortState already describes a PRIOR sort
        // (as if loaded from an xlsx someone sorted descending by column A in real Excel).
        // Running SortCommand ascending by column B over the same range must overwrite that
        // stale metadata with one describing the sort that actually just happened — not leave
        // the old "descending by A" condition sitting there misdescribing the sheet.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        for (uint row = 1; row <= 10; row++)
        {
            sheet.SetCell(new CellAddress(sid, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sid, row, 2), new NumberValue(11 - row));
            sheet.SetCell(new CellAddress(sid, row, 3), new NumberValue(row * 2));
        }

        var staleSortState = new WorksheetSortStateModel
        {
            Reference = "A1:C10",
            Conditions = { new WorksheetSortConditionModel { Reference = "A1:A10", Descending = true } }
        };
        sheet.SortState = staleSortState;

        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 10, 3));
        // Sort ascending by column B, which is offset 1 within A1:C10.
        var cmd = new SortCommand(sid, range, sortByColOffset: 1, ascending: true);
        cmd.Apply(ctx).Success.Should().BeTrue();

        // The data was in fact reordered by column B ascending — sanity-check that first.
        // Column B held 11-row (10,9,...,1), so ascending puts the original row 10 on top.
        sheet.GetValue(1, 2).Should().Be(new NumberValue(1));

        sheet.SortState.Should().NotBeNull();
        sheet.SortState.Should().NotBeSameAs(staleSortState, "the stale sortState must be replaced, not left in place");
        sheet.SortState!.Conditions.Should().HaveCount(1);
        sheet.SortState.Conditions[0].Reference.Should()
            .Be("B1:B10", "the applied sort key is column B, not the stale condition's column A");
        sheet.SortState.Conditions[0].Descending.Should().NotBe(true, "the applied sort was ascending, not the stale descending");
    }

    [Fact]
    public void Revert_RestoresThePriorSortState_IncludingNull()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sid, row, 1), new NumberValue(6 - row));

        sheet.SortState.Should().BeNull();

        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 5, 1));
        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);
        cmd.Apply(ctx).Success.Should().BeTrue();
        sheet.SortState.Should().NotBeNull();

        cmd.Revert(ctx);
        sheet.SortState.Should().BeNull("undo must restore the absence of a sortState, not leave the newly-applied one behind");
    }

    [Fact]
    public void Revert_RestoresThePriorSortState_WhenOneExistedBeforehand()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sid, row, 1), new NumberValue(6 - row));

        var priorSortState = new WorksheetSortStateModel
        {
            Reference = "A1:A5",
            Conditions = { new WorksheetSortConditionModel { Reference = "A1:A5", Descending = true } }
        };
        sheet.SortState = priorSortState;

        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 5, 1));
        var cmd = new SortCommand(sid, range, sortByColOffset: 0, ascending: true);
        cmd.Apply(ctx).Success.Should().BeTrue();
        sheet.SortState.Should().NotBeSameAs(priorSortState);

        cmd.Revert(ctx);
        sheet.SortState.Should().BeSameAs(priorSortState, "undo must put back exactly the sortState that was there before Apply ran");
    }

    [Fact]
    public void Apply_LeftToRight_CreatesSortStateWithColumnSortFlagAndRowCondition()
    {
        // A left-to-right sort's sortCondition must reference a ROW span (not a column span),
        // and columnSort must be set so a consumer knows to interpret the ref that way.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sid = sheet.Id;

        sheet.SetCell(new CellAddress(sid, 1, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sid, 1, 2), new NumberValue(3));
        sheet.SetCell(new CellAddress(sid, 1, 3), new NumberValue(8));
        sheet.SetCell(new CellAddress(sid, 1, 4), new NumberValue(1));

        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 1, 4));
        var cmd = new SortCommand(sid, range, [new SortKey(0, true)], new SortOptions(LeftToRight: true));
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.SortState.Should().NotBeNull();
        sheet.SortState!.ColumnSort.Should().Be(true, "left-to-right sorts must be flagged as columnSort");
        sheet.SortState.Conditions.Should().HaveCount(1);
        sheet.SortState.Conditions[0].Reference.Should().Be("A1:D1", "the condition spans the single sorted row, not a column");
    }
}
