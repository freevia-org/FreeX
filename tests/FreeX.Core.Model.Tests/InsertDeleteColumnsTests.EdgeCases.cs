using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public partial class InsertDeleteColumnsTests
{
    [Fact]
    public void InsertColumn_InsideMergedRegionExpandsRegion()
    {
        var (_, sheet, ctx) = Setup();
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 3),
            new CellAddress(sheet.Id, 2, 5)));

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 4, count: 2);
        cmd.Apply(ctx);

        sheet.MergedRegions[0].Start.Col.Should().Be(3);
        sheet.MergedRegions[0].End.Col.Should().Be(7);

        cmd.Revert(ctx);

        sheet.MergedRegions[0].Start.Col.Should().Be(3);
        sheet.MergedRegions[0].End.Col.Should().Be(5);
    }

    [Fact]
    public void DeleteColumn_ShiftsMergedRegionsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 6),
            new CellAddress(sheet.Id, 2, 7)));

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 2);
        cmd.Apply(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 4),
            new CellAddress(sheet.Id, 2, 5)));

        cmd.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 6),
            new CellAddress(sheet.Id, 2, 7)));
    }

    [Fact]
    public void InsertColumns_WhenDataWouldBePushedPastMaxCol_ReturnsFailed()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, CellAddress.MaxCol), new NumberValue(1));

        var result = new InsertColumnsCommand(sheet.Id, beforeCol: 1, count: 1).Apply(ctx);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("pushed past the last column");
    }

    [Fact]
    public void DeleteColumn_ShrinksMergeToSingleCell_DropsIt()
    {
        // E1:F1 (2-column single-row merge) minus column F → would become E1:E1 — must be dropped.
        var (_, sheet, ctx) = Setup();
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 5),
            new CellAddress(sheet.Id, 1, 6)));

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 6, count: 1);
        cmd.Apply(ctx);

        sheet.MergedRegions.Should().BeEmpty("a 1×1 merge is invalid and must be dropped");

        cmd.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(
            new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 1, 6)),
            "undo should restore the original 2-column merge");
    }
}
