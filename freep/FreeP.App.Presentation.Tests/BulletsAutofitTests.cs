using FreeP.App.Compositor;
using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Wave 19A: unit tests for bullet rendering (char/autonum formatting + indent) and
/// normAutofit font-scale application.
/// </summary>
public sealed class BulletsAutofitTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static PresentationModel MakePresentation() => PresentationModel.CreateEmpty();

    private static SlideShape MakeShapeWithText(TextBody body)
    {
        return new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu  = 457200,
            OffsetYEmu  = 274320,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 3000000,
            TextBody = body
        };
    }

    private static ResolvedTextLayout ComposeText(TextBody body)
    {
        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(MakeShapeWithText(body));
        var ops = SlideCompositor.Compose(p, p.Slides[0]);
        var shapeOp = ops.OfType<DrawOp.Shape>().First();
        return shapeOp.Text!;
    }

    // ─── FormatAutoNum formatter ───────────────────────────────────────────────

    [Theory]
    [InlineData(AutoNumType.ArabicPeriod,  1, "1.")]
    [InlineData(AutoNumType.ArabicPeriod,  2, "2.")]
    [InlineData(AutoNumType.ArabicPeriod,  3, "3.")]
    [InlineData(AutoNumType.ArabicParenR,  1, "1)")]
    [InlineData(AutoNumType.ArabicParenBoth, 1, "(1)")]
    [InlineData(AutoNumType.RomanUcPeriod, 1, "I.")]
    [InlineData(AutoNumType.RomanUcPeriod, 2, "II.")]
    [InlineData(AutoNumType.RomanUcPeriod, 3, "III.")]
    [InlineData(AutoNumType.RomanLcPeriod, 1, "i.")]
    [InlineData(AutoNumType.RomanLcPeriod, 4, "iv.")]
    [InlineData(AutoNumType.AlphaLcParenR, 1, "a)")]
    [InlineData(AutoNumType.AlphaLcParenR, 2, "b)")]
    [InlineData(AutoNumType.AlphaUcPeriod, 1, "A.")]
    [InlineData(AutoNumType.AlphaUcPeriod, 26, "Z.")]
    [InlineData(AutoNumType.AlphaUcPeriod, 27, "AA.")]
    [InlineData(AutoNumType.AlphaLcParenBoth, 1, "(a)")]
    public void FormatAutoNum_VariousTypes_CorrectText(AutoNumType type, int n, string expected)
    {
        SlideCompositor.FormatAutoNum(type, n).Should().Be(expected);
    }

    // ─── AutoNum counter increments across same-level paragraphs ──────────────

    [Fact]
    public void AutoNum_ArabicPeriod_ThreeParagraphs_Counts1to3()
    {
        var body = new TextBody();
        for (int i = 0; i < 3; i++)
        {
            var para = new Paragraph { BulletKind = BulletKind.Auto, AutoNumType = AutoNumType.ArabicPeriod };
            para.Runs.Add(new Run { Text = $"Item {i + 1}", FontSizePt = 18 });
            body.Paragraphs.Add(para);
        }

        var layout = ComposeText(body);

        layout.Paragraphs[0].BulletText.Should().Be("1.");
        layout.Paragraphs[1].BulletText.Should().Be("2.");
        layout.Paragraphs[2].BulletText.Should().Be("3.");
    }

    [Fact]
    public void AutoNum_RomanUcPeriod_TwoParagraphs_I_II()
    {
        var body = new TextBody();
        for (int i = 0; i < 2; i++)
        {
            var para = new Paragraph { BulletKind = BulletKind.Auto, AutoNumType = AutoNumType.RomanUcPeriod };
            para.Runs.Add(new Run { Text = $"Point {i + 1}", FontSizePt = 18 });
            body.Paragraphs.Add(para);
        }

        var layout = ComposeText(body);

        layout.Paragraphs[0].BulletText.Should().Be("I.");
        layout.Paragraphs[1].BulletText.Should().Be("II.");
    }

    [Fact]
    public void AutoNum_AlphaLcParenR_TwoParagraphs_a_b()
    {
        var body = new TextBody();
        for (int i = 0; i < 2; i++)
        {
            var para = new Paragraph { BulletKind = BulletKind.Auto, AutoNumType = AutoNumType.AlphaLcParenR };
            para.Runs.Add(new Run { Text = $"Sub {i + 1}", FontSizePt = 14 });
            body.Paragraphs.Add(para);
        }

        var layout = ComposeText(body);

        layout.Paragraphs[0].BulletText.Should().Be("a)");
        layout.Paragraphs[1].BulletText.Should().Be("b)");
    }

    [Fact]
    public void AutoNum_LevelCounterResets_WhenLevelDecrements()
    {
        // Level 0: "1." "2."  then Level 1: "1." "2."  then Level 0: "3."
        var body = new TextBody();
        AddAutoNumPara(body, level: 0, AutoNumType.ArabicPeriod);  // 1.
        AddAutoNumPara(body, level: 0, AutoNumType.ArabicPeriod);  // 2.
        AddAutoNumPara(body, level: 1, AutoNumType.ArabicPeriod);  // 1. (inner)
        AddAutoNumPara(body, level: 1, AutoNumType.ArabicPeriod);  // 2. (inner)
        AddAutoNumPara(body, level: 0, AutoNumType.ArabicPeriod);  // 3. (outer continues)

        var layout = ComposeText(body);

        layout.Paragraphs[0].BulletText.Should().Be("1.");
        layout.Paragraphs[1].BulletText.Should().Be("2.");
        layout.Paragraphs[2].BulletText.Should().Be("1.");
        layout.Paragraphs[3].BulletText.Should().Be("2.");
        layout.Paragraphs[4].BulletText.Should().Be("3.");
    }

    private static void AddAutoNumPara(TextBody body, int level, AutoNumType type)
    {
        var para = new Paragraph { Level = level, BulletKind = BulletKind.Auto, AutoNumType = type };
        para.Runs.Add(new Run { Text = "x", FontSizePt = 18 });
        body.Paragraphs.Add(para);
    }

    // ─── Char bullet ───────────────────────────────────────────────────────────

    [Fact]
    public void CharBullet_ResolvesBulletText()
    {
        var body = new TextBody();
        var para = new Paragraph { BulletKind = BulletKind.Char, BulletChar = "•" };
        para.Runs.Add(new Run { Text = "Bullet item", FontSizePt = 18 });
        body.Paragraphs.Add(para);

        var layout = ComposeText(body);

        layout.Paragraphs[0].BulletText.Should().Be("•");
    }

    [Fact]
    public void CharBullet_WithExplicitColor_UsesOverrideColor()
    {
        var body = new TextBody();
        var para = new Paragraph
        {
            BulletKind  = BulletKind.Char,
            BulletChar  = "–",
            BulletColor = new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00))
        };
        para.Runs.Add(new Run { Text = "Colored bullet", FontSizePt = 18 });
        body.Paragraphs.Add(para);

        var layout = ComposeText(body);

        layout.Paragraphs[0].BulletColor.Should().Be(new SrgbColor(0xFF, 0x00, 0x00));
    }

    // ─── Indent / hanging ─────────────────────────────────────────────────────

    [Fact]
    public void CharBullet_WithoutOverrides_UsesFirstNonEmptyRunTypographyAfterAutofit()
    {
        var body = new TextBody
        {
            AutoFit = true,
            FontScalePPT = 80000
        };
        var para = new Paragraph
        {
            BulletKind = BulletKind.Char,
            BulletChar = "\u2022",
            BulletSizePct = 75000
        };
        para.Runs.Add(new Run
        {
            Text = string.Empty,
            FontFamily = "Calibri",
            FontSizePt = 10,
            Color = new ThemeAwareColor(new SrgbColor(0x11, 0x11, 0x11))
        });
        para.Runs.Add(new Run
        {
            Text = "Styled bullet seed",
            FontFamily = "Aptos Display",
            FontSizePt = 24,
            Color = new ThemeAwareColor(new SrgbColor(0x12, 0x34, 0x56))
        });
        body.Paragraphs.Add(para);

        var layout = ComposeText(body);

        var resolved = layout.Paragraphs[0];
        resolved.BulletFontFamily.Should().Be("Aptos Display");
        resolved.BulletColor.Should().Be(new SrgbColor(0x12, 0x34, 0x56));
        resolved.BulletFontSizePt.Should().BeApproximately(24 * 0.8 * 0.75, 0.01);
        resolved.Runs[1].FontSizePt.Should().BeApproximately(24 * 0.8, 0.01);
    }

    [Fact]
    public void BulletParagraph_MarginLeft_PopulatesIndentDip()
    {
        const double EmuPerDip = 9525.0;
        long marLEmu = 457200L; // 48 DIP

        var body = new TextBody();
        var para = new Paragraph
        {
            BulletKind    = BulletKind.Char,
            BulletChar    = "•",
            MarginLeftEmu = marLEmu,
            IndentEmu     = -228600L  // -24 DIP (hanging)
        };
        para.Runs.Add(new Run { Text = "Indented bullet", FontSizePt = 18 });
        body.Paragraphs.Add(para);

        var layout = ComposeText(body);
        var rp = layout.Paragraphs[0];

        rp.IndentDip.Should().BeApproximately(marLEmu / EmuPerDip, 0.5);
        rp.HangingDip.Should().BeApproximately(228600.0 / EmuPerDip, 0.5);
    }

    // ─── Autofit font scaling ─────────────────────────────────────────────────

    [Fact]
    public void Autofit_FontScalePPT_62500_ScalesRunFontSizeTo62_5pct()
    {
        var body = new TextBody
        {
            AutoFit      = true,
            FontScalePPT = 62500  // 62.5%
        };
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Shrunk text", FontSizePt = 18.0 });
        body.Paragraphs.Add(para);

        var layout = ComposeText(body);

        // 18 * 0.625 = 11.25
        layout.Paragraphs[0].Runs[0].FontSizePt
            .Should().BeApproximately(18.0 * 0.625, 0.01);
    }

    [Fact]
    public void Autofit_LnSpcReductionPPT_20000_LayoutCarriesReduction()
    {
        var body = new TextBody
        {
            AutoFit          = true,
            FontScalePPT     = 100000,
            LnSpcReductionPPT = 20000  // 20%
        };
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Spacing reduced", FontSizePt = 18 });
        body.Paragraphs.Add(para);

        var layout = ComposeText(body);

        layout.LnSpcReduction.Should().BeApproximately(0.20, 0.001);
        layout.FontScale.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void Autofit_NoFontScale_DoesNotAlterFontSize()
    {
        var body = new TextBody { AutoFit = false };
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Normal", FontSizePt = 24.0 });
        body.Paragraphs.Add(para);

        var layout = ComposeText(body);

        layout.Paragraphs[0].Runs[0].FontSizePt.Should().BeApproximately(24.0, 0.01);
        layout.FontScale.Should().BeApproximately(1.0, 0.001);
    }

    // ─── Round-trip: bullets + marL/indent ────────────────────────────────────

    [Fact]
    public void RoundTrip_BulletsAndIndent_PreservedAcrossWriteRead()
    {
        // Build a TextBody with char bullet + marL/indent.
        var body = new TextBody();
        var para = new Paragraph
        {
            BulletKind    = BulletKind.Char,
            BulletChar    = "►",
            MarginLeftEmu = 457200L,
            IndentEmu     = -228600L,
        };
        para.Runs.Add(new Run { Text = "Bullet item", FontSizePt = 18 });
        body.Paragraphs.Add(para);

        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(MakeShapeWithText(body));

        // Write to stream and read back.
        using var ms = new System.IO.MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(p, ms);
        ms.Position = 0;
        var p2 = FreeP.Core.IO.PptxPackageReader.Read(ms);

        var roundTripped = p2.Slides[0].Shapes[0].TextBody!.Paragraphs[0];
        roundTripped.BulletKind.Should().Be(BulletKind.Char);
        roundTripped.BulletChar.Should().Be("►");
        roundTripped.MarginLeftEmu.Should().Be(457200L);
        roundTripped.IndentEmu.Should().Be(-228600L);
    }

    [Fact]
    public void RoundTrip_AutoNumType_PreservedAcrossWriteRead()
    {
        var body = new TextBody();
        var para = new Paragraph
        {
            BulletKind   = BulletKind.Auto,
            AutoNumType  = AutoNumType.RomanUcPeriod,
            AutoNumStartAt = 1,
        };
        para.Runs.Add(new Run { Text = "Roman I", FontSizePt = 18 });
        body.Paragraphs.Add(para);

        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(MakeShapeWithText(body));

        using var ms = new System.IO.MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(p, ms);
        ms.Position = 0;
        var p2 = FreeP.Core.IO.PptxPackageReader.Read(ms);

        var rt = p2.Slides[0].Shapes[0].TextBody!.Paragraphs[0];
        rt.BulletKind.Should().Be(BulletKind.Auto);
        rt.AutoNumType.Should().Be(AutoNumType.RomanUcPeriod);
    }

    [Fact]
    public void RoundTrip_NormAutofit_FontScaleAndLnSpcReduction_PreservedAcrossWriteRead()
    {
        var body = new TextBody
        {
            AutoFit           = true,
            FontScalePPT      = 62500,
            LnSpcReductionPPT = 20000
        };
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Autofit text", FontSizePt = 18 });
        body.Paragraphs.Add(para);

        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(MakeShapeWithText(body));

        using var ms = new System.IO.MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(p, ms);
        ms.Position = 0;
        var p2 = FreeP.Core.IO.PptxPackageReader.Read(ms);

        var rtBody = p2.Slides[0].Shapes[0].TextBody!;
        rtBody.AutoFit.Should().BeTrue();
        rtBody.FontScalePPT.Should().Be(62500);
        rtBody.LnSpcReductionPPT.Should().Be(20000);
    }

    // ─── BU1: explicit buNone suppresses inherited bullet ─────────────────────

    /// <summary>
    /// BU1: A paragraph with BulletSuppressed=true (explicit a:buNone) must NOT inherit
    /// the bullet from the master's OtherStyle even when the style has BulletKind.Char.
    /// </summary>
    [Fact]
    public void BU1_BulletSuppressed_True_DoesNotInheritStyleBullet()
    {
        var p = MakePresentation();

        // Seed the master's OtherStyle level 0 with a char bullet — simulates lstStyle inheritance.
        var master = p.Masters[0];
        master.TextStyles ??= new MasterTextStyles();
        master.TextStyles.OtherStyle[0] = new TextStyleLevel
        {
            BulletKind = BulletKind.Char,
            BulletChar = "•"
        };

        var body = new TextBody();
        // Paragraph with explicit buNone — BulletSuppressed prevents inheritance.
        var suppressed = new Paragraph { BulletSuppressed = true };
        suppressed.Runs.Add(new Run { Text = "No bullet", FontSizePt = 18 });
        body.Paragraphs.Add(suppressed);

        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(MakeShapeWithText(body));

        var layout = ComposeText(body, p);

        layout.Paragraphs[0].BulletKind.Should().Be(BulletKind.None,
            "explicit buNone (BulletSuppressed) must block style inheritance");
        layout.Paragraphs[0].BulletText.Should().BeEmpty(
            "no bullet text when suppressed");
    }

    /// <summary>
    /// BU1 regression: A paragraph with NO bullet element (BulletSuppressed=false, the default)
    /// MUST still inherit the style bullet — this is the existing behavior that must not regress.
    /// </summary>
    [Fact]
    public void BU1_BulletSuppressed_False_StillInheritsStyleBullet()
    {
        var p = MakePresentation();

        var master = p.Masters[0];
        master.TextStyles ??= new MasterTextStyles();
        master.TextStyles.OtherStyle[0] = new TextStyleLevel
        {
            BulletKind = BulletKind.Char,
            BulletChar = "▶"
        };

        var body = new TextBody();
        // Paragraph with NO bullet element set (BulletSuppressed defaults to false).
        var inheriting = new Paragraph(); // BulletSuppressed = false by default
        inheriting.Runs.Add(new Run { Text = "Should inherit bullet", FontSizePt = 18 });
        body.Paragraphs.Add(inheriting);

        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(MakeShapeWithText(body));

        var layout = ComposeText(body, p);

        layout.Paragraphs[0].BulletKind.Should().Be(BulletKind.Char,
            "absent bullet element (not suppressed) must inherit style bullet");
        layout.Paragraphs[0].BulletText.Should().Be("▶",
            "inherited bullet char must flow through");
    }

    // ─── BU3: unclamped para.Level does not crash autoNumCounters ─────────────

    /// <summary>
    /// BU3: A paragraph with lvl=9 (out-of-range) must be clamped to 8 by the reader
    /// so that autoNumCounters[level] never throws IndexOutOfRangeException.
    /// </summary>
    [Fact]
    public void BU3_AutoNum_LevelClamped_ToMax8_DoesNotThrow()
    {
        var body = new TextBody();
        // Manually construct a paragraph with Level=9 (as if read from a malformed PPTX).
        // The reader now clamps to 8, but we also test the compositor guard directly.
        var para = new Paragraph
        {
            Level      = 9,   // out of range — compositor must not IndexOutOfRange
            BulletKind = BulletKind.Auto,
            AutoNumType = AutoNumType.ArabicPeriod
        };
        para.Runs.Add(new Run { Text = "High level", FontSizePt = 14 });
        body.Paragraphs.Add(para);

        // Must not throw.
        var act = () => ComposeText(body);
        act.Should().NotThrow<IndexOutOfRangeException>(
            "para.Level=9 must be tolerated; reader clamps it and compositor guards the index");
    }

    // ─── Overloaded helper that accepts an explicit PresentationModel ──────────

    private static ResolvedTextLayout ComposeText(TextBody body, PresentationModel p)
    {
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(MakeShapeWithText(body));
        var ops = SlideCompositor.Compose(p, p.Slides[0]);
        var shapeOp = ops.OfType<DrawOp.Shape>().First();
        return shapeOp.Text!;
    }

    // ─── BU4: bullet theme/scheme color round-trip ───────────────────────────

    /// <summary>
    /// BU4: A paragraph bullet whose color is a DrawingML scheme color (e.g. accent1 with
    /// a lumMod modifier) must survive write→read with the scheme reference intact, NOT
    /// flattened to a plain sRGB value.  Verifies the writer calls BuildColorEl() rather
    /// than hard-coding a:srgbClr.
    /// </summary>
    [Fact]
    public void BU4_BulletSchemeColor_RoundTrip_PreservesSchemeRef()
    {
        // accent1 at 80% luminance — a realistic DrawingML theme color with a modifier.
        var schemeRef = new SchemeColorRef
        {
            Slot   = ThemeColorSlot.Accent1,
            LumMod = 0.8,
            LumOff = 0.0,
        };
        var themeColor = new ThemeAwareColor(new SrgbColor(0x44, 0x72, 0xC4), schemeRef);

        var body = new TextBody();
        var para = new Paragraph
        {
            BulletKind  = BulletKind.Char,
            BulletChar  = "•",
            BulletColor = themeColor,
        };
        para.Runs.Add(new Run { Text = "Theme-colored bullet", FontSizePt = 18 });
        body.Paragraphs.Add(para);

        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(MakeShapeWithText(body));

        using var ms = new System.IO.MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(p, ms);
        ms.Position = 0;
        var p2 = FreeP.Core.IO.PptxPackageReader.Read(ms);

        var rt = p2.Slides[0].Shapes[0].TextBody!.Paragraphs[0];
        rt.BulletColor.Should().NotBeNull("bullet color must survive round-trip");
        rt.BulletColor!.SchemeColor.Should().NotBeNull(
            "BU4: schemeClr theme reference must be preserved, not flattened to sRGB");
        rt.BulletColor.SchemeColor!.Slot.Should().Be(ThemeColorSlot.Accent1,
            "the accent1 slot must round-trip intact");
        rt.BulletColor.SchemeColor.LumMod.Should().BeApproximately(0.8, 1e-6,
            "the lumMod modifier (80%) must round-trip intact");
    }

    /// <summary>
    /// BU4 (no-regression): An explicit sRGB bullet color must still round-trip as sRGB
    /// (no SchemeColor), confirming BuildColorEl() falls through correctly when there is
    /// no scheme reference.
    /// </summary>
    [Fact]
    public void BU4_BulletSrgbColor_RoundTrip_StaysAsSrgb()
    {
        var explicitRed = new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00));

        var body = new TextBody();
        var para = new Paragraph
        {
            BulletKind  = BulletKind.Char,
            BulletChar  = "–",
            BulletColor = explicitRed,
        };
        para.Runs.Add(new Run { Text = "Red bullet", FontSizePt = 18 });
        body.Paragraphs.Add(para);

        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(MakeShapeWithText(body));

        using var ms = new System.IO.MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(p, ms);
        ms.Position = 0;
        var p2 = FreeP.Core.IO.PptxPackageReader.Read(ms);

        var rt = p2.Slides[0].Shapes[0].TextBody!.Paragraphs[0];
        rt.BulletColor.Should().NotBeNull("explicit sRGB bullet color must survive round-trip");
        rt.BulletColor!.SchemeColor.Should().BeNull(
            "a plain sRGB bullet color must NOT gain a scheme reference after round-trip");
        rt.BulletColor.Resolved.R.Should().Be(0xFF);
        rt.BulletColor.Resolved.G.Should().Be(0x00);
        rt.BulletColor.Resolved.B.Should().Be(0x00);
    }

    // ─── BU2: CT_TextParagraphProperties child order ──────────────────────────

    /// <summary>
    /// BU2: A paragraph with spcBef + buChar must emit a:spcBef BEFORE a:buChar in
    /// the a:pPr element.  CT_TextParagraphProperties schema order:
    ///   lnSpc → spcBef → spcAft → bullet group (buClr/buSz/buFont/buNone/buChar/buAutoNum)
    ///   → tabLst → defRPr.
    /// A reversed order is flagged as a schema error by OpenXmlValidator and can cause
    /// PowerPoint to drop the bullet or spacing when it repairs the file.
    /// </summary>
    [Fact]
    public void BU2_PprChildOrder_SpcBefAndSpcAft_BeforeBuChar_InWrittenXml()
    {
        var body = new TextBody();
        var para = new Paragraph
        {
            BulletKind    = BulletKind.Char,
            BulletChar    = "•",
            SpaceBeforePt = 6.0,
            SpaceAfterPt  = 3.0,
        };
        para.Runs.Add(new Run { Text = "Bullet with spacing", FontSizePt = 18 });
        body.Paragraphs.Add(para);

        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(MakeShapeWithText(body));

        using var ms = new System.IO.MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(p, ms);
        var bytes = ms.ToArray();

        // Inspect a:pPr child element order from the raw slide ZIP entry.
        using var zip = new System.IO.Compression.ZipArchive(
            new System.IO.MemoryStream(bytes), System.IO.Compression.ZipArchiveMode.Read);
        var slideEntry = zip.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        slideEntry.Should().NotBeNull("slide XML entry must exist in the PPTX");
        using var entryStream = slideEntry!.Open();
        var doc = System.Xml.Linq.XDocument.Load(entryStream);
        System.Xml.Linq.XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var pPr = doc.Descendants(a + "pPr").FirstOrDefault();
        pPr.Should().NotBeNull("a:pPr must be written when spacing or bullets are set");

        var childNames = pPr!.Elements().Select(e => e.Name.LocalName).ToList();
        var spcBefIdx = childNames.IndexOf("spcBef");
        var spcAftIdx = childNames.IndexOf("spcAft");
        var buCharIdx = childNames.IndexOf("buChar");

        spcBefIdx.Should().BeGreaterThanOrEqualTo(0, "a:spcBef must be present");
        spcAftIdx.Should().BeGreaterThanOrEqualTo(0, "a:spcAft must be present");
        buCharIdx.Should().BeGreaterThanOrEqualTo(0, "a:buChar must be present");
        spcBefIdx.Should().BeLessThan(buCharIdx,
            "BU2: a:spcBef must come BEFORE a:buChar per CT_TextParagraphProperties schema order");
        spcAftIdx.Should().BeLessThan(buCharIdx,
            "BU2: a:spcAft must come BEFORE a:buChar per CT_TextParagraphProperties schema order");
        spcBefIdx.Should().BeLessThan(spcAftIdx,
            "a:spcBef must come before a:spcAft");
    }
}
