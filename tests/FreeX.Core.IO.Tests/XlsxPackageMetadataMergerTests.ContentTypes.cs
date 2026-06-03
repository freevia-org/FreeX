using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxPackageMetadataMergerTests
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
    public void MergeContentTypes_SkipsDanglingAndInvalidOverrides()
    {
        using var sourcePackage = CreatePackageWithDanglingAndInvalidContentTypeOverrides();
        using var targetPackage = CreatePackageWithExistingContentTypes();
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        XlsxPackageMetadataMerger.MergeContentTypes(sourceArchive, targetArchive);

        var contentTypesXml = LoadXml(targetArchive.GetEntry("[Content_Types].xml")!);
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var overridePartNames = contentTypesXml.Root!
            .Elements(contentTypeNs + "Override")
            .Select(element => (string?)element.Attribute("PartName"))
            .ToList();

        overridePartNames.Should().Contain("/xl/worksheets/sheet2.xml");
        overridePartNames.Should().NotContain("/xl/customXml/missing.xml");
        overridePartNames.Should().NotContain("/xl/../evil.xml");
        overridePartNames.Should().NotContain("xl\\bad.xml");
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
}
