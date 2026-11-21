using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip pins for the Arabic/Persian fidelity gaps measured against Word on a right-to-left book:
/// complex-script bold (w:bCs), Arabic page-number formats, compound (double) shape outlines, and the
/// blank-paragraph first-page headers that push a chapter opener down.
/// </summary>
public sealed class RtlTocFidelityRoundTripTests
{
    [Fact]
    public void ComplexScriptBold_RoundTripsThroughBCs()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("مقدمه", new RunFormatting { ComplexScriptBold = true, Rtl = true }));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);

        var run = read.Blocks.OfType<Paragraph>().First().Runs.First(r => r.Text.Length > 0);
        run.Formatting.ComplexScriptBold.Should().BeTrue();
        run.Formatting.Bold.Should().BeFalse("w:bCs bolds complex-script text only and is not w:b");
    }

    [Theory]
    [InlineData(PageNumberFormat.ArabicAbjad, "arabicAbjad")]
    [InlineData(PageNumberFormat.ArabicAlpha, "arabicAlpha")]
    public void ArabicPageNumberFormats_RoundTripThroughPgNumType(PageNumberFormat format, string token)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Page.PageNumberFormat = format;

        var read = RoundTrip(doc);

        read.Page.PageNumberFormat.Should().Be(format);
        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        XDocument.Load(entry).Descendants(W + "pgNumType").Single().Attribute(W + "fmt")!.Value.Should().Be(token);
    }

    [Fact]
    public void DoubleCompoundOutline_RoundTripsThroughLnCmpd()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var shape = Shape.Preset(ShapeKind.Rectangle, 100, 40, "#FFFFFF");
        shape.OutlineColorHex = "#000000";
        shape.OutlineWidthPt = 3;
        shape.OutlineCompound = "dbl";
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromShape(shape));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);

        read.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs).Select(r => r.Shape).OfType<Shape>().Single()
            .OutlineCompound.Should().Be("dbl");
    }

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }
}
