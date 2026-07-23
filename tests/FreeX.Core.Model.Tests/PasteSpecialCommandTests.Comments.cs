using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PasteSpecialCommandTests
{
    [Fact]
    public void PasteCommentsCommand_CopiesCommentsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 3, 2);
        var untouchedSourceComment = new CellAddress(sheet.Id, 1, 2);
        var replacedDestinationComment = new CellAddress(sheet.Id, 3, 3);
        sheet.Comments[source] = "copy me";
        sheet.Comments[untouchedSourceComment] = "second";
        sheet.Comments[destination] = "old";
        sheet.Comments[replacedDestinationComment] = "old second";

        var command = new PasteCommentsCommand(
            sheet.Id,
            new GridRange(source, untouchedSourceComment),
            destination,
            transpose: false);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Comments[destination].Should().Be("copy me");
        sheet.Comments[replacedDestinationComment].Should().Be("second");

        command.Revert(ctx);

        sheet.Comments[destination].Should().Be("old");
        sheet.Comments[replacedDestinationComment].Should().Be("old second");
    }

    [Fact]
    public void PasteCommentsCommand_TransposesComments()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceEnd = new CellAddress(sheet.Id, 1, 2);
        var destination = new CellAddress(sheet.Id, 5, 5);
        sheet.Comments[sourceEnd] = "wide";

        var command = new PasteCommentsCommand(
            sheet.Id,
            new GridRange(sourceStart, sourceEnd),
            destination,
            transpose: true);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Comments[new CellAddress(sheet.Id, 6, 5)].Should().Be("wide");
    }

    [Fact]
    public void PasteCommentsCommand_CopiesThreadedCommentsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 3, 2);
        var sourceReplies = new List<CommentReply> { new("first", "User") };
        sheet.ThreadedComments[source] = new ThreadedComment("copy me", "Quinn")
        {
            Replies = sourceReplies,
            IsResolved = true
        };
        sheet.ThreadedComments[destination] = new ThreadedComment("old", "Codex");

        var command = new PasteCommentsCommand(
            sheet.Id,
            new GridRange(source, source),
            destination,
            transpose: false);

        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.ThreadedComments[destination];
        pasted.Text.Should().Be("copy me");
        pasted.Author.Should().Be("Quinn");
        pasted.IsResolved.Should().BeTrue();
        pasted.Replies.Should().Equal(new CommentReply("first", "User"));
        pasted.Should().NotBeSameAs(sheet.ThreadedComments[source]);

        sourceReplies.Add(new CommentReply("late source edit", "User"));
        sheet.ThreadedComments[destination].Replies.Should().Equal(new CommentReply("first", "User"));

        command.Revert(ctx);

        var restored = sheet.ThreadedComments[destination];
        restored.Text.Should().Be("old");
        restored.Author.Should().Be("Codex");
        restored.IsResolved.Should().BeFalse();
        restored.Replies.Should().BeEmpty();
    }

    [Fact]
    public void PasteCommentsCommand_PastedThreadedCommentGetsFreshIdsNotSourceDuplicate()
    {
        // R80-io-comments-threaded-5-1: pasting a threaded comment (root Id + reply Id already
        // set, e.g. loaded from a saved XLSX) onto a new destination cell must mint a brand-new
        // thread, not carry the source's stable Id onto the destination. Otherwise both cells
        // serialize with the identical <threadedComment id="..."> and reply lookup on reload
        // (grouped globally by parentId) leaks/duplicates replies across the two unrelated cells.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 3, 2);
        sheet.ThreadedComments[source] = new ThreadedComment("copy me", "Quinn")
        {
            Id = "{ROOT-GUID}",
            Replies = [new CommentReply("first", "User") { Id = "{REPLY-GUID}" }],
        };

        var command = new PasteCommentsCommand(
            sheet.Id,
            new GridRange(source, source),
            destination,
            transpose: false);

        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.ThreadedComments[destination];
        pasted.Id.Should().BeNull();
        pasted.Replies.Should().ContainSingle().Which.Id.Should().BeNull();

        // The source thread must be left completely untouched.
        var untouchedSource = sheet.ThreadedComments[source];
        untouchedSource.Id.Should().Be("{ROOT-GUID}");
        untouchedSource.Replies.Should().ContainSingle().Which.Id.Should().Be("{REPLY-GUID}");
    }

    [Fact]
    public void PasteCommentsCommand_UndoRestoresDestinationThreadedCommentIdAfterIdClearingPaste()
    {
        // No-regression sibling: clearing the PASTED thread's Id (fixed above) must not affect
        // undo -- Revert still restores the destination's own pre-existing thread, Id included,
        // exactly as it was before the paste overwrote it.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 3, 2);
        sheet.ThreadedComments[source] = new ThreadedComment("copy me", "Quinn")
        {
            Id = "{ROOT-GUID}",
            Replies = [new CommentReply("first", "User") { Id = "{REPLY-GUID}" }],
        };
        sheet.ThreadedComments[destination] = new ThreadedComment("old", "Codex")
        {
            Id = "{DEST-GUID}",
        };

        var command = new PasteCommentsCommand(
            sheet.Id,
            new GridRange(source, source),
            destination,
            transpose: false);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.ThreadedComments[destination].Id.Should().BeNull();

        command.Revert(ctx);

        var restored = sheet.ThreadedComments[destination];
        restored.Text.Should().Be("old");
        restored.Id.Should().Be("{DEST-GUID}");
    }

    [Fact]
    public void PasteCommentsCommand_CopiesCommentsAcrossSheets()
    {
        var wb = new Workbook("test");
        var sourceSheet = wb.AddSheet("Source");
        var targetSheet = wb.AddSheet("Target");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sourceSheet.Id, 1, 1);
        var destination = new CellAddress(targetSheet.Id, 3, 2);
        sourceSheet.Comments[source] = "copy me";
        targetSheet.Comments[destination] = "old";

        var command = new PasteCommentsCommand(
            targetSheet.Id,
            new GridRange(source, source),
            destination,
            transpose: false);

        command.Apply(ctx).Success.Should().BeTrue();

        sourceSheet.Comments[source].Should().Be("copy me");
        targetSheet.Comments[destination].Should().Be("copy me");

        command.Revert(ctx);

        targetSheet.Comments[destination].Should().Be("old");
    }

    [Fact]
    public void PasteCommentsCommand_RejectsProtectedSheetWithoutEditObjectsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 3, 2);
        sheet.Comments[source] = "copy me";
        sheet.Comments[destination] = "old";
        sheet.IsProtected = true;

        var outcome = new PasteCommentsCommand(
            sheet.Id,
            new GridRange(source, source),
            destination,
            transpose: false).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.Comments[destination].Should().Be("old");
    }

    [Fact]
    public void PasteCommentsCommand_AllowsProtectedSheetWithEditObjectsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 3, 2);
        sheet.Comments[source] = "copy me";
        sheet.Comments[destination] = "old";
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditObjects);

        var outcome = new PasteCommentsCommand(
            sheet.Id,
            new GridRange(source, source),
            destination,
            transpose: false).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.Comments[destination].Should().Be("copy me");
    }

}
