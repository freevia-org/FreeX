using System.IO.Compression;
using System.Text;
using FreeW.Core.IO;
using FreeW.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeW.Core.IO.Tests;

public sealed class R367_MalformedDocumentDoesNotThrowTests
{
    private static byte[] Package(string bodyXml)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Add(new Paragraph("seed"));
        using var seed = new MemoryStream();
        DocxWriter.Write(document, seed);

        var bytes = seed.ToArray();
        using var stream = new MemoryStream();
        stream.Write(bytes, 0, bytes.Length);
        stream.Position = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry("word/document.xml")?.Delete();
            var entry = archive.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(
                "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                "<w:body>" + bodyXml + "</w:body></w:document>");
        }

        return stream.ToArray();
    }

    public static TheoryData<string, string> Cases() => new()
    {
        { "negative table cell width", "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"-500\"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>x</w:t></w:r></w:p></w:tc></w:tr></w:tbl>" },
        { "cell width beyond int range", "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"99999999999999999999\"/></w:tblGrid><w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl>" },
        { "table with no tblGrid", "<w:tbl><w:tblPr/><w:tr><w:tc><w:p><w:r><w:t>x</w:t></w:r></w:p></w:tc></w:tr></w:tbl>" },
        { "row with no cells", "<w:tbl><w:tblPr/><w:tblGrid/><w:tr/></w:tbl>" },
        { "gridSpan of zero", "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"1000\"/></w:tblGrid><w:tr><w:tc><w:tcPr><w:gridSpan w:val=\"0\"/></w:tcPr><w:p/></w:tc></w:tr></w:tbl>" },
        { "gridSpan enormous", "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"1000\"/></w:tblGrid><w:tr><w:tc><w:tcPr><w:gridSpan w:val=\"2147483647\"/></w:tcPr><w:p/></w:tc></w:tr></w:tbl>" },
        { "negative font size", "<w:p><w:r><w:rPr><w:sz w:val=\"-40\"/></w:rPr><w:t>x</w:t></w:r></w:p>" },
        { "font size not a number", "<w:p><w:r><w:rPr><w:sz w:val=\"huge\"/></w:rPr><w:t>x</w:t></w:r></w:p>" },
        { "colour that is not hex", "<w:p><w:r><w:rPr><w:color w:val=\"zzzzzz\"/></w:rPr><w:t>x</w:t></w:r></w:p>" },
        { "indent beyond int range", "<w:p><w:pPr><w:ind w:left=\"99999999999\"/></w:pPr><w:r><w:t>x</w:t></w:r></w:p>" },
        { "numbering id with no definition", "<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"424242\"/></w:numPr></w:pPr><w:r><w:t>x</w:t></w:r></w:p>" },
        { "ilvl beyond 8", "<w:p><w:pPr><w:numPr><w:ilvl w:val=\"99\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr><w:r><w:t>x</w:t></w:r></w:p>" },
        { "style reference that does not exist", "<w:p><w:pPr><w:pStyle w:val=\"NoSuchStyle\"/></w:pPr><w:r><w:t>x</w:t></w:r></w:p>" },
        { "hyperlink with no relationship", "<w:p><w:hyperlink r:id=\"rIdNope\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><w:r><w:t>x</w:t></w:r></w:hyperlink></w:p>" },
        { "deeply nested tables", "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"1000\"/></w:tblGrid><w:tr><w:tc><w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"500\"/></w:tblGrid><w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl></w:tc></w:tr></w:tbl>" },
    };

    private static void Open(string body)
    {
        using var stream = new MemoryStream(Package(body));
        _ = DocxReader.Read(stream).Blocks.Count;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void AMalformedFileOpensWithoutThrowing(string label, string body)
    {
        var act = () => Open(body);

        act.Should().NotThrow(
            "a reader must ignore what it cannot use rather than refuse the file; throwing here costs " +
            "the user the whole document over one bad attribute ({0})", label);
    }
}
