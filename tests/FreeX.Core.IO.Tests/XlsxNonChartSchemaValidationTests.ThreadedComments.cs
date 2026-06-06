using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    [Fact]
    public void ThreadedComments_ProducesSchemaValidWorkbook()
    {
        using var stream = Save(CreateThreadedCommentSourceWorkbook());

        SchemaErrors(stream).Should().BeEmpty();
        AssertThreadedCommentPackageGraph(stream);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithThreadedComments_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateThreadedCommentSourceWorkbook());
        var sourceThreadedComments = ReadPackageRootElement(source, "xl/threadedComments/threadedComment1.xml");
        var sourcePersons = ReadPackageRootElement(source, "xl/persons/person.xml");
        var sourceWorkbookRelationships = ReadPackageRootElement(source, "xl/_rels/workbook.xml.rels");
        var sourceWorksheetRelationships = ReadPackageRootElement(source, "xl/worksheets/_rels/sheet1.xml.rels");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        AssertThreadedCommentPackageGraph(saved);
        ReadPackageRootElement(saved, "xl/threadedComments/threadedComment1.xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceThreadedComments.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/persons/person.xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourcePersons.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/_rels/workbook.xml.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWorkbookRelationships.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/worksheets/_rels/sheet1.xml.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWorksheetRelationships.ToString(SaveOptions.DisableFormatting));
    }

    private static Workbook CreateThreadedCommentSourceWorkbook()
    {
        var workbook = new Workbook("ThreadedCommentPatchSave");
        var sheet = workbook.AddSheet("Data");
        var address = new CellAddress(sheet.Id, 2, 3);
        var createdAt = new DateTimeOffset(2026, 6, 2, 10, 0, 0, TimeSpan.Zero);
        var repliedAt = new DateTimeOffset(2026, 6, 2, 10, 15, 0, TimeSpan.Zero);

        SeedNumericGrid(sheet);
        sheet.SetCell(address, new TextValue("Total"));
        sheet.ThreadedComments[address] = new ThreadedComment("Please review total", "Quinn")
        {
            CreatedAtUtc = createdAt,
            ModifiedAtUtc = repliedAt,
            IsResolved = true,
            Replies =
            [
                new CommentReply("Adjusted after audit", "Codex")
                {
                    CreatedAtUtc = repliedAt,
                    ModifiedAtUtc = repliedAt
                }
            ]
        };

        return workbook;
    }

    private static void AssertThreadedCommentPackageGraph(Stream stream)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace packageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace threadedCommentNs = "http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments";

        ReadPackageRootElement(stream, "[Content_Types].xml")
            .Elements(contentTypeNs + "Override")
            .Should()
            .Contain(element =>
                ThreadedAttributeValue(element, "PartName") == "/xl/threadedComments/threadedComment1.xml" &&
                ThreadedAttributeValue(element, "ContentType") == "application/vnd.ms-excel.threadedcomments+xml")
            .And
            .Contain(element =>
                ThreadedAttributeValue(element, "PartName") == "/xl/persons/person.xml" &&
                ThreadedAttributeValue(element, "ContentType") == "application/vnd.ms-excel.person+xml");

        ReadPackageRootElement(stream, "xl/_rels/workbook.xml.rels")
            .Elements(packageRelationshipNs + "Relationship")
            .Should()
            .Contain(element =>
                ThreadedAttributeValue(element, "Type") == "http://schemas.microsoft.com/office/2017/10/relationships/person" &&
                ThreadedAttributeValue(element, "Target") == "persons/person.xml");

        ReadPackageRootElement(stream, "xl/worksheets/_rels/sheet1.xml.rels")
            .Elements(packageRelationshipNs + "Relationship")
            .Should()
            .Contain(element =>
                ThreadedAttributeValue(element, "Type") == "http://schemas.microsoft.com/office/2017/10/relationships/threadedComment" &&
                ThreadedAttributeValue(element, "Target") == "../threadedComments/threadedComment1.xml");

        var threadedComments = ReadPackageRootElement(stream, "xl/threadedComments/threadedComment1.xml");
        threadedComments.Name.Should().Be(threadedCommentNs + "ThreadedComments");
        threadedComments.Elements(threadedCommentNs + "threadedComment")
            .Should()
            .HaveCount(2);

        ReadPackageRootElement(stream, "xl/persons/person.xml")
            .Elements(threadedCommentNs + "person")
            .Select(element => element.Attribute("displayName")?.Value)
            .Should()
            .BeEquivalentTo("Quinn", "Codex");
    }

    private static string? ThreadedAttributeValue(XElement element, string name) =>
        element.Attribute(name)?.Value;
}
