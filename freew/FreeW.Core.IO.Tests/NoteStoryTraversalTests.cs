using System.IO;
using System.IO.Compression;

namespace FreeW.Core.IO.Tests;

public class NoteStoryTraversalTests
{
    [Fact]
    public void NestedNoteAndCommentParagraphs_SurviveRoundTrip()
    {
        using var input = BuildPackage();
        var read = DocxReader.Read(input);

        read.Footnotes[1].Content.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Footnote direct", "Footnote table");
        read.Endnotes[1].Content.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Endnote control");
        read.Comments[0].Content.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Comment table");

        byte[] rewritten;
        using (var output = new MemoryStream())
        {
            DocxWriter.Write(read, output);
            rewritten = output.ToArray();
        }

        var reread = DocxReader.Read(new MemoryStream(rewritten));
        reread.Footnotes[1].Content.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Footnote direct", "Footnote table");
        reread.Endnotes[1].Content.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Endnote control");
        reread.Comments[0].Content.Select(paragraph => paragraph.PlainText)
            .Should().Equal("Comment table");

        using var zip = new ZipArchive(new MemoryStream(rewritten), ZipArchiveMode.Read);
        ReadText(zip, "word/footnotes.xml").Should().Contain("Footnote table");
        ReadText(zip, "word/endnotes.xml").Should().Contain("Endnote control");
        ReadText(zip, "word/comments.xml").Should().Contain("Comment table");
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
                  <Override PartName="/word/footnotes.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml"/>
                  <Override PartName="/word/endnotes.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.endnotes+xml"/>
                  <Override PartName="/word/comments.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml"/>
                </Types>
                """);
            Add(zip, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Add(zip, "word/document.xml", """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body><w:p><w:r><w:t>Body</w:t></w:r></w:p><w:sectPr/></w:body>
                </w:document>
                """);
            Add(zip, "word/footnotes.xml", """
                <w:footnotes xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:footnote w:id="1">
                    <w:p><w:r><w:t>Footnote direct</w:t></w:r></w:p>
                    <w:tbl><w:tr><w:tc><w:tcPr/><w:p><w:r><w:t>Footnote table</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
                  </w:footnote>
                </w:footnotes>
                """);
            Add(zip, "word/endnotes.xml", """
                <w:endnotes xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:endnote w:id="1"><w:sdt><w:sdtPr/><w:sdtContent><w:p><w:r><w:t>Endnote control</w:t></w:r></w:p></w:sdtContent></w:sdt></w:endnote>
                </w:endnotes>
                """);
            Add(zip, "word/comments.xml", """
                <w:comments xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:comment w:id="0" w:author="A" w:initials="A">
                    <w:tbl><w:tr><w:tc><w:tcPr/><w:p><w:r><w:t>Comment table</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
                  </w:comment>
                </w:comments>
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

    private static string ReadText(ZipArchive zip, string path)
    {
        using var reader = new StreamReader(zip.GetEntry(path)!.Open());
        return reader.ReadToEnd();
    }
}
