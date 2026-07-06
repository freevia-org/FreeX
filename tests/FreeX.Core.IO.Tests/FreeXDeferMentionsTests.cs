using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Deferred-fix regression coverage (group P3): Excel's real @mention metadata is a direct
/// &lt;mentions&gt; child of &lt;threadedComment&gt; (a sibling of &lt;text&gt;, per
/// CT_ThreadedComment) -- not something folded into &lt;extLst&gt;. XlsxWorksheetThreadedCommentMapper
/// must read that element, preserve it verbatim across a full save (re-emitting it as a real
/// &lt;mentions&gt; child, not dropping it), and must not let a preserved
/// <c>mention/@mentionpersonId</c> reference dangle when the persons part is rewritten on
/// save (the referenced person's id must be preserved rather than replaced by a freshly minted
/// guid).
/// </summary>
public sealed class FreeXDeferMentionsTests
{
    private static readonly XNamespace ThreadedCommentNs =
        "http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments";

    private const string MentionedPersonId = "{5A2F1234-0000-0000-0000-000000000001}";

    // Excel's real CT_ThreadedComment.mentions element (a direct sibling of <text>, per the
    // 2018 threadedcomments schema) -- NOT the legacy extLst/mtc extension shape.
    private const string MentionsElement =
        """
        <mentions xmlns="http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments"><mention mentionpersonId="{5A2F1234-0000-0000-0000-000000000001}" mentionId="{5A2F1234-0000-0000-0000-000000000099}" startIndex="0" length="5"/></mentions>
        """;

    [Fact]
    public void Load_ThenFullSaveAndReload_PreservesRealMentionsElementAndPersonIdReference()
    {
        // Arrange: build a package whose root threaded comment carries a real <mentions> child
        // element (a sibling of <text>, per CT_ThreadedComment) referencing a person id that is
        // present in xl/persons/person.xml -- exactly as real Excel 365 writes @mentions.
        var workbook = CreateWorkbookWithComment();
        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);

        PatchThreadedCommentPart(package, root =>
        {
            var rootComment = root.Elements(ThreadedCommentNs + "threadedComment").Single();
            // The comment's own author personId is reused as the mentioned person id so the
            // reference is guaranteed to resolve against a person actually present in the
            // persons part written for "Quinn".
            rootComment.Add(XElement.Parse(MentionsElement.Replace(MentionedPersonId, rootComment.Attribute("personId")!.Value)));
        });

        // Act 1: load the patched package.
        package.Position = 0;
        var loaded = new XlsxFileAdapter().Load(package);
        var loadedSheet = loaded.GetSheetAt(0);
        var address = new CellAddress(loadedSheet.Id, 2, 3);

        // Assert: the real <mentions> element (not merely <extLst>) was captured, and the source
        // personId was preserved alongside it.
        loadedSheet.ThreadedComments.Should().ContainKey(address);
        var loadedComment = loadedSheet.ThreadedComments[address];
        loadedComment.MentionsXml.Should().NotBeNullOrWhiteSpace();
        loadedComment.MentionsXml.Should().Contain("<mention ", "the real <mentions> child element must be captured, not dropped");
        loadedComment.SourcePersonId.Should().NotBeNullOrWhiteSpace();

        var mentionPersonIdBeforeSave = loadedComment.SourcePersonId;

        // Act 2: edit only the unrelated root text and force a full save, then reload.
        loadedSheet.ThreadedComments[address] = loadedComment with { Text = "Please review total (v2)" };
        using var resavedPackage = new MemoryStream();
        new XlsxFileAdapter().Save(loaded, resavedPackage);

        resavedPackage.Position = 0;
        using (var archive = new ZipArchive(resavedPackage, ZipArchiveMode.Read, leaveOpen: true))
        {
            var threadedCommentsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/threadedComments/threadedComment1.xml");
            var savedRoot = threadedCommentsXml.Root!.Elements(ThreadedCommentNs + "threadedComment").Single();

            // (a) The <mentions> element must round-trip as a real child element, not be dropped.
            var savedMentions = savedRoot.Element(ThreadedCommentNs + "mentions");
            savedMentions.Should().NotBeNull("the real <mentions> element must round-trip on a full save, not be dropped");
            savedMentions!.Elements().Should().ContainSingle();
            var savedMentionPersonId = savedMentions!.Elements().Single().Attribute("mentionpersonId")!.Value;

            // (b) The mentionpersonId must still resolve to a person id present in the rewritten
            // persons part -- no dangling reference after CreateAuthorIds/person-part rewrite.
            var personsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/persons/person.xml");
            var writtenPersonIds = personsXml.Root!
                .Elements(ThreadedCommentNs + "person")
                .Select(person => person.Attribute("id")!.Value)
                .ToList();
            writtenPersonIds.Should().Contain(savedMentionPersonId, "a preserved mentionpersonId reference must not dangle after the persons part is rewritten");

            // The saved comment's own personId must be the preserved source id, not a fresh mint.
            savedRoot.Attribute("personId")!.Value.Should().Be(mentionPersonIdBeforeSave);
        }

        // Act 3: reload the re-saved package and confirm everything survives a second round trip.
        resavedPackage.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(resavedPackage);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedAddress = new CellAddress(reloadedSheet.Id, 2, 3);
        var reloadedComment = reloadedSheet.ThreadedComments[reloadedAddress];

        reloadedComment.Text.Should().Be("Please review total (v2)");
        reloadedComment.MentionsXml.Should().NotBeNullOrWhiteSpace();
        reloadedComment.MentionsXml.Should().Contain("<mention ");
        reloadedComment.SourcePersonId.Should().Be(mentionPersonIdBeforeSave);
    }

    private static Workbook CreateWorkbookWithComment()
    {
        var createdAt = new DateTimeOffset(2026, 6, 2, 10, 0, 0, TimeSpan.Zero);
        var workbook = new Workbook("DeferMentionsTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(address, new TextValue("Total"));
        sheet.ThreadedComments[address] = new ThreadedComment("Please review total", "Quinn")
        {
            CreatedAtUtc = createdAt,
            ModifiedAtUtc = createdAt
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
