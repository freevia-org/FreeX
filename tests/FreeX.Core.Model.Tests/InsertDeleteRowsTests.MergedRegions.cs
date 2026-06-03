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

}
