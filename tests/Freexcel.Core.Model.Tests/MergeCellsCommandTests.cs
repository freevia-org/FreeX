using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

public class MergeCellsCommandTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new SimpleCtx(wb));
    }

    [Fact]
    public void Merge_AddsRegionToSheet()
    {
        var (_, sheet, ctx) = Setup();
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 3));

        new MergeCellsCommand(sheet.Id, range).Apply(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(range);
    }

    [Fact]
    public void Merge_ClearsNonTopLeftCells()
    {
        var (_, sheet, ctx) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(99));
        sheet.SetCell(b1, new NumberValue(42));

        var range = new GridRange(a1, b1);
        new MergeCellsCommand(sheet.Id, range).Apply(ctx);

        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(99));
        sheet.GetCell(b1).Should().BeNull();
    }

    [Fact]
    public void Merge_RejectsOverlappingRegion()
    {
        var (_, sheet, ctx) = Setup();
        var r1 = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));
        var r2 = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 4, 4));

        new MergeCellsCommand(sheet.Id, r1).Apply(ctx);
        var outcome = new MergeCellsCommand(sheet.Id, r2).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.MergedRegions.Should().HaveCount(1);
    }

    [Fact]
    public void MergeRevert_RemovesRegionAndRestoresCells()
    {
        var (_, sheet, ctx) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(99));
        sheet.SetCell(b1, new NumberValue(42));

        var range = new GridRange(a1, b1);
        var cmd = new MergeCellsCommand(sheet.Id, range);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.MergedRegions.Should().BeEmpty();
        sheet.GetCell(b1)!.Value.Should().Be(new NumberValue(42));
    }

    [Fact]
    public void Merge_AllowsProtectedSheetWithFormatCellsPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatCells);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));

        var outcome = new MergeCellsCommand(sheet.Id, range).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(range);
    }

    [Fact]
    public void Unmerge_RemovesExistingRegion()
    {
        var (_, sheet, ctx) = Setup();
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));

        sheet.AddMergedRegion(range);
        new UnmergeCellsCommand(sheet.Id, range).Apply(ctx);

        sheet.MergedRegions.Should().BeEmpty();
    }

    [Fact]
    public void UnmergeRevert_RestoresRegion()
    {
        var (_, sheet, ctx) = Setup();
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));
        sheet.AddMergedRegion(range);

        var cmd = new UnmergeCellsCommand(sheet.Id, range);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(range);
    }

    [Fact]
    public void Unmerge_RejectsProtectedSheetWithoutFormatCellsPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));
        sheet.AddMergedRegion(range);

        var outcome = new UnmergeCellsCommand(sheet.Id, range).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(range);
    }

    [Fact]
    public void Unmerge_AllowsProtectedSheetWithFormatCellsPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatCells);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));
        sheet.AddMergedRegion(range);

        var outcome = new UnmergeCellsCommand(sheet.Id, range).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.MergedRegions.Should().BeEmpty();
    }

    [Fact]
    public void UnmergeRevert_DoesNotCreateRegionWhenApplyDidNothing()
    {
        var (_, sheet, ctx) = Setup();
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));

        var cmd = new UnmergeCellsCommand(sheet.Id, range);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.MergedRegions.Should().BeEmpty();
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
