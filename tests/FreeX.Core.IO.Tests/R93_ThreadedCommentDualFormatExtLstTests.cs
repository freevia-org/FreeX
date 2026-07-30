using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R93 threaded-comment-extLst backlog probe: enumerates the paths called out by the backlog
/// item -- a thread with a reply, a resolved thread, a sheet carrying BOTH a threaded comment
/// (with its legacy compatibility shim, written by the real product writer) and an independent
/// legacy note, and a sheet with only a legacy note (no threaded comments at all) -- through the
/// real <see cref="XlsxFileAdapter"/> Load/Save entry points. Verifies the thread's own @mention
/// <c>extLst</c>, the resolved <c>done</c> flag, and reply parentId linkage all survive, and that
/// the legacy shim/note stay exactly one entry each (no orphan, no duplicate).
///
/// The scaffold (styles/VML/legacy-comment/threaded-comment parts) is produced by the real
/// <see cref="XlsxFileAdapter"/> writer -- guaranteed ClosedXML/Excel-compatible -- and only the
/// already-valid threadedComments part is hand-patched afterward (mirrors
/// <see cref="XlsxWorksheetThreadedCommentMapperIdAndMentionPreservationTests"/>) to add the
/// @mention extLst and the done="1" resolved flag, rather than hand-building an entire raw XLSX
/// package from scratch (fragile and not representative of what the product actually emits).
/// </summary>
public sealed class R93_ThreadedCommentDualFormatExtLstTests
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace ThreadedCommentNs = "http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments";

    private const string MentionsExtLst =
        """
        <extLst xmlns="http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments"><ext uri="{DAA4B39F-CE7A-4D0B-9932-C231FA8BC017}" xmlns:mtc="http://schemas.microsoft.com/office/spreadsheetml/2018/mentions"><mtc:mentions><mtc:mention mentionpersonId="{5A2F1234-0000-0000-0000-000000000001}" mentionId="{5A2F1234-0000-0000-0000-000000000099}" startIndex="0" length="5"/></mtc:mentions></ext></extLst>
        """;

    [Fact]
    public void DualFormat_ThreadWithReplyMentionsExtLstAndResolvedFlag_RoundTripsThroughRealAdapter()
    {
        // Arrange: a sheet with BOTH a threaded comment (reply included) AND a fully independent
        // legacy note, written entirely through the real product writer first.
        var workbook = new Workbook("R93DualFormat");
        var sheet = workbook.AddSheet("S1");
        var threadAddress = new CellAddress(sheet.Id, 5, 2); // B5
        var noteAddress = new CellAddress(sheet.Id, 2, 3); // C2
        sheet.SetCell(threadAddress, new TextValue("review"));
        sheet.SetCell(noteAddress, new TextValue("note"));
        sheet.Comments[noteAddress] = "Confidential";
        sheet.ThreadedComments[threadAddress] = new ThreadedComment("Please review", "Quinn")
        {
            CreatedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Replies =
            [
                new CommentReply("Agreed", "Codex")
                {
                    CreatedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 5, 0, TimeSpan.Zero),
                    ModifiedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 5, 0, TimeSpan.Zero)
                }
            ]
        };

        using var basePackage = XlsxPackageTestHelper.SaveWorkbook(workbook);

        // Patch the (already product-valid) threadedComments part to add the @mention extLst and
        // mark the thread resolved -- exercising the two attributes the backlog claims are lost.
        PatchThreadedCommentPart(basePackage, root =>
        {
            var root0 = root.Elements(ThreadedCommentNs + "threadedComment").First(e => e.Attribute("parentId") is null);
            root0.SetAttributeValue("done", "1");
            root0.Add(XElement.Parse(MentionsExtLst));
        });

        var adapter = new XlsxFileAdapter();
        basePackage.Position = 0;
        var loaded = adapter.Load(basePackage);
        var loadedSheet = loaded.GetSheetAt(0);
        threadAddress = new CellAddress(loadedSheet.Id, 5, 2);
        noteAddress = new CellAddress(loadedSheet.Id, 2, 3);

        loadedSheet.ThreadedComments.Should().ContainKey(threadAddress);
        var loadedThread = loadedSheet.ThreadedComments[threadAddress];
        loadedThread.IsResolved.Should().BeTrue("the source thread was marked done=\"1\"");
        loadedThread.MentionsXml.Should().NotBeNullOrWhiteSpace("the @mention extLst must be captured on load");
        loadedThread.Replies.Should().ContainSingle();
        loadedSheet.Comments.Should().ContainSingle().Which.Value.Should().Be("Confidential");

        // Act: save unchanged through the real entry point (no model edits at all).
        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        // Assert: the resaved threadedComments part still has exactly one root + one reply, the
        // extLst intact, and done="1" preserved.
        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var threadedXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/threadedComments/threadedComment1.xml");
            var elements = threadedXml.Root!.Elements(ThreadedCommentNs + "threadedComment").ToList();
            elements.Should().HaveCount(2, "exactly one root + one reply, no duplication");

            var root = elements.Single(e => e.Attribute("parentId") is null);
            root.Attribute("done")?.Value.Should().Be("1", "a resolved thread must stay resolved after a lossless save");
            root.Element(ThreadedCommentNs + "extLst").Should().NotBeNull("the @mention extLst must round-trip verbatim");

            var reply = elements.Single(e => e.Attribute("parentId") is not null);
            reply.Attribute("parentId")!.Value.Should().Be(root.Attribute("id")!.Value);

            // Legacy comments1.xml must keep exactly one shim entry (for the thread) and exactly
            // one real note entry (Confidential) -- no orphan, no duplicate.
            var legacyXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/comments1.xml");
            var legacyEntries = legacyXml.Root!.Element(MainNs + "commentList")!.Elements(MainNs + "comment").ToList();
            legacyEntries.Should().HaveCount(2);
            legacyEntries.Count(e => e.Attribute("ref")?.Value == "B5").Should().Be(1, "the threaded-comment shim must appear exactly once");
            legacyEntries.Count(e => e.Attribute("ref")?.Value == "C2").Should().Be(1, "the independent legacy note must appear exactly once");
        }

        // Sibling check: reload the resaved package and confirm the model still sees everything.
        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedThreadAddress = new CellAddress(reloadedSheet.Id, 5, 2);
        var reloadedThread = reloadedSheet.ThreadedComments[reloadedThreadAddress];
        reloadedThread.IsResolved.Should().BeTrue();
        reloadedThread.MentionsXml.Should().NotBeNullOrWhiteSpace();
        reloadedThread.Replies.Should().ContainSingle();
        reloadedSheet.Comments.Should().ContainSingle().Which.Value.Should().Be("Confidential");
    }

    [Fact]
    public void LegacyNoteOnly_NoThreadedComments_DoesNotGainABogusThread()
    {
        // Sibling/no-regression path: a sheet that has ONLY a legacy note (no threaded comments at
        // all) must not sprout a phantom thread across a real Save/Load round trip.
        var workbook = new Workbook("R93LegacyOnly");
        var sheet = workbook.AddSheet("S1");
        var noteAddress = new CellAddress(sheet.Id, 2, 3); // C2
        sheet.SetCell(noteAddress, new TextValue("note"));
        sheet.Comments[noteAddress] = "Confidential";

        using var sourcePackage = XlsxPackageTestHelper.SaveWorkbook(workbook);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(sourcePackage);
        var loadedSheet = loaded.GetSheetAt(0);

        loadedSheet.ThreadedComments.Should().BeEmpty();
        loadedSheet.Comments.Should().ContainSingle().Which.Value.Should().Be("Confidential");

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            (archive.GetEntry("xl/threadedComments/threadedComment1.xml") is null).Should().BeTrue(
                "a legacy-only sheet must not gain a bogus threaded-comment part");
        }

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        reloaded.GetSheetAt(0).ThreadedComments.Should().BeEmpty();
        reloaded.GetSheetAt(0).Comments.Should().ContainSingle().Which.Value.Should().Be("Confidential");
    }

    private static void PatchThreadedCommentPart(MemoryStream package, Action<XElement> patchRoot)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            const string path = "xl/threadedComments/threadedComment1.xml";
            var entry = archive.GetEntry(path)!;
            var document = XlsxPackageTestFixtures.LoadPackageXml(entry);
            patchRoot(document.Root!);

            entry.Delete();
            var replacement = archive.CreateEntry(path, CompressionLevel.Optimal);
            using var stream = replacement.Open();
            document.Save(stream, SaveOptions.DisableFormatting);
        }

        package.Position = 0;
    }
}
