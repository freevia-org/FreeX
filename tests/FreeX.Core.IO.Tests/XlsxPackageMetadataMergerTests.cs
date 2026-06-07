using System.IO.Compression;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxPackageMetadataMergerTests
{
    private static MemoryStream CreatePackageWithAdditionalContentTypes() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="png" ContentType="image/png"/>
                  <Override PartName="/xl/media/image1.png"
                            ContentType="image/png"/>
                  <Override PartName="/xl/worksheets/sheet2.xml"
                            ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """));

    private static MemoryStream CreatePackageWithExistingContentTypes() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml"
                            ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """),
            ("xl/worksheets/sheet1.xml", "<worksheet />"),
            ("xl/worksheets/sheet2.xml", "<worksheet />"));

    private static MemoryStream CreatePackageWithMacroEnabledWorkbookContentType() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml"
                            ContentType="application/vnd.ms-excel.sheet.macroEnabled.main+xml"/>
                  <Override PartName="/xl/vbaProject.bin"
                            ContentType="application/vnd.ms-office.vbaProject"/>
                </Types>
                """),
            ("xl/workbook.xml", "<workbook />"),
            ("xl/vbaProject.bin", "macro"));

    private static MemoryStream CreatePackageWithPlainWorkbookContentType() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml"
                            ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                </Types>
                """),
            ("xl/workbook.xml", "<workbook />"),
            ("xl/vbaProject.bin", "macro"));

    private static MemoryStream CreatePackageWithDanglingAndInvalidContentTypeOverrides() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/worksheets/sheet2.xml"
                            ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/customXml/missing.xml"
                            ContentType="application/xml"/>
                  <Override PartName="/xl/../evil.xml"
                            ContentType="application/xml"/>
                  <Override PartName="xl\bad.xml"
                            ContentType="application/xml"/>
                </Types>
                """));

    private static MemoryStream CreatePackageWithUnrootedWorksheetOverride() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="xl/worksheets/sheet1.xml"
                            ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """));

    private static MemoryStream CreatePackageWithWhitespacePaddedWorksheetOverride() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName=" /xl/worksheets/sheet1.xml "
                            ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """));

    private static MemoryStream CreatePackageWithWhitespacePaddedExcludedImageOverride() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName=" /xl/media/image1.png "
                            ContentType="image/png"/>
                </Types>
                """));

    private static MemoryStream CreatePackageWithEquivalentImageDefaultExtension() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension=" .PNG " ContentType="image/png"/>
                </Types>
                """));

    private static MemoryStream CreatePackageWithExistingImageDefault() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="png" ContentType="image/png"/>
                </Types>
                """));

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

    private static MemoryStream CreatePackageWithExternalWorksheetRelationship() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                </Types>
                """),
            ("xl/worksheets/_rels/sheet1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdHyperlink"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink"
                                Target="https://example.com/from-source"
                                TargetMode="External"/>
                </Relationships>
                """));

    private static MemoryStream CreatePackageWithWhitespacePaddedExternalWorksheetRelationship() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                </Types>
                """),
            ("xl/worksheets/_rels/sheet1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdHyperlink"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink"
                                Target="https://example.com/docs"
                                TargetMode=" External "/>
                </Relationships>
                """));

    private static MemoryStream CreatePackageWithWhitespacePaddedExternalWorksheetRelationshipType() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                </Types>
                """),
            ("xl/worksheets/_rels/sheet1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdHyperlink"
                                Type=" http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink "
                                Target="https://example.com/docs"
                                TargetMode="External"/>
                </Relationships>
                """));

    private static MemoryStream CreatePackageWithWhitespacePaddedCorePropertiesRelationship() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                </Types>
                """),
            ("_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdCore"
                                Type=" http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties "
                                Target="docProps/core.xml"/>
                </Relationships>
                """),
            ("docProps/core.xml", """
                <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"/>
                """));

    private static MemoryStream CreatePackageWithExistingWorksheetRelationships() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                </Types>
                """),
            ("xl/worksheets/_rels/sheet1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdHyperlink"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink"
                                Target="https://example.com/docs"
                                TargetMode="External"/>
                </Relationships>
                """));

    private static MemoryStream CreatePackageWithExistingRootRelationships() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                </Types>
                """),
            ("_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdWorkbook"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"
                                Target="xl/workbook.xml"/>
                </Relationships>
                """));

    private static MemoryStream CreatePackageWithWorkbookWebExtensionTaskpaneGraph() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml"
                            ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/webextensions/taskpanes.xml"
                            ContentType="application/vnd.ms-office.webextensiontaskpanes+xml"/>
                  <Override PartName="/xl/webextensions/webextension1.xml"
                            ContentType="application/vnd.ms-office.webextension+xml"/>
                </Types>
                """),
            ("xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets/>
                </workbook>
                """),
            ("xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdOfficeAddinTaskpanes"
                                Type="http://schemas.microsoft.com/office/2011/relationships/webextensiontaskpanes"
                                Target="webextensions/taskpanes.xml"/>
                </Relationships>
                """),
            ("xl/webextensions/taskpanes.xml", """
                <wetp:taskpanes xmlns:wetp="http://schemas.microsoft.com/office/webextensions/taskpanes/2010/11">
                  <wetp:taskpane dockstate="right" visibility="0" width="350" row="4">
                    <wetp:webextensionref xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" r:id="rIdWebExtension1"/>
                  </wetp:taskpane>
                </wetp:taskpanes>
                """),
            ("xl/webextensions/_rels/taskpanes.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdWebExtension1"
                                Type="http://schemas.microsoft.com/office/2011/relationships/webextension"
                                Target="webextension1.xml"/>
                </Relationships>
                """),
            ("xl/webextensions/webextension1.xml", """
                <we:webextension xmlns:we="http://schemas.microsoft.com/office/webextensions/webextension/2010/11">
                  <we:reference id="wa104379955" version="1.0.0.0" store="en-US" storeType="OMEX"/>
                  <we:alternateReferences/>
                  <we:properties/>
                  <we:bindings/>
                  <we:snapshot>AAAA</we:snapshot>
                </we:webextension>
                """));

    private static MemoryStream CreatePackageWithMissingMediaWorksheetRelationship() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                </Types>
                """),
            ("xl/worksheets/_rels/sheet1.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdImage"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"
                                Target="../media/image%201.png"/>
                </Relationships>
                """));

    private static void WritePackageEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

}
