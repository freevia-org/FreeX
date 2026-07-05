using System.IO;
using System.IO.Compression;
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

        saved.Position = 0;
        var reloadedSheet = new XlsxFileAdapter().Load(saved).GetSheetAt(0);
        var reloadedAddress = new CellAddress(reloadedSheet.Id, 2, 3);
        reloadedSheet.ThreadedComments.Should().ContainKey(reloadedAddress);
        var reloadedComment = reloadedSheet.ThreadedComments[reloadedAddress];
        reloadedComment.Text.Should().Be("Please review total");
        reloadedComment.Author.Should().Be("Quinn");
        reloadedComment.CreatedAtUtc.Should().Be(new DateTimeOffset(2026, 6, 2, 10, 0, 0, TimeSpan.Zero));
        reloadedComment.ModifiedAtUtc.Should().Be(new DateTimeOffset(2026, 6, 2, 10, 15, 0, TimeSpan.Zero));
        reloadedComment.IsResolved.Should().BeTrue();
        reloadedComment.Id.Should().NotBeNullOrWhiteSpace();
        reloadedComment.Replies.Should().ContainSingle().Which.Id.Should().NotBeNullOrWhiteSpace();
        reloadedComment.Replies.Should().Equal(new CommentReply("Adjusted after audit", "Codex")
        {
            CreatedAtUtc = new DateTimeOffset(2026, 6, 2, 10, 15, 0, TimeSpan.Zero),
            ModifiedAtUtc = new DateTimeOffset(2026, 6, 2, 10, 15, 0, TimeSpan.Zero),
            Id = reloadedComment.Replies[0].Id
        });
    }

    [Fact]
    public void LoadedWorkbookFullSave_WithSourceThreadedCommentsAtAlternatePaths_DropsStaleSourceParts()
    {
        using var source = Save(CreateThreadedCommentSourceWorkbook());
        MovePackageEntry(source, "xl/threadedComments/threadedComment1.xml", "xl/threadedComments/threadedComment2.xml");
        MovePackageEntry(source, "xl/persons/person.xml", "xl/persons/person2.xml");
        PatchPackageRootElement(source, "[Content_Types].xml", root =>
        {
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
            root.Elements(contentTypeNs + "Override")
                .Single(element => ThreadedAttributeValue(element, "PartName") == "/xl/threadedComments/threadedComment1.xml")
                .SetAttributeValue("PartName", "/xl/threadedComments/threadedComment2.xml");
            root.Elements(contentTypeNs + "Override")
                .Single(element => ThreadedAttributeValue(element, "PartName") == "/xl/persons/person.xml")
                .SetAttributeValue("PartName", "/xl/persons/person2.xml");
        });
        PatchPackageRootElement(source, "xl/_rels/workbook.xml.rels", root =>
        {
            XNamespace packageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            root.Elements(packageRelationshipNs + "Relationship")
                .Single(element => ThreadedAttributeValue(element, "Type") == "http://schemas.microsoft.com/office/2017/10/relationships/person")
                .SetAttributeValue("Target", "persons/person2.xml");
        });
        PatchPackageRootElement(source, "xl/worksheets/_rels/sheet1.xml.rels", root =>
        {
            XNamespace packageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            root.Elements(packageRelationshipNs + "Relationship")
                .Single(element => ThreadedAttributeValue(element, "Type") == "http://schemas.microsoft.com/office/2017/10/relationships/threadedComment")
                .SetAttributeValue("Target", "../threadedComments/threadedComment2.xml");
        });
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new NumberValue(42));
        workbook.RegisterStyle(new CellStyle { Bold = true });

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        SchemaErrors(saved).Should().BeEmpty();
        AssertThreadedCommentPackageGraph(saved);
        AssertReloadedThreadedCommentModel(adapter, saved);
        ReadPackageEntryNames(saved)
            .Should()
            .Contain("xl/threadedComments/threadedComment1.xml")
            .And.Contain("xl/persons/person.xml")
            .And.NotContain("xl/threadedComments/threadedComment2.xml")
            .And.NotContain("xl/persons/person2.xml");
    }

    [Fact]
    public void LoadedWorkbookFullSave_WithStaleThreadedCommentRelationships_DropsStaleRelationships()
    {
        using var source = Save(CreateThreadedCommentSourceWorkbook());
        PatchPackageRootElement(source, "xl/_rels/workbook.xml.rels", root =>
        {
            XNamespace packageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            root.Add(new XElement(
                packageRelationshipNs + "Relationship",
                new XAttribute("Id", "rIdStalePerson"),
                new XAttribute("Type", "http://schemas.microsoft.com/office/2017/10/relationships/person"),
                new XAttribute("Target", "https://example.invalid/person.xml"),
                new XAttribute("TargetMode", "External")));
        });
        PatchPackageRootElement(source, "xl/worksheets/_rels/sheet1.xml.rels", root =>
        {
            XNamespace packageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            root.Elements(packageRelationshipNs + "Relationship")
                .Single(element => ThreadedAttributeValue(element, "Type") == "http://schemas.microsoft.com/office/2017/10/relationships/threadedComment")
                .SetAttributeValue("Id", "rIdThreadedComment");
            root.Add(new XElement(
                packageRelationshipNs + "Relationship",
                new XAttribute("Id", "rIdStaleThreadedComment"),
                new XAttribute("Type", "http://schemas.microsoft.com/office/2017/10/relationships/threadedComment"),
                new XAttribute("Target", "https://example.invalid/threadedComment.xml"),
                new XAttribute("TargetMode", "External")));
        });
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new NumberValue(42));
        workbook.RegisterStyle(new CellStyle { Bold = true });

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        SchemaErrors(saved).Should().BeEmpty();
        AssertThreadedCommentPackageGraph(saved);
        AssertReloadedThreadedCommentModel(adapter, saved);
    }

    [Fact]
    public void LoadedWorkbookFullSave_WithModernCommentSidecar_PreservesPackageGraph()
    {
        using var source = Save(CreateThreadedCommentSourceWorkbook());
        AddModernCommentMetadataSidecar(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new NumberValue(42));
        workbook.RegisterStyle(new CellStyle { Italic = true });

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        SchemaErrors(saved).Should().BeEmpty();
        AssertThreadedCommentPackageGraph(saved);
        AssertModernCommentMetadataSidecarPackageGraph(saved);
        AssertReloadedThreadedCommentModel(adapter, saved);
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
            .Where(element => ThreadedAttributeValue(element, "Type") == "http://schemas.microsoft.com/office/2017/10/relationships/person")
            .Should()
            .ContainSingle(element =>
                ThreadedAttributeValue(element, "Target") == "persons/person.xml" &&
                ThreadedAttributeValue(element, "TargetMode") == null);

        ReadPackageRootElement(stream, "xl/worksheets/_rels/sheet1.xml.rels")
            .Elements(packageRelationshipNs + "Relationship")
            .Where(element => ThreadedAttributeValue(element, "Type") == "http://schemas.microsoft.com/office/2017/10/relationships/threadedComment")
            .Should()
            .ContainSingle(element =>
                ThreadedAttributeValue(element, "Target") == "../threadedComments/threadedComment1.xml" &&
                ThreadedAttributeValue(element, "TargetMode") == null);

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

    private static void AssertReloadedThreadedCommentModel(XlsxFileAdapter adapter, Stream stream)
    {
        stream.Position = 0;
        var reloaded = adapter.Load(stream);
        var sheet = reloaded.GetSheetAt(0);
        sheet.GetValue(4, 4).Should().Be(new NumberValue(42));

        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.ThreadedComments.Should().ContainKey(address);
        var comment = sheet.ThreadedComments[address];
        comment.Id.Should().NotBeNullOrWhiteSpace();
        comment.Replies.Should().ContainSingle().Which.Id.Should().NotBeNullOrWhiteSpace();
        comment.Should().BeEquivalentTo(new ThreadedComment("Please review total", "Quinn")
        {
            CreatedAtUtc = new DateTimeOffset(2026, 6, 2, 10, 0, 0, TimeSpan.Zero),
            ModifiedAtUtc = new DateTimeOffset(2026, 6, 2, 10, 15, 0, TimeSpan.Zero),
            IsResolved = true,
            Id = comment.Id,
            Replies =
            [
                new CommentReply("Adjusted after audit", "Codex")
                {
                    CreatedAtUtc = new DateTimeOffset(2026, 6, 2, 10, 15, 0, TimeSpan.Zero),
                    ModifiedAtUtc = new DateTimeOffset(2026, 6, 2, 10, 15, 0, TimeSpan.Zero),
                    Id = comment.Replies[0].Id
                }
            ]
        });
    }

    private static IReadOnlyList<string> ReadPackageEntryNames(Stream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        return archive.Entries.Select(entry => entry.FullName).ToList();
    }

    private static void MovePackageEntry(MemoryStream stream, string sourcePath, string targetPath)
    {
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var sourceEntry = archive.GetEntry(sourcePath);
            sourceEntry.Should().NotBeNull(sourcePath);
            using var sourceContent = new MemoryStream();
            using (var sourceEntryStream = sourceEntry!.Open())
                sourceEntryStream.CopyTo(sourceContent);

            sourceEntry.Delete();
            var targetEntry = archive.CreateEntry(targetPath, CompressionLevel.Optimal);
            sourceContent.Position = 0;
            using var targetEntryStream = targetEntry.Open();
            sourceContent.CopyTo(targetEntryStream);
        }

        stream.Position = 0;
    }

    private static void PatchPackageRootElement(MemoryStream stream, string path, Action<XElement> patchRoot)
    {
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var document = XlsxPackageTestFixtures.LoadPackageXml(archive, path, path);
            patchRoot(document.Root!);
            archive.GetEntry(path)?.Delete();
            var replacement = archive.CreateEntry(path, CompressionLevel.Optimal);
            using var replacementStream = replacement.Open();
            document.Save(replacementStream, SaveOptions.DisableFormatting);
        }

        stream.Position = 0;
    }

    private static void AddModernCommentMetadataSidecar(MemoryStream stream)
    {
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            PatchPackageRootElement(archive, "[Content_Types].xml", root =>
            {
                XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
                root.Add(new XElement(
                    contentTypeNs + "Override",
                    new XAttribute("PartName", "/xl/threadedComments/threadedCommentMetadata1.xml"),
                    new XAttribute("ContentType", "application/vnd.ms-excel.threadedcommentmetadata+xml")));
            });

            PatchPackageRootElement(archive, "xl/worksheets/_rels/sheet1.xml.rels", root =>
            {
                XNamespace packageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
                root.Add(new XElement(
                    packageRelationshipNs + "Relationship",
                    new XAttribute("Id", "rIdModernCommentMetadata"),
                    new XAttribute("Type", "http://schemas.microsoft.com/office/2021/relationships/threadedCommentMetadata"),
                    new XAttribute("Target", "../threadedComments/threadedCommentMetadata1.xml")));
            });

            WriteTextEntry(
                archive,
                "xl/threadedComments/threadedCommentMetadata1.xml",
                """
                <xltc2:threadedCommentMetadata xmlns:xltc2="http://schemas.microsoft.com/office/spreadsheetml/2021/threadedcomments2">
                  <xltc2:commentMetadata ref="C2"/>
                </xltc2:threadedCommentMetadata>
                """);
            WriteTextEntry(
                archive,
                "xl/threadedComments/_rels/threadedCommentMetadata1.xml.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdThread"
                                Type="http://schemas.microsoft.com/office/2017/10/relationships/threadedComment"
                                Target="threadedComment1.xml"/>
                  <Relationship Id="rIdPerson"
                                Type="http://schemas.microsoft.com/office/2017/10/relationships/person"
                                Target="../persons/person.xml"/>
                </Relationships>
                """);
        }

        stream.Position = 0;
    }

    private static void AssertModernCommentMetadataSidecarPackageGraph(Stream stream)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace packageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        ReadPackageEntryNames(stream)
            .Should()
            .Contain("xl/threadedComments/threadedCommentMetadata1.xml")
            .And.Contain("xl/threadedComments/_rels/threadedCommentMetadata1.xml.rels");

        ReadPackageRootElement(stream, "[Content_Types].xml")
            .Elements(contentTypeNs + "Override")
            .Should()
            .ContainSingle(element =>
                ThreadedAttributeValue(element, "PartName") == "/xl/threadedComments/threadedCommentMetadata1.xml" &&
                ThreadedAttributeValue(element, "ContentType") == "application/vnd.ms-excel.threadedcommentmetadata+xml");

        ReadPackageRootElement(stream, "xl/worksheets/_rels/sheet1.xml.rels")
            .Elements(packageRelationshipNs + "Relationship")
            .Should()
            .ContainSingle(element =>
                ThreadedAttributeValue(element, "Type") == "http://schemas.microsoft.com/office/2021/relationships/threadedCommentMetadata" &&
                ThreadedAttributeValue(element, "Target") == "../threadedComments/threadedCommentMetadata1.xml");

        ReadPackageRootElement(stream, "xl/threadedComments/_rels/threadedCommentMetadata1.xml.rels")
            .Elements(packageRelationshipNs + "Relationship")
            .Should()
            .Contain(element =>
                ThreadedAttributeValue(element, "Type") == "http://schemas.microsoft.com/office/2017/10/relationships/threadedComment" &&
                ThreadedAttributeValue(element, "Target") == "threadedComment1.xml")
            .And
            .Contain(element =>
                ThreadedAttributeValue(element, "Type") == "http://schemas.microsoft.com/office/2017/10/relationships/person" &&
                ThreadedAttributeValue(element, "Target") == "../persons/person.xml");
    }

    private static string? ThreadedAttributeValue(XElement element, string name) =>
        element.Attribute(name)?.Value;

    private static void PatchPackageRootElement(ZipArchive archive, string path, Action<XElement> patchRoot)
    {
        var document = XlsxPackageTestFixtures.LoadPackageXml(archive, path, path);
        patchRoot(document.Root!);
        archive.GetEntry(path)?.Delete();
        var replacement = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var replacementStream = replacement.Open();
        document.Save(replacementStream, SaveOptions.DisableFormatting);
    }

    private static void WriteTextEntry(ZipArchive archive, string path, string content)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }
}
