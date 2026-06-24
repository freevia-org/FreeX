using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public partial class CommentCommandTests
{
    // -----------------------------------------------------------------------
    // ConvertNotesToCommentsCommand
    // -----------------------------------------------------------------------

    [Fact]
    public void ConvertNotesToCommentsCommand_TwoNotes_CreatesThreadedCommentsAndRemovesNotes()
    {
        var (_, sheet, ctx) = Setup();
        var addr1 = new CellAddress(sheet.Id, 1, 1);
        var addr2 = new CellAddress(sheet.Id, 2, 3);

        sheet.Comments[addr1] = "First note";
        sheet.CommentAuthors[addr1] = "Alice";
        sheet.Comments[addr2] = "Second note";
        sheet.CommentAuthors[addr2] = "Bob";

        var cmd = new ConvertNotesToCommentsCommand(sheet.Id, timestampUtc: CreatedAtUtc);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().HaveCount(2);
        outcome.AffectedCells.Should().Contain(addr1);
        outcome.AffectedCells.Should().Contain(addr2);

        // Legacy notes removed.
        sheet.Comments.Should().BeEmpty();
        sheet.CommentAuthors.Should().BeEmpty();

        // Threaded comments created with correct text and author.
        sheet.ThreadedComments.Should().ContainKey(addr1);
        sheet.ThreadedComments[addr1].Text.Should().Be("First note");
        sheet.ThreadedComments[addr1].Author.Should().Be("Alice");
        sheet.ThreadedComments[addr1].CreatedAtUtc.Should().Be(CreatedAtUtc);

        sheet.ThreadedComments.Should().ContainKey(addr2);
        sheet.ThreadedComments[addr2].Text.Should().Be("Second note");
        sheet.ThreadedComments[addr2].Author.Should().Be("Bob");
    }

    [Fact]
    public void ConvertNotesToCommentsCommand_Undo_RestoresBothNotesAndRemovesThreadedComments()
    {
        var (_, sheet, ctx) = Setup();
        var addr1 = new CellAddress(sheet.Id, 1, 1);
        var addr2 = new CellAddress(sheet.Id, 2, 3);

        sheet.Comments[addr1] = "First note";
        sheet.CommentAuthors[addr1] = "Alice";
        sheet.Comments[addr2] = "Second note";
        sheet.CommentAuthors[addr2] = "Bob";

        var cmd = new ConvertNotesToCommentsCommand(sheet.Id, timestampUtc: CreatedAtUtc);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        // Legacy notes restored.
        sheet.Comments.Should().ContainKey(addr1);
        sheet.Comments[addr1].Should().Be("First note");
        sheet.CommentAuthors[addr1].Should().Be("Alice");

        sheet.Comments.Should().ContainKey(addr2);
        sheet.Comments[addr2].Should().Be("Second note");
        sheet.CommentAuthors[addr2].Should().Be("Bob");

        // Threaded comments removed.
        sheet.ThreadedComments.Should().NotContainKey(addr1);
        sheet.ThreadedComments.Should().NotContainKey(addr2);
    }

    [Fact]
    public void ConvertNotesToCommentsCommand_DefaultAuthor_UsedWhenCommentAuthorAbsent()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.Comments[addr] = "Note without author";
        // Deliberately do NOT set CommentAuthors[addr].

        var cmd = new ConvertNotesToCommentsCommand(sheet.Id, timestampUtc: CreatedAtUtc);
        cmd.Apply(ctx);

        sheet.ThreadedComments[addr].Author.Should().Be("FreeX");
    }

    [Fact]
    public void ConvertNotesToCommentsCommand_CellWithPreExistingThreadedComment_IsSkipped()
    {
        var (_, sheet, ctx) = Setup();
        var addrNote = new CellAddress(sheet.Id, 1, 1);   // note only → should convert
        var addrBoth = new CellAddress(sheet.Id, 2, 2);   // has BOTH note and threaded → skip

        sheet.Comments[addrNote] = "Plain note";
        sheet.CommentAuthors[addrNote] = "Alice";

        sheet.Comments[addrBoth] = "Note on dual cell";
        sheet.CommentAuthors[addrBoth] = "Bob";
        var existingThreaded = new ThreadedComment("Existing thread", "Carol");
        sheet.ThreadedComments[addrBoth] = existingThreaded;

        var cmd = new ConvertNotesToCommentsCommand(sheet.Id, timestampUtc: CreatedAtUtc);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().ContainSingle().Which.Should().Be(addrNote);

        // addrNote converted correctly.
        sheet.ThreadedComments[addrNote].Text.Should().Be("Plain note");
        sheet.Comments.Should().NotContainKey(addrNote);

        // addrBoth: note left intact, existing threaded comment preserved unchanged.
        sheet.Comments.Should().ContainKey(addrBoth);
        sheet.Comments[addrBoth].Should().Be("Note on dual cell");
        sheet.ThreadedComments[addrBoth].Should().Be(existingThreaded);
    }

    [Fact]
    public void ConvertNotesToCommentsCommand_NoNotes_Fails()
    {
        var (_, sheet, ctx) = Setup();
        // No notes on sheet.

        var outcome = new ConvertNotesToCommentsCommand(sheet.Id).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("no notes");
    }

    [Fact]
    public void ConvertNotesToCommentsCommand_AllNotesAlreadyHaveThreadedComments_Fails()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.Comments[addr] = "Note";
        sheet.ThreadedComments[addr] = new ThreadedComment("Already threaded", "Alice");

        var outcome = new ConvertNotesToCommentsCommand(sheet.Id).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("already have threaded comments");
        // Note left intact.
        sheet.Comments.Should().ContainKey(addr);
    }

    [Fact]
    public void ConvertNotesToCommentsCommand_PinnedNote_RemovedFromShownCommentsDuringApplyRestoredOnRevert()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.Comments[addr] = "Pinned note";
        sheet.ShownComments.Add(addr);

        var cmd = new ConvertNotesToCommentsCommand(sheet.Id, timestampUtc: CreatedAtUtc);
        cmd.Apply(ctx);

        sheet.ShownComments.Should().NotContain(addr);

        cmd.Revert(ctx);

        sheet.ShownComments.Should().Contain(addr);
    }

    [Fact]
    public void ConvertNotesToCommentsCommand_ProtectedSheet_Blocked()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.Comments[addr] = "Note";

        var outcome = new ConvertNotesToCommentsCommand(sheet.Id).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.Comments.Should().ContainKey(addr);
        sheet.ThreadedComments.Should().BeEmpty();
    }

    [Fact]
    public void ConvertNotesToCommentsCommand_ProtectedSheetWithEditObjectsPermission_Succeeds()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditObjects);
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.Comments[addr] = "Note";
        sheet.CommentAuthors[addr] = "Alice";

        var outcome = new ConvertNotesToCommentsCommand(sheet.Id, timestampUtc: CreatedAtUtc).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.Comments.Should().BeEmpty();
        sheet.ThreadedComments[addr].Text.Should().Be("Note");
    }
}
