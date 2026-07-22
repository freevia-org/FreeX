using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R74-io-comments-threaded-4-2 regression coverage: XlsxWorksheetThreadedCommentMapper's
/// ReadPersons captured only a person's id+displayName, and WritePersonsPart re-emitted only
/// those two attributes on every save that rewrites <c>xl/persons/person.xml</c> -- silently
/// discarding <c>userId</c>/<c>providerId</c> (and any <c>extLst</c>) that Excel uses to resolve
/// @mentions to a real account.
///
/// The new <see cref="XlsxWorksheetThreadedCommentMapper.ReadPersonRecords"/>/<see cref="PersonRecord"/>
/// + <see cref="XlsxWorksheetThreadedCommentMapper.Save"/>'s optional
/// <c>sourcePersonRecordsById</c> parameter let a caller with access to the ORIGINAL source
/// package preserve those attributes across such a save.
///
/// NOTE: the actual production call site
/// (<c>XlsxFileAdapter.SavePostProcessing.cs</c>, which invokes
/// <see cref="XlsxWorksheetThreadedCommentMapper.Save"/> without this parameter) needs updating
/// to obtain and pass those records for the fix to take effect end-to-end; that file is out of
/// scope for the io-comments bucket, so these tests exercise the mapper's new capability
/// directly.
/// </summary>
public sealed class XlsxWorksheetThreadedCommentMapperPersonIdentityPreservationTests
{
    private static readonly XNamespace ThreadedCommentNs =
        "http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments";

    [Fact]
    public void ReadPersonRecords_CapturesUserIdAndProviderId_AndOmitsThemWhenAbsent()
    {
        const string richPersonId = "{5A2F1234-0000-0000-0000-000000000AAA}";
        const string plainPersonId = "{5A2F1234-0000-0000-0000-000000000BBB}";
        var personXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <personList xmlns="http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments">
              <person displayName="Jane Doe" id="{richPersonId}" userId="jane@x.com" providerId="AD"/>
              <person displayName="Plain Author" id="{plainPersonId}"/>
            </personList>
            """;

        using var package = XlsxPackageTestFixtures.CreatePackage(("xl/persons/person.xml", personXml));
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var records = XlsxWorksheetThreadedCommentMapper.ReadPersonRecords(archive);

        records.Should().ContainKey(richPersonId);
        records[richPersonId].DisplayName.Should().Be("Jane Doe");
        records[richPersonId].UserId.Should().Be("jane@x.com");
        records[richPersonId].ProviderId.Should().Be("AD");

        records.Should().ContainKey(plainPersonId);
        records[plainPersonId].DisplayName.Should().Be("Plain Author");
        records[plainPersonId].UserId.Should().BeNull(
            "a person with no source userId must not fabricate one");
        records[plainPersonId].ProviderId.Should().BeNull(
            "a person with no source providerId must not fabricate one");
    }

    [Fact]
    public void Save_WithSourcePersonRecords_PreservesUserIdAndProviderId_ForMatchedPerson_NoSpuriousAttrsForUnmatched()
    {
        // Arrange: a workbook with two threaded-comment authors. Jane's comment carries her real,
        // stable source person id (as XlsxWorksheetThreadedCommentMapper.Read captures it
        // unconditionally on load -- see ThreadedComment.SourcePersonId); Bob has no such source
        // record at all (e.g. a freshly-authored, never-before-saved comment).
        const string janePersonId = "{5A2F1234-0000-0000-0000-000000000AAA}";
        var workbook = new Workbook("PersonIdentityPreservationTest");
        var sheet = workbook.AddSheet("S1");
        var janeAddress = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(janeAddress, new TextValue("Total"));
        sheet.ThreadedComments[janeAddress] = new ThreadedComment("Please review", "Jane Doe")
        {
            SourcePersonId = janePersonId
        };
        var bobAddress = new CellAddress(sheet.Id, 4, 5);
        sheet.SetCell(bobAddress, new TextValue("Detail"));
        sheet.ThreadedComments[bobAddress] = new ThreadedComment("Done", "Bob");

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);

        XlsxWorkbookWorksheetPathMap? worksheetPathMap;
        using (var readArchive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
            worksheetPathMap = XlsxWorkbookWorksheetPathMap.TryCreate(readArchive);
        worksheetPathMap.Should().NotBeNull();

        var sourceRecords = new Dictionary<string, PersonRecord>
        {
            [janePersonId] = new PersonRecord("Jane Doe", "jane@x.com", "AD", null)
        };

        // Act: re-run the mapper's own Save directly with the preserved source records -- this is
        // the capability the production call site (out of scope for this bucket) would wire in.
        package.Position = 0;
        XlsxWorksheetThreadedCommentMapper.Save(package, workbook, worksheetPathMap, sourceRecords);

        // Assert
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var personsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/persons/person.xml");
        var personElements = personsXml.Root!.Elements(ThreadedCommentNs + "person").ToList();

        var janeElement = personElements.Single(e => e.Attribute("displayName")!.Value == "Jane Doe");
        janeElement.Attribute("id")!.Value.Should().Be(janePersonId);
        janeElement.Attribute("userId")!.Value.Should().Be("jane@x.com",
            "userId must be preserved across a save that rewrites person.xml (R74-io-comments-threaded-4-2)");
        janeElement.Attribute("providerId")!.Value.Should().Be("AD",
            "providerId must be preserved across a save that rewrites person.xml (R74-io-comments-threaded-4-2)");

        var bobElement = personElements.Single(e => e.Attribute("displayName")!.Value == "Bob");
        bobElement.Attribute("userId").Should().BeNull(
            "a person with no matching source record must not get a spurious empty userId attribute");
        bobElement.Attribute("providerId").Should().BeNull(
            "a person with no matching source record must not get a spurious empty providerId attribute");
    }

    [Fact]
    public void Save_WithoutSourcePersonRecords_StillRoundTripsDisplayNameAndId()
    {
        // Sibling no-regression case: omitting the new optional parameter entirely (the default,
        // unwired production behavior today) must still produce a valid, working person.xml --
        // exactly as before this change.
        var workbook = new Workbook("NoSourceRecordsBaseline");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(address, new TextValue("Total"));
        sheet.ThreadedComments[address] = new ThreadedComment("Please review", "Alice");

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var personsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/persons/person.xml");
        var personElement = personsXml.Root!.Elements(ThreadedCommentNs + "person").Single();
        personElement.Attribute("displayName")!.Value.Should().Be("Alice");
        personElement.Attribute("id").Should().NotBeNull();
        personElement.Attribute("userId").Should().BeNull();
        personElement.Attribute("providerId").Should().BeNull();
    }
}
