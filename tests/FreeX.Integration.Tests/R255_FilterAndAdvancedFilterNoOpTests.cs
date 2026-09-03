using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r255: the last two of the AutoFilter block. Both write things no filter model can express --
/// FilterCommand repaints a banded table's data body and rewrites slicer selections,
/// AdvancedFilterCommand writes a copy-to block of cells -- so both decisions include a cell-level
/// comparison alongside the filter-model ones.
///
/// <para>Both directions are pinned. The changed direction matters more here than anywhere else in
/// the group: these two write actual cell content, so a decision that wrongly reported a no-op would
/// drop a block of copied data from the undo stack.</para>
/// </summary>
public sealed class R255_FilterAndAdvancedFilterNoOpTests
{
    private static (Workbook Wb, Sheet Sheet, TestCommandContext Ctx, GridRange Range) SetUpList()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("North"));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        return (wb, sheet, ctx, range);
    }

    [Fact]
    public void FilterCommand_ReapplyingTheSameValueSetReportsANoOp()
    {
        var (_, sheet, ctx, range) = SetUpList();

        new FilterCommand(sheet.Id, range, filterColOffset: 0, ["North"])
            .Apply(ctx).IsNoOp.Should().BeFalse("the South row was hidden");
        var hidden = new HashSet<uint>(sheet.FilterHiddenRows);

        new FilterCommand(sheet.Id, range, filterColOffset: 0, ["North"])
            .Apply(ctx).IsNoOp.Should().BeTrue(
                "re-confirming the same checkbox set writes the same allowed values and the same "
                + "hidden rows; pushing an undo entry for it clears the redo stack");
        sheet.FilterHiddenRows.Should().BeEquivalentTo(hidden);
    }

    [Fact]
    public void FilterCommand_ADifferentValueSetIsNotANoOp()
    {
        var (_, sheet, ctx, range) = SetUpList();

        new FilterCommand(sheet.Id, range, filterColOffset: 0, ["North"]).Apply(ctx);

        new FilterCommand(sheet.Id, range, filterColOffset: 0, ["South"])
            .Apply(ctx).IsNoOp.Should().BeFalse("the opposite rows are hidden now");
    }

    [Fact]
    public void FilterCommand_ClearingAColumnThatCarriesNoFilterIsANoOp()
    {
        var (_, sheet, ctx, range) = SetUpList();

        new FilterCommand(sheet.Id, range, filterColOffset: 0, [])
            .Apply(ctx).IsNoOp.Should().BeTrue(
                "Clear Filter on an unfiltered column has nothing to clear");
        sheet.FilterHiddenRows.Should().BeEmpty();
    }

    [Fact]
    public void FilterCommand_ClearingAnActiveFilterIsNotANoOp()
    {
        var (_, sheet, ctx, range) = SetUpList();

        new FilterCommand(sheet.Id, range, filterColOffset: 0, ["North"]).Apply(ctx);

        new FilterCommand(sheet.Id, range, filterColOffset: 0, [])
            .Apply(ctx).IsNoOp.Should().BeFalse("clearing un-hides the South row");
        sheet.FilterHiddenRows.Should().BeEmpty();
    }

    private static (Workbook Wb, Sheet Sheet, TestCommandContext Ctx, GridRange List, GridRange Criteria)
        SetUpAdvancedFilter()
    {
        var (wb, sheet, ctx, list) = SetUpList();

        // Criteria block: header matching the list's, one criterion row.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("North"));
        var criteria = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 2, 3));

        return (wb, sheet, ctx, list, criteria);
    }

    [Fact]
    public void AdvancedFilterCommand_FilterInPlace_RerunningWithTheSameCriteriaIsANoOp()
    {
        var (_, sheet, ctx, list, criteria) = SetUpAdvancedFilter();

        new AdvancedFilterCommand(list, criteria, CopyTo: null, UniqueRecordsOnly: false).Apply(ctx)
            .IsNoOp.Should().BeFalse("the non-matching row was hidden");
        var hidden = new HashSet<uint>(sheet.FilterHiddenRows);

        new AdvancedFilterCommand(list, criteria, CopyTo: null, UniqueRecordsOnly: false).Apply(ctx)
            .IsNoOp.Should().BeTrue("the same criteria match the same rows");
        sheet.FilterHiddenRows.Should().BeEquivalentTo(hidden);
    }

    [Fact]
    public void AdvancedFilterCommand_FilterInPlace_EditedCriteriaThatMatchDifferentRowsIsNotANoOp()
    {
        var (_, sheet, ctx, list, criteria) = SetUpAdvancedFilter();

        new AdvancedFilterCommand(list, criteria, CopyTo: null, UniqueRecordsOnly: false).Apply(ctx);

        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("South"));

        new AdvancedFilterCommand(list, criteria, CopyTo: null, UniqueRecordsOnly: false).Apply(ctx)
            .IsNoOp.Should().BeFalse("the edited criterion matches the opposite rows");
    }

    /// <summary>
    /// The copy-to half, which is where the cell-level comparison does the work: re-running a
    /// copy-to Advanced Filter writes the same block over itself, and only a cell-by-cell comparison
    /// can tell that from a block that changed.
    /// </summary>
    [Fact]
    public void AdvancedFilterCommand_CopyTo_RerunningWritesTheSameBlockAndIsANoOp()
    {
        var (_, sheet, ctx, list, criteria) = SetUpAdvancedFilter();
        var copyTo = new CellAddress(sheet.Id, 1, 5);

        new AdvancedFilterCommand(list, criteria, copyTo, UniqueRecordsOnly: false).Apply(ctx)
            .IsNoOp.Should().BeFalse("the destination block was empty and is now filled");
        sheet.GetValue(2, 5).Should().Be(new TextValue("North"));

        new AdvancedFilterCommand(list, criteria, copyTo, UniqueRecordsOnly: false).Apply(ctx)
            .IsNoOp.Should().BeTrue("the second run writes exactly the block that is already there");
    }

    [Fact]
    public void AdvancedFilterCommand_CopyTo_EditedCriteriaThatChangeTheBlockIsNotANoOp()
    {
        var (_, sheet, ctx, list, criteria) = SetUpAdvancedFilter();
        var copyTo = new CellAddress(sheet.Id, 1, 5);

        new AdvancedFilterCommand(list, criteria, copyTo, UniqueRecordsOnly: false).Apply(ctx);

        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("South"));

        new AdvancedFilterCommand(list, criteria, copyTo, UniqueRecordsOnly: false).Apply(ctx)
            .IsNoOp.Should().BeFalse("a different row is copied out, so the destination block differs");
    }
}
