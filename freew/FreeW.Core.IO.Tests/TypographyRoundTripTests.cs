using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for the Z1 advanced-typography run properties (character spacing, kerning,
/// position, ligatures, stylistic sets, number form/spacing). Each feature is written, re-read, and the
/// recovered value is asserted; defaults are asserted unchanged so existing runs round-trip byte-identical.
/// </summary>
public class TypographyRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W14 = "http://schemas.microsoft.com/office/word/2010/wordml";

    private static RunFormatting RoundTrip(RunFormatting formatting)
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("x", formatting));
        doc.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        var read = DocxReader.Read(stream);
        return read.Blocks.OfType<Paragraph>().First().Runs.First().Formatting;
    }

    /// <summary>Writes the document and returns the first run's w:rPr element for structural assertions.</summary>
    private static XElement WriteRunProperties(RunFormatting formatting)
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("x", formatting));
        doc.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry).Descendants(W + "rPr").First();
    }

    [Theory]
    [InlineData(1.5)]   // expanded
    [InlineData(-0.75)] // condensed
    public void CharacterSpacing_RoundTrips(double pt)
    {
        var result = RoundTrip(new RunFormatting { CharacterSpacingPt = pt });
        result.CharacterSpacingPt.Should().BeApproximately(pt, 0.05);
    }

    [Fact]
    public void CharacterSpacing_WrittenInTwentiethsOfAPoint()
    {
        // 1.5pt -> 30 dxa (twentieths). Condensed is negative.
        WriteRunProperties(new RunFormatting { CharacterSpacingPt = 1.5 })
            .Element(W + "spacing")!.Attribute(W + "val")!.Value.Should().Be("30");
        WriteRunProperties(new RunFormatting { CharacterSpacingPt = -0.75 })
            .Element(W + "spacing")!.Attribute(W + "val")!.Value.Should().Be("-15");
    }

    [Fact]
    public void Kerning_RoundTrips()
    {
        var result = RoundTrip(new RunFormatting { KerningMinSizePt = 12 });
        result.KerningMinSizePt.Should().Be(12);
        // 12pt -> 24 half-points.
        WriteRunProperties(new RunFormatting { KerningMinSizePt = 12 })
            .Element(W + "kern")!.Attribute(W + "val")!.Value.Should().Be("24");
    }

    /// <summary>
    /// Regression: DocxReader.ReadRunFormatting reads w:sz (font size, half-points) and w:kern (kerning
    /// min size, half-points) via the shared HalfPointsToPoints conversion. An explicit
    /// <c>w:val="0"</c> must be read back as 0.0, not collapsed into "attribute absent" (null) — the two
    /// XML shapes are distinct and must produce distinct model values. Constructs the w:rPr directly
    /// (bypassing DocxWriter, whose own w:kern guard only emits the element for KerningMinSizePt &gt; 0)
    /// so this exercises exactly the reader path that owns the parsing bug.
    /// </summary>
    [Fact]
    public void FontSizeAndKerning_ExplicitZero_IsPreservedNotDefaulted()
    {
        var rPr = new XElement(W + "rPr",
            new XElement(W + "sz", new XAttribute(W + "val", "0")),
            new XElement(W + "kern", new XAttribute(W + "val", "0")));

        var formatting = DocxReader.ReadRunFormatting(rPr);

        formatting.FontSizePt.Should().Be(0.0,
            "an explicit w:sz val=\"0\" is a real value, not an absent attribute");
        formatting.KerningMinSizePt.Should().Be(0.0,
            "an explicit w:kern val=\"0\" is a real value, not an absent attribute");
    }

    /// <summary>
    /// Sibling no-regression: when w:sz / w:kern are genuinely absent from w:rPr, the reader must still
    /// map that to null (not 0), so the fix for explicit-zero above does not also start treating "unset"
    /// as zero.
    /// </summary>
    [Fact]
    public void FontSizeAndKerning_Absent_IsStillNull()
    {
        var rPr = new XElement(W + "rPr", new XElement(W + "b"));

        var formatting = DocxReader.ReadRunFormatting(rPr);

        formatting.FontSizePt.Should().BeNull("w:sz was never written, so there is no size to recover");
        formatting.KerningMinSizePt.Should().BeNull("w:kern was never written, so kerning is unset");
    }

    [Theory]
    [InlineData(6)]   // raised
    [InlineData(-4)]  // lowered
    public void Position_RoundTrips(double pt)
    {
        var result = RoundTrip(new RunFormatting { PositionPt = pt });
        result.PositionPt.Should().BeApproximately(pt, 0.01);
        // Position is in half-points, signed.
        WriteRunProperties(new RunFormatting { PositionPt = pt })
            .Element(W + "position")!.Attribute(W + "val")!.Value
            .Should().Be(((int)(pt * 2)).ToString());
    }

    [Theory]
    [InlineData(LigatureMode.NoneExplicit, "none")]
    [InlineData(LigatureMode.Standard, "standard")]
    [InlineData(LigatureMode.Contextual, "contextual")]
    [InlineData(LigatureMode.StandardContextual, "standardContextual")]
    [InlineData(LigatureMode.Historical, "historical")]
    [InlineData(LigatureMode.Discretional, "discretional")]
    [InlineData(LigatureMode.All, "all")]
    public void Ligatures_RoundTrips(LigatureMode mode, string expectedToken)
    {
        var result = RoundTrip(new RunFormatting { Ligatures = mode });
        result.Ligatures.Should().Be(mode);
        WriteRunProperties(new RunFormatting { Ligatures = mode })
            .Element(W14 + "ligatures")!.Attribute(W14 + "val")!.Value.Should().Be(expectedToken);
    }

    [Fact]
    public void StylisticSet_RoundTrips()
    {
        var result = RoundTrip(new RunFormatting { StylisticSet = 4 });
        result.StylisticSet.Should().Be(4);
        var styleSet = WriteRunProperties(new RunFormatting { StylisticSet = 4 })
            .Element(W14 + "stylisticSets")!.Element(W14 + "styleSet")!;
        styleSet.Attribute(W14 + "id")!.Value.Should().Be("4");
    }

    [Theory]
    [InlineData(NumberForm.Lining, "lining")]
    [InlineData(NumberForm.OldStyle, "oldStyle")]
    public void NumberForm_RoundTrips(NumberForm form, string expectedToken)
    {
        var result = RoundTrip(new RunFormatting { NumberForm = form });
        result.NumberForm.Should().Be(form);
        WriteRunProperties(new RunFormatting { NumberForm = form })
            .Element(W14 + "numForm")!.Attribute(W14 + "val")!.Value.Should().Be(expectedToken);
    }

    [Theory]
    [InlineData(NumberSpacing.Proportional, "proportional")]
    [InlineData(NumberSpacing.Tabular, "tabular")]
    public void NumberSpacing_RoundTrips(NumberSpacing spacing, string expectedToken)
    {
        var result = RoundTrip(new RunFormatting { NumberSpacing = spacing });
        result.NumberSpacing.Should().Be(spacing);
        WriteRunProperties(new RunFormatting { NumberSpacing = spacing })
            .Element(W14 + "numSpacing")!.Attribute(W14 + "val")!.Value.Should().Be(expectedToken);
    }

    [Fact]
    public void DefaultRun_EmitsNoAdvancedTypographyChildren()
    {
        // Regression: a run with no advanced typography must round-trip with an unchanged w:rPr — none of
        // the new elements appear. (A plain run still emits its existing rPr children only when present;
        // here the run carries bold so an rPr exists, letting us assert the *absence* of the new children.)
        var rPr = WriteRunProperties(new RunFormatting { Bold = true });
        var localNames = rPr.Elements().Select(e => e.Name.LocalName).ToList();
        localNames.Should().NotContain("spacing");
        localNames.Should().NotContain("kern");
        localNames.Should().NotContain("position");
        localNames.Should().NotContain("ligatures");
        localNames.Should().NotContain("numForm");
        localNames.Should().NotContain("numSpacing");
        localNames.Should().NotContain("stylisticSets");
        // The bold toggle is still the only child.
        localNames.Should().Equal("b");
    }

    [Fact]
    public void DefaultRun_RoundTripsToDefaults()
    {
        var result = RoundTrip(RunFormatting.Default);
        result.CharacterSpacingPt.Should().Be(0);
        result.KerningMinSizePt.Should().BeNull();
        result.PositionPt.Should().Be(0);
        result.Ligatures.Should().Be(LigatureMode.None);
        result.StylisticSet.Should().BeNull();
        result.NumberForm.Should().Be(NumberForm.Default);
        result.NumberSpacing.Should().Be(NumberSpacing.Default);
    }

    [Fact]
    public void CombinedFeatures_EmitRPrChildrenInCanonicalSchemaOrder()
    {
        // A run combining bold + character spacing + kern + position + sz + ligatures must keep the
        // CT_RPr (EG_RPrBase) sequence: the core elements in order, then the w14 extension elements last.
        var rPr = WriteRunProperties(new RunFormatting
        {
            FontFamily = "Arial",
            Bold = true,
            ColorHex = "#112233",
            CharacterSpacingPt = 1.0,
            KerningMinSizePt = 10,
            PositionPt = 3,
            FontSizePt = 14,
            Underline = true,
            VerticalAlign = VerticalAlign.Superscript,
            Ligatures = LigatureMode.Standard,
            NumberForm = NumberForm.Lining,
            NumberSpacing = NumberSpacing.Tabular,
            StylisticSet = 1
        });

        var names = rPr.Elements().Select(e => e.Name.LocalName).ToList();

        // Canonical order: core EG_RPrBase elements (rFonts..vertAlign with spacing/kern/position between
        // color and sz), then the w14 extension elements in writer order.
        var canonical = new[]
        {
            "rFonts", "b", "i", "caps", "smallCaps", "strike", "color",
            "spacing", "kern", "position", "sz", "szCs", "u", "shd", "vertAlign",
            "ligatures", "numForm", "numSpacing", "stylisticSets"
        };
        var expected = canonical.Where(names.Contains).ToList();
        names.Should().Equal(expected);

        // Spot-check the three core advanced elements sit between color and sz, in spacing/kern/position order.
        names.IndexOf("spacing").Should().BeGreaterThan(names.IndexOf("color"));
        names.IndexOf("kern").Should().BeGreaterThan(names.IndexOf("spacing"));
        names.IndexOf("position").Should().BeGreaterThan(names.IndexOf("kern"));
        names.IndexOf("sz").Should().BeGreaterThan(names.IndexOf("position"));
        // And the w14 elements come after the last core element (vertAlign).
        names.IndexOf("ligatures").Should().BeGreaterThan(names.IndexOf("vertAlign"));
    }

    [Fact]
    public void CombinedFeatures_RoundTripAllValues()
    {
        var original = new RunFormatting
        {
            Bold = true,
            CharacterSpacingPt = 2.0,
            KerningMinSizePt = 8,
            PositionPt = -5,
            Ligatures = LigatureMode.StandardContextual,
            StylisticSet = 7,
            NumberForm = NumberForm.OldStyle,
            NumberSpacing = NumberSpacing.Proportional
        };
        var result = RoundTrip(original);

        result.CharacterSpacingPt.Should().BeApproximately(2.0, 0.05);
        result.KerningMinSizePt.Should().Be(8);
        result.PositionPt.Should().BeApproximately(-5, 0.01);
        result.Ligatures.Should().Be(LigatureMode.StandardContextual);
        result.StylisticSet.Should().Be(7);
        result.NumberForm.Should().Be(NumberForm.OldStyle);
        result.NumberSpacing.Should().Be(NumberSpacing.Proportional);
    }
}
