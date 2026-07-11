using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for round-27 finding R27-comments-threaded-deep-2: editing an existing
/// threaded comment's or reply's text must update the persisted dT timestamp instead of forever
/// pinning it to the original CreatedAtUtc. The command layer (ThreadedCommentCommands.Touch)
/// only ever bumps ModifiedAtUtc, never clearing/replacing CreatedAtUtc, so these tests simulate
/// a real edit the same way: setting Text and ModifiedAtUtc together while leaving CreatedAtUtc
/// untouched -- exactly what UpdateThreadedCommentTextCommand/UpdateThreadedCommentReplyCommand
/// produce.
/// </summary>
public sealed class XlsxWorksheetThreadedCommentMapperEditedTimestampTests
{
    private static readonly XNamespace ThreadedCommentNs = "http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments";

    [Fact]
    public void Save_UpdatesRootDT_WhenRootTextIsEditedAfterLoad()
    {
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var editedAt = new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.Zero);

        var workbook = new Workbook("ThreadedRootEditTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(address, new TextValue("Total"));
        sheet.ThreadedComments[address] = new ThreadedComment("Please review total", "Quinn")
        {
            CreatedAtUtc = createdAt,
            ModifiedAtUtc = createdAt
        };

        using var firstPackage = XlsxPackageTestHelper.SaveWorkbook(workbook);
        firstPackage.Position = 0;
        var loaded = new XlsxFileAdapter().Load(firstPackage);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedAddress = new CellAddress(loadedSheet.Id, 2, 3);
        var loadedComment = loadedSheet.ThreadedComments[loadedAddress];

        // Simulate UpdateThreadedCommentTextCommand: Touch bumps ModifiedAtUtc but never clears
        // the original CreatedAtUtc.
        loadedSheet.ThreadedComments[loadedAddress] = loadedComment with
        {
            Text = "Please review total (revised)",
            ModifiedAtUtc = editedAt
        };

        using var secondPackage = new MemoryStream();
        new XlsxFileAdapter().Save(loaded, secondPackage);

        secondPackage.Position = 0;
        using (var archive = new ZipArchive(secondPackage, ZipArchiveMode.Read, leaveOpen: true))
        {
            var threadedCommentsXml = XlsxPackageTestFixtures.LoadPackageXml(
                archive, "xl/threadedComments/threadedComment1.xml");
            var root = threadedCommentsXml.Root!.Element(ThreadedCommentNs + "threadedComment")!;
            root.Element(ThreadedCommentNs + "text")!.Value.Should().Be("Please review total (revised)");
            root.Attribute("dT")!.Value.Should().Be(
                "2026-01-15T09:30:00Z",
                "editing the root comment's own text must bump its persisted dT, not pin it to the stale creation time");
        }

        secondPackage.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(secondPackage);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedAddress = new CellAddress(reloadedSheet.Id, 2, 3);
        var reloadedComment = reloadedSheet.ThreadedComments[reloadedAddress];
        reloadedComment.CreatedAtUtc.Should().Be(editedAt);
        reloadedComment.ModifiedAtUtc.Should().Be(editedAt);
    }

    [Fact]
    public void Save_UpdatesReplyDT_WhenReplyTextIsEditedAfterLoad()
    {
        var rootCreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var replyCreatedAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var replyEditedAt = new DateTimeOffset(2026, 1, 20, 14, 0, 0, TimeSpan.Zero);

        var workbook = new Workbook("ThreadedReplyEditTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(address, new TextValue("Total"));
        sheet.ThreadedComments[address] = new ThreadedComment("Please review total", "Quinn")
        {
            CreatedAtUtc = rootCreatedAt,
            ModifiedAtUtc = replyCreatedAt,
            Replies =
            [
                new CommentReply("Looks high", "Codex")
                {
                    CreatedAtUtc = replyCreatedAt,
                    ModifiedAtUtc = replyCreatedAt
                }
            ]
        };

        using var firstPackage = XlsxPackageTestHelper.SaveWorkbook(workbook);
        firstPackage.Position = 0;
        var loaded = new XlsxFileAdapter().Load(firstPackage);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedAddress = new CellAddress(loadedSheet.Id, 2, 3);
        var loadedComment = loadedSheet.ThreadedComments[loadedAddress];
        var loadedReply = loadedComment.Replies[0];

        // Simulate UpdateThreadedCommentReplyCommand: Touch bumps only this reply's ModifiedAtUtc,
        // leaving its CreatedAtUtc (and the root's own CreatedAtUtc) untouched.
        var editedReply = loadedReply with { Text = "Looks high (confirmed)", ModifiedAtUtc = replyEditedAt };
        loadedSheet.ThreadedComments[loadedAddress] = loadedComment with { Replies = [editedReply] };

        using var secondPackage = new MemoryStream();
        new XlsxFileAdapter().Save(loaded, secondPackage);

        secondPackage.Position = 0;
        using (var archive = new ZipArchive(secondPackage, ZipArchiveMode.Read, leaveOpen: true))
        {
            var threadedCommentsXml = XlsxPackageTestFixtures.LoadPackageXml(
                archive, "xl/threadedComments/threadedComment1.xml");
            var comments = threadedCommentsXml.Root!.Elements(ThreadedCommentNs + "threadedComment").ToList();
            comments.Should().HaveCount(2);

            var savedReply = comments[1];
            savedReply.Element(ThreadedCommentNs + "text")!.Value.Should().Be("Looks high (confirmed)");
            savedReply.Attribute("dT")!.Value.Should().Be(
                "2026-01-20T14:00:00Z",
                "editing a reply's own text must bump its persisted dT, not pin it to the stale creation time");
        }

        secondPackage.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(secondPackage);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedAddress = new CellAddress(reloadedSheet.Id, 2, 3);
        var reloadedReply = reloadedSheet.ThreadedComments[reloadedAddress].Replies[0];
        reloadedReply.CreatedAtUtc.Should().Be(replyEditedAt);
        reloadedReply.ModifiedAtUtc.Should().Be(replyEditedAt);
    }

    [Fact]
    public void Save_KeepsRootDTUnchanged_WhenOnlyAReplyIsAddedAfterLoad()
    {
        // Sibling already-working case this fix must NOT regress: adding a reply is a *separate*
        // <threadedComment> element in real Excel and never rewrites the root's own dT, even
        // though the root's in-memory ModifiedAtUtc is bumped to track "last thread activity"
        // (see GetThreadModifiedAt / AddThreadedCommentReplyCommand).
        var rootCreatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var replyAddedAt = new DateTimeOffset(2026, 2, 5, 0, 0, 0, TimeSpan.Zero);

        var workbook = new Workbook("ThreadedRootUnaffectedByReplyTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(address, new TextValue("Total"));
        sheet.ThreadedComments[address] = new ThreadedComment("Please review total", "Quinn")
        {
            CreatedAtUtc = rootCreatedAt,
            // As AddThreadedCommentReplyCommand's Touch would leave it: bumped to the new reply's
            // timestamp purely because a reply was appended, not because the root text changed.
            ModifiedAtUtc = replyAddedAt,
            Replies =
            [
                new CommentReply("Looks high", "Codex")
                {
                    CreatedAtUtc = replyAddedAt,
                    ModifiedAtUtc = replyAddedAt
                }
            ]
        };

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var threadedCommentsXml = XlsxPackageTestFixtures.LoadPackageXml(
            archive, "xl/threadedComments/threadedComment1.xml");
        var comments = threadedCommentsXml.Root!.Elements(ThreadedCommentNs + "threadedComment").ToList();
        comments.Should().HaveCount(2);

        var root = comments[0];
        root.Attribute("dT")!.Value.Should().Be(
            "2026-02-01T00:00:00Z",
            "adding a reply must not move the root comment's own dT away from its own creation time");

        var reply = comments[1];
        reply.Attribute("dT")!.Value.Should().Be("2026-02-05T00:00:00Z");
    }
}
