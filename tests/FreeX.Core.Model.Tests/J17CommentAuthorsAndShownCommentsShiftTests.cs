using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// J17: Sheet.CommentAuthors (Dictionary&lt;CellAddress,string&gt;) and Sheet.ShownComments
/// (HashSet&lt;CellAddress&gt;) are address-keyed companions of Sheet.Comments (legacy note author
/// + pinned/"Show Comment" state) and must shift/undo-restore in lockstep with Comments across
/// every structural edit that already shifts Comments: insert/delete rows, insert/delete columns,
/// insert/delete cells (band-scoped), sort, remove-duplicates, and move-range.
/// </summary>
public sealed class J17CommentAuthorsAndShownCommentsShiftTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    // ── InsertRowsCommand ──────────────────────────────────────────────────────

    [Fact]
    public void InsertRows_ShiftsCommentAuthorsAndShownCommentsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var original = new CellAddress(sheet.Id, 5, 2);
        var shifted = new CellAddress(sheet.Id, 6, 2);
        sheet.Comments[original] = "Check this";
        sheet.CommentAuthors[original] = "Alice";
        sheet.ShownComments.Add(original);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 1);
        cmd.Apply(ctx);

        sheet.CommentAuthors.Should().NotContainKey(original);
        sheet.CommentAuthors[shifted].Should().Be("Alice");
        sheet.ShownComments.Should().NotContain(original);
        sheet.ShownComments.Should().Contain(shifted);

        cmd.Revert(ctx);

        sheet.CommentAuthors[original].Should().Be("Alice");
        sheet.CommentAuthors.Should().NotContainKey(shifted);
        sheet.ShownComments.Should().Contain(original);
        sheet.ShownComments.Should().NotContain(shifted);
    }

    // ── DeleteRowsCommand ──────────────────────────────────────────────────────

    [Fact]
    public void DeleteRows_ShiftsCommentAuthorsAndShownCommentsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var original = new CellAddress(sheet.Id, 6, 2);
        var shifted = new CellAddress(sheet.Id, 5, 2);
        sheet.Comments[original] = "Check this";
        sheet.CommentAuthors[original] = "Alice";
        sheet.ShownComments.Add(original);

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 1);
        cmd.Apply(ctx);

        sheet.CommentAuthors.Should().NotContainKey(original);
        sheet.CommentAuthors[shifted].Should().Be("Alice");
        sheet.ShownComments.Should().NotContain(original);
        sheet.ShownComments.Should().Contain(shifted);

        cmd.Revert(ctx);

        sheet.CommentAuthors[original].Should().Be("Alice");
        sheet.CommentAuthors.Should().NotContainKey(shifted);
        sheet.ShownComments.Should().Contain(original);
        sheet.ShownComments.Should().NotContain(shifted);
    }

    [Fact]
    public void DeleteRows_RemovesCommentAuthorsAndShownCommentsInDeletedRowAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var deleted = new CellAddress(sheet.Id, 3, 2);
        sheet.Comments[deleted] = "Gone";
        sheet.CommentAuthors[deleted] = "Bob";
        sheet.ShownComments.Add(deleted);

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 1);
        cmd.Apply(ctx);

        sheet.CommentAuthors.Should().NotContainKey(deleted);
        sheet.ShownComments.Should().NotContain(deleted);

        cmd.Revert(ctx);

        sheet.CommentAuthors[deleted].Should().Be("Bob");
        sheet.ShownComments.Should().Contain(deleted);
    }

    // ── InsertColumnsCommand ───────────────────────────────────────────────────

    [Fact]
    public void InsertColumns_ShiftsCommentAuthorsAndShownCommentsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var original = new CellAddress(sheet.Id, 2, 5);
        var shifted = new CellAddress(sheet.Id, 2, 6);
        sheet.Comments[original] = "Check this";
        sheet.CommentAuthors[original] = "Alice";
        sheet.ShownComments.Add(original);

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 1);
        cmd.Apply(ctx);

        sheet.CommentAuthors.Should().NotContainKey(original);
        sheet.CommentAuthors[shifted].Should().Be("Alice");
        sheet.ShownComments.Should().NotContain(original);
        sheet.ShownComments.Should().Contain(shifted);

        cmd.Revert(ctx);

        sheet.CommentAuthors[original].Should().Be("Alice");
        sheet.CommentAuthors.Should().NotContainKey(shifted);
        sheet.ShownComments.Should().Contain(original);
        sheet.ShownComments.Should().NotContain(shifted);
    }

    // ── DeleteColumnsCommand ───────────────────────────────────────────────────

    [Fact]
    public void DeleteColumns_ShiftsCommentAuthorsAndShownCommentsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var original = new CellAddress(sheet.Id, 2, 6);
        var shifted = new CellAddress(sheet.Id, 2, 5);
        sheet.Comments[original] = "Check this";
        sheet.CommentAuthors[original] = "Alice";
        sheet.ShownComments.Add(original);

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 1);
        cmd.Apply(ctx);

        sheet.CommentAuthors.Should().NotContainKey(original);
        sheet.CommentAuthors[shifted].Should().Be("Alice");
        sheet.ShownComments.Should().NotContain(original);
        sheet.ShownComments.Should().Contain(shifted);

        cmd.Revert(ctx);

        sheet.CommentAuthors[original].Should().Be("Alice");
        sheet.CommentAuthors.Should().NotContainKey(shifted);
        sheet.ShownComments.Should().Contain(original);
        sheet.ShownComments.Should().NotContain(shifted);
    }

    // ── InsertCellsCommand (band-scoped) ───────────────────────────────────────

    [Fact]
    public void InsertCellsShiftRight_ShiftsCommentAuthorsAndShownCommentsInBandAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var original = new CellAddress(sheet.Id, 3, 2);
        var shifted = new CellAddress(sheet.Id, 3, 3);
        sheet.Comments[original] = "Check this";
        sheet.CommentAuthors[original] = "Alice";
        sheet.ShownComments.Add(original);

        var range = new GridRange(new CellAddress(sheet.Id, 3, 2), new CellAddress(sheet.Id, 3, 2));
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.CommentAuthors.Should().NotContainKey(original);
        sheet.CommentAuthors[shifted].Should().Be("Alice");
        sheet.ShownComments.Should().NotContain(original);
        sheet.ShownComments.Should().Contain(shifted);

        cmd.Revert(ctx);

        sheet.CommentAuthors[original].Should().Be("Alice");
        sheet.CommentAuthors.Should().NotContainKey(shifted);
        sheet.ShownComments.Should().Contain(original);
        sheet.ShownComments.Should().NotContain(shifted);
    }

    [Fact]
    public void InsertCellsShiftDown_ShiftsCommentAuthorsAndShownCommentsInBandAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var original = new CellAddress(sheet.Id, 3, 2);
        var shifted = new CellAddress(sheet.Id, 4, 2);
        sheet.Comments[original] = "Check this";
        sheet.CommentAuthors[original] = "Alice";
        sheet.ShownComments.Add(original);

        var range = new GridRange(new CellAddress(sheet.Id, 3, 2), new CellAddress(sheet.Id, 3, 2));
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Down);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.CommentAuthors.Should().NotContainKey(original);
        sheet.CommentAuthors[shifted].Should().Be("Alice");
        sheet.ShownComments.Should().NotContain(original);
        sheet.ShownComments.Should().Contain(shifted);

        cmd.Revert(ctx);

        sheet.CommentAuthors[original].Should().Be("Alice");
        sheet.CommentAuthors.Should().NotContainKey(shifted);
        sheet.ShownComments.Should().Contain(original);
        sheet.ShownComments.Should().NotContain(shifted);
    }

    // ── DeleteCellsCommand (band-scoped) ───────────────────────────────────────

    [Fact]
    public void DeleteCellsShiftLeft_ShiftsCommentAuthorsAndShownCommentsInBandAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var original = new CellAddress(sheet.Id, 3, 3);
        var shifted = new CellAddress(sheet.Id, 3, 2);
        sheet.Comments[original] = "Check this";
        sheet.CommentAuthors[original] = "Alice";
        sheet.ShownComments.Add(original);

        var range = new GridRange(new CellAddress(sheet.Id, 3, 2), new CellAddress(sheet.Id, 3, 2));
        var cmd = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Left);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.CommentAuthors.Should().NotContainKey(original);
        sheet.CommentAuthors[shifted].Should().Be("Alice");
        sheet.ShownComments.Should().NotContain(original);
        sheet.ShownComments.Should().Contain(shifted);

        cmd.Revert(ctx);

        sheet.CommentAuthors[original].Should().Be("Alice");
        sheet.CommentAuthors.Should().NotContainKey(shifted);
        sheet.ShownComments.Should().Contain(original);
        sheet.ShownComments.Should().NotContain(shifted);
    }

    [Fact]
    public void DeleteCellsShiftUp_ShiftsCommentAuthorsAndShownCommentsInBandAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var original = new CellAddress(sheet.Id, 4, 2);
        var shifted = new CellAddress(sheet.Id, 3, 2);
        sheet.Comments[original] = "Check this";
        sheet.CommentAuthors[original] = "Alice";
        sheet.ShownComments.Add(original);

        var range = new GridRange(new CellAddress(sheet.Id, 3, 2), new CellAddress(sheet.Id, 3, 2));
        var cmd = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Up);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.CommentAuthors.Should().NotContainKey(original);
        sheet.CommentAuthors[shifted].Should().Be("Alice");
        sheet.ShownComments.Should().NotContain(original);
        sheet.ShownComments.Should().Contain(shifted);

        cmd.Revert(ctx);

        sheet.CommentAuthors[original].Should().Be("Alice");
        sheet.CommentAuthors.Should().NotContainKey(shifted);
        sheet.ShownComments.Should().Contain(original);
        sheet.ShownComments.Should().NotContain(shifted);
    }

    // ── SortCommand ────────────────────────────────────────────────────────────

    [Fact]
    public void Sort_MovesCommentAuthorsAndShownCommentsWithRowAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        var westNote = new CellAddress(sheet.Id, 1, 1);
        sheet.Comments[westNote] = "West note";
        sheet.CommentAuthors[westNote] = "Alice";
        sheet.ShownComments.Add(westNote);

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        var cmd = new SortCommand(sheet.Id, range, sortByColOffset: 0, ascending: true);
        cmd.Apply(ctx).Success.Should().BeTrue();

        // "East" sorts before "West" ascending, so West's note (with author/pinned state)
        // must now be at row 2, and row 1 must have no leftover author/pinned entry.
        var newWestAddr = new CellAddress(sheet.Id, 2, 1);
        var oldWestAddr = new CellAddress(sheet.Id, 1, 1);
        sheet.GetValue(2, 1).Should().Be(new TextValue("West"));
        sheet.CommentAuthors[newWestAddr].Should().Be("Alice");
        sheet.ShownComments.Should().Contain(newWestAddr);
        sheet.CommentAuthors.Should().NotContainKey(oldWestAddr);
        sheet.ShownComments.Should().NotContain(oldWestAddr);

        cmd.Revert(ctx);

        sheet.CommentAuthors[westNote].Should().Be("Alice");
        sheet.ShownComments.Should().Contain(westNote);
    }

    // ── RemoveDuplicateRowsCommand ─────────────────────────────────────────────

    [Fact]
    public void RemoveDuplicateRows_CompactsCommentAuthorsAndShownCommentsWithSurvivingRowAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A")); // duplicate of row 1
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));

        var survivorSourceAddr = new CellAddress(sheet.Id, 3, 1);
        sheet.Comments[survivorSourceAddr] = "Row 3 note";
        sheet.CommentAuthors[survivorSourceAddr] = "Carol";
        sheet.ShownComments.Add(survivorSourceAddr);

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));
        var cmd = new RemoveDuplicateRowsCommand(sheet.Id, range);
        cmd.Apply(ctx).Success.Should().BeTrue();

        // Row 3 ("B") compacts up to row 2 after the row-2 duplicate ("A") is dropped.
        var survivorTargetAddr = new CellAddress(sheet.Id, 2, 1);
        sheet.CommentAuthors[survivorTargetAddr].Should().Be("Carol");
        sheet.ShownComments.Should().Contain(survivorTargetAddr);
        sheet.CommentAuthors.Should().NotContainKey(survivorSourceAddr);
        sheet.ShownComments.Should().NotContain(survivorSourceAddr);

        cmd.Revert(ctx);

        sheet.CommentAuthors[survivorSourceAddr].Should().Be("Carol");
        sheet.ShownComments.Should().Contain(survivorSourceAddr);
        sheet.CommentAuthors.Should().NotContainKey(survivorTargetAddr);
        sheet.ShownComments.Should().NotContain(survivorTargetAddr);
    }

    // ── MoveRangeCommand ───────────────────────────────────────────────────────

    [Fact]
    public void MoveRange_MovesCommentAuthorsAndShownCommentsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 5, 5);
        sheet.SetCell(source, new TextValue("payload"));
        sheet.Comments[source] = "Move me";
        sheet.CommentAuthors[source] = "Alice";
        sheet.ShownComments.Add(source);

        var cmd = new MoveRangeCommand(sheet.Id, new GridRange(source, source), destination);
        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.CommentAuthors.Should().NotContainKey(source);
        sheet.CommentAuthors[destination].Should().Be("Alice");
        sheet.ShownComments.Should().NotContain(source);
        sheet.ShownComments.Should().Contain(destination);

        cmd.Revert(ctx);

        sheet.CommentAuthors[source].Should().Be("Alice");
        sheet.CommentAuthors.Should().NotContainKey(destination);
        sheet.ShownComments.Should().Contain(source);
        sheet.ShownComments.Should().NotContain(destination);
    }
}
