using System.IO.Compression;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxExternalLinkMetadataReaderTests
{
    [Fact]
    public void Load_IgnoresWorkbookExternalReferenceRelationshipWithExternalTargetMode()
    {
        using var package = CreateWorkbookWithExternalReferenceToExternalTarget();

        var links = XlsxExternalLinkMetadataReader.Load(package);

        links.Should().BeEmpty();
    }

    private static MemoryStream CreateWorkbookWithExternalReferenceToExternalTarget()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageEntry(archive, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                </Types>
                """);
            WritePackageEntry(archive, "xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <externalReferences>
                    <externalReference r:id="rIdExternal"/>
                  </externalReferences>
                </workbook>
                """);
            WritePackageEntry(archive, "xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdExternal"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink"
                                Target="https://example.com/externalLink1.xml"
                                TargetMode="External"/>
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
}
