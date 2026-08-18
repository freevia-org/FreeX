using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for the W20 character border (w:bdr), character shading (w:shd with pattern),
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
    public void CharacterBorder_WritesBdrElement()
    {
        var border = new ParagraphBorder("#000000", 0.5);
        var rPr = WriteRunProperties(new RunFormatting { CharacterBorder = border });

        rPr.Element(W + "bdr").Should().NotBeNull("w:bdr must appear in the run properties (not the non-standard w:rBdr)");
        rPr.Element(W + "bdr")!.Element(W + "top").Should().NotBeNull("top edge drawn");
        rPr.Element(W + "bdr")!.Element(W + "bottom").Should().NotBeNull("bottom edge drawn");
        // Regression: old code used the non-standard w:rBdr element name.
        rPr.Element(W + "rBdr").Should().BeNull("w:rBdr is not a valid WordprocessingML run property — must be w:bdr");
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
    public void CharacterBorder_Null_WritesNoBdr()
    {
        var rPr = WriteRunProperties(new RunFormatting { Bold = true });
        rPr.Element(W + "bdr").Should().BeNull("no character border → no w:bdr element");
        rPr.Element(W + "rBdr").Should().BeNull("no character border → no w:rBdr element either");
    }

    [Fact]
    public void CharacterBorder_ExistingRunDefaults_AreUnaffected()
    {
        // A plain run without character border must round-trip without picking one up.
        var result = RoundTrip(new RunFormatting { Bold = true, Italic = true });
        result.CharacterBorder.Should().BeNull();
    }

    [Fact]
    public void CharacterBorder_EmittedBeforeShd_SchemaOrder()
    {
        // Z4 regression: w:bdr must precede w:shd in CT_RPr/EG_RPrBase. A run with BOTH a character
        // border AND character shading previously had the elements in the wrong order (bdr after shd),
        // which causes Word to report "unreadable content / repair". Assert the element order directly.
        var border = new ParagraphBorder("#FF0000", 1.0)
        {
            LineStyle = BorderLineStyle.Single,
            Top = true, Left = true, Bottom = true, Right = true
        };
        var rPr = WriteRunProperties(new RunFormatting
        {
            CharacterBorder = border,
            CharacterShadingHex = "#92D050",
            CharacterShadingPattern = ShadingPattern.Pct25,
        });

        var bdr = rPr.Element(W + "bdr");
        var shd = rPr.Element(W + "shd");
        bdr.Should().NotBeNull("w:bdr must be present");
        shd.Should().NotBeNull("w:shd must be present");

        var children = rPr.Elements().ToList();
        var bdrIdx = children.IndexOf(bdr!);
        var shdIdx = children.IndexOf(shd!);
        bdrIdx.Should().BeLessThan(shdIdx,
            "w:bdr must precede w:shd in CT_RPr (EG_RPrBase schema order) — wrong order causes Word repair");
    }

    [Fact]
    public void CharacterBorder_WithShading_BothSurviveRoundTrip()
    {
        // Z3+Z4 combined: a run with a character border AND character shading must round-trip both
        // properties intact now that the element name is w:bdr (not w:rBdr) and the order is correct.
        var border = new ParagraphBorder("#0070C0", 0.75)
        {
            LineStyle = BorderLineStyle.Single,
            Top = true, Left = true, Bottom = true, Right = true
        };
        var result = RoundTrip(new RunFormatting
        {
            CharacterBorder = border,
            CharacterShadingHex = "#FFC000",
            CharacterShadingPattern = ShadingPattern.Pct10,
        });

        result.CharacterBorder.Should().NotBeNull();
        result.CharacterBorder!.ColorHex.Should().Be("#0070C0");
        result.CharacterBorder.LineStyle.Should().Be(BorderLineStyle.Single);
        result.CharacterShadingHex.Should().Be("#FFC000");
        result.CharacterShadingPattern.Should().Be(ShadingPattern.Pct10);
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

    // Word's standard three-script default (e.g. val="en-US" eastAsia="en-US" bidi="ar-SA") stores each
    // w:lang attribute independently. The three fields must round-trip without any one script's tag
    // clobbering another's -- this is the important regression coverage for the open-and-save case.
    [Fact]
    public void LanguageTag_EastAsiaAndBidi_RoundTripIndependently()
    {
        var result = RoundTrip(new RunFormatting
        {
            LanguageTag = "en-US",
            EastAsiaLanguageTag = "ja-JP",
            BidiLanguageTag = "ar-SA",
        });

        result.LanguageTag.Should().Be("en-US");
        result.EastAsiaLanguageTag.Should().Be("ja-JP");
        result.BidiLanguageTag.Should().Be("ar-SA");
    }

    [Fact]
    public void LanguageTag_EastAsiaAndBidi_WriteIndependentAttributes()
    {
        var rPr = WriteRunProperties(new RunFormatting
        {
            LanguageTag = "en-US",
            EastAsiaLanguageTag = "ja-JP",
            BidiLanguageTag = "ar-SA",
        });

        var lang = rPr.Element(W + "lang");
        lang.Should().NotBeNull();
        lang!.Attribute(W + "val")?.Value.Should().Be("en-US");
        lang.Attribute(W + "eastAsia")?.Value.Should().Be("ja-JP");
        lang.Attribute(W + "bidi")?.Value.Should().Be("ar-SA");
    }

    // Sibling/no-regression coverage: a run that only ever set the general-script language (the common
    // shape for a document authored/edited purely in FreeW) must not gain fabricated eastAsia/bidi
    // attributes that were never set.
    [Fact]
    public void LanguageTag_OnlyValSet_DoesNotFabricateEastAsiaOrBidi()
    {
        var rPr = WriteRunProperties(new RunFormatting { LanguageTag = "en-US" });

        var lang = rPr.Element(W + "lang");
        lang.Should().NotBeNull();
        lang!.Attribute(W + "val")?.Value.Should().Be("en-US");
        lang.Attribute(W + "eastAsia").Should().BeNull();
        lang.Attribute(W + "bidi").Should().BeNull();

        var result = RoundTrip(new RunFormatting { LanguageTag = "en-US" });
        result.LanguageTag.Should().Be("en-US");
        result.EastAsiaLanguageTag.Should().BeNull();
        result.BidiLanguageTag.Should().BeNull();
    }

    // ---- Run Typefaces (w:rFonts) ----

    [Fact]
    public void FontFamily_Ascii_RoundTrips()
    {
        var result = RoundTrip(new RunFormatting { FontFamily = "Calibri" });
        result.FontFamily.Should().Be("Calibri");
    }

    [Fact]
    public void FontFamily_Null_WritesNoRFonts()
    {
        var rPr = WriteRunProperties(new RunFormatting { Bold = true });
        rPr.Element(W + "rFonts").Should().BeNull("no typeface set → no w:rFonts element");
    }

    // Word's normal mixed-script pattern (e.g. ascii="Calibri" eastAsia="MS Gothic" cs="Arial") stores
    // each w:rFonts typeface attribute independently. The three fields must round-trip without any one
    // script's font clobbering another's -- this is the important regression coverage for the
    // open-and-save case (round 144 finding freew-run-eastasia-cs-font-lost).
    [Fact]
    public void FontFamily_EastAsiaAndComplexScript_RoundTripIndependently()
    {
        var result = RoundTrip(new RunFormatting
        {
            FontFamily = "Calibri",
            EastAsiaFontFamily = "MS Gothic",
            ComplexScriptFontFamily = "Arial",
        });

        result.FontFamily.Should().Be("Calibri");
        result.EastAsiaFontFamily.Should().Be("MS Gothic");
        result.ComplexScriptFontFamily.Should().Be("Arial");
    }

    [Fact]
    public void FontFamily_EastAsiaAndComplexScript_WriteIndependentAttributes()
    {
        var rPr = WriteRunProperties(new RunFormatting
        {
            FontFamily = "Calibri",
            EastAsiaFontFamily = "MS Gothic",
            ComplexScriptFontFamily = "Arial",
        });

        var rFonts = rPr.Element(W + "rFonts");
        rFonts.Should().NotBeNull();
        rFonts!.Attribute(W + "ascii")?.Value.Should().Be("Calibri");
        rFonts.Attribute(W + "hAnsi")?.Value.Should().Be("Calibri");
        rFonts.Attribute(W + "eastAsia")?.Value.Should().Be("MS Gothic");
        rFonts.Attribute(W + "cs")?.Value.Should().Be("Arial");
    }

    // A pure-CJK run written by Word can carry only @eastAsia with no @ascii at all. Must still round-trip
    // (the earlier code read only @ascii, so this case came back as a completely blank FontFamily).
    [Fact]
    public void FontFamily_EastAsiaOnly_NoAscii_RoundTrips()
    {
        var result = RoundTrip(new RunFormatting { EastAsiaFontFamily = "MS Gothic" });

        result.FontFamily.Should().BeNull();
        result.EastAsiaFontFamily.Should().Be("MS Gothic");
    }

    [Fact]
    public void FontFamily_EastAsiaOnly_NoAscii_WritesRFontsWithoutAscii()
    {
        var rPr = WriteRunProperties(new RunFormatting { EastAsiaFontFamily = "MS Gothic" });

        var rFonts = rPr.Element(W + "rFonts");
        rFonts.Should().NotBeNull("an East Asian-only typeface must still emit w:rFonts");
        rFonts!.Attribute(W + "ascii").Should().BeNull();
        rFonts.Attribute(W + "eastAsia")?.Value.Should().Be("MS Gothic");
    }

    // Sibling/no-regression coverage: a run that only ever set the ascii typeface (the common shape for a
    // document authored/edited purely in FreeW) must not gain fabricated eastAsia/cs attributes.
    [Fact]
    public void FontFamily_OnlyAsciiSet_DoesNotFabricateEastAsiaOrComplexScript()
    {
        var rPr = WriteRunProperties(new RunFormatting { FontFamily = "Calibri" });

        var rFonts = rPr.Element(W + "rFonts");
        rFonts.Should().NotBeNull();
        rFonts!.Attribute(W + "ascii")?.Value.Should().Be("Calibri");
        rFonts.Attribute(W + "hAnsi")?.Value.Should().Be("Calibri");
        rFonts.Attribute(W + "eastAsia").Should().BeNull();
        rFonts.Attribute(W + "cs").Should().BeNull();

        var result = RoundTrip(new RunFormatting { FontFamily = "Calibri" });
        result.FontFamily.Should().Be("Calibri");
        result.EastAsiaFontFamily.Should().BeNull();
        result.ComplexScriptFontFamily.Should().BeNull();
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
