using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for the W20 character border (w:rBdr), character shading (w:shd with pattern),
/// and proofing language (w:lang) run properties. Each feature is written via DocxWriter, re-read via
/// DocxReader, and the recovered value is asserted. The defaults are also asserted so existing runs
/// that lack these fields remain byte-unchanged through the round-trip.
/// </summary>
public class CharacterBorderShadingLanguageRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    // Helpers shared with TypographyRoundTripTests pattern.
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

    // ---- Character Border (w:rBdr) ----

    [Fact]
    public void CharacterBorder_AllEdges_RoundTrips()
    {
        var border = new ParagraphBorder("#FF0000", 1.5)
        {
            LineStyle = BorderLineStyle.Single,
            Top = true, Left = true, Bottom = true, Right = true
        };
        var result = RoundTrip(new RunFormatting { CharacterBorder = border });

        result.CharacterBorder.Should().NotBeNull();
        result.CharacterBorder!.ColorHex.Should().Be("#FF0000");
        result.CharacterBorder.WidthPt.Should().BeApproximately(1.5, 0.05);
        result.CharacterBorder.LineStyle.Should().Be(BorderLineStyle.Single);
        result.CharacterBorder.Top.Should().BeTrue();
        result.CharacterBorder.Left.Should().BeTrue();
        result.CharacterBorder.Bottom.Should().BeTrue();
        result.CharacterBorder.Right.Should().BeTrue();
    }

    [Fact]
    public void CharacterBorder_DashedStyle_RoundTrips()
    {
        var border = new ParagraphBorder("#0070C0", 0.5)
        {
            LineStyle = BorderLineStyle.Dashed,
        };
        var result = RoundTrip(new RunFormatting { CharacterBorder = border });

        result.CharacterBorder.Should().NotBeNull();
        result.CharacterBorder!.LineStyle.Should().Be(BorderLineStyle.Dashed);
        result.CharacterBorder.ColorHex.Should().Be("#0070C0");
    }

    [Fact]
    public void CharacterBorder_WritesRBdrElement()
    {
        var border = new ParagraphBorder("#000000", 0.5);
        var rPr = WriteRunProperties(new RunFormatting { CharacterBorder = border });

        rPr.Element(W + "rBdr").Should().NotBeNull("w:rBdr must appear in the run properties");
        rPr.Element(W + "rBdr")!.Element(W + "top").Should().NotBeNull("top edge drawn");
        rPr.Element(W + "rBdr")!.Element(W + "bottom").Should().NotBeNull("bottom edge drawn");
    }

    [Fact]
    public void CharacterBorder_BottomOnly_RoundTrips()
    {
        var border = new ParagraphBorder("#00B050", 0.5, BottomOnly: true);
        var result = RoundTrip(new RunFormatting { CharacterBorder = border });

        result.CharacterBorder.Should().NotBeNull();
        result.CharacterBorder!.BottomOnly.Should().BeTrue("bottom-only flag must survive the round-trip");
        result.CharacterBorder.Bottom.Should().BeTrue();
        result.CharacterBorder.Top.Should().BeFalse();
    }

    [Fact]
    public void CharacterBorder_Null_WritesNoRBdr()
    {
        var rPr = WriteRunProperties(new RunFormatting { Bold = true });
        rPr.Element(W + "rBdr").Should().BeNull("no character border → no w:rBdr element");
    }

    [Fact]
    public void CharacterBorder_ExistingRunDefaults_AreUnaffected()
    {
        // A plain run without character border must round-trip without picking one up.
        var result = RoundTrip(new RunFormatting { Bold = true, Italic = true });
        result.CharacterBorder.Should().BeNull();
    }

    // ---- Character Shading (w:shd with pattern on run) ----

    [Fact]
    public void CharacterShading_Clear_WritesLikePlainHighlight()
    {
        // A CharacterShadingHex with ShadingPattern.Clear emits w:shd val="clear" — the same token
        // as a plain HighlightColorHex. The reader cannot distinguish them so it maps the fill back to
        // HighlightColorHex. This is by design: meaningful character shading always uses a non-clear
        // pattern (e.g. Pct10, Pct25, Pct50); val="clear" is only used for solid highlight colours.
        var result = RoundTrip(new RunFormatting
        {
            CharacterShadingHex = "#FFFF00",
            CharacterShadingPattern = ShadingPattern.Clear
        });

        // Comes back as highlight, not character shading.
        result.HighlightColorHex.Should().Be("#FFFF00");
        result.CharacterShadingHex.Should().BeNull();
    }

    [Fact]
    public void CharacterShading_Pct25Pattern_RoundTrips()
    {
        var result = RoundTrip(new RunFormatting
        {
            CharacterShadingHex = "#92D050",
            CharacterShadingPattern = ShadingPattern.Pct25
        });

        result.CharacterShadingHex.Should().Be("#92D050");
        result.CharacterShadingPattern.Should().Be(ShadingPattern.Pct25);
    }

    [Fact]
    public void CharacterShading_WritesCorrectShdPattern()
    {
        var rPr = WriteRunProperties(new RunFormatting
        {
            CharacterShadingHex = "#A6A6A6",
            CharacterShadingPattern = ShadingPattern.Pct50
        });

        var shd = rPr.Element(W + "shd");
        shd.Should().NotBeNull();
        shd!.Attribute(W + "val")?.Value.Should().Be("pct50");
        shd.Attribute(W + "fill")?.Value.Should().Be("A6A6A6");
    }

    [Fact]
    public void CharacterShading_TakesPrecedenceOverHighlight_InWriter()
    {
        // When both CharacterShadingHex and HighlightColorHex are set, CharacterShadingHex wins in the
        // w:shd slot. The reader maps it back to CharacterShadingHex (not HighlightColorHex).
        var rPr = WriteRunProperties(new RunFormatting
        {
            HighlightColorHex = "#FF0000",
            CharacterShadingHex = "#00FF00",
            CharacterShadingPattern = ShadingPattern.Clear,
        });

        var shd = rPr.Element(W + "shd");
        shd!.Attribute(W + "fill")?.Value.Should().Be("00FF00", "CharacterShadingHex wins in the w:shd slot");
    }

    [Fact]
    public void HighlightColor_Clear_StillWritesClearPattern()
    {
        // Legacy HighlightColorHex must continue to write w:shd val="clear" when no CharacterShadingHex.
        var rPr = WriteRunProperties(new RunFormatting { HighlightColorHex = "#FFFF00" });

        var shd = rPr.Element(W + "shd");
        shd.Should().NotBeNull();
        shd!.Attribute(W + "val")?.Value.Should().Be("clear");
        shd.Attribute(W + "fill")?.Value.Should().Be("FFFF00");
    }

    // ---- Proofing Language (w:lang) ----

    [Fact]
    public void LanguageTag_EnUS_RoundTrips()
    {
        var result = RoundTrip(new RunFormatting { LanguageTag = "en-US" });
        result.LanguageTag.Should().Be("en-US");
    }

    [Fact]
    public void LanguageTag_FrFR_RoundTrips()
    {
        var result = RoundTrip(new RunFormatting { LanguageTag = "fr-FR" });
        result.LanguageTag.Should().Be("fr-FR");
    }

    [Fact]
    public void LanguageTag_WritesLangElement()
    {
        var rPr = WriteRunProperties(new RunFormatting { LanguageTag = "de-DE" });

        var lang = rPr.Element(W + "lang");
        lang.Should().NotBeNull("w:lang must appear in the run properties");
        lang!.Attribute(W + "val")?.Value.Should().Be("de-DE");
    }

    [Fact]
    public void LanguageTag_Null_WritesNoLang()
    {
        var rPr = WriteRunProperties(new RunFormatting { Bold = true });
        rPr.Element(W + "lang").Should().BeNull("no language tag → no w:lang element");
    }

    [Fact]
    public void LanguageTag_Null_DefaultRunIsUnaffected()
    {
        var result = RoundTrip(new RunFormatting { Bold = true });
        result.LanguageTag.Should().BeNull();
    }

    // ---- Combined round-trip: border + shading + language all present ----

    [Fact]
    public void AllThree_SurviveRoundTrip()
    {
        var border = new ParagraphBorder("#FF0000", 1.0)
        {
            LineStyle = BorderLineStyle.Dashed,
            Top = true, Left = true, Bottom = true, Right = true
        };
        var input = new RunFormatting
        {
            Bold = true,
            CharacterBorder = border,
            CharacterShadingHex = "#FFC000",
            CharacterShadingPattern = ShadingPattern.Pct10,
            LanguageTag = "en-GB",
        };

        var result = RoundTrip(input);

        // Bold unchanged
        result.Bold.Should().BeTrue();
        // Character border
        result.CharacterBorder.Should().NotBeNull();
        result.CharacterBorder!.ColorHex.Should().Be("#FF0000");
        result.CharacterBorder.LineStyle.Should().Be(BorderLineStyle.Dashed);
        // Character shading
        result.CharacterShadingHex.Should().Be("#FFC000");
        result.CharacterShadingPattern.Should().Be(ShadingPattern.Pct10);
        // Language
        result.LanguageTag.Should().Be("en-GB");
    }
}
