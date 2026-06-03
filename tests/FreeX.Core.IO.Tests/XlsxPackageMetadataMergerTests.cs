using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxPackageMetadataMergerTests
{
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
