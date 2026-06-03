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

        var relsXml = LoadXml(targetArchive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
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

        var relsXml = LoadXml(targetArchive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
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

        var relsXml = LoadXml(targetArchive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
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
    public void MergeRelationshipParts_PreservesExternalTargetsWithoutPackageEntriesAndRemapsIds()
    {
        using var sourcePackage = CreatePackageWithExternalWorksheetRelationship();
        using var targetPackage = CreatePackageWithExistingWorksheetRelationships();
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(sourceArchive, targetArchive);
        XlsxPackageMetadataMerger.MergeRelationshipParts(sourceArchive, targetArchive, generatedEntriesBeforeMerge);

        var relsXml = LoadXml(targetArchive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
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
    public void MergeRelationshipParts_DeduplicatesExternalTargetsWithTrimmedTargetMode()
    {
        using var sourcePackage = CreatePackageWithWhitespacePaddedExternalWorksheetRelationship();
        using var targetPackage = CreatePackageWithExistingWorksheetRelationships();
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(sourceArchive, targetArchive);
        XlsxPackageMetadataMerger.MergeRelationshipParts(sourceArchive, targetArchive, generatedEntriesBeforeMerge);

        var relsXml = LoadXml(targetArchive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
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

        var relsXml = LoadXml(targetArchive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
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

        var relsXml = LoadXml(targetArchive.GetEntry("_rels/.rels")!);
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationshipTypes = relsXml.Root!
            .Elements(relationshipNs + "Relationship")
            .Select(element => element.Attribute("Type")?.Value.Trim())
            .ToList();

        relationshipTypes
            .Should()
            .NotContain("http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties");
    }
}
