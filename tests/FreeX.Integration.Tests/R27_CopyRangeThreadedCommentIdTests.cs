using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression test for R27-comments-threaded-deep-1: CopyRangeCommand pasted a threaded comment's
/// root Id and every reply's Id verbatim into the destination cell's thread. Since the destination
/// keeps a fully independent thread (unlike MoveRangeCommand, the source is left untouched), this
/// produced two live threads sharing the same persisted Id/reply Ids. On save,
/// XlsxWorksheetThreadedCommentMapper reuses comment.Id verbatim as the XML "id"/"parentId", so
/// both threads round-tripped with identical ids -- and because reply lookup on reload is keyed
/// globally by parentId string (not scoped per cell), the source's reply got cross-attached to the
/// pasted thread too. The fix clears Id (root + replies) whenever a thread is cloned into a new
/// address (CaptureSourcePayloads), while still preserving Id when a thread is merely
/// snapshotted/restored in place for undo (CaptureCellSnapshots/RestoreCellSnapshot).
/// </summary>
public class R27_CopyRangeThreadedCommentIdTests
{
    [Fact]
    public void Apply_CopyingCellWithThreadedCommentAndReply_GivesDestinationFreshIds()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        var source = new CellAddress(sheet.Id, 1, 1); // A1
        var destination = new CellAddress(sheet.Id, 5, 2); // B5

        var reply = new CommentReply("Looks good", "Codex") { Id = "{REPLY-GUID}" };
        var comment = new ThreadedComment("Reviewed", "Quinn")
        {
            Id = "{ROOT-GUID}",
            Replies = [reply],
        };
        sheet.ThreadedComments[source] = comment;

        var ctx = new TestCommandContext(wb);
        var command = new CopyRangeCommand(sheet.Id, new GridRange(source, source), destination);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // The source thread is untouched (copy, not move) and keeps its original persisted ids.
        sheet.ThreadedComments.Should().ContainKey(source);
        var sourceThread = sheet.ThreadedComments[source];
        sourceThread.Id.Should().Be("{ROOT-GUID}");
        sourceThread.Replies.Should().ContainSingle().Which.Id.Should().Be("{REPLY-GUID}");

        // The destination gets an independent thread whose root Id and reply Id must NOT collide
        // with the source's -- otherwise the mapper would serialize both with the same
        // <threadedComment id="..."> / parentId on save and cross-attach the reply on reload.
        sheet.ThreadedComments.Should().ContainKey(destination);
        var pastedThread = sheet.ThreadedComments[destination];
        pastedThread.Id.Should().BeNull("a pasted thread must get a fresh, address-derived id on save, not reuse the source's");
        pastedThread.Text.Should().Be("Reviewed");
        pastedThread.Replies.Should().ContainSingle();
        pastedThread.Replies[0].Id.Should().BeNull("a pasted reply must get a fresh id on save, not reuse the source reply's");
        pastedThread.Replies[0].Text.Should().Be("Looks good");

        // Undo must restore the destination to its pre-paste state (no comment at all here).
        command.Revert(ctx);
        sheet.ThreadedComments.Should().NotContainKey(destination);
        sheet.ThreadedComments.Should().ContainKey(source, "the source thread must still be exactly as it was");
        sheet.ThreadedComments[source].Id.Should().Be("{ROOT-GUID}");
    }

    [Fact]
    public void Apply_CopyingOntoExistingThreadedCommentThenUndo_RestoresOriginalIdsInPlace()
    {
        // Sibling already-working case: pasting onto a destination cell that already carries its
        // own (unrelated) threaded comment must, on undo, restore that destination comment with
        // its own original Id intact -- this is a same-address snapshot/restore, not a copy to a
        // new address, so CaptureCellSnapshots/RestoreCellSnapshot must keep preserving Id as
        // before.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        var source = new CellAddress(sheet.Id, 1, 1); // A1
        var destination = new CellAddress(sheet.Id, 1, 2); // B1

        sheet.ThreadedComments[source] = new ThreadedComment("Source comment", "Quinn")
        {
            Id = "{SOURCE-GUID}",
        };
        sheet.ThreadedComments[destination] = new ThreadedComment("Original destination comment", "Jane")
        {
            Id = "{DEST-GUID}",
        };

        var ctx = new TestCommandContext(wb);
        var command = new CopyRangeCommand(sheet.Id, new GridRange(source, source), destination);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.ThreadedComments[destination].Text.Should().Be("Source comment");
        sheet.ThreadedComments[destination].Id.Should().BeNull();

        command.Revert(ctx);

        sheet.ThreadedComments.Should().ContainKey(destination);
        var restored = sheet.ThreadedComments[destination];
        restored.Text.Should().Be("Original destination comment");
        restored.Id.Should().Be("{DEST-GUID}", "undo must restore the destination's own original thread, ids included, exactly as it was");
    }
}
