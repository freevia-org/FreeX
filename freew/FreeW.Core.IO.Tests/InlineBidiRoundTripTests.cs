using System.IO;
using System.IO.Compression;

namespace FreeW.Core.IO.Tests;

public class InlineBidiRoundTripTests
{
    [Fact]
    public void InlineBidiContainers_PreserveVisibleContentAndNormalizeRtlRuns()
    {
        using var input = BuildPackage();
        var paragraph = DocxReader.Read(input).Paragraphs.Single();

        paragraph.PlainText.Should().Be("Before אבג forced مرحبا after");
        paragraph.Runs.Single(run => run.Text == "אבג ").Formatting.Rtl.Should().BeTrue();
        paragraph.Runs.Single(run => run.Text == "forced ").Formatting.Rtl.Should().BeTrue();
        paragraph.Runs.Single(run => run.Text == "مرحبا").Formatting.Rtl.Should().BeTrue();
        paragraph.Runs.Single(run => run.Text == "Before ").Formatting.Rtl.Should().BeFalse();
        paragraph.Runs.Single(run => run.Text == " after").Formatting.Rtl.Should().BeFalse();

        byte[] rewritten;
        using (var output = new MemoryStream())
        {
            DocxWriter.Write(new TextDocument { Blocks = { paragraph } }, output);
            rewritten = output.ToArray();
        }

        using (var zip = new ZipArchive(new MemoryStream(rewritten), ZipArchiveMode.Read))
        using (var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open()))
        {
            var documentXml = reader.ReadToEnd();
            documentXml.Should().Contain("אבג").And.Contain("forced").And.Contain("مرحبا");
            documentXml.Should().Contain("w:rtl");
        }

        var reread = DocxReader.Read(new MemoryStream(rewritten)).Paragraphs.Single();
        reread.PlainText.Should().Be("Before אבג forced مرحبا after");
        reread.Runs.Single(run => run.Text == "אבג ").Formatting.Rtl.Should().BeTrue();
        reread.Runs.Single(run => run.Text == "forced ").Formatting.Rtl.Should().BeTrue();
        reread.Runs.Single(run => run.Text == "مرحبا").Formatting.Rtl.Should().BeTrue();
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
                  <w:body><w:p>
                    <w:r><w:t>Before </w:t></w:r>
                    <w:dir w:val="rtl"><w:r><w:t>אבג </w:t></w:r><w:hyperlink w:anchor="bookmark"><w:r><w:t>forced </w:t></w:r></w:hyperlink></w:dir>
                    <w:bdo w:val="rtl"><w:r><w:t>مرحبا</w:t></w:r></w:bdo>
                    <w:r><w:t> after</w:t></w:r>
                  </w:p><w:sectPr/></w:body>
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
