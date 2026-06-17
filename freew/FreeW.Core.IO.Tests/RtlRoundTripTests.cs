using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for right-to-left direction: a paragraph's <c>w:bidi</c>
/// (<see cref="ParagraphFormatting.Rtl"/>) and a run's <c>w:rtl</c> (<see cref="RunFormatting.Rtl"/>) must
/// survive write→read and be emitted as the corresponding OOXML toggle elements.
/// </summary>
public class RtlRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static byte[] WriteBytes(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static TextDocument RtlDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p = new Paragraph { Formatting = ParagraphFormatting.Default with { Rtl = true } };
        p.Runs.Add(new Run("שלום", new RunFormatting { Rtl = true })); // "שלום"
        doc.Blocks.Add(p);
        return doc;
    }

    [Fact]
    public void ParagraphAndRunRtl_SurviveRoundTrip()
    {
        var result = RoundTrip(RtlDocument());

        var paragraph = result.Blocks.OfType<Paragraph>().First();
        Assert.True(paragraph.Formatting.Rtl);
        Assert.True(paragraph.Runs[0].Formatting.Rtl);
    }

    [Fact]
    public void Rtl_EmitsBidiAndRtlElements()
    {
        var docx = WriteBytes(RtlDocument());
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        var doc = XDocument.Load(entry);

        Assert.NotNull(doc.Descendants(W + "bidi").FirstOrDefault());
        Assert.NotNull(doc.Descendants(W + "rtl").FirstOrDefault());
    }

    [Fact]
    public void NonRtlDocument_EmitsNoBidiOrRtl()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("hello"));
        doc.Blocks.Add(p);

        var docx = WriteBytes(doc);
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        var xml = XDocument.Load(entry);

        Assert.Empty(xml.Descendants(W + "bidi"));
        Assert.Empty(xml.Descendants(W + "rtl"));
    }
}
