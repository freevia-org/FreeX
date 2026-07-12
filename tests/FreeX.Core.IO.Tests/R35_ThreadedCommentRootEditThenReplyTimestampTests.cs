using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R35-deferred-comment-edit-timestamp-1: editing a threaded comment's
/// root text and THEN adding a reply (two separate undoable steps in the same editing session,
/// both before the next save) must persist the root's own dT as the root-text edit time -- not
/// silently revert it to the creation time, and not let it get clobbered by the reply's later
/// activity timestamp.
///
/// Before the fix, ThreadedComment had only a single shared ModifiedAtUtc used for BOTH "root
/// text last edited at" and "thread last activity at". AddThreadedCommentReplyCommand's Touch
/// unconditionally overwrote that shared field with the reply's own timestamp, so by the time of
/// save the mapper's reply-activity-vs-root-edit heuristic could no longer distinguish a real
/// root-text edit from mere reply activity and silently dropped the root edit (reverting the
/// persisted dT all the way back to CreatedAtUtc).
///
/// The fix adds a distinct <see cref="ThreadedComment.RootTextEditedAtUtc"/>, stamped ONLY by a
/// genuine root-text edit (ThreadedCommentTimestamps.TouchRootTextEdit, used by
/// UpdateThreadedCommentTextCommand and the root-text branch of
/// ApplyThreadedCommentChangesCommand) and never touched by a reply's own Touch. These tests
/// simulate the exact record states those commands produce (the same pattern used by the sibling
/// XlsxWorksheetThreadedCommentMapperEditedTimestampTests) without taking a dependency on
/// FreeX.Core.Commands from this IO test project.
/// </summary>
public sealed class R35_ThreadedCommentRootEditThenReplyTimestampTests
{
    private static readonly XNamespace ThreadedCommentNs = "http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments";

    [Fact]
    public void Save_PersistsRootEditTime_WhenReplyIsAddedAfterRootTextEditInSameSession()
    {
        var createdAt = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var rootEditedAt = new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);
        var replyAddedAt = new DateTimeOffset(2026, 3, 3, 10, 0, 0, TimeSpan.Zero);

        var workbook = new Workbook("ThreadedRootEditThenReplyTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(address, new TextValue("Total"));

        // 1) SetThreadedCommentCommand: create the root comment.
        var comment = new ThreadedComment("Please review total", "Quinn")
        {
            CreatedAtUtc = createdAt,
            ModifiedAtUtc = createdAt
        };

        // 2) UpdateThreadedCommentTextCommand: a genuine root-text edit stamps
        // RootTextEditedAtUtc (ThreadedCommentTimestamps.TouchRootTextEdit) alongside
        // ModifiedAtUtc.
        comment = comment with { Text = "Please review total (revised)" };
        comment = comment with { ModifiedAtUtc = rootEditedAt, RootTextEditedAtUtc = rootEditedAt };

        // 3) AddThreadedCommentReplyCommand: appending a reply only Touches (generic) the root,
        // bumping the thread-wide ModifiedAtUtc but leaving RootTextEditedAtUtc untouched.
        var reply = new CommentReply("Looks high", "Codex")
        {
            CreatedAtUtc = replyAddedAt,
            ModifiedAtUtc = replyAddedAt
        };
        comment = comment with { Replies = [.. comment.Replies, reply] };
        comment = comment with { ModifiedAtUtc = replyAddedAt };

        comment.RootTextEditedAtUtc.Should().Be(rootEditedAt);
        sheet.ThreadedComments[address] = comment;

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var threadedCommentsXml = XlsxPackageTestFixtures.LoadPackageXml(
            archive, "xl/threadedComments/threadedComment1.xml");
        var elements = threadedCommentsXml.Root!.Elements(ThreadedCommentNs + "threadedComment").ToList();
        elements.Should().HaveCount(2);

        var root = elements[0];
        root.Element(ThreadedCommentNs + "text")!.Value.Should().Be("Please review total (revised)");
        root.Attribute("dT")!.Value.Should().Be(
            "2026-03-02T09:00:00Z",
            "the root's own persisted dT must reflect the root-text edit, not the creation time and not the later reply's activity timestamp");

        var replyElement = elements[1];
        replyElement.Attribute("dT")!.Value.Should().Be("2026-03-03T10:00:00Z");
    }

    [Fact]
    public void Save_KeepsRootDTUnchanged_WhenOnlyReplyIsAdded_NoRegression()
    {
        // Sibling no-regression case: when the root text is never independently edited (no
        // RootTextEditedAtUtc is ever stamped), adding a reply must still never move the root's
        // own persisted dT away from its creation time (matching real Excel, where a reply is a
        // wholly separate <threadedComment> element) -- the pre-existing
        // ModifiedAtUtc/reply-ceiling fallback heuristic must keep working unchanged.
        var createdAt = new DateTimeOffset(2026, 3, 10, 8, 0, 0, TimeSpan.Zero);
        var replyAddedAt = new DateTimeOffset(2026, 3, 11, 9, 0, 0, TimeSpan.Zero);

        var workbook = new Workbook("ThreadedReplyOnlyNoRegressionTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(address, new TextValue("Total"));

        var comment = new ThreadedComment("Please review total", "Quinn")
        {
            CreatedAtUtc = createdAt,
            ModifiedAtUtc = createdAt
        };

        var reply = new CommentReply("Looks high", "Codex")
        {
            CreatedAtUtc = replyAddedAt,
            ModifiedAtUtc = replyAddedAt
        };
        // AddThreadedCommentReplyCommand's plain Touch: bumps only the thread-wide ModifiedAtUtc.
        comment = comment with { Replies = [.. comment.Replies, reply], ModifiedAtUtc = replyAddedAt };

        comment.RootTextEditedAtUtc.Should().BeNull("no root-text edit ever occurred in this session");
        sheet.ThreadedComments[address] = comment;

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var threadedCommentsXml = XlsxPackageTestFixtures.LoadPackageXml(
            archive, "xl/threadedComments/threadedComment1.xml");
        var elements = threadedCommentsXml.Root!.Elements(ThreadedCommentNs + "threadedComment").ToList();
        elements.Should().HaveCount(2);

        var root = elements[0];
        root.Attribute("dT")!.Value.Should().Be(
            "2026-03-10T08:00:00Z",
            "adding a reply alone must never move the root comment's own dT away from its creation time");

        var replyElement = elements[1];
        replyElement.Attribute("dT")!.Value.Should().Be("2026-03-11T09:00:00Z");
    }
}
