using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for G-comment-io findings K8 and K41:
/// - K8: Excel's @mention extLst block on a threaded comment/reply must be preserved verbatim
///   across a save, not silently dropped.
/// - K41: a threaded comment's id (and each reply's id/parentId, which reference it) must be
///   preserved from the source file across saves, not regenerated as a content hash that
///   cascade-changes reply linkage whenever the root comment's text is edited.
/// </summary>
public sealed class XlsxWorksheetThreadedCommentMapperIdAndMentionPreservationTests
{
    private static readonly XNamespace ThreadedCommentNs = "http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments";

    private const string MentionsExtLst =
        """
        <extLst xmlns="http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments"><ext uri="{DAA4B39F-CE7A-4D0B-9932-C231FA8BC017}" xmlns:mtc="http://schemas.microsoft.com/office/spreadsheetml/2018/mentions"><mtc:mentions><mtc:mention mentionpersonId="{5A2F1234-0000-0000-0000-000000000001}" mentionId="{5A2F1234-0000-0000-0000-000000000099}" startIndex="0" length="5"/></mtc:mentions></ext></extLst>
        """;

    [Fact]
    public void Save_PreservesSourceIdAndMentionsExtLst_AfterEditingUnrelatedRootText()
    {
        // Arrange: build a package whose root threaded comment carries a source id and an
        // Excel @mention extLst block, as a real Excel-authored workbook would.
        var workbook = CreateWorkbookWithReply();
        using var originalPackage = XlsxPackageTestHelper.SaveWorkbook(workbook);
        const string sourceRootId = "{11111111-0000-0000-0000-000000000001}";
        const string sourceReplyId = "{22222222-0000-0000-0000-000000000002}";
        PatchThreadedCommentPart(originalPackage, root =>
        {
            var elements = root.Elements(ThreadedCommentNs + "threadedComment").ToList();
            var rootComment = elements[0];
            var reply = elements[1];

            rootComment.SetAttributeValue("id", sourceRootId);
            rootComment.Add(XElement.Parse(MentionsExtLst));

            reply.SetAttributeValue("id", sourceReplyId);
            reply.SetAttributeValue("parentId", sourceRootId);
        });

        // Act 1: load the patched package, then edit only the root comment's text (the reply is
        // untouched), and save again.
        originalPackage.Position = 0;
        var loaded = new XlsxFileAdapter().Load(originalPackage);
        var loadedSheet = loaded.GetSheetAt(0);
        var address = new CellAddress(loadedSheet.Id, 2, 3);

        loadedSheet.ThreadedComments.Should().ContainKey(address);
        var loadedComment = loadedSheet.ThreadedComments[address];
        loadedComment.Id.Should().Be(sourceRootId);
        loadedComment.MentionsXml.Should().NotBeNullOrWhiteSpace();
        loadedComment.Replies.Should().ContainSingle();
        loadedComment.Replies[0].Id.Should().Be(sourceReplyId);

        loadedSheet.ThreadedComments[address] = loadedComment with { Text = "Please review total (v2)" };

        using var resavedPackage = new MemoryStream();
        new XlsxFileAdapter().Save(loaded, resavedPackage);

        // Assert: the freshly written package part still carries the ORIGINAL ids/parentId
        // linkage and the mentions extLst, even though the root text changed.
        resavedPackage.Position = 0;
        using var archive = new ZipArchive(resavedPackage, ZipArchiveMode.Read, leaveOpen: true);
        var threadedCommentsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/threadedComments/threadedComment1.xml");
        var savedComments = threadedCommentsXml.Root!.Elements(ThreadedCommentNs + "threadedComment").ToList();
        savedComments.Should().HaveCount(2);

        var savedRoot = savedComments[0];
        savedRoot.Attribute("id")!.Value.Should().Be(sourceRootId);
        savedRoot.Element(ThreadedCommentNs + "text")!.Value.Should().Be("Please review total (v2)");
        savedRoot.Element(ThreadedCommentNs + "extLst").Should().NotBeNull("the @mention extLst must round-trip verbatim");

        var savedReply = savedComments[1];
        savedReply.Attribute("id")!.Value.Should().Be(sourceReplyId, "an untouched reply's id must not regenerate when the root comment's text changes");
        savedReply.Attribute("parentId")!.Value.Should().Be(sourceRootId, "reply parentId linkage must stay stable across an unrelated text edit");

        // Act 2: reload the re-saved package and confirm the ids/mentions survive a second round trip too.
        resavedPackage.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(resavedPackage);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedAddress = new CellAddress(reloadedSheet.Id, 2, 3);
        var reloadedComment = reloadedSheet.ThreadedComments[reloadedAddress];
        reloadedComment.Id.Should().Be(sourceRootId);
        reloadedComment.MentionsXml.Should().NotBeNullOrWhiteSpace();
        reloadedComment.Replies[0].Id.Should().Be(sourceReplyId);
    }

    [Fact]
    public void Save_MintsAndThenPreservesStableId_ForACommentWithNoSourceId()
    {
        // A comment created fresh in FreeX (never loaded from an XLSX with an id) has no Id yet;
        // the first save must mint one, and every subsequent save must reuse that same id rather
        // than recomputing a new content hash.
        var workbook = CreateWorkbookWithReply();

        using var firstPackage = XlsxPackageTestHelper.SaveWorkbook(workbook);
        firstPackage.Position = 0;
        var loaded = new XlsxFileAdapter().Load(firstPackage);
        var sheet = loaded.GetSheetAt(0);
        var address = new CellAddress(sheet.Id, 2, 3);
        var firstSavedId = sheet.ThreadedComments[address].Id;
        firstSavedId.Should().NotBeNullOrWhiteSpace();

        // Edit only the root text, then save again.
        sheet.ThreadedComments[address] = sheet.ThreadedComments[address] with { Text = "Edited text" };
        using var secondPackage = new MemoryStream();
        new XlsxFileAdapter().Save(loaded, secondPackage);

        secondPackage.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(secondPackage);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedAddress = new CellAddress(reloadedSheet.Id, 2, 3);
        var reloadedComment = reloadedSheet.ThreadedComments[reloadedAddress];

        reloadedComment.Id.Should().Be(firstSavedId, "the id minted on first save must be preserved, not regenerated from the edited text");
        reloadedComment.Text.Should().Be("Edited text");
        reloadedComment.Replies[0].Id.Should().Be(sheet.ThreadedComments[address].Replies[0].Id);
    }

    private static Workbook CreateWorkbookWithReply()
    {
        var createdAt = new DateTimeOffset(2026, 6, 2, 10, 0, 0, TimeSpan.Zero);
        var repliedAt = new DateTimeOffset(2026, 6, 2, 10, 5, 0, TimeSpan.Zero);
        var workbook = new Workbook("ThreadedCommentIdMentionTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(address, new TextValue("Total"));
        sheet.ThreadedComments[address] = new ThreadedComment("Please review total", "Quinn")
        {
            CreatedAtUtc = createdAt,
            ModifiedAtUtc = repliedAt,
            Replies =
            [
                new CommentReply("Looks high", "Codex")
                {
                    CreatedAtUtc = repliedAt,
                    ModifiedAtUtc = repliedAt
                }
            ]
        };
        return workbook;
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
