using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Header/footer markup the corpus showed being mis-read: a PAGE field whose fldChar runs sit inside
/// tracked insertions (stress023), and a legacy VML circle holding the page number in the bottom margin
/// (stress015/stress018, Word's "Page Numbers (Margins)" gallery).
/// </summary>
public class HeaderFooterFieldAndFrameReadTests
{
    private const string Wns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Rns = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string Vns = "urn:schemas-microsoft-com:vml";

    internal static TextDocument ReadWithFooter(string footerInnerXml, string bodyXml = "<w:p><w:r><w:t>Body</w:t></w:r></w:p>")
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                var entry = zip.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(content);
            }

            Add("word/document.xml",
                $"<w:document xmlns:w=\"{Wns}\" xmlns:r=\"{Rns}\"><w:body>{bodyXml}" +
                "<w:sectPr><w:footerReference w:type=\"default\" r:id=\"rIdF\"/>" +
                "<w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"720\" w:right=\"720\" w:bottom=\"720\" w:left=\"720\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/>" +
                "</w:sectPr></w:body></w:document>");
            Add("word/_rels/document.xml.rels",
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rIdF\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer\" Target=\"footer1.xml\"/>" +
                "</Relationships>");
            Add("word/footer1.xml",
                $"<w:ftr xmlns:w=\"{Wns}\" xmlns:r=\"{Rns}\" xmlns:v=\"{Vns}\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:w10=\"urn:schemas-microsoft-com:office:word\">{footerInnerXml}</w:ftr>");
        }

        ms.Position = 0;
        return DocxReader.Read(ms);
    }

    [Fact]
    public void PageFieldWithFieldCharactersInsideTrackedInsertions_ReadsAsOneField()
    {
        var doc = ReadWithFooter(
            "<w:p>" +
            "<w:ins w:id=\"1\" w:author=\"A\"><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
            "<w:r><w:instrText xml:space=\"preserve\"> PAGE   \\* MERGEFORMAT </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r></w:ins>" +
            "<w:r><w:t>1</w:t></w:r>" +
            "<w:ins w:id=\"2\" w:author=\"A\"><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:ins>" +
            "<w:r><w:t xml:space=\"preserve\"> - Footer text</w:t></w:r></w:p>");

        var footer = doc.FinalSectionHeadersFooters.Footer;
        footer.Should().NotBeNull();
        var runs = footer!.Paragraphs.Single().Runs;
        var field = runs.Single(run => run.ComplexField is not null);
        field.ComplexField!.Instruction.Trim().Should().StartWith("PAGE");
        field.Text.Should().Be("1");
        runs.Where(run => run.ComplexField is null).Select(run => run.Text)
            .Should().Equal(" - Footer text");
    }

    internal const string MarginCirclePageNumberFooter =
        "<w:p><w:r><w:pict><v:oval id=\"_x0000_s2049\" style=\"position:absolute;margin-left:0;margin-top:0;width:49.35pt;height:49.35pt;" +
        "z-index:251658240;mso-position-horizontal:center;mso-position-horizontal-relative:margin;mso-position-vertical:center;" +
        "mso-position-vertical-relative:bottom-margin-area;v-text-anchor:middle\" fillcolor=\"#365f91 [2404]\" stroked=\"f\">" +
        "<v:textbox inset=\",0,,0\"><w:txbxContent><w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr>" +
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> PAGE </w:instrText></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>1</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>" +
        "</w:p></w:txbxContent></v:textbox><w10:wrap anchorx=\"margin\" anchory=\"page\"/></v:oval></w:pict></w:r></w:p>" +
        "<w:p><w:r><w:t>Plain footer</w:t></w:r></w:p>";

    [Fact]
    public void PositionedVmlTextBox_TagsItsFlattenedParagraphsWithTheShapeFrame()
    {
        var doc = ReadWithFooter(MarginCirclePageNumberFooter);

        var paragraphs = doc.FinalSectionHeadersFooters.Footer!.Paragraphs;
        var framed = paragraphs.Single(paragraph => paragraph.Runs.Any(run => run.ComplexField is not null));
        var frame = framed.LegacyTextBoxFrame;
        frame.Should().NotBeNull();
        frame!.IsEllipse.Should().BeTrue();
        frame.WidthPt.Should().BeApproximately(49.35, 0.001);
        frame.FillColorHex.Should().Be("#365F91");
        frame.StrokeColorHex.Should().BeNull();
        frame.HorizontalAlignment.Should().Be(LegacyFrameHorizontalAlignment.Center);
        frame.HorizontalReference.Should().Be(LegacyFrameReference.Margin);
        frame.VerticalAlignment.Should().Be(LegacyFrameVerticalAlignment.Center);
        frame.VerticalReference.Should().Be(LegacyFrameReference.BottomMarginArea);
        frame.TextAnchor.Should().Be(LegacyFrameTextAnchor.Middle);
        frame.InsetTopPt.Should().Be(0);
        frame.InsetLeftPt.Should().BeApproximately(7.2, 0.001);
        paragraphs.Single(paragraph => paragraph.PlainText == "Plain footer").LegacyTextBoxFrame.Should().BeNull();
    }

    [Fact]
    public void PlainTrackedInsertion_IsUnaffected()
    {
        var doc = ReadWithFooter(
            "<w:p><w:ins w:id=\"1\" w:author=\"A\"><w:r><w:t>new</w:t></w:r></w:ins></w:p>");

        var run = doc.FinalSectionHeadersFooters.Footer!.Paragraphs.Single().Runs.Single();
        run.Text.Should().Be("new");
        run.Revision.Should().Be(RevisionKind.Inserted);
        run.ComplexField.Should().BeNull();
    }
}
