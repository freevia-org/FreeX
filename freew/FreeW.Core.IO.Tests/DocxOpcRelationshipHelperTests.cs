using System.IO;
using System.IO.Compression;
using System.Text;

namespace FreeW.Core.IO.Tests;

public sealed class DocxOpcRelationshipHelperTests
{
    [Fact]
    public void Reader_ResolvesHeaderFooterRelationshipTargetsThroughSharedOpcPaths()
    {
        using var docx = BuildDocxWithNonCanonicalHeaderFooterTargets();

        var document = DocxReader.Read(docx);

        document.Header!.PlainText.Should().Be("Absolute header target");
        document.Footer!.PlainText.Should().Be("Dot footer target");
    }

    [Fact]
    public void DocxRelationshipHelpers_UseSharedOpcRelationshipLoader()
    {
        var readerSource = ReadRepoText("freew", "FreeW.Core.IO", "DocxReader.cs");
        var writerSource = ReadRepoText("freew", "FreeW.Core.IO", "DocxWriter.cs");

        SourceBetween(
                readerSource,
                "private static Dictionary<string, string> ReadDocumentRelationshipTypesByTarget",
                "    /// Resolves word/fontTable.xml")
            .Should()
            .Contain("OpcRelationships.LoadTypeByTargetMap(archive, \"word/_rels/document.xml.rels\")")
            .And.NotContain("Elements(Rel + \"Relationship\")");

        SourceBetween(
                readerSource,
                "private static Dictionary<string, string> ReadContentTypeOverrides",
                "    /// <summary>")
            .Should()
            .Contain("OpcMediaTypes.ReadOverrideContentTypes(archive)")
            .And.NotContain("Elements(Ct + \"Override\")");

        SourceBetween(
                readerSource,
                "private static string? ResolveDocumentRelPartPath",
                "    /// Finds the settings part path")
            .Should()
            .Contain("OpcRelationships.Load(archive, \"word/_rels/document.xml.rels\")")
            .And.Contain("OpcPathHelper.ResolveRelativeZipPath(\"word\", relationship.Target)")
            .And.NotContain("Elements(Rel + \"Relationship\")");

        SourceBetween(
                readerSource,
                "private static Dictionary<string, string> ReadFontTableRelationships",
                "    /// Finds a document-relationship target")
            .Should()
            .Contain("OpcRelationships.LoadTargetMap(");

        SourceBetween(
                readerSource,
                "private static Dictionary<string, string> ReadPartImageRelationships(ZipArchive archive, string partPath)",
                "    /// <summary>Maps relationship id")
            .Should()
            .Contain("OpcRelationships.LoadTargetMap(")
            .And.NotContain("Elements(Rel + \"Relationship\")");

        SourceBetween(
                readerSource,
                "private static Dictionary<string, string> ReadHeaderFooterRelationships",
                "    private static XDocument? LoadPart")
            .Should()
            .Contain("OpcRelationships.LoadTargetMap(")
            .And.Contain("OpcPathHelper.ResolveRelativeZipPath(\"word\", relationship.Target)")
            .And.NotContain("Elements(Rel + \"Relationship\")");

        SourceBetween(
                readerSource,
                "private static Dictionary<string, string> ReadContentTypeDefaults",
                "    /// <summary>Reads document.xml.rels")
            .Should()
            .Contain("OpcMediaTypes.ReadDefaultContentTypes(archive)")
            .And.NotContain("Elements(Ct + \"Default\")");

        SourceBetween(
                writerSource,
                "private static byte[] BuildChartWorkbook",
                "    /// <summary>Builds an inline-string worksheet cell")
            .Should()
            .Contain("OpcRelationships.CreateRelationship(")
            .And.Contain("OfficeDocumentRel")
            .And.NotContain("new XElement(Rel + \"Relationship\"");
    }

    [Fact]
    public void DocxDocumentPropertyPackageParts_UseSharedOpcPackagePropertyConstants()
    {
        var readerSource = ReadRepoText("freew", "FreeW.Core.IO", "DocxReader.cs");
        var writerSource = ReadRepoText("freew", "FreeW.Core.IO", "DocxWriter.cs");
        var ooxmlSource = ReadRepoText("freew", "FreeW.Core.IO", "OoxmlWordprocessing.cs");

        readerSource.Should()
            .Contain("OpcPackageProperties.ExtendedPropertiesZipEntry")
            .And.Contain("OpcPackageProperties.ExtendedPropertiesPartName")
            .And.Contain("OpcPackageProperties.CustomPropertiesZipEntry")
            .And.NotContain("\"docProps/app.xml\"")
            .And.NotContain("\"docProps/custom.xml\"");

        writerSource.Should()
            .Contain("OpcPackageProperties.CorePropertiesZipEntry")
            .And.Contain("OpcPackageProperties.CorePropertiesPartName")
            .And.Contain("OpcPackageProperties.CorePropertiesContentType")
            .And.Contain("OpcPackageProperties.CorePropertiesRelationshipType")
            .And.Contain("OpcPackageProperties.CustomPropertiesZipEntry")
            .And.Contain("OpcPackageProperties.CustomPropertiesPartName")
            .And.Contain("OpcPackageProperties.CustomPropertiesContentType")
            .And.Contain("OpcPackageProperties.CustomPropertiesRelationshipType")
            .And.Contain("OpcPackageProperties.ExtendedPropertiesPartName")
            .And.Contain("OpcPackageProperties.ExtendedPropertiesRelationshipType")
            .And.Contain("OpcPackageProperties.ExtendedPropertiesZipEntry")
            .And.NotContain("\"docProps/core.xml\"")
            .And.NotContain("\"docProps/app.xml\"")
            .And.NotContain("\"docProps/custom.xml\"");

        ooxmlSource.Should()
            .NotContain("ToW3CDtf(")
            .And.NotContain("ParseW3CDtf(");
    }

    private static MemoryStream BuildDocxWithNonCanonicalHeaderFooterTargets()
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddText(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
                  <Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/>
                </Types>
                """);

            AddText(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);

            AddText(zip, "word/_rels/document.xml.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdHeader" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="/word/header1.xml"/>
                  <Relationship Id="rIdFooter" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="./footer1.xml"/>
                </Relationships>
                """);

            AddText(zip, "word/document.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body>
                    <w:p><w:r><w:t>Body</w:t></w:r></w:p>
                    <w:sectPr>
                      <w:headerReference w:type="default" r:id="rIdHeader"/>
                      <w:footerReference w:type="default" r:id="rIdFooter"/>
                    </w:sectPr>
                  </w:body>
                </w:document>
                """);

            AddText(zip, "word/header1.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:p><w:r><w:t>Absolute header target</w:t></w:r></w:p>
                </w:hdr>
                """);

            AddText(zip, "word/footer1.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <w:ftr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:p><w:r><w:t>Dot footer target</w:t></w:r></w:p>
                </w:ftr>
                """);
        }

        stream.Position = 0;
        return stream;
    }

    private static string ReadRepoText(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllTextFromCurrentDirectoryOrFallback(relativeParts);

    private static string SourceBetween(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0, $"source should contain marker {start}");

        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        endIndex.Should().BeGreaterThan(startIndex, $"source should contain marker {end} after {start}");

        return source[startIndex..endIndex];
    }

    private static void AddText(ZipArchive zip, string path, string text)
    {
        var entry = zip.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(text);
    }
}
