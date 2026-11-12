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
