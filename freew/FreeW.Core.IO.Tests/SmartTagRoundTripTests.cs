using System.IO;
using System.IO.Compression;

namespace FreeW.Core.IO.Tests;

public class SmartTagRoundTripTests
{
    [Fact]
    public void SmartTagContent_SurvivesAsOrdinaryFormattedRuns()
    {
        using var input = BuildPackage();
        var read = DocxReader.Read(input);
        var paragraph = read.Paragraphs.Single();

        paragraph.PlainText.Should().Be("23 July 2026");
        paragraph.Runs.Single().Formatting.Bold.Should().BeTrue();

        byte[] rewritten;
        using (var output = new MemoryStream())
        {
            DocxWriter.Write(read, output);
            rewritten = output.ToArray();
        }

        using (var zip = new ZipArchive(new MemoryStream(rewritten), ZipArchiveMode.Read))
        using (var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open()))
        {
            var documentXml = reader.ReadToEnd();
            documentXml.Should().Contain("23 July 2026");
            documentXml.Should().NotContain("smartTag");
        }

        var reread = DocxReader.Read(new MemoryStream(rewritten)).Paragraphs.Single();
        reread.PlainText.Should().Be("23 July 2026");
        reread.Runs.Single().Formatting.Bold.Should().BeTrue();
    }

    private static MemoryStream BuildPackage()
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """);
            Add(zip, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Add(zip, "word/document.xml", """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:smartTag w:uri="urn:schemas-microsoft-com:office:smarttags" w:element="date"><w:r><w:rPr><w:b/></w:rPr><w:t>23 July 2026</w:t></w:r></w:smartTag></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);
        }
        stream.Position = 0;
        return stream;
    }

    private static void Add(ZipArchive zip, string path, string xml)
    {
        var entry = zip.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(xml);
    }
}
