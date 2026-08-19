using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R147-note-vs-threaded F1: every direct-authoring path in the codebase enforces "a cell is
/// never both an independent legacy Note and a threaded Comment" (SetCommentCommand /
/// SetThreadedCommentCommand via CommentCommandGuards.RejectIfCellHasThreadedComment /
/// RejectIfCellHasNote). PasteCommentsCommand.Apply must uphold the same invariant: pasting a
/// Note onto a cell that already carries a threaded comment thread (or vice versa) must REPLACE
/// the destination's existing annotation, not union with it -- mirroring how every other Paste
/// Special target (Values/Formats/Validation/etc.) makes the destination match the copied source
/// exactly.
/// </summary>
public sealed class R147_PasteCommentsNoteVsThreadedInvariantTests
{
    [Fact]
    public void PasteCommentsCommand_PastingNoteOntoThreadedCommentCell_ClearsThreadAndUndoRestoresIt()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);

        sheet.Comments[source] = "This is a legacy note";
        sheet.ThreadedComments[destination] = new ThreadedComment("Original threaded comment", "Bob")
        {
            Replies = [new CommentReply("reply text", "Alice")]
        };

        var command = new PasteCommentsCommand(sheet.Id, new GridRange(source, source), destination, transpose: false);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        // The destination must end up matching the copied source exactly: a Note, and nothing
        // else -- not both a Note AND the pre-existing thread (the bug this test pins).
        sheet.Comments[destination].Should().Be("This is a legacy note");
        sheet.ThreadedComments.ContainsKey(destination).Should().BeFalse(
            "pasting a Note must clear any pre-existing threaded comment at the destination, " +
            "matching the mutual-exclusion invariant SetCommentCommand/SetThreadedCommentCommand enforce");

        command.Revert(ctx);

        sheet.Comments.ContainsKey(destination).Should().BeFalse();
        var restoredThread = sheet.ThreadedComments[destination];
        restoredThread.Text.Should().Be("Original threaded comment");
        restoredThread.Author.Should().Be("Bob");
        restoredThread.Replies.Should().ContainSingle(r => r.Text == "reply text");
    }

    [Fact]
    public void PasteCommentsCommand_PastingThreadedCommentOntoNoteCell_ClearsNoteAndUndoRestoresIt()
    {
        // Sibling/adjacent case (rule 10): the symmetric direction must behave the same way --
        // pasting a threaded comment onto a cell that carries a legacy Note (with author +
        // pinned "Show Comment" state) must clear the note and its companion state, then undo
        // must restore all of it.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);

        sheet.ThreadedComments[source] = new ThreadedComment("copy me", "Quinn");
        sheet.Comments[destination] = "old note";
        sheet.CommentAuthors[destination] = "OldAuthor";
        sheet.ShownComments.Add(destination);

        var command = new PasteCommentsCommand(sheet.Id, new GridRange(source, source), destination, transpose: false);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        sheet.ThreadedComments[destination].Text.Should().Be("copy me");
        sheet.Comments.ContainsKey(destination).Should().BeFalse(
            "pasting a threaded comment must clear any pre-existing Note at the destination");
        sheet.CommentAuthors.ContainsKey(destination).Should().BeFalse();
        sheet.ShownComments.Contains(destination).Should().BeFalse();

        command.Revert(ctx);

        sheet.ThreadedComments.ContainsKey(destination).Should().BeFalse();
        sheet.Comments[destination].Should().Be("old note");
        sheet.CommentAuthors[destination].Should().Be("OldAuthor");
        sheet.ShownComments.Contains(destination).Should().BeTrue();
    }

    [Fact]
    public void PasteCommentsCommand_PastingNoteOntoPlainNoteCell_StillReplacesNormally()
    {
        // No-regression sibling: the ordinary same-kind case (Note-over-Note, already covered
        // elsewhere in PasteSpecialCommandTests.Comments.cs) must be untouched by the new
        // cross-kind clearing logic -- no threaded-comment bookkeeping should fire when there is
        // no threaded comment at the destination to begin with.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);

        sheet.Comments[source] = "new note";
        sheet.Comments[destination] = "old note";

        var command = new PasteCommentsCommand(sheet.Id, new GridRange(source, source), destination, transpose: false);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Comments[destination].Should().Be("new note");
        sheet.ThreadedComments.ContainsKey(destination).Should().BeFalse();

        command.Revert(ctx);

        sheet.Comments[destination].Should().Be("old note");
    }
}
