using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using Freexcel.Core.IO;

namespace Freexcel.Core.IO.Tests;

public sealed class XlsxPackageMetadataMergerTests
{
    [Fact]
    public void MergeContentTypes_PreservesDefaultsAndSkipsExcludedOverrides()
    {
        using var sourcePackage = CreatePackageWithAdditionalContentTypes();
        using var targetPackage = CreatePackageWithExistingContentTypes();
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        XlsxPackageMetadataMerger.MergeContentTypes(
            sourceArchive,
            targetArchive,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "xl/media/image1.png" });

        var contentTypesXml = LoadXml(targetArchive.GetEntry("[Content_Types].xml")!);
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

        contentTypesXml.Root!
            .Elements(contentTypeNs + "Default")
            .Should()
            .ContainSingle(element =>
                (string?)element.Attribute("Extension") == "png" &&
                (string?)element.Attribute("ContentType") == "image/png");
        contentTypesXml.Root!
            .Elements(contentTypeNs + "Override")
            .Should()
            .ContainSingle(element => (string?)element.Attribute("PartName") == "/xl/worksheets/sheet2.xml");
        contentTypesXml.Root!
            .Elements(contentTypeNs + "Override")
            .Should()
            .NotContain(element => (string?)element.Attribute("PartName") == "/xl/media/image1.png");
    }

    [Fact]
    public void MergeContentTypes_DeduplicatesOverridesWithEquivalentRootedPartNames()
    {
        using var sourcePackage = CreatePackageWithUnrootedWorksheetOverride();
        using var targetPackage = CreatePackageWithExistingContentTypes();
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        XlsxPackageMetadataMerger.MergeContentTypes(sourceArchive, targetArchive);

        var contentTypesXml = LoadXml(targetArchive.GetEntry("[Content_Types].xml")!);
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

        contentTypesXml.Root!
            .Elements(contentTypeNs + "Override")
            .Where(element => ((string?)element.Attribute("PartName"))?.TrimStart('/') == "xl/worksheets/sheet1.xml")
            .Should()
            .ContainSingle();
    }

    [Fact]
    public void MergeContentTypes_DeduplicatesOverridesWithEquivalentTrimmedPartNames()
    {
        using var sourcePackage = CreatePackageWithWhitespacePaddedWorksheetOverride();
        using var targetPackage = CreatePackageWithExistingContentTypes();
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        XlsxPackageMetadataMerger.MergeContentTypes(sourceArchive, targetArchive);

        var contentTypesXml = LoadXml(targetArchive.GetEntry("[Content_Types].xml")!);
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

        contentTypesXml.Root!
            .Elements(contentTypeNs + "Override")
            .Where(element => ((string?)element.Attribute("PartName"))?.Trim().TrimStart('/') == "xl/worksheets/sheet1.xml")
            .Should()
            .ContainSingle();
    }

    [Fact]
    public void MergeContentTypes_SkipsExcludedOverridesWithEquivalentTrimmedPartNames()
    {
        using var sourcePackage = CreatePackageWithWhitespacePaddedExcludedImageOverride();
        using var targetPackage = CreatePackageWithExistingContentTypes();
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        XlsxPackageMetadataMerger.MergeContentTypes(
            sourceArchive,
            targetArchive,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "xl/media/image1.png" });

        var contentTypesXml = LoadXml(targetArchive.GetEntry("[Content_Types].xml")!);
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

        var overridePartNames = contentTypesXml.Root!
            .Elements(contentTypeNs + "Override")
            .Select(element => element.Attribute("PartName")?.Value.Trim().TrimStart('/'))
            .ToList();

        overridePartNames
            .Should()
            .NotContain("xl/media/image1.png");
    }

    [Fact]
    public void MergeContentTypes_DeduplicatesDefaultsWithEquivalentExtensions()
    {
        using var sourcePackage = CreatePackageWithEquivalentImageDefaultExtension();
        using var targetPackage = CreatePackageWithExistingImageDefault();
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        XlsxPackageMetadataMerger.MergeContentTypes(sourceArchive, targetArchive);

        var contentTypesXml = LoadXml(targetArchive.GetEntry("[Content_Types].xml")!);
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

        contentTypesXml.Root!
            .Elements(contentTypeNs + "Default")
            .Where(element => string.Equals(
                ((string?)element.Attribute("Extension"))?.Trim().TrimStart('.'),
                "png",
                StringComparison.OrdinalIgnoreCase))
            .Should()
            .ContainSingle();
    }

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

    private static MemoryStream CreatePackageWithAdditionalContentTypes()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="png" ContentType="image/png"/>
                  <Override PartName="/xl/media/image1.png"
                            ContentType="image/png"/>
                  <Override PartName="/xl/worksheets/sheet2.xml"
                            ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreatePackageWithExistingContentTypes()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml"
                            ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreatePackageWithUnrootedWorksheetOverride()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="xl/worksheets/sheet1.xml"
                            ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreatePackageWithWhitespacePaddedWorksheetOverride()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName=" /xl/worksheets/sheet1.xml "
                            ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreatePackageWithWhitespacePaddedExcludedImageOverride()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName=" /xl/media/image1.png "
                            ContentType="image/png"/>
                </Types>
                """);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreatePackageWithEquivalentImageDefaultExtension()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension=" .PNG " ContentType="image/png"/>
                </Types>
                """);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreatePackageWithExistingImageDefault()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="png" ContentType="image/png"/>
                </Types>
                """);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreatePackageWithPercentEncodedMediaRelationship()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="png" ContentType="image/png"/>
                </Types>
                """);
            WritePackageEntry(archive, "xl/worksheets/_rels/sheet1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdImage"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"
                                Target="../media/image%201.png"/>
                </Relationships>
                """);
            var mediaEntry = archive.CreateEntry("xl/media/image 1.png");
            using var mediaStream = mediaEntry.Open();
            mediaStream.Write([0x89, 0x50, 0x4E, 0x47]);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreatePackageWithWhitespacePaddedInternalMediaRelationship()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="png" ContentType="image/png"/>
                </Types>
                """);
            WritePackageEntry(archive, "xl/worksheets/_rels/sheet1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdImage"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"
                                Target=" ../media/image%201.png "/>
                </Relationships>
                """);
            var mediaEntry = archive.CreateEntry("xl/media/image 1.png");
            using var mediaStream = mediaEntry.Open();
            mediaStream.Write([0x89, 0x50, 0x4E, 0x47]);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreatePackageWithBackslashInternalMediaRelationship()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="png" ContentType="image/png"/>
                </Types>
                """);
            WritePackageEntry(archive, "xl/worksheets/_rels/sheet1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdImage"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"
                                Target="..\media\image%201.png"/>
                </Relationships>
                """);
            var mediaEntry = archive.CreateEntry("xl/media/image 1.png");
            using var mediaStream = mediaEntry.Open();
            mediaStream.Write([0x89, 0x50, 0x4E, 0x47]);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreatePackageWithExternalWorksheetRelationship()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                </Types>
                """);
            WritePackageEntry(archive, "xl/worksheets/_rels/sheet1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdHyperlink"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink"
                                Target="https://example.com/from-source"
                                TargetMode="External"/>
                </Relationships>
                """);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreatePackageWithWhitespacePaddedExternalWorksheetRelationship()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                </Types>
                """);
            WritePackageEntry(archive, "xl/worksheets/_rels/sheet1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdHyperlink"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink"
                                Target="https://example.com/docs"
                                TargetMode=" External "/>
                </Relationships>
                """);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreatePackageWithWhitespacePaddedExternalWorksheetRelationshipType()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                </Types>
                """);
            WritePackageEntry(archive, "xl/worksheets/_rels/sheet1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdHyperlink"
                                Type=" http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink "
                                Target="https://example.com/docs"
                                TargetMode="External"/>
                </Relationships>
                """);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreatePackageWithWhitespacePaddedCorePropertiesRelationship()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                </Types>
                """);
            WritePackageEntry(archive, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdCore"
                                Type=" http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties "
                                Target="docProps/core.xml"/>
                </Relationships>
                """);
            WritePackageEntry(archive, "docProps/core.xml", """
                <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"/>
                """);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreatePackageWithExistingWorksheetRelationships()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                </Types>
                """);
            WritePackageEntry(archive, "xl/worksheets/_rels/sheet1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdHyperlink"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink"
                                Target="https://example.com/docs"
                                TargetMode="External"/>
                </Relationships>
                """);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreatePackageWithExistingRootRelationships()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                </Types>
                """);
            WritePackageEntry(archive, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdWorkbook"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"
                                Target="xl/workbook.xml"/>
                </Relationships>
                """);
        }

        package.Position = 0;
        return package;
    }

    private static MemoryStream CreatePackageWithMissingMediaWorksheetRelationship()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                </Types>
                """);
            WritePackageEntry(archive, "xl/worksheets/_rels/sheet1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdImage"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"
                                Target="../media/image%201.png"/>
                </Relationships>
                """);
        }

        package.Position = 0;
        return package;
    }

    private static void WritePackageEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }
}
