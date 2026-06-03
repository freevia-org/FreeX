using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxBroaderRetentionChecksTests
{
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

    private static void AssertWorkbookMetadataWasRetainedWithoutOverridingModeledState(ZipArchive archive)
    {
        var workbookXml = LoadXml(archive, "xl/workbook.xml");
        var workbookText = workbookXml.ToString(SaveOptions.DisableFormatting);
        var workbookPr = workbookXml.Root!.Element(MainNs + "workbookPr");
        workbookPr.Should().NotBeNull();
        workbookPr!.Attribute("date1904")?.Value.Should().NotBe("1");
        workbookPr.Attribute("defaultThemeVersion")!.Value.Should().Be("166925");
        workbookPr.Element(FxNs + "workbookPrNativeChild")!.Attribute("id")!.Value.Should().Be("workbook-pr");

        var fileSharing = workbookXml.Root.Element(MainNs + "fileSharing");
        fileSharing.Should().NotBeNull();
        fileSharing!.Attribute("userName")!.Value.Should().Be("EditedUser");
        fileSharing.Attribute("customFileSharingAttr")!.Value.Should().Be("keep");
        workbookText.Should().NotContain("userName=\"SourceUser\"");

        workbookText.Should().Contain("customVersionFlag=\"keep\"");
        workbookText.Should().Contain("customRecoveryFlag=\"keep\"");
        workbookText.Should().Contain("customSmartTagFlag=\"keep\"");
        workbookText.Should().Contain("customSmartTagTypeFlag=\"keep\"");
        workbookText.Should().Contain("customFunctionGroupFlag=\"keep\"");
        workbookText.Should().Contain("FreeXNativeFunctions");
        workbookText.Should().Contain("nativeHiddenViewAttr=\"kept\"");
        workbookXml.Root.Element(MainNs + "customWorkbookViews").Should().BeNull();
        workbookText.Should().Contain("{FREEX-WORKBOOK-EXT}");
        workbookText.Should().Contain("externalReferences");

        var workbookRels = LoadXml(archive, "xl/_rels/workbook.xml.rels").ToString(SaveOptions.DisableFormatting);
        workbookRels.Should().Contain("externalLinks/externalLink1.xml");
        workbookRels.Should().Contain("/externalLink");
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
        stylesText.Should().Contain("nativeDxfAttr=\"kept\"");
        stylesText.Should().Contain("dxfNativeChild");
        stylesText.Should().Contain("nativeTableStylesAttr=\"kept\"");
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
    }
}
