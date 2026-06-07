using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxPackageMetadataMergerTests
{
    [Fact]
    public void MergeRelationshipParts_PreservesPercentEncodedInternalTargetsForCopiedParts()
    {
        using var sourcePackage = CreatePackageWithPercentEncodedMediaRelationship();
        using var targetPackage = CreatePackageWithExistingWorksheetRelationships();
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(sourceArchive, targetArchive);
        XlsxPackageMetadataMerger.MergeRelationshipParts(sourceArchive, targetArchive, generatedEntriesBeforeMerge);

        targetArchive.GetEntry("xl/media/image 1.png").Should().NotBeNull();

        var relsXml = LoadWorksheetRelationshipsXml(targetArchive);
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        relsXml.Root!
            .Elements(relationshipNs + "Relationship")
            .Where(element =>
                element.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" &&
                element.Attribute("Target")?.Value == "../media/image%201.png")
            .Should()
            .ContainSingle();
    }

    [Fact]
    public void MergeRelationshipParts_PreservesWhitespacePaddedInternalTargetsForCopiedParts()
    {
        using var sourcePackage = CreatePackageWithWhitespacePaddedInternalMediaRelationship();
        using var targetPackage = CreatePackageWithExistingWorksheetRelationships();
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(sourceArchive, targetArchive);
        XlsxPackageMetadataMerger.MergeRelationshipParts(sourceArchive, targetArchive, generatedEntriesBeforeMerge);

        targetArchive.GetEntry("xl/media/image 1.png").Should().NotBeNull();

        var relsXml = LoadWorksheetRelationshipsXml(targetArchive);
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        relsXml.Root!
            .Elements(relationshipNs + "Relationship")
            .Where(element =>
                element.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" &&
                element.Attribute("Target")?.Value == " ../media/image%201.png ")
            .Should()
            .ContainSingle();
    }

    [Fact]
    public void MergeRelationshipParts_DeduplicatesInternalTargetsWithBackslashes()
    {
        using var sourcePackage = CreatePackageWithBackslashInternalMediaRelationship();
        using var targetPackage = CreatePackageWithMissingMediaWorksheetRelationship();
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(sourceArchive, targetArchive);
        XlsxPackageMetadataMerger.MergeRelationshipParts(sourceArchive, targetArchive, generatedEntriesBeforeMerge);

        targetArchive.GetEntry("xl/media/image 1.png").Should().NotBeNull();

        var relsXml = LoadWorksheetRelationshipsXml(targetArchive);
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        relsXml.Root!
            .Elements(relationshipNs + "Relationship")
            .Where(element =>
                element.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" &&
                element.Attribute("Target")?.Value is "../media/image%201.png" or @"..\media\image%201.png")
            .Should()
            .ContainSingle();
    }

    [Fact]
    public void MergeRelationshipParts_PreservesInternalTargetsWhenCopiedPartDiffersOnlyByCase()
    {
        using var sourcePackage = XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="png" ContentType="image/png"/>
                </Types>
                """),
            ("xl/worksheets/_rels/sheet1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdImage"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"
                                Target="../media/image1.png"/>
                </Relationships>
                """),
            ("xl/media/Image1.png", "image"));

        using var targetPackage = CreatePackageWithExistingWorksheetRelationships();
        using var source = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var target = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(source, target);
        XlsxPackageMetadataMerger.MergeRelationshipParts(source, target, generatedEntriesBeforeMerge);

        target.GetEntry("xl/media/Image1.png").Should().NotBeNull();

        var relsXml = LoadWorksheetRelationshipsXml(target);
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        relsXml.Root!
            .Elements(relationshipNs + "Relationship")
            .Where(element =>
                element.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" &&
                element.Attribute("Target")?.Value == "../media/image1.png")
            .Should()
            .ContainSingle("OPC part existence checks are case-insensitive");
    }

    [Fact]
    public void MergeRelationshipParts_PreservesExternalTargetsWithoutPackageEntriesAndRemapsIds()
    {
        using var sourcePackage = CreatePackageWithExternalWorksheetRelationship();
        using var targetPackage = CreatePackageWithExistingWorksheetRelationships();
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(sourceArchive, targetArchive);
        XlsxPackageMetadataMerger.MergeRelationshipParts(sourceArchive, targetArchive, generatedEntriesBeforeMerge);

        var relsXml = LoadWorksheetRelationshipsXml(targetArchive);
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var externalRelationships = relsXml.Root!
            .Elements(relationshipNs + "Relationship")
            .Where(element => element.Attribute("TargetMode")?.Value == "External")
            .ToList();

        externalRelationships.Should().HaveCount(2);
        externalRelationships.Should().ContainSingle(element =>
            (string?)element.Attribute("Target") == "https://example.com/docs" &&
            (string?)element.Attribute("Id") == "rIdHyperlink");
        externalRelationships.Should().ContainSingle(element =>
            (string?)element.Attribute("Target") == "https://example.com/from-source" &&
            (string?)element.Attribute("Id") != "rIdHyperlink");
    }

    [Fact]
    public void MergeRelationshipParts_RebindsCopiedDrawingImageReferenceWhenRelationshipIdCollides()
    {
        using var sourcePackage = CreatePackageWithDrawingImageRelationshipIdCollisionSource();
        using var targetPackage = CreatePackageWithDrawingImageRelationshipIdCollisionTarget();
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(sourceArchive, targetArchive);
        XlsxPackageMetadataMerger.MergeRelationshipParts(sourceArchive, targetArchive, generatedEntriesBeforeMerge);

        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationshipsXml = XlsxPackageTestFixtures.LoadPackageXml(targetArchive, "xl/drawings/_rels/drawing1.xml.rels");
        var imageRelationship = relationshipsXml.Root!
            .Elements(relationshipNs + "Relationship")
            .Single(element =>
                (string?)element.Attribute("Type") == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" &&
                (string?)element.Attribute("Target") == "../media/image1.png");
        var reboundId = imageRelationship.Attribute("Id")!.Value;

        reboundId.Should().NotBe("rIdImage");

        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(targetArchive, "xl/drawings/drawing1.xml");
        drawingXml.Root!
            .Descendants(drawingNs + "blip")
            .Should()
            .ContainSingle(element => (string?)element.Attribute(relNs + "embed") == reboundId);
        drawingXml.Root!
            .Descendants(drawingNs + "blip")
            .Should()
            .NotContain(element => (string?)element.Attribute(relNs + "embed") == "rIdImage");
    }

    [Fact]
    public void MergeRelationshipParts_PreservesWorkbookWebExtensionTaskpaneGraph()
    {
        using var sourcePackage = CreatePackageWithWorkbookWebExtensionTaskpaneGraph();
        using var targetPackage = CreatePackageWithExistingRootRelationships();
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(sourceArchive, targetArchive);
        XlsxPackageMetadataMerger.MergeContentTypes(sourceArchive, targetArchive);
        XlsxPackageMetadataMerger.MergeRelationshipParts(sourceArchive, targetArchive, generatedEntriesBeforeMerge);

        targetArchive.GetEntry("xl/webextensions/taskpanes.xml").Should().NotBeNull();
        targetArchive.GetEntry("xl/webextensions/webextension1.xml").Should().NotBeNull();
        targetArchive.GetEntry("xl/webextensions/_rels/taskpanes.xml.rels").Should().NotBeNull();

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(targetArchive, "[Content_Types].xml");
        contentTypesXml.Root!
            .Elements(contentTypeNs + "Override")
            .Should()
            .ContainSingle(element =>
                (string?)element.Attribute("PartName") == "/xl/webextensions/taskpanes.xml" &&
                (string?)element.Attribute("ContentType") == "application/vnd.ms-office.webextensiontaskpanes+xml");
        contentTypesXml.Root!
            .Elements(contentTypeNs + "Override")
            .Should()
            .ContainSingle(element =>
                (string?)element.Attribute("PartName") == "/xl/webextensions/webextension1.xml" &&
                (string?)element.Attribute("ContentType") == "application/vnd.ms-office.webextension+xml");

        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var workbookRelationshipsXml = XlsxPackageTestFixtures.LoadPackageXml(targetArchive, "xl/_rels/workbook.xml.rels");
        workbookRelationshipsXml.Root!
            .Elements(relationshipNs + "Relationship")
            .Should()
            .ContainSingle(element =>
                (string?)element.Attribute("Type") == "http://schemas.microsoft.com/office/2011/relationships/webextensiontaskpanes" &&
                (string?)element.Attribute("Target") == "webextensions/taskpanes.xml");

        var taskpanesRelationshipsXml = XlsxPackageTestFixtures.LoadPackageXml(targetArchive, "xl/webextensions/_rels/taskpanes.xml.rels");
        taskpanesRelationshipsXml.Root!
            .Elements(relationshipNs + "Relationship")
            .Should()
            .ContainSingle(element =>
                (string?)element.Attribute("Type") == "http://schemas.microsoft.com/office/2011/relationships/webextension" &&
                (string?)element.Attribute("Target") == "webextension1.xml");
    }

    [Fact]
    public void MergeRelationshipParts_DeduplicatesExternalTargetsWithTrimmedTargetMode()
    {
        using var sourcePackage = CreatePackageWithWhitespacePaddedExternalWorksheetRelationship();
        using var targetPackage = CreatePackageWithExistingWorksheetRelationships();
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(sourceArchive, targetArchive);
        XlsxPackageMetadataMerger.MergeRelationshipParts(sourceArchive, targetArchive, generatedEntriesBeforeMerge);

        var relsXml = LoadWorksheetRelationshipsXml(targetArchive);
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        relsXml.Root!
            .Elements(relationshipNs + "Relationship")
            .Where(element =>
                element.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" &&
                element.Attribute("Target")?.Value == "https://example.com/docs")
            .Should()
            .ContainSingle();
    }

    [Fact]
    public void MergeRelationshipParts_DeduplicatesExternalTargetsWithTrimmedType()
    {
        using var sourcePackage = CreatePackageWithWhitespacePaddedExternalWorksheetRelationshipType();
        using var targetPackage = CreatePackageWithExistingWorksheetRelationships();
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(sourceArchive, targetArchive);
        XlsxPackageMetadataMerger.MergeRelationshipParts(sourceArchive, targetArchive, generatedEntriesBeforeMerge);

        var relsXml = LoadWorksheetRelationshipsXml(targetArchive);
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        relsXml.Root!
            .Elements(relationshipNs + "Relationship")
            .Where(element =>
                element.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" &&
                element.Attribute("Target")?.Value == "https://example.com/docs")
            .Should()
            .ContainSingle();
    }

    [Fact]
    public void MergeRelationshipParts_SkipsCorePropertiesRelationshipsWithTrimmedType()
    {
        using var sourcePackage = CreatePackageWithWhitespacePaddedCorePropertiesRelationship();
        using var targetPackage = CreatePackageWithExistingRootRelationships();
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(sourceArchive, targetArchive);
        XlsxPackageMetadataMerger.MergeRelationshipParts(sourceArchive, targetArchive, generatedEntriesBeforeMerge);

        targetArchive.GetEntry("docProps/core.xml").Should().NotBeNull();

        var relsXml = LoadRootRelationshipsXml(targetArchive);
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationshipTypes = relsXml.Root!
            .Elements(relationshipNs + "Relationship")
            .Select(element => element.Attribute("Type")?.Value.Trim())
            .ToList();

        relationshipTypes
            .Should()
            .NotContain("http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties");
    }

    private static XDocument LoadWorksheetRelationshipsXml(ZipArchive archive) =>
        XlsxPackageTestFixtures.LoadPackageXml(
            archive,
            "xl/worksheets/_rels/sheet1.xml.rels",
            "xl/worksheets/_rels/sheet1.xml.rels");

    private static XDocument LoadRootRelationshipsXml(ZipArchive archive) =>
        XlsxPackageTestFixtures.LoadPackageXml(archive, "_rels/.rels", "_rels/.rels");
}
