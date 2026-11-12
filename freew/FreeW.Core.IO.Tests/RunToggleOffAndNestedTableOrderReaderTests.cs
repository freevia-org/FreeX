using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Reader coverage for three Word-parity gaps found on the real-world corpus (drawing.docx):
/// run toggles authored OFF (<c>&lt;w:b w:val="0"/&gt;</c>) inside a bold style, a serialized but empty
/// <c>w:docDefaults</c> (single line spacing, not the application 1.15 default), and the reading order
/// of tables nested between a cell's paragraphs.
/// </summary>
public sealed class RunToggleOffAndNestedTableOrderReaderTests
{
    private const string Wns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W = Wns;

    private static TextDocument Read(string bodyXml, string stylesInnerXml = "")
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "word/document.xml", $"<w:document xmlns:w=\"{Wns}\"><w:body>{bodyXml}</w:body></w:document>");
            Add(zip, "word/styles.xml", $"<w:styles xmlns:w=\"{Wns}\">{stylesInnerXml}</w:styles>");
        }
        ms.Position = 0;
        return DocxReader.Read(ms);
    }

    private static void Add(ZipArchive zip, string path, string xml)
    {
        var entry = zip.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(xml);
    }

    [Fact]
    public void ExplicitToggleOff_IsRecordedAndRoundTrips()
    {
        var doc = Read(
            "<w:p><w:r><w:rPr><w:b w:val=\"0\"/><w:i w:val=\"false\"/><w:u w:val=\"none\"/></w:rPr><w:t>plain</w:t></w:r>"
            + "<w:r><w:rPr><w:b/></w:rPr><w:t>bold</w:t></w:r><w:r><w:t>unset</w:t></w:r></w:p>");
        var runs = doc.Blocks.OfType<Paragraph>().Single().Runs;

        runs[0].Formatting.Bold.Should().BeFalse();
        runs[0].Formatting.ExplicitlyOff.Should().Be(
            RunToggleOverrides.Bold | RunToggleOverrides.Italic | RunToggleOverrides.Underline);
        runs[1].Formatting.Bold.Should().BeTrue();
        runs[1].Formatting.ExplicitlyOff.Should().Be(RunToggleOverrides.None);
        runs[2].Formatting.Should().Be(RunFormatting.Default);

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        var firstRunProperties = XDocument.Load(entry).Descendants(W + "r").First().Element(W + "rPr")!;
        firstRunProperties.Element(W + "b")!.Attribute(W + "val")!.Value.Should().Be("0");
        firstRunProperties.Element(W + "i")!.Attribute(W + "val")!.Value.Should().Be("0");
        firstRunProperties.Element(W + "u")!.Attribute(W + "val")!.Value.Should().Be("none");
    }

    [Fact]
    public void EmptySerializedDocDefaults_AreAuthoritativeForLineSpacing()
    {
        // Word 2007 writes <w:pPrDefault/> for converted documents: single spacing, not 1.15.
        var doc = Read(
            "<w:p><w:r><w:t>body</w:t></w:r></w:p>",
            "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Times New Roman\"/></w:rPr></w:rPrDefault><w:pPrDefault/></w:docDefaults>");

        doc.UseWordApplicationDefaultLineSpacing.Should().BeFalse();
    }

    [Fact]
    public void NestedTable_RecordsItsPositionBetweenTheCellParagraphs()
    {
        const string nested = "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>inner</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
        var doc = Read(
            "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>before</w:t></w:r></w:p>" + nested
            + "<w:p><w:r><w:t>middle</w:t></w:r></w:p>" + nested + "<w:p/></w:tc></w:tr></w:tbl>");
        var cell = doc.Blocks.OfType<Table>().Single().Rows[0].Cells[0];

        cell.NestedTables.Should().HaveCount(2);
        cell.NestedTableParagraphOffsets.Should().Equal(1, 2);
    }

    [Fact]
    public void ExplicitLeftAlignmentAndComplexScriptSize_AreReadAndRoundTrip()
    {
        var doc = Read(
            "<w:p><w:pPr><w:jc w:val=\"left\"/></w:pPr><w:r><w:rPr><w:sz w:val=\"20\"/><w:szCs w:val=\"30\"/></w:rPr><w:t>a</w:t></w:r>"
            + "<w:r><w:rPr><w:sz w:val=\"24\"/><w:szCs w:val=\"24\"/></w:rPr><w:t>b</w:t></w:r></w:p>"
            + "<w:p><w:r><w:t>c</w:t></w:r></w:p>",
            "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Times New Roman\" w:cs=\"Traditional Arabic\"/></w:rPr></w:rPrDefault><w:pPrDefault/></w:docDefaults>");
        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();

        paragraphs[0].Formatting.Alignment.Should().Be(TextAlignment.Left);
        paragraphs[0].Formatting.AlignmentIsSet.Should().BeTrue();
        paragraphs[1].Formatting.AlignmentIsSet.Should().BeFalse();
        paragraphs[0].Runs[0].Formatting.FontSizePt.Should().Be(10);
        paragraphs[0].Runs[0].Formatting.ComplexScriptFontSizePt.Should().Be(15);
        paragraphs[0].Runs[1].Formatting.ComplexScriptFontSizePt.Should().BeNull();
        // A serialized rPrDefault without w:sz is the specification default of 10pt.
        doc.DefaultRun.FontSizePt.Should().Be(10);
        doc.DefaultRun.ComplexScriptFontFamily.Should().Be("Traditional Arabic");

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        var body = XDocument.Load(entry);
        var first = body.Descendants(W + "p").First();
        first.Element(W + "pPr")!.Element(W + "jc")!.Attribute(W + "val")!.Value.Should().Be("left");
        var firstRun = first.Elements(W + "r").First().Element(W + "rPr")!;
        firstRun.Element(W + "sz")!.Attribute(W + "val")!.Value.Should().Be("20");
        firstRun.Element(W + "szCs")!.Attribute(W + "val")!.Value.Should().Be("30");
    }

    [Fact]
    public void PicturelessLegacyVml_MapsToWordArtTextBoxAndRule()
    {
        const string ns = " xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\"";
        var doc = Read(
            "<w:p><w:r><w:pict" + ns + "><v:shape id=\"_x0000_i1027\" type=\"#_x0000_t136\" style=\"width:148.5pt;height:28.5pt\">"
            + "<v:fill type=\"gradient\"/><v:textpath style=\"font-family:&quot;Arial Black&quot;;font-size:20pt\" fitpath=\"t\" string=\"My Text Here\"/></v:shape></w:pict></w:r></w:p>"
            + "<w:p><w:r><w:pict" + ns + "><v:group style=\"width:126pt;height:99pt\" coordorigin=\"0,0\" coordsize=\"1260,990\">"
            + "<v:shape type=\"#_x0000_t202\" style=\"position:absolute;left:100;top:100;width:720;height:500\"><v:textbox><w:txbxContent>"
            + "<w:p><w:r><w:t>boxed</w:t></w:r></w:p></w:txbxContent></v:textbox></v:shape></v:group></w:pict></w:r></w:p>"
            + "<w:p><w:r><w:pict" + ns + "><v:rect style=\"width:0;height:1.5pt\" o:hralign=\"center\" o:hrstd=\"t\" o:hr=\"t\" fillcolor=\"#aca899\" stroked=\"f\"/></w:pict></w:r></w:p>"
            + "<w:p><w:r><w:pict" + ns + "><v:shape id=\"PowerPlusWaterMarkObject1\" style=\"position:absolute;width:400pt;height:100pt\">"
            + "<v:textpath string=\"DRAFT\"/></v:shape></w:pict></w:r></w:p>");
        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();

        var wordArt = paragraphs[0].Runs.Single().WordArt!;
        wordArt.Text.Should().Be("My Text Here");
        wordArt.FontFamily.Should().Be("Arial Black");
        wordArt.WidthPt.Should().Be(148.5);
        wordArt.HeightPt.Should().Be(28.5);

        var textBox = paragraphs[1].Runs.Single().Shape!;
        textBox.Kind.Should().Be(ShapeKind.TextBox);
        textBox.WidthPt.Should().BeApproximately(72, 0.01);
        textBox.HeightPt.Should().BeApproximately(50, 0.01);
        string.Concat(textBox.TextParagraphs.Single().Runs.Select(run => run.Text)).Should().Be("boxed");

        paragraphs[2].Formatting.Border.Should().Be(new ParagraphBorder("#ACA899", 1.5, BottomOnly: true));
        // An absolutely positioned watermark keeps its dedicated reader and is not inlined.
        paragraphs[3].Runs.Should().NotContain(run => run.WordArt != null);
    }
}