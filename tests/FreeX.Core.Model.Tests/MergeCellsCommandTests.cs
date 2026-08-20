using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public class MergeCellsCommandTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
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

    // R116: UnmergeCellsCommand.Apply must report IsNoOp when the target range was never actually
    // merged, so CommandBus (Success && !IsNoOp gate) skips pushing an undo entry -- matching the
    // NoOpWorkbookCommand convention CellMergePlanner already documents for this exact scenario
    // ("Unmerge Cells run over a plain, never-merged selection... matching Excel, which leaves the
    // workbook and undo history untouched rather than recording a phantom edit"). Before the fix,
    // Apply always returned CommandOutcome(true) with IsNoOp defaulting to false.
    [Fact]
    public void R116_Unmerge_ReportsIsNoOp_WhenRangeWasNeverMerged()
    {
        var (_, sheet, ctx) = Setup();
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));

        var outcome = new UnmergeCellsCommand(sheet.Id, range).Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.IsNoOp.Should().BeTrue();
        sheet.MergedRegions.Should().BeEmpty();
    }

    // Sibling/no-regression: when the range WAS actually merged, Apply must still report a real
    // (non-no-op) success so the removal is correctly recorded on the undo stack.
    [Fact]
    public void R116_Unmerge_DoesNotReportIsNoOp_WhenRangeWasActuallyMerged()
    {
        var (_, sheet, ctx) = Setup();
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));
        sheet.AddMergedRegion(range);

        var outcome = new UnmergeCellsCommand(sheet.Id, range).Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.IsNoOp.Should().BeFalse();
        sheet.MergedRegions.Should().BeEmpty();
    }

    // freex-cell-comments F1: a legacy note/comment on a non-anchor cell being merged away must not
    // be left behind at its now-covered address -- every comment-aware UI path (GridView.Rendering's
    // indicator, GridView.CommentPreview's hit-testing, CommentNavigationPlanner's Next Note/Comment)
    // assumes a merged range's comments only ever live at the anchor cell. Before the fix, Apply only
    // touched Sheet cell values and left B1's comment orphaned at B1 (now covered, unreachable), never
    // relocating it to the anchor A1.
    [Fact]
    public void F1_Merge_RelocatesNonAnchorLegacyCommentToAnchor()
    {
        var (_, sheet, ctx) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.Comments[b1] = "note on b1";
        sheet.CommentAuthors[b1] = "Alice";
        sheet.ShownComments.Add(b1);

        var range = new GridRange(a1, b1);
        new MergeCellsCommand(sheet.Id, range).Apply(ctx);

        sheet.Comments.Should().ContainKey(a1).WhoseValue.Should().Be("note on b1");
        sheet.CommentAuthors.Should().ContainKey(a1).WhoseValue.Should().Be("Alice");
        sheet.ShownComments.Should().Contain(a1);
        sheet.Comments.Should().NotContainKey(b1);
        sheet.CommentAuthors.Should().NotContainKey(b1);
        sheet.ShownComments.Should().NotContain(b1);
    }

    // Same relocation must happen for threaded comments (the modern comment model), independently of
    // legacy notes.
    [Fact]
    public void F1_Merge_RelocatesNonAnchorThreadedCommentToAnchor()
    {
        var (_, sheet, ctx) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.ThreadedComments[b1] = new ThreadedComment("threaded on b1", "Bob");

        var range = new GridRange(a1, b1);
        new MergeCellsCommand(sheet.Id, range).Apply(ctx);

        sheet.ThreadedComments.Should().ContainKey(a1).WhoseValue.Text.Should().Be("threaded on b1");
        sheet.ThreadedComments.Should().NotContainKey(b1);
    }

    // Sibling/no-regression: when the anchor cell already carries its own comment, that comment must
    // not be clobbered by a covered cell's comment -- the covered cell's comment is discarded (matching
    // the "upper-left survives" content-loss rule Excel already applies to cell values), not silently
    // overwriting the anchor's own note.
    [Fact]
    public void F1_Merge_KeepsAnchorsOwnComment_WhenNonAnchorAlsoHasOne()
    {
        var (_, sheet, ctx) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.Comments[a1] = "anchor's own note";
        sheet.Comments[b1] = "covered note";

        var range = new GridRange(a1, b1);
        new MergeCellsCommand(sheet.Id, range).Apply(ctx);

        sheet.Comments[a1].Should().Be("anchor's own note");
        sheet.Comments.Should().NotContainKey(b1);
    }

    // Sibling/no-regression: Revert must put a relocated comment back on its original covered cell and
    // remove it from the anchor, exactly undoing the migration performed by Apply.
    [Fact]
    public void F1_MergeRevert_RestoresRelocatedCommentToOriginalCell()
    {
        var (_, sheet, ctx) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.Comments[b1] = "note on b1";
        sheet.CommentAuthors[b1] = "Alice";
        sheet.ShownComments.Add(b1);

        var range = new GridRange(a1, b1);
        var cmd = new MergeCellsCommand(sheet.Id, range);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.Comments.Should().NotContainKey(a1);
        sheet.Comments.Should().ContainKey(b1).WhoseValue.Should().Be("note on b1");
        sheet.CommentAuthors.Should().ContainKey(b1).WhoseValue.Should().Be("Alice");
        sheet.ShownComments.Should().Contain(b1);
    }

}
