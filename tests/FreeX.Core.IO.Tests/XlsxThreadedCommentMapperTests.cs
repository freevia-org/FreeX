using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxThreadedCommentMapperTests
{
    private static readonly XNamespace ThreadedCommentNs = "http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    [Fact]
    public void Save_WritesRootThreadedCommentPackageParts()
    {
        var createdAt = new DateTimeOffset(2026, 6, 2, 10, 30, 0, TimeSpan.Zero);
        using var package = SaveWorkbook(CreateWorkbook(createdAt));

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var threadedCommentsXml = LoadXml(archive, "xl/threadedComments/threadedComment1.xml");
        var personsXml = LoadXml(archive, "xl/persons/person.xml");
        var contentTypesXml = LoadXml(archive, "[Content_Types].xml");
        var workbookRelsXml = LoadXml(archive, "xl/_rels/workbook.xml.rels");
        var worksheetRelsXml = LoadXml(archive, "xl/worksheets/_rels/sheet1.xml.rels");

        var person = personsXml.Root!.Element(ThreadedCommentNs + "person")!;
        person.Attribute("displayName")!.Value.Should().Be("Quinn");
        var personId = person.Attribute("id")!.Value;

        var comment = threadedCommentsXml.Root!.Element(ThreadedCommentNs + "threadedComment")!;
        comment.Attribute("ref")!.Value.Should().Be("C2");
        comment.Attribute("personId")!.Value.Should().Be(personId);
        comment.Attribute("dT")!.Value.Should().Be("2026-06-02T10:30:00Z");
        comment.Attribute("done")!.Value.Should().Be("1");
        comment.Element(ThreadedCommentNs + "text")!.Value.Should().Be("Please review total");

        contentTypesXml.Root!
            .Elements(ContentTypeNs + "Override")
            .Should()
            .Contain(element =>
                element.Attribute("PartName")?.Value == "/xl/threadedComments/threadedComment1.xml" &&
                element.Attribute("ContentType")?.Value == "application/vnd.ms-excel.threadedcomments+xml")
            .And.Contain(element =>
                element.Attribute("PartName")?.Value == "/xl/persons/person.xml" &&
                element.Attribute("ContentType")?.Value == "application/vnd.ms-excel.person+xml");

        workbookRelsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Should()
            .Contain(element =>
                element.Attribute("Type")?.Value == "http://schemas.microsoft.com/office/2017/10/relationships/person" &&
                element.Attribute("Target")?.Value == "persons/person.xml");
        worksheetRelsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Should()
            .Contain(element =>
                element.Attribute("Type")?.Value == "http://schemas.microsoft.com/office/2017/10/relationships/threadedComment" &&
                element.Attribute("Target")?.Value == "../threadedComments/threadedComment1.xml");
    }

    [Fact]
    public void SaveLoad_RoundTripsRootThreadedComment()
    {
        var createdAt = new DateTimeOffset(2026, 6, 2, 10, 30, 0, TimeSpan.Zero);
        using var package = SaveWorkbook(CreateWorkbook(createdAt));

        package.Position = 0;
        var loaded = new XlsxFileAdapter().Load(package);
        var sheet = loaded.GetSheetAt(0);
        var loadedAddress = new CellAddress(sheet.Id, 2, 3);

        sheet.ThreadedComments.Should().ContainKey(loadedAddress);
        sheet.ThreadedComments[loadedAddress].Should().Be(new ThreadedComment("Please review total", "Quinn")
        {
            CreatedAtUtc = createdAt,
            ModifiedAtUtc = createdAt,
            IsResolved = true
        });
    }

    private static Workbook CreateWorkbook(DateTimeOffset createdAt)
    {
        var workbook = new Workbook("ThreadedRootXlsxTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(address, new TextValue("Total"));
        sheet.ThreadedComments[address] = new ThreadedComment("Please review total", "Quinn")
        {
            CreatedAtUtc = createdAt,
            ModifiedAtUtc = createdAt,
            IsResolved = true
        };
        return workbook;
    }

    private static MemoryStream SaveWorkbook(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    private static XDocument LoadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        entry.Should().NotBeNull(path);
        return XlsxPackageXmlEditor.LoadXml(entry!);
    }
}
