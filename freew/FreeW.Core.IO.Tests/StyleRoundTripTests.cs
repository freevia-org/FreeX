using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for custom style definitions in word/styles.xml: the "Style for following
/// paragraph" (w:next) and the style-level paragraph formatting (alignment / indents / spacing), neither
/// of which FreeW previously emitted — a custom paragraph style lost all of that on save.
/// </summary>
public class StyleRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static XDocument StylesXml(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/styles.xml")!.Open();
        return XDocument.Load(entry);
    }

    private static XElement StyleElement(XDocument styles, string styleId) =>
        styles.Root!.Elements(W + "style").Single(e => (string?)e.Attribute(W + "styleId") == styleId);

    [Fact]
    public void NextStyleId_RoundTrips_ThroughStylesXml()
    {
        var doc = TextDocument.CreateEmpty();
        // A heading-like style whose follow-on is Normal (Word's body-text-after-a-heading behaviour).
        StyleManager.CreateStyle(
            doc, "My Heading", basedOnId: "Heading1",
            new RunFormatting { Bold = true }, ParagraphFormatting.Default,
            nextStyleId: "Normal");

        var result = RoundTrip(doc);

        result.Styles.Should().ContainKey("MyHeading");
        result.Styles["MyHeading"].NextStyleId.Should().Be("Normal");
    }

    [Fact]
    public void NextStyleId_EmitsWNext_AfterBasedOn_InSchemaOrder()
    {
        var doc = TextDocument.CreateEmpty();
        StyleManager.CreateStyle(
            doc, "Lead In", basedOnId: "Normal",
            RunFormatting.Default, ParagraphFormatting.Default, nextStyleId: "Normal");

        var element = StyleElement(StylesXml(doc), "LeadIn");
        var childNames = element.Elements().Select(e => e.Name.LocalName).ToList();

        childNames.Should().Contain("next");
        // CT_Style requires w:basedOn before w:next.
        childNames.IndexOf("basedOn").Should().BeLessThan(childNames.IndexOf("next"));
        element.Element(W + "next")!.Attribute(W + "val")!.Value.Should().Be("Normal");
    }

    [Fact]
    public void Style_WithoutNext_EmitsNoWNext()
    {
        var doc = TextDocument.CreateEmpty();
        StyleManager.CreateStyle(doc, "Plain", basedOnId: null, RunFormatting.Default, ParagraphFormatting.Default);

        StyleElement(StylesXml(doc), "Plain").Element(W + "next").Should().BeNull();
    }

    [Fact]
    public void CharacterStyle_NeverEmitsWNext_EvenIfSet()
    {
        var doc = TextDocument.CreateEmpty();
        // Author a character style directly (StyleManager only creates paragraph styles) and force a next id.
        doc.Styles["Emphasis2"] = new DocumentStyle
        {
            Id = "Emphasis2",
            Name = "Emphasis 2",
            Type = StyleType.Character,
            NextStyleId = "Normal",
            Run = new RunFormatting { Italic = true },
        };

        StyleElement(StylesXml(doc), "Emphasis2").Element(W + "next").Should().BeNull();
    }

    [Fact]
    public void OutlineLevel_RoundTripsAndWritesHeadingStyleMetadata()
    {
        var doc = TextDocument.CreateEmpty();

        var styles = StylesXml(doc);
        StyleElement(styles, "Heading1").Element(W + "pPr")!.Element(W + "outlineLvl")!
            .Attribute(W + "val")!.Value.Should().Be("0");

        var result = RoundTrip(doc);
        result.Styles["Heading1"].OutlineLevel.Should().Be(0);
        result.Styles["Heading2"].OutlineLevel.Should().Be(1);
        result.Styles["Heading3"].OutlineLevel.Should().Be(2);
    }

    [Fact]
    public void StyleParagraphFormatting_RoundTrips()
    {
        var doc = TextDocument.CreateEmpty();
        StyleManager.CreateStyle(
            doc, "Body Indented", basedOnId: "Normal",
            RunFormatting.Default,
            new ParagraphFormatting
            {
                Alignment = TextAlignment.Justify,
                IndentLeftPt = 18,
                FirstLineIndentPt = 12,
                SpaceBeforePt = 6,
                SpaceAfterPt = 10,
            });

        var p = RoundTrip(doc).Styles["BodyIndented"].Paragraph;

        p.Alignment.Should().Be(TextAlignment.Justify);
        p.IndentLeftPt.Should().BeApproximately(18, 0.5);
        p.FirstLineIndentPt.Should().BeApproximately(12, 0.5);
        p.SpaceBeforePt.Should().BeApproximately(6, 0.5);
        p.SpaceAfterPt.Should().BeApproximately(10, 0.5);
    }

    [Fact]
    public void StyleTabStopAndParagraphClear_RoundTripAsDistinctOperations()
    {
        var doc = TextDocument.CreateEmpty();
        StyleManager.CreateStyle(
            doc,
            "Tabbed Body",
            basedOnId: "Normal",
            RunFormatting.Default,
            ParagraphFormatting.Default with
            {
                TabStops = [new TabStop(72, TabStopAlignment.Right, TabLeader.Dots)],
            });
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("clear the style tab")
        {
            StyleId = "TabbedBody",
            Formatting = ParagraphFormatting.Default with
            {
                TabStops = [new TabStop(72, IsClear: true)],
            },
        });

        var result = RoundTrip(doc);

        result.Styles["TabbedBody"].Paragraph.TabStops.Should().Equal(
            new TabStop(72, TabStopAlignment.Right, TabLeader.Dots));
        result.Paragraphs.Single().Formatting.TabStops.Should().Equal(
            new TabStop(72, IsClear: true));
    }

    [Fact]
    public void StyleParagraphFormatting_AndNumbering_ShareSinglePPr()
    {
        var doc = TextDocument.CreateEmpty();
        StyleManager.CreateStyle(
            doc, "Centered", basedOnId: "Normal",
            RunFormatting.Default,
            ParagraphFormatting.Default with { Alignment = TextAlignment.Center });

        var element = StyleElement(StylesXml(doc), "Centered");
        // A style definition may carry at most one w:pPr (CT_Style); the formatting must not create a second.
        element.Elements(W + "pPr").Should().HaveCount(1);
        element.Element(W + "pPr")!.Element(W + "jc")!.Attribute(W + "val")!.Value.Should().Be("center");
    }

    [Fact]
    public void DocumentStyleSetFormatting_RoundTripsThroughStylesXml()
    {
        var doc = TextDocument.CreateEmpty();
        DocumentStyleSet.Apply(doc, DocumentStyleSet.FindByName("Elegant")!);

        var result = RoundTrip(doc);

        result.Styles["Title"].Run.FontFamily.Should().Be("Cambria");
        result.Styles["Title"].Run.ColorHex.Should().Be("#5B3A29");
        result.Styles["Heading1"].Run.ColorHex.Should().Be("#5B3A29");
        result.Styles["Quote"].Paragraph.IndentLeftPt.Should().BeApproximately(36, 0.5);
        result.Styles["Quote"].Paragraph.IndentRightPt.Should().BeApproximately(36, 0.5);
    }

    [Fact]
    public void DocumentParagraphSpacingSet_RoundTripsThroughStylesXml()
    {
        var doc = TextDocument.CreateEmpty();
        DocumentParagraphSpacingSet.Apply(doc, DocumentParagraphSpacingSet.FindByName("Double")!);

        var result = RoundTrip(doc);
        var normal = result.Styles["Normal"].Paragraph;
        var heading = result.Styles["Heading1"].Paragraph;

        normal.SpaceBeforePt.Should().BeApproximately(0, 0.5);
        normal.SpaceAfterPt.Should().BeApproximately(8, 0.5);
        normal.LineSpacing.Should().BeApproximately(2.0, 0.01);
        heading.SpaceAfterPt.Should().BeApproximately(8, 0.5);
        heading.LineSpacing.Should().BeApproximately(2.0, 0.01);
    }
}
