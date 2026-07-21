using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R56-io-comments-threaded-5-2: two DISTINCT real authors who happen to
/// share the same threaded-comment displayName (e.g. two people with the same name from different
/// organizations in a cross-company review workbook) must stay two separate &lt;person&gt; records
/// across a load/resave round trip, not silently collapse into one.
/// </summary>
public sealed class XlsxWorksheetThreadedCommentMapperPersonIdentityCollisionTests
{
    private static readonly XNamespace ThreadedCommentNs = "http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments";

    private const string AlexKimFromContosoId = "{5A2F1234-0000-0000-0000-0000000000C1}";
    private const string AlexKimFromFabrikamId = "{5A2F1234-0000-0000-0000-0000000000F1}";

    [Fact]
    public void Save_KeepsTwoDistinctPersonRecords_WhenTwoAuthorsShareADisplayName()
    {
        // Arrange: build a package the way real Excel/M365 would for a cross-organization review --
        // two genuinely distinct persons.xml entries that happen to share the displayName
        // "Alex Kim", each authoring an unrelated threaded comment on a different cell.
        var workbook = new Workbook("PersonIdentityCollisionTest");
        var sheet = workbook.AddSheet("S1");
        var contosoAddress = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(contosoAddress, new TextValue("Total"));
        sheet.ThreadedComments[contosoAddress] = new ThreadedComment("Looks good", "Alex Kim");

        var fabrikamAddress = new CellAddress(sheet.Id, 4, 4);
        sheet.SetCell(fabrikamAddress, new TextValue("Detail"));
        sheet.ThreadedComments[fabrikamAddress] = new ThreadedComment("Please revise", "Alex Kim");

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);

        // Patch the source package so the two comments' authors resolve to two DIFFERENT person
        // ids, exactly like a real Excel-authored file with a genuine displayName collision.
        XlsxPackageTestHelper.PatchPackageXml(package, "xl/persons/person.xml", document =>
        {
            var personElements = document.Root!.Elements(ThreadedCommentNs + "person").ToList();
            personElements.Should().HaveCount(1, "the single-author save above minted one shared person record to split apart");

            personElements[0].SetAttributeValue("id", AlexKimFromContosoId);
            document.Root!.Add(new XElement(
                ThreadedCommentNs + "person",
                new XAttribute("displayName", "Alex Kim"),
                new XAttribute("id", AlexKimFromFabrikamId)));
        });

        XlsxPackageTestHelper.PatchPackageXml(package, "xl/threadedComments/threadedComment1.xml", document =>
        {
            var elements = document.Root!.Elements(ThreadedCommentNs + "threadedComment").ToList();
            var contosoComment = elements.Single(element => element.Attribute("ref")!.Value == contosoAddress.ToA1());
            contosoComment.SetAttributeValue("personId", AlexKimFromContosoId);

            var fabrikamComment = elements.Single(element => element.Attribute("ref")!.Value == fabrikamAddress.ToA1());
            fabrikamComment.SetAttributeValue("personId", AlexKimFromFabrikamId);
        });

        // Act: load the patched package (bug case: ReadThreadedComments resolved both comments'
        // Author purely via displayName, losing which real person wrote which), then resave.
        package.Position = 0;
        var loaded = new XlsxFileAdapter().Load(package);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedContosoAddress = new CellAddress(loadedSheet.Id, 2, 2);
        var loadedFabrikamAddress = new CellAddress(loadedSheet.Id, 4, 4);

        // Sanity: the fix must capture each comment's own source person id unconditionally, even
        // though neither comment carries any @mention metadata.
        loadedSheet.ThreadedComments[loadedContosoAddress].SourcePersonId.Should().Be(AlexKimFromContosoId);
        loadedSheet.ThreadedComments[loadedFabrikamAddress].SourcePersonId.Should().Be(AlexKimFromFabrikamId);

        using var resavedPackage = new MemoryStream();
        new XlsxFileAdapter().Save(loaded, resavedPackage);

        // Assert: exactly TWO <person> records survive -- one per real person -- not one merged
        // record under a single freshly-minted or first-wins id.
        resavedPackage.Position = 0;
        using var archive = new ZipArchive(resavedPackage, ZipArchiveMode.Read, leaveOpen: true);
        var personsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/persons/person.xml");
        var savedPersonElements = personsXml.Root!.Elements(ThreadedCommentNs + "person").ToList();

        savedPersonElements.Should().HaveCount(2, "there are two distinct real people named \"Alex Kim\" -- they must not be merged into one person record");
        savedPersonElements.Select(element => element.Attribute("id")!.Value)
            .Should().BeEquivalentTo([AlexKimFromContosoId, AlexKimFromFabrikamId]);
        savedPersonElements.Should().OnlyContain(element => element.Attribute("displayName")!.Value == "Alex Kim");

        // And each comment's re-serialized personId must still point at ITS OWN real person, not
        // have been repointed at whichever id happened to win a displayName-keyed collapse.
        var threadedCommentsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/threadedComments/threadedComment1.xml");
        var savedContosoComment = threadedCommentsXml.Root!.Elements(ThreadedCommentNs + "threadedComment")
            .Single(element => element.Attribute("ref")!.Value == contosoAddress.ToA1());
        var savedFabrikamComment = threadedCommentsXml.Root!.Elements(ThreadedCommentNs + "threadedComment")
            .Single(element => element.Attribute("ref")!.Value == fabrikamAddress.ToA1());

        savedContosoComment.Attribute("personId")!.Value.Should().Be(AlexKimFromContosoId);
        savedFabrikamComment.Attribute("personId")!.Value.Should().Be(AlexKimFromFabrikamId);
    }

    [Fact]
    public void Save_KeepsOnePersonRecord_WhenTheSameAuthorPostsMultipleCommentsWithNoCollision()
    {
        // Sibling no-regression case: the ordinary (non-colliding) scenario -- one real author
        // posting several threaded comments -- must still collapse to exactly ONE stable person
        // record across saves, unaffected by now resolving personId per-comment via
        // ResolvePersonId/ResolvePersonIdentitiesById instead of purely by author displayName.
        var workbook = new Workbook("NoCollisionBaseline");
        var sheet = workbook.AddSheet("S1");
        var firstAddress = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(firstAddress, new TextValue("Total"));
        sheet.ThreadedComments[firstAddress] = new ThreadedComment("Looks good", "Priya Singh");

        var secondAddress = new CellAddress(sheet.Id, 4, 4);
        sheet.SetCell(secondAddress, new TextValue("Detail"));
        sheet.ThreadedComments[secondAddress] = new ThreadedComment("One more note", "Priya Singh");

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

        personElements.Should().ContainSingle("one real author writing two comments must still get exactly one person record");
        personElements[0].Attribute("displayName")!.Value.Should().Be("Priya Singh");

        var threadedCommentsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/threadedComments/threadedComment1.xml");
        var personId = personElements[0].Attribute("id")!.Value;
        threadedCommentsXml.Root!.Elements(ThreadedCommentNs + "threadedComment")
            .Should().OnlyContain(element => element.Attribute("personId")!.Value == personId);
    }
}
