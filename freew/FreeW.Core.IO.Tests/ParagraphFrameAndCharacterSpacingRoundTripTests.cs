using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>Round-trip coverage for the paragraph frame (w:framePr) and the run character spacing (w:spacing).</summary>
public class ParagraphFrameAndCharacterSpacingRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextDocument RoundTrip(TextDocument doc, out XDocument xml)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        using (var entry = zip.GetEntry("word/document.xml")!.Open())
            xml = XDocument.Load(entry);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    [Fact]
    public void Frame_RoundTripsEveryAttribute_AndConsecutiveParagraphsStayEqual()
    {
        var frame = ParagraphFrame.FromAttributes(
        [
            new("w", "9792"), new("h", "432"), new("hSpace", "180"), new("wrap", "around"),
            new("hAnchor", "text"), new("vAnchor", "text"), new("y", "1")
        ]);
        var doc = new TextDocument();
        for (var i = 0; i < 2; i++)
        {
            var paragraph = new Paragraph { Frame = frame };
            paragraph.Runs.Add(new Run("framed " + i));
            doc.Blocks.Add(paragraph);
        }

        var read = RoundTrip(doc, out var xml);

        var framePr = xml.Descendants(W + "framePr").ToList();
        framePr.Should().HaveCount(2);
        framePr[0].Attribute(W + "w")!.Value.Should().Be("9792");
        framePr[0].Attribute(W + "wrap")!.Value.Should().Be("around");
        var paragraphs = read.Blocks.OfType<Paragraph>().ToList();
        paragraphs[0].Frame.Should().Be(frame);
        paragraphs[1].Frame.Should().Be(paragraphs[0].Frame);
    }

    [Fact]
    public void Frame_IsReadFromAuthoredMarkup_AndAnUnframedParagraphHasNone()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph { Frame = ParagraphFrame.FromAttributes([new("wrap", "around"), new("xAlign", "right")]) });
        doc.Blocks.Add(new Paragraph());

        var read = RoundTrip(doc, out _);

        var paragraphs = read.Blocks.OfType<Paragraph>().ToList();
        paragraphs[0].Frame!.Get("xAlign").Should().Be("right");
        paragraphs[0].Frame!.Get("wrap").Should().Be("around");
        paragraphs[0].Frame!.Get("missing").Should().BeNull();
        paragraphs[1].Frame.Should().BeNull();
    }

    [Fact]
    public void DropCap_StillWritesItsOwnFrame_WithoutAGenericOne()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph { Frame = ParagraphFrame.FromAttributes([new("wrap", "around")]) };
        paragraph.Runs.Add(new Run("Dropped"));
        DropCap.ApplyDropCap(paragraph);
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc, out var xml);

        xml.Descendants(W + "framePr").Should().ContainSingle();
        xml.Descendants(W + "framePr").Single().Attribute(W + "dropCap").Should().NotBeNull();
        read.Blocks.OfType<Paragraph>().First().Frame.Should().BeNull();
    }

    [Theory]
    [InlineData(3.0)]
    [InlineData(-0.15)]
    [InlineData(-1.0)]
    public void CharacterSpacing_RoundTripsAsTwentiethsOfAPoint(double spacingPt)
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("spaced", new RunFormatting { CharacterSpacingPt = spacingPt }));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc, out var xml);

        var spacing = xml.Descendants(W + "rPr").Elements(W + "spacing").Single();
        int.Parse(spacing.Attribute(W + "val")!.Value, System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be((int)Math.Round(spacingPt * 20));
        read.Blocks.OfType<Paragraph>().Single().Runs[0].Formatting.CharacterSpacingPt.Should().BeApproximately(spacingPt, 0.001);
    }
}
