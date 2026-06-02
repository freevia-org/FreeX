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
                AttributeValue(element, "PartName") == "/xl/threadedComments/threadedComment1.xml" &&
                AttributeValue(element, "ContentType") == "application/vnd.ms-excel.threadedcomments+xml")
            .And.Contain(element =>
                AttributeValue(element, "PartName") == "/xl/persons/person.xml" &&
                AttributeValue(element, "ContentType") == "application/vnd.ms-excel.person+xml");

        workbookRelsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Should()
            .Contain(element =>
                AttributeValue(element, "Type") == "http://schemas.microsoft.com/office/2017/10/relationships/person" &&
                AttributeValue(element, "Target") == "persons/person.xml");
        worksheetRelsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Should()
            .Contain(element =>
                AttributeValue(element, "Type") == "http://schemas.microsoft.com/office/2017/10/relationships/threadedComment" &&
                AttributeValue(element, "Target") == "../threadedComments/threadedComment1.xml");
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

    [Fact]
    public void Save_WritesThreadedCommentRepliesAsParentedPackageElements()
    {
        using var package = SaveWorkbook(CreateWorkbookWithReplies());

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var threadedCommentsXml = LoadXml(archive, "xl/threadedComments/threadedComment1.xml");
        var personsXml = LoadXml(archive, "xl/persons/person.xml");

        var personIds = personsXml.Root!
            .Elements(ThreadedCommentNs + "person")
            .ToDictionary(
                person => person.Attribute("displayName")!.Value,
                person => person.Attribute("id")!.Value,
                StringComparer.Ordinal);

        personIds.Keys.Should().BeEquivalentTo("Quinn", "Codex", "Dana");

        var comments = threadedCommentsXml.Root!
            .Elements(ThreadedCommentNs + "threadedComment")
            .ToList();
        comments.Should().HaveCount(3);

        var root = comments[0];
        var rootId = root.Attribute("id")!.Value;
        root.Attribute("ref")!.Value.Should().Be("C2");
        root.Attribute("personId")!.Value.Should().Be(personIds["Quinn"]);
        root.Attribute("parentId").Should().BeNull();
        root.Attribute("dT")!.Value.Should().Be("2026-06-02T10:00:00Z");
        root.Attribute("done")!.Value.Should().Be("1");
        root.Element(ThreadedCommentNs + "text")!.Value.Should().Be("Please review total");

        comments[1].Attribute("ref")!.Value.Should().Be("C2");
        comments[1].Attribute("personId")!.Value.Should().Be(personIds["Codex"]);
        comments[1].Attribute("parentId")!.Value.Should().Be(rootId);
        comments[1].Attribute("dT")!.Value.Should().Be("2026-06-02T10:05:00Z");
        comments[1].Element(ThreadedCommentNs + "text")!.Value.Should().Be("Looks high");

        comments[2].Attribute("ref")!.Value.Should().Be("C2");
        comments[2].Attribute("personId")!.Value.Should().Be(personIds["Dana"]);
        comments[2].Attribute("parentId")!.Value.Should().Be(rootId);
        comments[2].Attribute("dT")!.Value.Should().Be("2026-06-02T10:10:00Z");
        comments[2].Element(ThreadedCommentNs + "text")!.Value.Should().Be("Updated after audit");
    }

    [Fact]
    public void SaveLoadSaveLoad_RoundTripsThreadedCommentRepliesAndResolvedMetadata()
    {
        using var firstPackage = SaveWorkbook(CreateWorkbookWithReplies());

        firstPackage.Position = 0;
        var loaded = new XlsxFileAdapter().Load(firstPackage);

        using var secondPackage = SaveWorkbook(loaded);
        secondPackage.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(secondPackage);
        var sheet = reloaded.GetSheetAt(0);
        var loadedAddress = new CellAddress(sheet.Id, 2, 3);

        sheet.ThreadedComments.Should().ContainKey(loadedAddress);
        var comment = sheet.ThreadedComments[loadedAddress];
        comment.Text.Should().Be("Please review total");
        comment.Author.Should().Be("Quinn");
        comment.CreatedAtUtc.Should().Be(new DateTimeOffset(2026, 6, 2, 10, 0, 0, TimeSpan.Zero));
        comment.ModifiedAtUtc.Should().Be(new DateTimeOffset(2026, 6, 2, 10, 10, 0, TimeSpan.Zero));
        comment.IsResolved.Should().BeTrue();
        comment.Replies.Should().Equal(
            new CommentReply("Looks high", "Codex")
            {
                CreatedAtUtc = new DateTimeOffset(2026, 6, 2, 10, 5, 0, TimeSpan.Zero),
                ModifiedAtUtc = new DateTimeOffset(2026, 6, 2, 10, 5, 0, TimeSpan.Zero)
            },
            new CommentReply("Updated after audit", "Dana")
            {
                CreatedAtUtc = new DateTimeOffset(2026, 6, 2, 10, 10, 0, TimeSpan.Zero),
                ModifiedAtUtc = new DateTimeOffset(2026, 6, 2, 10, 10, 0, TimeSpan.Zero)
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

    private static Workbook CreateWorkbookWithReplies()
    {
        var rootCreatedAt = new DateTimeOffset(2026, 6, 2, 10, 0, 0, TimeSpan.Zero);
        var firstReplyCreatedAt = new DateTimeOffset(2026, 6, 2, 10, 5, 0, TimeSpan.Zero);
        var secondReplyCreatedAt = new DateTimeOffset(2026, 6, 2, 10, 10, 0, TimeSpan.Zero);
        var workbook = new Workbook("ThreadedRepliesXlsxTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(address, new TextValue("Total"));
        sheet.ThreadedComments[address] = new ThreadedComment("Please review total", "Quinn")
        {
            CreatedAtUtc = rootCreatedAt,
            ModifiedAtUtc = secondReplyCreatedAt,
            IsResolved = true,
            Replies =
            [
                new CommentReply("Looks high", "Codex")
                {
                    CreatedAtUtc = firstReplyCreatedAt,
                    ModifiedAtUtc = firstReplyCreatedAt
                },
                new CommentReply("Updated after audit", "Dana")
                {
                    CreatedAtUtc = secondReplyCreatedAt,
                    ModifiedAtUtc = secondReplyCreatedAt
                }
            ]
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

    private static string? AttributeValue(XElement element, string name) =>
        element.Attribute(name)?.Value;
}
