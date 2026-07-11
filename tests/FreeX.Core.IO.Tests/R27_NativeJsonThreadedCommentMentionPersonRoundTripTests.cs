using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R27-comments-threaded-deep-3 regression coverage: a threaded comment's/reply's
/// <see cref="ThreadedComment.SourcePersonId"/> and <see cref="ThreadedComment.MentionedPersonDisplayNames"/>
/// (and the <see cref="CommentReply"/> equivalents) must survive a save-then-load round trip through
/// FreeX's native JSON format, exactly like the already-round-tripped <see cref="ThreadedComment.MentionsXml"/>
/// fragment that references them by person id. Without this, re-saving the native JSON file back to XLSX
/// mints a fresh author person id and drops the non-authoring mentioned person's record, leaving the
/// preserved @mention XML referencing a person id that no longer exists in the file.
/// </summary>
public sealed class R27_NativeJsonThreadedCommentMentionPersonRoundTripTests
{
    [Fact]
    public void SaveThenLoad_PreservesSourcePersonIdAndMentionedPersonDisplayNames_OnRootCommentAndReply()
    {
        const string authorPersonId = "{5A2F1234-0000-0000-0000-000000000001}";
        const string mentionedPersonId = "{5A2F1234-0000-0000-0000-000000000002}";
        const string replyAuthorPersonId = "{5A2F1234-0000-0000-0000-000000000003}";

        var workbook = new Workbook("MentionPersonRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("Total"));

        sheet.ThreadedComments[address] = new ThreadedComment("Please review @Jane", "Quinn")
        {
            MentionsXml = "<mentions><mention mentionpersonId=\"" + mentionedPersonId + "\" mentionId=\"m1\" startIndex=\"7\" length=\"5\"/></mentions>",
            SourcePersonId = authorPersonId,
            MentionedPersonDisplayNames = new Dictionary<string, string> { [mentionedPersonId] = "Jane" },
            Replies =
            [
                new CommentReply("Looks good", "Reviewer")
                {
                    MentionsXml = "<mentions><mention mentionpersonId=\"" + mentionedPersonId + "\" mentionId=\"m2\" startIndex=\"0\" length=\"5\"/></mentions>",
                    SourcePersonId = replyAuthorPersonId,
                    MentionedPersonDisplayNames = new Dictionary<string, string> { [mentionedPersonId] = "Jane" }
                }
            ]
        };

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);

        stream.Position = 0;
        var loadedSheet = adapter.Load(stream).GetSheetAt(0);
        var loadedComment = loadedSheet.ThreadedComments[new CellAddress(loadedSheet.Id, 1, 1)];

        // Bug case: person linkage metadata must survive the round trip on the root comment.
        loadedComment.SourcePersonId.Should().Be(authorPersonId);
        loadedComment.MentionedPersonDisplayNames.Should().NotBeNull();
        loadedComment.MentionedPersonDisplayNames!.Should().ContainKey(mentionedPersonId)
            .WhoseValue.Should().Be("Jane");

        // Bug case: same linkage metadata must survive on a reply.
        var loadedReply = loadedComment.Replies.Should().ContainSingle().Subject;
        loadedReply.SourcePersonId.Should().Be(replyAuthorPersonId);
        loadedReply.MentionedPersonDisplayNames.Should().NotBeNull();
        loadedReply.MentionedPersonDisplayNames!.Should().ContainKey(mentionedPersonId)
            .WhoseValue.Should().Be("Jane");

        // Already-working sibling case: MentionsXml (which references the ids above) must still
        // round-trip verbatim, same as before this fix.
        loadedComment.MentionsXml.Should().Contain(mentionedPersonId);
        loadedReply.MentionsXml.Should().Contain(mentionedPersonId);
    }

    [Fact]
    public void SaveThenLoad_LeavesMentionPersonFieldsNull_WhenCommentHasNoMentionMetadata()
    {
        // Representative already-working sibling case: a plain threaded comment/reply with no
        // @mention metadata at all must continue to round-trip with these fields absent/null,
        // not spuriously populated.
        var workbook = new Workbook("NoMentionMetadata");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(address, new TextValue("Total"));

        sheet.ThreadedComments[address] = new ThreadedComment("Plain comment", "Quinn")
        {
            Replies = [new CommentReply("Plain reply", "Reviewer")]
        };

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);

        stream.Position = 0;
        var loadedSheet = adapter.Load(stream).GetSheetAt(0);
        var loadedComment = loadedSheet.ThreadedComments[new CellAddress(loadedSheet.Id, 2, 2)];

        loadedComment.SourcePersonId.Should().BeNull();
        loadedComment.MentionedPersonDisplayNames.Should().BeNull();
        var loadedReply = loadedComment.Replies.Should().ContainSingle().Subject;
        loadedReply.SourcePersonId.Should().BeNull();
        loadedReply.MentionedPersonDisplayNames.Should().BeNull();
    }
}
