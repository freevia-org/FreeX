using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-12 fix bucket Q3 regression coverage.
///
/// R12-comments-notes-1 [HIGH]: deleting every threaded comment from a loaded workbook and saving
/// must actually remove xl/threadedComments/*.xml and xl/persons/person.xml (and their
/// relationships) from the saved package -- not silently resurrect them from the source package's
/// preserved parts.
///
/// R12-comments-notes-3 [MED]: an @mention referencing a person who never authors any
/// comment/reply in the workbook must not dangle after a save; that mentioned person's
/// &lt;person&gt; record must still be written to the rewritten xl/persons/person.xml.
/// </summary>
public sealed class FreeXR12Q3Tests
{
    private static readonly XNamespace ThreadedCommentNs =
        "http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments";
    private static readonly XNamespace PackageRelNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private const string MentionsElement =
        """
        <mentions xmlns="http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments"><mention mentionpersonId="{B0B00000-0000-0000-0000-000000000001}" mentionId="{B0B00000-0000-0000-0000-000000000099}" startIndex="0" length="3"/></mentions>
        """;

    [Fact]
    public void Save_AfterDeletingAllThreadedComments_DoesNotResurrectSourceThreadedCommentsOrPersonParts()
    {
        // Arrange: an Excel-shaped workbook with one threaded comment on Sheet1 (source package
        // has xl/threadedComments/threadedComment1.xml + xl/persons/person.xml + the worksheet's
        // and workbook's relationships to them).
        var workbook = CreateWorkbookWithComment();
        using var originalPackage = XlsxPackageTestHelper.SaveWorkbook(workbook);

        originalPackage.Position = 0;
        using (var archive = new ZipArchive(originalPackage, ZipArchiveMode.Read, leaveOpen: true))
        {
            archive.GetEntry("xl/threadedComments/threadedComment1.xml").Should().NotBeNull(
                "the fixture save must have produced a threaded-comment part to delete");
            archive.GetEntry("xl/persons/person.xml").Should().NotBeNull(
                "the fixture save must have produced a persons part to delete");
        }

        // Act: load the workbook (registers the source package for preservation), delete every
        // threaded comment from the model (as DeleteThreadedCommentCommand/ClearCommentsCommand
        // would), and save again.
        originalPackage.Position = 0;
        var loaded = new XlsxFileAdapter().Load(originalPackage);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.ThreadedComments.Should().NotBeEmpty("the source package has a comment to delete");
        loadedSheet.ThreadedComments.Clear();
        loaded.Sheets.Any(XlsxWorksheetThreadedCommentMapper.HasThreadedComments).Should().BeFalse();

        using var resavedPackage = new MemoryStream();
        new XlsxFileAdapter().Save(loaded, resavedPackage);

        // Assert: the deletion must actually persist -- both in the reloaded model and in the raw
        // saved package (no resurrected threadedComments/persons parts or relationships).
        resavedPackage.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(resavedPackage);
        reloaded.GetSheetAt(0).ThreadedComments.Should().BeEmpty(
                "deleting every threaded comment and saving must not resurrect them on reload");

        resavedPackage.Position = 0;
        using var savedArchive = new ZipArchive(resavedPackage, ZipArchiveMode.Read, leaveOpen: true);
        savedArchive.GetEntry("xl/threadedComments/threadedComment1.xml").Should().BeNull(
            "the deleted threaded-comment part must not be copied back from the source package");
        savedArchive.GetEntry("xl/persons/person.xml").Should().BeNull(
            "the deleted persons part must not be copied back from the source package");

        var worksheetRelsEntry = savedArchive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels");
        if (worksheetRelsEntry is not null)
        {
            var worksheetRelsXml = XlsxPackageTestFixtures.LoadPackageXml(worksheetRelsEntry);
            worksheetRelsXml.Root!.Elements(PackageRelNs + "Relationship")
                .Should().NotContain(element =>
                    string.Equals(
                        (string?)element.Attribute("Type"),
                        "http://schemas.microsoft.com/office/2017/10/relationships/threadedComment",
                        StringComparison.OrdinalIgnoreCase),
                    "the worksheet must not keep a relationship to a deleted threaded-comment part");
        }

        var workbookRelsXml = XlsxPackageTestFixtures.LoadPackageXml(savedArchive, "xl/_rels/workbook.xml.rels");
        workbookRelsXml.Root!.Elements(PackageRelNs + "Relationship")
            .Should().NotContain(element =>
                string.Equals(
                    (string?)element.Attribute("Type"),
                    "http://schemas.microsoft.com/office/2017/10/relationships/person",
                    StringComparison.OrdinalIgnoreCase),
                "the workbook must not keep a relationship to a deleted persons part");
    }

    [Fact]
    public void Save_MentionOfNonAuthoringPerson_WritesMentionedPersonRecordSoTheReferenceDoesNotDangle()
    {
        // Arrange: root comment authored by Quinn @-mentions Bob, but Bob never authors any
        // comment/reply anywhere in the workbook -- exactly the shape a real Excel-authored
        // workbook produces when you @mention a collaborator who hasn't replied yet.
        var workbook = CreateWorkbookWithComment();
        using var originalPackage = XlsxPackageTestHelper.SaveWorkbook(workbook);

        const string bobPersonId = "{B0B00000-0000-0000-0000-000000000001}";
        const string bobDisplayName = "Bob";
        PatchThreadedCommentAndPersonParts(originalPackage, (commentsRoot, personsRoot) =>
        {
            var rootComment = commentsRoot.Elements(ThreadedCommentNs + "threadedComment").Single();
            rootComment.Add(XElement.Parse(MentionsElement));

            // Bob has a person record in the SOURCE package (Excel always writes one for anyone
            // who has ever been @mentioned), but Bob never authors a comment/reply.
            personsRoot.Add(new XElement(
                ThreadedCommentNs + "person",
                new XAttribute("displayName", bobDisplayName),
                new XAttribute("id", bobPersonId)));
        });

        // Act: load the patched package, edit only the root comment's unrelated text, and save.
        originalPackage.Position = 0;
        var loaded = new XlsxFileAdapter().Load(originalPackage);
        var loadedSheet = loaded.GetSheetAt(0);
        var address = new CellAddress(loadedSheet.Id, 2, 3);
        var loadedComment = loadedSheet.ThreadedComments[address];
        loadedSheet.ThreadedComments[address] = loadedComment with { Text = "Please review total (v2)" };

        using var resavedPackage = new MemoryStream();
        new XlsxFileAdapter().Save(loaded, resavedPackage);

        // Assert: Bob's person record must still be present in the rewritten persons part, even
        // though Bob never authored a comment/reply, so the round-tripped mentionpersonId resolves.
        resavedPackage.Position = 0;
        using var savedArchive = new ZipArchive(resavedPackage, ZipArchiveMode.Read, leaveOpen: true);
        var personsXml = XlsxPackageTestFixtures.LoadPackageXml(savedArchive, "xl/persons/person.xml");
        var writtenPersons = personsXml.Root!.Elements(ThreadedCommentNs + "person").ToList();
        writtenPersons.Should().Contain(
            person => (string?)person.Attribute("id") == bobPersonId,
            "a mentioned person who never authors a comment/reply must still get a <person> record so the mentionpersonId reference does not dangle");
        writtenPersons.Single(person => (string?)person.Attribute("id") == bobPersonId)
            .Attribute("displayName")!.Value.Should().Be(bobDisplayName);

        // The mention itself must still reference Bob's (unchanged) id.
        var threadedCommentsXml = XlsxPackageTestFixtures.LoadPackageXml(savedArchive, "xl/threadedComments/threadedComment1.xml");
        var savedRoot = threadedCommentsXml.Root!.Elements(ThreadedCommentNs + "threadedComment").Single();
        var savedMention = savedRoot.Element(ThreadedCommentNs + "mentions")?.Element(ThreadedCommentNs + "mention");
        savedMention.Should().NotBeNull("the <mentions> element must round-trip on save");
        savedMention!.Attribute("mentionpersonId")!.Value.Should().Be(bobPersonId);
    }

    private static Workbook CreateWorkbookWithComment()
    {
        var createdAt = new DateTimeOffset(2026, 6, 2, 10, 0, 0, TimeSpan.Zero);
        var workbook = new Workbook("R12Q3ThreadedCommentTest");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(address, new TextValue("Total"));
        sheet.ThreadedComments[address] = new ThreadedComment("Please review total", "Quinn")
        {
            CreatedAtUtc = createdAt,
            ModifiedAtUtc = createdAt
        };
        return workbook;
    }

    private static void PatchThreadedCommentAndPersonParts(
        MemoryStream package,
        Action<XElement, XElement> patchRoots)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            const string commentsPath = "xl/threadedComments/threadedComment1.xml";
            const string personsPath = "xl/persons/person.xml";
            var commentsEntry = archive.GetEntry(commentsPath)!;
            var personsEntry = archive.GetEntry(personsPath)!;
            var commentsDocument = XlsxPackageTestFixtures.LoadPackageXml(commentsEntry);
            var personsDocument = XlsxPackageTestFixtures.LoadPackageXml(personsEntry);

            patchRoots(commentsDocument.Root!, personsDocument.Root!);

            commentsEntry.Delete();
            var commentsReplacement = archive.CreateEntry(commentsPath, CompressionLevel.Optimal);
            using (var stream = commentsReplacement.Open())
                commentsDocument.Save(stream, SaveOptions.DisableFormatting);

            personsEntry.Delete();
            var personsReplacement = archive.CreateEntry(personsPath, CompressionLevel.Optimal);
            using (var stream = personsReplacement.Open())
                personsDocument.Save(stream, SaveOptions.DisableFormatting);
        }

        package.Position = 0;
    }
}
