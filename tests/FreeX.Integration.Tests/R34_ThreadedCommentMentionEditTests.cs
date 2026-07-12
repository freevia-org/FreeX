using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression test for R34-io-comments-threaded-mentions-3: editing a threaded comment's (or
/// reply's) text kept the old raw &lt;mentions&gt; XML fragment verbatim via `_previous with { Text
/// = _text }`. That fragment's @mention startIndex/length attributes anchor into the OLD text, so
/// after the edit they pointed at the wrong -- or out-of-range -- substring of the NEW text. Since
/// FreeX does not model @mention linkage as first-class data (it only round-trips the raw XML),
/// re-anchoring per-mention offsets is not attempted; instead, MentionsXml is cleared whenever the
/// text actually changes, matching Excel's own behavior of dropping a mention whose text was
/// edited away. Unrelated edits (resolved-state toggles, unchanged text, adding a reply) must keep
/// preserving the existing MentionsXml exactly as before.
/// </summary>
public class R34_ThreadedCommentMentionEditTests
{
    private const string SampleMentionsXml =
        "<mentions xmlns=\"http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments2\">" +
        "<mention mentionId=\"{11111111-1111-1111-1111-111111111111}\" mentionpersonId=\"{22222222-2222-2222-2222-222222222222}\" startIndex=\"0\" length=\"5\"/>" +
        "</mentions>";

    [Fact]
    public void UpdateThreadedCommentTextCommand_TextChanged_ClearsStaleMentionsXml()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);

        sheet.ThreadedComments[address] = new ThreadedComment("@Quinn please review", "Jane")
        {
            Id = "{ROOT-GUID}",
            MentionsXml = SampleMentionsXml,
        };

        var ctx = new TestCommandContext(wb);
        var command = new UpdateThreadedCommentTextCommand(sheet.Id, address, "Totally different text now");

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var updated = sheet.ThreadedComments[address];
        updated.Text.Should().Be("Totally different text now");
        updated.MentionsXml.Should().BeNull(
            "the preserved mention's startIndex/length anchored into the old text and would now point at the wrong or out-of-range substring");

        // Undo restores the original comment, mention included.
        command.Revert(ctx);
        sheet.ThreadedComments[address].MentionsXml.Should().Be(SampleMentionsXml);
    }

    [Fact]
    public void UpdateThreadedCommentTextCommand_TextUnchanged_PreservesMentionsXml()
    {
        // Sibling already-working case: re-applying the exact same text (e.g. a no-op save/touch)
        // must not clobber a still-valid mention.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);

        sheet.ThreadedComments[address] = new ThreadedComment("@Quinn please review", "Jane")
        {
            Id = "{ROOT-GUID}",
            MentionsXml = SampleMentionsXml,
        };

        var ctx = new TestCommandContext(wb);
        var command = new UpdateThreadedCommentTextCommand(sheet.Id, address, "@Quinn please review");

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.ThreadedComments[address].MentionsXml.Should().Be(SampleMentionsXml);
    }

    [Fact]
    public void UpdateThreadedCommentReplyCommand_ReplyTextChanged_ClearsStaleMentionsXml()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);

        var reply = new CommentReply("@Jane can you check?", "Quinn")
        {
            Id = "{REPLY-GUID}",
            MentionsXml = SampleMentionsXml,
        };
        sheet.ThreadedComments[address] = new ThreadedComment("Root text", "Jane")
        {
            Id = "{ROOT-GUID}",
            Replies = [reply],
        };

        var ctx = new TestCommandContext(wb);
        var command = new UpdateThreadedCommentReplyCommand(sheet.Id, address, replyIndex: 0, text: "Never mind, resolved elsewhere");

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var updatedReply = sheet.ThreadedComments[address].Replies[0];
        updatedReply.Text.Should().Be("Never mind, resolved elsewhere");
        updatedReply.MentionsXml.Should().BeNull(
            "the reply's preserved mention offsets anchored into its old text and are now stale");

        command.Revert(ctx);
        sheet.ThreadedComments[address].Replies[0].MentionsXml.Should().Be(SampleMentionsXml);
    }

    [Fact]
    public void ApplyThreadedCommentChangesCommand_RootTextChanged_ClearsStaleMentionsXml()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);

        sheet.ThreadedComments[address] = new ThreadedComment("@Quinn please review", "Jane")
        {
            Id = "{ROOT-GUID}",
            MentionsXml = SampleMentionsXml,
        };

        var ctx = new TestCommandContext(wb);
        var command = new ApplyThreadedCommentChangesCommand(
            sheet.Id, address, rootText: "Rewritten root text", replyText: null, isResolved: false);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var updated = sheet.ThreadedComments[address];
        updated.Text.Should().Be("Rewritten root text");
        updated.MentionsXml.Should().BeNull("editing the root text invalidates the old mention's offsets");
    }

    [Fact]
    public void ApplyThreadedCommentChangesCommand_OnlyResolvedStateChanged_PreservesRootMentionsXml()
    {
        // Sibling already-working case: toggling resolved state (or adding a reply) without
        // touching the root text must not disturb the root comment's existing mention metadata.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);

        sheet.ThreadedComments[address] = new ThreadedComment("@Quinn please review", "Jane")
        {
            Id = "{ROOT-GUID}",
            MentionsXml = SampleMentionsXml,
            IsResolved = false,
        };

        var ctx = new TestCommandContext(wb);
        var command = new ApplyThreadedCommentChangesCommand(
            sheet.Id, address, rootText: null, replyText: "Adding a reply", isResolved: true);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        var updated = sheet.ThreadedComments[address];
        updated.Text.Should().Be("@Quinn please review");
        updated.IsResolved.Should().BeTrue();
        updated.Replies.Should().ContainSingle().Which.Text.Should().Be("Adding a reply");
        updated.MentionsXml.Should().Be(SampleMentionsXml, "the root text was not edited, so its mention must survive");
    }
}
