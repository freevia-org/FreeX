using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Markup Word's header/footer building blocks rely on, which the reader used to drop: a framed
/// page-number paragraph (<c>w:framePr w:xAlign</c>), absolute position tabs (<c>w:ptab</c>) and a
/// table cell wrapped in a cell-level content control (<c>w:tr/w:sdt/w:sdtContent/w:tc</c>).
/// </summary>
public class HeaderFooterCompositionReadTests
{
    private const string Wns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextDocument Read(string bodyXml)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write($"<w:document xmlns:w=\"{Wns}\"><w:body>{bodyXml}</w:body></w:document>");
        }

        ms.Position = 0;
        return DocxReader.Read(ms);
    }

    [Theory]
    [InlineData("right", TextAlignment.Right)]
    [InlineData("center", TextAlignment.Center)]
    [InlineData("left", TextAlignment.Left)]
    public void FramedParagraph_KeepsItsHorizontalFrameAlignment(string xAlign, TextAlignment expected)
    {
        var doc = Read(
            $"<w:p><w:pPr><w:framePr w:wrap=\"around\" w:vAnchor=\"text\" w:hAnchor=\"margin\" w:xAlign=\"{xAlign}\" w:y=\"1\"/></w:pPr>" +
            "<w:r><w:t>1</w:t></w:r></w:p><w:p><w:r><w:t>plain</w:t></w:r></w:p>");

        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();
        paragraphs[0].FrameHorizontalAlignment.Should().Be(expected);
        paragraphs[1].FrameHorizontalAlignment.Should().BeNull();
    }

    [Fact]
    public void DropCapFrame_IsNotTreatedAsAnAlignedTextFrame()
    {
        var doc = Read(
            "<w:p><w:pPr><w:framePr w:dropCap=\"drop\" w:lines=\"3\" w:wrap=\"around\" w:vAnchor=\"text\" w:hAnchor=\"text\" w:xAlign=\"left\"/></w:pPr>" +
            "<w:r><w:t>D</w:t></w:r></w:p>");

        doc.Blocks.OfType<Paragraph>().First().FrameHorizontalAlignment.Should().BeNull();
    }

    [Fact]
    public void AbsolutePositionTab_ReadsAsATabBetweenItsNeighbours()
    {
        var doc = Read(
            "<w:p><w:r><w:t>Left</w:t></w:r>" +
            "<w:r><w:ptab w:relativeTo=\"margin\" w:alignment=\"center\" w:leader=\"none\"/></w:r>" +
            "<w:r><w:t>Middle</w:t></w:r>" +
            "<w:r><w:ptab w:relativeTo=\"margin\" w:alignment=\"right\" w:leader=\"none\"/></w:r>" +
            "<w:r><w:t>Right</w:t></w:r></w:p>");

        doc.Blocks.OfType<Paragraph>().First().PlainText.Should().Be("Left\tMiddle\tRight");
    }

    [Fact]
    public void CellWrappedInAContentControl_StaysInItsRow()
    {
        var doc = Read(
            "<w:tbl><w:tblGrid><w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"4000\"/></w:tblGrid><w:tr>" +
            "<w:sdt><w:sdtPr><w:alias w:val=\"Date\"/></w:sdtPr><w:sdtContent>" +
            "<w:tc><w:p><w:r><w:t>August 20, 2008</w:t></w:r></w:p></w:tc></w:sdtContent></w:sdt>" +
            "<w:tc><w:p><w:r><w:t>HEADER TEXT</w:t></w:r></w:p></w:tc>" +
            "</w:tr></w:tbl><w:p/>");

        var row = doc.Blocks.OfType<Table>().Single().Rows.Single();
        row.Cells.Select(cell => cell.Paragraphs.Single().PlainText)
            .Should().Equal("August 20, 2008", "HEADER TEXT");
    }
}
