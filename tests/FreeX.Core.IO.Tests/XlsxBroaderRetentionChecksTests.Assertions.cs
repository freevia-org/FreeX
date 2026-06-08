using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxBroaderRetentionChecksTests
{
    private static void AssertPackageHasNoHealthIssues(MemoryStream package)
    {
        var position = package.Position;
        package.Position = 0;
        try
        {
            XlsxPackageHealthValidator.Validate(package)
                .Should()
                .BeEmpty("FreeX should not introduce package issues that can trigger Excel repair warnings");
        }
        finally
        {
            package.Position = position;
        }
    }

    private static void AssertDocumentPropertiesWereRetained(ZipArchive archive)
    {
        var coreXml = LoadXml(archive, "docProps/core.xml");
        coreXml.Root!.Element(DcNs + "subject")!.Value.Should().Be("FreeX retention subject");
        coreXml.Root!.Element(CorePropsNs + "keywords")!.Value.Should().Be("freex,xlsx,retention");
        coreXml.Root!.Element(CorePropsNs + "category")!.Value.Should().Be("Native Metadata");
        coreXml.Root!.Element(CorePropsNs + "contentStatus")!.Value.Should().Be("Reviewed");
        coreXml.Root!.Element(DcNs + "language")!.Value.Should().Be("en-US");
        coreXml.Root!.Element(CorePropsNs + "version")!.Value.Should().Be("2026.06");

        var appXml = LoadXml(archive, "docProps/app.xml");
        appXml.Root!.Element(AppPropsNs + "Application")!.Value.Should().Be("Microsoft Excel");
        appXml.Root!.Element(AppPropsNs + "Company")!.Value.Should().Be("FreeX Test Lab");
        appXml.Root!.Element(AppPropsNs + "Manager")!.Value.Should().Be("XLSX Fidelity");
        appXml.Root!.Element(AppPropsNs + "Template")!.Value.Should().Be("RetentionTemplate.xltx");

        var customXml = LoadXml(archive, "docProps/custom.xml").ToString(SaveOptions.DisableFormatting);
        customXml.Should().Contain("FreeXCustomProperty");
        customXml.Should().Contain("MSIP_Label_01234567-89ab-cdef-0123-456789abcdef_Enabled");
    }

    private static void AssertRootPackageRelationshipsWereRetained(ZipArchive archive)
    {
        var relationships = LoadXml(archive, "_rels/.rels")
            .Root!
            .Elements(PackageRelNs + "Relationship")
            .Select(relationship => (
                Type: relationship.Attribute("Type")?.Value,
                Target: relationship.Attribute("Target")?.Value))
            .ToList();

        relationships.Should().Contain(("http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml"));
        relationships.Should().Contain(("http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties", "docProps/app.xml"));
        relationships.Should().Contain(("http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties", "docProps/custom.xml"));
        relationships.Should().Contain(("http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml", "customXml/item1.xml"));
    }

    private static void AssertContentTypeOverridesWereRetained(ZipArchive archive)
    {
        var expectedOverrides = new (string PartName, string ContentType)[]
        {
            ("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml"),
            ("/docProps/app.xml", "application/vnd.openxmlformats-officedocument.extended-properties+xml"),
            ("/docProps/custom.xml", "application/vnd.openxmlformats-officedocument.custom-properties+xml"),
            ("/xl/externalLinks/externalLink1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml"),
            ("/customXml/itemProps1.xml", "application/vnd.openxmlformats-officedocument.customXmlProperties+xml")
        };

        var overrides = LoadXml(archive, "[Content_Types].xml")
            .Root!
            .Elements(ContentTypeNs + "Override")
            .Select(overrideElement => (
                PartName: overrideElement.Attribute("PartName")!.Value,
                ContentType: overrideElement.Attribute("ContentType")!.Value))
            .ToList();

        foreach (var expectedOverride in expectedOverrides)
        {
            overrides.Should().Contain(expectedOverride);
            archive.GetEntry(expectedOverride.PartName.TrimStart('/'))
                .Should()
                .NotBeNull($"{expectedOverride.PartName} should remain addressable by its content type override");
        }
    }

    private static void AssertInternalRelationshipTargetWasRetained(
        ZipArchive archive,
        string relationshipsPart,
        string relationshipType,
        string expectedTarget)
    {
        var relationship = LoadXml(archive, relationshipsPart)
            .Root!
            .Elements(PackageRelNs + "Relationship")
            .SingleOrDefault(element =>
                string.Equals(element.Attribute("Type")?.Value, relationshipType, StringComparison.Ordinal) &&
                string.Equals(element.Attribute("Target")?.Value, expectedTarget, StringComparison.Ordinal));

        relationship.Should().NotBeNull($"{relationshipsPart} should retain its {relationshipType} relationship");
        relationship!.Attribute("TargetMode")?.Value.Should().NotBe("External");

        var resolvedTarget = ResolveInternalRelationshipTarget(relationshipsPart, expectedTarget);
        archive.GetEntry(resolvedTarget)
            .Should()
            .NotBeNull($"{relationshipsPart} target {expectedTarget} should resolve to retained package part {resolvedTarget}");
    }

    private static string ResolveInternalRelationshipTarget(string relationshipsPart, string target)
    {
        var sourceDirectory = relationshipsPart.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)
            ? relationshipsPart[..relationshipsPart.LastIndexOf("/_rels/", StringComparison.Ordinal)]
            : string.Empty;
        var segments = sourceDirectory.Length == 0
            ? new List<string>()
            : sourceDirectory.Split('/').ToList();

        foreach (var segment in target.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;
            if (segment == "..")
                segments.RemoveAt(segments.Count - 1);
            else
                segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    private static void AssertWorkbookMetadataWasRetainedWithoutOverridingModeledState(ZipArchive archive)
    {
        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        var workbookText = workbookXml.ToString(SaveOptions.DisableFormatting);
        var workbookPr = workbookXml.Root!.Element(MainNs + "workbookPr");
        workbookPr.Should().NotBeNull();
        workbookPr!.Attribute("date1904")?.Value.Should().NotBe("1");
        workbookPr.Attribute("defaultThemeVersion")!.Value.Should().Be("166925");
        workbookPr.Element(FxNs + "workbookPrNativeChild").Should().BeNull();

        var fileSharing = workbookXml.Root.Element(MainNs + "fileSharing");
        fileSharing.Should().NotBeNull();
        fileSharing!.Attribute("userName")!.Value.Should().Be("EditedUser");
        fileSharing.Attribute("customFileSharingAttr").Should().BeNull();
        workbookText.Should().NotContain("userName=\"SourceUser\"");

        workbookText.Should().NotContain("customVersionFlag=\"keep\"");
        workbookText.Should().NotContain("customRecoveryFlag=\"keep\"");
        workbookText.Should().NotContain("customSmartTagFlag=\"keep\"");
        workbookText.Should().NotContain("customSmartTagTypeFlag=\"keep\"");
        workbookText.Should().NotContain("customFunctionGroupFlag=\"keep\"");
        workbookText.Should().Contain("FreeXNativeFunctions");
        workbookText.Should().NotContain("nativeHiddenViewAttr=\"kept\"");
        workbookXml.Root.Element(MainNs + "customWorkbookViews").Should().BeNull();
        workbookText.Should().Contain("{FREEX-WORKBOOK-EXT}");
        workbookText.Should().Contain("externalReferences");

        var workbookRels = LoadXml(archive, "xl/_rels/workbook.xml.rels").ToString(SaveOptions.DisableFormatting);
        workbookRels.Should().Contain("externalLinks/externalLink1.xml");
        workbookRels.Should().Contain("/externalLink");
        AssertInternalRelationshipTargetWasRetained(
            archive,
            "xl/_rels/workbook.xml.rels",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink",
            "externalLinks/externalLink1.xml");
        LoadXml(archive, "xl/externalLinks/externalLink1.xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Contain("LinkedSheet");
        LoadXml(archive, "xl/externalLinks/_rels/externalLink1.xml.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Contain("linked-workbook.xlsx");
    }

    private static void AssertStyleAndPackagePartsWereRetained(ZipArchive archive)
    {
        var stylesText = LoadXml(archive, "xl/styles.xml").ToString(SaveOptions.DisableFormatting);
        stylesText.Should().Contain("FF010203");
        stylesText.Should().NotContain("nativeDxfAttr=\"kept\"");
        stylesText.Should().NotContain("dxfNativeChild");
        stylesText.Should().NotContain("nativeTableStylesAttr=\"kept\"");
        stylesText.Should().Contain("FreeXNativeTableStyle");
        stylesText.Should().Contain("{FREEX-STYLES-EXT}");

        ReadEntryText(archive, "customXml/item1.xml").Should().Contain("retained-custom-xml");
        LoadXml(archive, "customXml/itemProps1.xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Contain("{01234567-89AB-CDEF-0123-456789ABCDEF}");
        LoadXml(archive, "customXml/_rels/item1.xml.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Contain("customXmlProps");
        AssertInternalRelationshipTargetWasRetained(
            archive,
            "customXml/_rels/item1.xml.rels",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps",
            "itemProps1.xml");
    }
}
