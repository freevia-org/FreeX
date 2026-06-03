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
}
