using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R110-core-io-2: <see cref="XlsxWorksheetThreadedCommentMapper"/> groups every parsed
/// &lt;threadedComment&gt; reply (an element with a <c>parentId</c>) by that parentId, but the ONLY
/// enumeration path that consumes those groups walks roots (elements with no <c>parentId</c>) and
/// looks up a matching group by the root's own id. A reply group whose parentId never matches any
/// root actually present in the part -- e.g. the root was deleted by a third-party/non-Excel writer,
/// a hand-edit, or an Excel co-authoring merge conflict, while its replies survived -- was
/// previously left completely unconsumed and silently dropped, with no exception and no diagnostic.
/// Because this happens on LOAD, the very next Save made the loss permanent (Save only re-serializes
/// what reached <c>Sheet.ThreadedComments</c>). These tests exercise the real product entry point
/// (<see cref="XlsxFileAdapter"/> Load/Save), never a hand-built model.
/// </summary>
public sealed class R110_ThreadedCommentOrphanedReplyRecoveryTests
{
    private static readonly XNamespace ThreadedCommentNs = "http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments";

    [Fact]
    public void Load_OrphanedReplyWithOwnAddress_IsRecoveredAsSyntheticRootInsteadOfSilentlyDropped()
    {
        // Arrange: a normal round trip through the real Save entry point produces a valid
        // threadedComments1.xml with a root ("Please review total") and one reply ("Looks high")
        // parented to it. We then hand-edit that saved package -- simulating a third-party/non-Excel
        // writer or a co-authoring merge conflict -- to delete the ROOT element while leaving the
        // reply element in place, and give the orphaned reply its own `ref` (schema-legal: `ref` is
        // optional on every CT_ThreadedComment, not just roots) so the malformed file still carries
        // enough information to place the surviving content on a cell.
        using var package = XlsxPackageTestHelper.SaveWorkbook(CreateWorkbookWithRootAndReply());

        string orphanedParentId;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var threadedCommentsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/threadedComments/threadedComment1.xml");
            var elements = threadedCommentsXml.Root!.Elements(ThreadedCommentNs + "threadedComment").ToList();
            var root = elements.Single(e => e.Attribute("parentId") is null);
            var reply = elements.Single(e => e.Attribute("parentId") is not null);

            orphanedParentId = root.Attribute("id")!.Value;
            reply.Attribute("parentId")!.Value.Should().Be(orphanedParentId, "sanity: the reply must actually be parented to the root we are about to delete");
            reply.SetAttributeValue("ref", "C2");

            root.Remove();

            archive.GetEntry("xl/threadedComments/threadedComment1.xml")!.Delete();
            var entry = archive.CreateEntry("xl/threadedComments/threadedComment1.xml", CompressionLevel.Optimal);
            using var stream = entry.Open();
            threadedCommentsXml.Save(stream, SaveOptions.DisableFormatting);
        }

        // Act: load through the real product entry point.
        package.Position = 0;
        var loaded = new XlsxFileAdapter().Load(package);
        var sheet = loaded.GetSheetAt(0);
        var loadedAddress = new CellAddress(sheet.Id, 2, 3); // C2

        // Assert: the orphaned reply's content survived -- promoted to stand in as the thread's
        // root -- instead of vanishing with no trace.
        sheet.ThreadedComments.Should().ContainKey(loadedAddress,
            "the orphaned reply carried its own cell address and must be recovered, not dropped");
        var recovered = sheet.ThreadedComments[loadedAddress];
        recovered.Text.Should().Be("Looks high");
        recovered.Author.Should().Be("Codex");
        recovered.Replies.Should().BeEmpty("the orphaned group had only this one member");

        // Sibling: the recovered thread must survive a further Save/Load, proving the content is
        // now durably part of the model and not merely visible on this one load.
        using var resaved = new MemoryStream();
        new XlsxFileAdapter().Save(loaded, resaved);
        resaved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(resaved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedAddress = new CellAddress(reloadedSheet.Id, 2, 3);
        reloadedSheet.ThreadedComments.Should().ContainKey(reloadedAddress);
        reloadedSheet.ThreadedComments[reloadedAddress].Text.Should().Be("Looks high");
    }

    [Fact]
    public void Load_ReplyWithMatchingRoot_StillAttachesNormally()
    {
        // No-regression sibling: the ordinary case (a reply's parentId DOES match a root present in
        // the part) must still attach the reply under its root exactly as before -- the new
        // orphan-recovery pass must never re-home or duplicate a reply that was already consumed by
        // the root loop.
        using var package = XlsxPackageTestHelper.SaveWorkbook(CreateWorkbookWithRootAndReply());

        package.Position = 0;
        var loaded = new XlsxFileAdapter().Load(package);
        var sheet = loaded.GetSheetAt(0);
        var loadedAddress = new CellAddress(sheet.Id, 2, 3); // C2

        sheet.ThreadedComments.Should().ContainKey(loadedAddress);
        var comment = sheet.ThreadedComments[loadedAddress];
        comment.Text.Should().Be("Please review total");
        comment.Author.Should().Be("Quinn");
        comment.Replies.Should().HaveCount(1);
        comment.Replies[0].Text.Should().Be("Looks high");
        comment.Replies[0].Author.Should().Be("Codex");

        // Exactly one ThreadedComments entry on the sheet: no phantom synthetic root was created for
        // the reply that was legitimately consumed by its matching root.
        sheet.ThreadedComments.Should().HaveCount(1);
    }

    private static Workbook CreateWorkbookWithRootAndReply()
    {
        var rootCreatedAt = new DateTimeOffset(2026, 6, 2, 10, 0, 0, TimeSpan.Zero);
        var replyCreatedAt = new DateTimeOffset(2026, 6, 2, 10, 5, 0, TimeSpan.Zero);
        var workbook = new Workbook("R110OrphanedReplyTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 2, 3); // C2
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
        return workbook;
    }
}
