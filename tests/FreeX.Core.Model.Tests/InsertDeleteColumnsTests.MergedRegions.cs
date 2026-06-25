using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public partial class InsertDeleteColumnsTests
{
    // S9 regression: deleting columns must not drop a merge that shrinks to 1 column wide
    // when it still spans multiple rows (valid vertical merge in Excel).

    [Fact]
    public void DeleteColumns_MergeShrinkToOneColButMultiRow_IsKept()
    {
        // B1:C5 (2 cols, 5 rows); delete column C → must become B1:B5 (1 col, 5 rows, valid vertical merge).
        var (_, sheet, ctx) = Setup();
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, 5, 3)));

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 1);
        cmd.Apply(ctx);

        sheet.MergedRegions.Should().ContainSingle(
            because: "a merge that becomes 1 column wide but spans 5 rows is a valid vertical merge and must not be dropped");
        var kept = sheet.MergedRegions[0];
        kept.Start.Col.Should().Be(2);
        kept.End.Col.Should().Be(2);
        kept.Start.Row.Should().Be(1);
        kept.End.Row.Should().Be(5);

        cmd.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(
            new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 5, 3)),
            "undo must restore the original 2-column merge");
    }

    [Fact]
    public void DeleteColumns_MergeShrinkToSingleCell_DropsIt()
    {
        // B1:C1 (1-row, 2-col merge); delete column C → becomes B1 (1×1, must be dropped).
        var (_, sheet, ctx) = Setup();
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, 1, 3)));

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 1);
        cmd.Apply(ctx);

        sheet.MergedRegions.Should().BeEmpty("a 1×1 collapsed merge must be dropped");
    }

    [Fact]
    public void DeleteColumns_MultipleMerges_VerticalKeptSingleCellDropped()
    {
        // Two merges: B1:C5 (vertical, shrinks to B1:B5, kept) and B7:C7 (single-row 2-col,
        // shrinks to B7:B7 which is a 1×1 single cell and must be dropped).
        var (_, sheet, ctx) = Setup();
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, 5, 3)));
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 7, 2),
            new CellAddress(sheet.Id, 7, 3)));

        new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 1).Apply(ctx);

        // vertical merge B1:C5 → B1:B5 (kept); B7:C7 → B7:B7 (1×1, dropped)
        sheet.MergedRegions.Should().ContainSingle(
            because: "only the multi-row merge should survive; the single-row 2-col merge collapses to 1×1");
        var kept = sheet.MergedRegions[0];
        kept.Start.Col.Should().Be(2);
        kept.End.Col.Should().Be(2);
        kept.Start.Row.Should().Be(1);
        kept.End.Row.Should().Be(5);
    }
}
