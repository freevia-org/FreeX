using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public partial class InsertDeleteRowsTests
{
    [Fact]
    public void InsertRow_ShiftsMergedRegions()
    {
        var (_, sheet, ctx) = Setup();
        var mergeRange = new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 4, 2));
        sheet.AddMergedRegion(mergeRange);

        new InsertRowsCommand(sheet.Id, beforeRow: 2, count: 1).Apply(ctx);

        sheet.MergedRegions[0].Start.Row.Should().Be(4);
        sheet.MergedRegions[0].End.Row.Should().Be(5);
    }

    [Fact]
    public void InsertRow_InsideMergedRegionExpandsRegion()
    {
        var (_, sheet, ctx) = Setup();
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 5, 2)));

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 4, count: 2);
        cmd.Apply(ctx);

        sheet.MergedRegions[0].Start.Row.Should().Be(3);
        sheet.MergedRegions[0].End.Row.Should().Be(7);

        cmd.Revert(ctx);

        sheet.MergedRegions[0].Start.Row.Should().Be(3);
        sheet.MergedRegions[0].End.Row.Should().Be(5);
    }

    [Fact]
    public void DeleteRow_ShiftsMergedRegionsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 6, 1),
            new CellAddress(sheet.Id, 7, 2)));

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 2);
        cmd.Apply(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 4, 1),
            new CellAddress(sheet.Id, 5, 2)));

        cmd.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 6, 1),
            new CellAddress(sheet.Id, 7, 2)));
    }

    [Fact]
    public void DeleteRows_PartiallyOverlappingMerge_ShrinksInsteadOfDropping()
    {
        // Merge spans rows 2-6; delete rows 4-6 → merge should shrink to rows 2-3
        var (_, sheet, ctx) = Setup();
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 6, 2)));

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 4, count: 3);
        cmd.Apply(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 2)));

        cmd.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 6, 2)));
    }

    [Fact]
    public void DeleteRows_EntirelyEnclosedMerge_DropsIt()
    {
        // Merge entirely within deleted rows → should be dropped
        var (_, sheet, ctx) = Setup();
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 5, 2)));

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 2, count: 5);
        cmd.Apply(ctx);

        sheet.MergedRegions.Should().BeEmpty();
    }

    [Fact]
    public void DeleteRow_ShrinksMergeToSingleCell_DropsIt()
    {
        // A5:A6 (2-row single-column merge) minus row 6 → would become A5:A5 — must be dropped.
        var (_, sheet, ctx) = Setup();
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 5, 1),
            new CellAddress(sheet.Id, 6, 1)));

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 6, count: 1);
        cmd.Apply(ctx);

        sheet.MergedRegions.Should().BeEmpty("a 1×1 merge is invalid and must be dropped");

        cmd.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(
            new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 6, 1)),
            "undo should restore the original 2-row merge");
    }

    // S9 regression: deleting rows must not drop a merge that shrinks to 1 row tall
    // when it still spans multiple columns (valid horizontal merge).

    [Fact]
    public void DeleteRows_MergeShrinkToOneRowButMultiCol_IsKept()
    {
        // A1:C2 merge (2 rows, 3 cols); delete row 2 → must become A1:C1 (1 row, 3 cols, valid).
        var (_, sheet, ctx) = Setup();
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 3)));

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 2, count: 1);
        cmd.Apply(ctx);

        sheet.MergedRegions.Should().ContainSingle(
            because: "a merge that becomes 1 row tall but spans 3 columns is a valid horizontal merge and must not be dropped");
        var kept = sheet.MergedRegions[0];
        kept.Start.Row.Should().Be(1);
        kept.End.Row.Should().Be(1);
        kept.Start.Col.Should().Be(1);
        kept.End.Col.Should().Be(3);

        cmd.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3)),
            "undo must restore the original 2-row merge");
    }

    [Fact]
    public void DeleteRows_MergeShrinkToSingleCell_DropsIt()
    {
        // A1:A2 (2-row single-column merge); delete row 2 → becomes A1 (1×1, must be dropped).
        var (_, sheet, ctx) = Setup();
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1)));

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 2, count: 1);
        cmd.Apply(ctx);

        sheet.MergedRegions.Should().BeEmpty("a 1×1 collapsed merge must be dropped");
    }

}
