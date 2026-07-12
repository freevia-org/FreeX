using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R34-io-comments-threaded-mentions-2: an author who is @mentioned by
/// someone else, but whose OWN authored comment carries no mentions of its own, must still reuse
/// their real source person id on save instead of being split into a freshly-minted second
/// &lt;person&gt; record while the original mentionpersonId reference is left dangling / duplicated.
/// </summary>
public sealed class XlsxWorksheetThreadedCommentMapperAuthorPersonIdReuseTests
{
    private static readonly XNamespace ThreadedCommentNs = "http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments";

    private const string BobPersonId = "{5A2F1234-0000-0000-0000-0000000000B0}";

    [Fact]
    public void Save_ReusesAuthorsRealPersonId_WhenAuthorIsMentionedElsewhereButOwnCommentHasNoMentions()
    {
        // Arrange: Alice's comment (with no mentions of its own yet) and Bob's separate,
        // mention-free comment, saved once to get a valid package with real person ids minted.
        var workbook = new Workbook("AuthorPersonIdReuseTest");
        var sheet = workbook.AddSheet("S1");
        var aliceAddress = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(aliceAddress, new TextValue("Total"));
        sheet.ThreadedComments[aliceAddress] = new ThreadedComment("Please review", "Alice");

        var bobAddress = new CellAddress(sheet.Id, 4, 5);
        sheet.SetCell(bobAddress, new TextValue("Detail"));
        sheet.ThreadedComments[bobAddress] = new ThreadedComment("Done", "Bob");

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);

        // Patch the source package the way a real Excel-authored file would look: Bob's own
        // comment element's personId attribute is his real, original person id (BobPersonId), and
        // Alice's comment carries a <mentions> block that references that SAME id -- but Bob's own
        // comment itself has no mentions block at all.
        XlsxPackageTestHelper.PatchPackageXml(package, "xl/persons/person.xml", document =>
        {
            var bobPerson = document.Root!.Elements(ThreadedCommentNs + "person")
                .Single(element => element.Attribute("displayName")!.Value == "Bob");
            bobPerson.SetAttributeValue("id", BobPersonId);
        });

        XlsxPackageTestHelper.PatchPackageXml(package, "xl/threadedComments/threadedComment1.xml", document =>
        {
            var elements = document.Root!.Elements(ThreadedCommentNs + "threadedComment").ToList();
            var bobComment = elements.Single(element => element.Attribute("ref")!.Value == bobAddress.ToA1());
            bobComment.SetAttributeValue("personId", BobPersonId);

            var aliceComment = elements.Single(element => element.Attribute("ref")!.Value == aliceAddress.ToA1());
            aliceComment.Add(new XElement(
                ThreadedCommentNs + "mentions",
                new XElement(
                    ThreadedCommentNs + "mention",
                    new XAttribute("mentionpersonId", BobPersonId),
                    new XAttribute("mentionId", "{5A2F1234-0000-0000-0000-0000000000M1}"),
                    new XAttribute("startIndex", "0"),
                    new XAttribute("length", "4"))));
        });

        // Act: load the patched package (bug case: Bob's own comment's SourcePersonId used to be
        // dropped here because his comment has no mentions of its own), then resave.
        package.Position = 0;
        var loaded = new XlsxFileAdapter().Load(package);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedAliceAddress = new CellAddress(loadedSheet.Id, 2, 3);
        var loadedBobAddress = new CellAddress(loadedSheet.Id, 4, 5);

        loadedSheet.ThreadedComments[loadedBobAddress].Author.Should().Be("Bob");
        // Bob's own comment has no mentions of its own, so SourcePersonId stays null on load (the
        // existing, already-working semantics of ThreadedComment.SourcePersonId are unchanged);
        // the fix instead cross-references Alice's preserved mention against Bob's author name
        // when minting/reusing ids on save, asserted below.
        loadedSheet.ThreadedComments[loadedBobAddress].SourcePersonId.Should().BeNull();
        loadedSheet.ThreadedComments[loadedAliceAddress].MentionedPersonDisplayNames.Should()
            .ContainKey(BobPersonId).WhoseValue.Should().Be("Bob");

        using var resavedPackage = new MemoryStream();
        new XlsxFileAdapter().Save(loaded, resavedPackage);

        // Assert: exactly ONE <person> record for Bob, under his real original id -- not a fresh
        // guid plus a duplicate synthetic entry to keep the dangling mention resolvable.
        resavedPackage.Position = 0;
        using var archive = new ZipArchive(resavedPackage, ZipArchiveMode.Read, leaveOpen: true);
        var personsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/persons/person.xml");
        var personElements = personsXml.Root!.Elements(ThreadedCommentNs + "person").ToList();

        personElements.Should().HaveCount(2, "there are exactly two real people (Alice, Bob) -- no duplicate person record for Bob");
        var bobPersons = personElements.Where(element => element.Attribute("displayName")!.Value == "Bob").ToList();
        bobPersons.Should().ContainSingle("Bob must have exactly one person record, not a minted duplicate alongside the preserved mention id");
        bobPersons[0].Attribute("id")!.Value.Should().Be(BobPersonId);

        // And Bob's own re-serialized comment element must use that SAME id, so his authored
        // comment and Alice's untouched mention of him both resolve to one person record.
        var threadedCommentsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/threadedComments/threadedComment1.xml");
        var savedBobComment = threadedCommentsXml.Root!.Elements(ThreadedCommentNs + "threadedComment")
            .Single(element => element.Attribute("ref")!.Value == bobAddress.ToA1());
        savedBobComment.Attribute("personId")!.Value.Should().Be(BobPersonId);
    }

    [Fact]
    public void SaveLoadSave_KeepsOnePersonRecordPerAuthor_WhenNoCommentHasAnyMentions()
    {
        // Representative already-working sibling case: plain authors with no @mention metadata
        // anywhere must still each get exactly one stable person record across repeated saves,
        // unaffected by now capturing SourcePersonId unconditionally on load.
        var workbook = new Workbook("NoMentionsBaseline");
        var sheet = workbook.AddSheet("S1");
        var aliceAddress = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(aliceAddress, new TextValue("Total"));
        sheet.ThreadedComments[aliceAddress] = new ThreadedComment("Please review", "Alice");

        var bobAddress = new CellAddress(sheet.Id, 4, 5);
        sheet.SetCell(bobAddress, new TextValue("Detail"));
        sheet.ThreadedComments[bobAddress] = new ThreadedComment("Done", "Bob");

        using var firstPackage = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var loaded = new XlsxFileAdapter().Load(firstPackage);

        using var secondPackage = new MemoryStream();
        new XlsxFileAdapter().Save(loaded, secondPackage);
        secondPackage.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(secondPackage);

        using var thirdPackage = new MemoryStream();
        new XlsxFileAdapter().Save(reloaded, thirdPackage);
        thirdPackage.Position = 0;

        using var archive = new ZipArchive(thirdPackage, ZipArchiveMode.Read, leaveOpen: true);
        var personsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/persons/person.xml");
        var personElements = personsXml.Root!.Elements(ThreadedCommentNs + "person").ToList();

        personElements.Should().HaveCount(2);
        personElements.Select(element => element.Attribute("displayName")!.Value)
            .Should().BeEquivalentTo("Alice", "Bob");
        personElements.Select(element => element.Attribute("id")!.Value).Distinct().Should().HaveCount(2);
    }
}
