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

    [Fact]
    public void AutoNum_ExplicitStartAt_ContinuesUntilAnotherExplicitRestart()
    {
        var body = new TextBody();
        body.Paragraphs.Add(AutoParagraph("first", 4, specified: true));
        body.Paragraphs.Add(AutoParagraph("second"));
        body.Paragraphs.Add(AutoParagraph("restart", 1, specified: true));
        body.Paragraphs.Add(AutoParagraph("after restart"));

        var layout = ComposeText(body);

        layout.Paragraphs.Select(paragraph => paragraph.BulletText)
            .Should().Equal("4.", "5.", "1.", "2.");
    }

    [Fact]
    public void AutoNum_NonListBoundary_RestartsAtFollowingExplicitStart()
    {
        var body = new TextBody();
        body.Paragraphs.Add(AutoParagraph("first", 4, specified: true));
        body.Paragraphs.Add(AutoParagraph("second"));
        body.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = "plain" } } });
        body.Paragraphs.Add(AutoParagraph("new list", 7, specified: true));
        body.Paragraphs.Add(AutoParagraph("continues"));

        var layout = ComposeText(body);

        layout.Paragraphs.Select(paragraph => paragraph.BulletText)
            .Should().Equal("4.", "5.", string.Empty, "7.", "8.");
    }

    [Fact]
    public void AutoNum_CharacterListBoundary_RestartsAtFollowingExplicitStart()
    {
        var body = new TextBody();
        body.Paragraphs.Add(AutoParagraph("first", 4, specified: true));
        body.Paragraphs.Add(new Paragraph
        {
            BulletKind = BulletKind.Char,
            BulletChar = "*",
            Runs = { new Run { Text = "bullet" } },
        });
        body.Paragraphs.Add(AutoParagraph("new list", 9, specified: true));

        var layout = ComposeText(body);

        layout.Paragraphs.Select(paragraph => paragraph.BulletText)
            .Should().Equal("4.", "*", "9.");
    }

    [Fact]
    public void AutoNum_SplitContinuation_ClearsExplicitStartOnDescendants()
    {
        var source = new TextBody();
        source.Paragraphs.Add(new Paragraph
        {
            BulletKind = BulletKind.Auto,
            AutoNumStartAt = 4,
            AutoNumStartAtSpecified = true,
            Runs = { new Run { Text = "AB" } },
        });
        var buffer = new InCanvasRichTextEditBuffer(source);

        buffer.ReplacePlainText("A\nB\nC");

        var edited = buffer.Body;
        edited.Paragraphs.Should().HaveCount(3);
        edited.Paragraphs[0].AutoNumStartAtSpecified.Should().BeTrue();
        edited.Paragraphs.Skip(1).Should().OnlyContain(paragraph =>
            !paragraph.AutoNumStartAtSpecified);
        ComposeText(edited).Paragraphs.Select(paragraph => paragraph.BulletText)
            .Should().Equal("4.", "5.", "6.");
    }

    [Fact]
    public void ListJoin_UsesLeadingMetadataAndMarkerVisibility()
    {
        var listFirst = new Paragraph
        {
            BulletKind = BulletKind.Auto,
            AutoNumStartAt = 4,
            AutoNumStartAtSpecified = true,
            Runs = { new Run { Text = "A" } },
        };
        var plainSecond = new Paragraph { Runs = { new Run { Text = "B" } } };
        var source = new TextBody();
        source.Paragraphs.Add(listFirst);
        source.Paragraphs.Add(plainSecond);
        var buffer = new InCanvasRichTextEditBuffer(source);

        buffer.ReplacePlainText("AB");

        var edited = buffer.Body;
        edited.Paragraphs.Should().ContainSingle();
        edited.Paragraphs[0].BulletKind.Should().Be(BulletKind.Auto);
        ComposeText(edited).Paragraphs[0].BulletText.Should().Be("4.");

        var plainFirst = new Paragraph { Runs = { new Run { Text = "A" } } };
        var listSecond = AutoParagraph("B", 7, specified: true);
        var reverseSource = new TextBody();
        reverseSource.Paragraphs.Add(plainFirst);
        reverseSource.Paragraphs.Add(listSecond);
        var reverseBuffer = new InCanvasRichTextEditBuffer(reverseSource);

        reverseBuffer.ReplacePlainText("AB");

        var reverseEdited = reverseBuffer.Body;
        reverseEdited.Paragraphs.Should().ContainSingle();
        reverseEdited.Paragraphs[0].BulletKind.Should().Be(BulletKind.None);
        ComposeText(reverseEdited).Paragraphs[0].BulletText.Should().BeEmpty();
    }

    private static Paragraph AutoParagraph(
        string text,
        int startAt = 1,
        bool specified = false) =>
        new()
        {
            BulletKind = BulletKind.Auto,
            AutoNumStartAt = startAt,
            AutoNumStartAtSpecified = specified,
            Runs = { new Run { Text = text, FontSizePt = 18 } },
        };

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
    public void CharBullet_WithParagraphFollowTextSources_OverridesInheritedBulletTypography()
    {
        var p = MakePresentation();
        var textStyles = p.Masters[0].TextStyles ??= new MasterTextStyles();
        textStyles.OtherStyle[0] = new TextStyleLevel
        {
            BulletKind = BulletKind.Char,
            BulletChar = "\u2022",
            BulletFontFamily = "Wingdings",
            BulletColor = new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)),
            BulletSizePct = 75000
        };

        var body = new TextBody();
        var para = new Paragraph
        {
            BulletColorFollowsText = true,
            BulletSizeFollowsText = true,
            BulletFontFollowsText = true
        };
        para.Runs.Add(new Run
        {
            Text = string.Empty,
            FontFamily = "Calibri",
            FontSizePt = 9,
            Color = new ThemeAwareColor(new SrgbColor(0x11, 0x11, 0x11))
        });
        para.Runs.Add(new Run
        {
            Text = "Follow text bullet",
            FontFamily = "Aptos Display",
            FontSizePt = 22,
            Color = new ThemeAwareColor(new SrgbColor(0x12, 0x34, 0x56))
        });
        body.Paragraphs.Add(para);

        var layout = ComposeText(body, p);

        var resolved = layout.Paragraphs[0];
        resolved.BulletText.Should().Be("\u2022");
        resolved.BulletFontFamily.Should().Be("Aptos Display");
        resolved.BulletColor.Should().Be(new SrgbColor(0x12, 0x34, 0x56));
        resolved.BulletFontSizePt.Should().BeApproximately(22.0, 0.01);
    }

    [Fact]
    public void CharBullet_WithAbsolutePointSize_UsesExactSizeBeforeStoredAutofitScale()
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
            BulletSizePt = 10.0
        };
        para.Runs.Add(new Run { Text = "Absolute bullet", FontSizePt = 24 });
        body.Paragraphs.Add(para);

        var layout = ComposeText(body);

        layout.Paragraphs[0].BulletFontSizePt.Should().BeApproximately(8.0, 0.01);
        layout.Paragraphs[0].Runs[0].FontSizePt.Should().BeApproximately(24 * 0.8, 0.01);
    }

    [Fact]
    public void LevelTwoStyle_BulletAbsoluteAndFollowTextFields_ApplyToLevelOneParagraph()
    {
        var p = MakePresentation();
        var textStyles = p.Masters[0].TextStyles ??= new MasterTextStyles();
        textStyles.OtherStyle[1] = new TextStyleLevel
        {
            BulletKind = BulletKind.Char,
            BulletChar = "\u25B8",
            BulletSizePt = 11.0,
            BulletColorFollowsText = true,
            BulletFontFollowsText = true
        };

        var body = new TextBody();
        var para = new Paragraph { Level = 1 };
        para.Runs.Add(new Run
        {
            Text = "Level two",
            FontFamily = "Aptos",
            FontSizePt = 20,
            Color = new ThemeAwareColor(new SrgbColor(0x44, 0x55, 0x66))
        });
        body.Paragraphs.Add(para);

        var layout = ComposeText(body, p);

        var resolved = layout.Paragraphs[0];
        resolved.BulletText.Should().Be("\u25B8");
        resolved.BulletFontSizePt.Should().BeApproximately(11.0, 0.01);
        resolved.BulletFontFamily.Should().Be("Aptos");
        resolved.BulletColor.Should().Be(new SrgbColor(0x44, 0x55, 0x66));
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
        layout.AutoFit.Should().BeTrue();
        layout.HasStoredFontScale.Should().BeTrue();
        layout.FontScale.Should().BeApproximately(0.625, 0.001);
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
        layout.AutoFit.Should().BeFalse();
        layout.HasStoredFontScale.Should().BeFalse();
    }

    [Fact]
    public void Autofit_WithoutCachedFontScale_CarriesRuntimePlannerMetadataWithoutPreScaling()
    {
        var body = new TextBody { AutoFit = true };
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Runtime planner candidate", FontSizePt = 24.0 });
        body.Paragraphs.Add(para);

        var layout = ComposeText(body);

        layout.Paragraphs[0].Runs[0].FontSizePt.Should().BeApproximately(24.0, 0.01);
        layout.AutoFit.Should().BeTrue();
        layout.HasStoredFontScale.Should().BeFalse();
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
    public void RoundTrip_BulletTypographySources_PreservedAcrossWriteRead()
    {
        var body = new TextBody
        {
            LstStyle = new TextStyleLevels()
        };
        body.LstStyle[1] = new TextStyleLevel
        {
            BulletKind = BulletKind.Char,
            BulletChar = "\u25B8",
            BulletColorFollowsText = true,
            BulletSizePt = 13.5,
            BulletFontFollowsText = true
        };

        var para = new Paragraph
        {
            BulletKind = BulletKind.Char,
            BulletChar = "\u2022",
            BulletColorFollowsText = true,
            BulletSizeFollowsText = true,
            BulletFontFollowsText = true
        };
        para.Runs.Add(new Run { Text = "Bullet typography sources", FontSizePt = 18 });
        body.Paragraphs.Add(para);

        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(MakeShapeWithText(body));

        using var ms = new System.IO.MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(p, ms);
        ms.Position = 0;
        var p2 = FreeP.Core.IO.PptxPackageReader.Read(ms);

        var rtBody = p2.Slides[0].Shapes[0].TextBody!;
        var rtPara = rtBody.Paragraphs[0];
        rtPara.BulletColorFollowsText.Should().BeTrue();
        rtPara.BulletSizeFollowsText.Should().BeTrue();
        rtPara.BulletFontFollowsText.Should().BeTrue();
        rtPara.BulletColor.Should().BeNull();
        rtPara.BulletSizePct.Should().BeNull();
        rtPara.BulletSizePt.Should().BeNull();
        rtPara.BulletFontFamily.Should().BeNull();

        var rtLevel = rtBody.LstStyle![1]!;
        rtLevel.BulletKind.Should().Be(BulletKind.Char);
        rtLevel.BulletChar.Should().Be("\u25B8");
        rtLevel.BulletColorFollowsText.Should().BeTrue();
        rtLevel.BulletSizePt.Should().BeApproximately(13.5, 0.001);
        rtLevel.BulletFontFollowsText.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_ParagraphBulletSizePoints_PreservedAcrossWriteRead()
    {
        var body = new TextBody();
        var para = new Paragraph
        {
            BulletKind = BulletKind.Char,
            BulletChar = "\u2022",
            BulletSizePt = 12.25
        };
        para.Runs.Add(new Run { Text = "Absolute bullet size", FontSizePt = 18 });
        body.Paragraphs.Add(para);

        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(MakeShapeWithText(body));

        using var ms = new System.IO.MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(p, ms);
        ms.Position = 0;
        var p2 = FreeP.Core.IO.PptxPackageReader.Read(ms);

        var rt = p2.Slides[0].Shapes[0].TextBody!.Paragraphs[0];
        rt.BulletSizePt.Should().BeApproximately(12.25, 0.001);
        rt.BulletSizePct.Should().BeNull();
        rt.BulletSizeFollowsText.Should().BeFalse();
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
            AutoNumStartAtSpecified = true,
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
        rt.AutoNumStartAt.Should().Be(1);
        rt.AutoNumStartAtSpecified.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_AutoNumNonDefaultStartAt_PreservesExplicitPresence()
    {
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph
        {
            BulletKind = BulletKind.Auto,
            AutoNumType = AutoNumType.ArabicPeriod,
            AutoNumStartAt = 4,
            AutoNumStartAtSpecified = true,
            Runs = { new Run { Text = "Restart at four", FontSizePt = 18 } },
        });

        var presentation = MakePresentation();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(MakeShapeWithText(body));

        using var stream = new System.IO.MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;
        var roundTripped = FreeP.Core.IO.PptxPackageReader.Read(stream);
        var paragraph = roundTripped.Slides[0].Shapes[0].TextBody!.Paragraphs[0];

        paragraph.AutoNumStartAt.Should().Be(4);
        paragraph.AutoNumStartAtSpecified.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_AutoNumLevelTextTemplate_PreservesMultiLevelMarker()
    {
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph
        {
            BulletKind = BulletKind.Auto,
            AutoNumType = AutoNumType.ArabicPeriod,
            AutoNumStartAtSpecified = true,
            AutoNumTextTemplate = "%1.",
            Runs = { new Run { Text = "Root", FontSizePt = 18 } },
        });
        body.Paragraphs.Add(new Paragraph
        {
            Level = 1,
            BulletKind = BulletKind.Auto,
            AutoNumType = AutoNumType.ArabicPeriod,
            AutoNumTextTemplate = "%1.%2.",
            Runs = { new Run { Text = "Child", FontSizePt = 18 } },
        });

        var presentation = MakePresentation();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(MakeShapeWithText(body));

        using var stream = new System.IO.MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;
        var roundTripped = FreeP.Core.IO.PptxPackageReader.Read(stream);

        roundTripped.Slides[0].Shapes[0].TextBody!.Paragraphs
            .Select(paragraph => paragraph.AutoNumTextTemplate)
            .Should().Equal("%1.", "%1.%2.");
    }

    [Fact]
    public void RoundTrip_LegacyProgrammaticNonDefaultStartAt_RemainsSerialized()
    {
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph
        {
            BulletKind = BulletKind.Auto,
            AutoNumStartAt = 4,
            // Older callers only set the value; the writer must keep emitting it.
            AutoNumStartAtSpecified = false,
            Runs = { new Run { Text = "Legacy start", FontSizePt = 18 } },
        });

        var presentation = MakePresentation();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(MakeShapeWithText(body));

        using var stream = new System.IO.MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;
        var roundTripped = FreeP.Core.IO.PptxPackageReader.Read(stream);
        var paragraph = roundTripped.Slides[0].Shapes[0].TextBody!.Paragraphs[0];

        paragraph.AutoNumStartAt.Should().Be(4);
        paragraph.AutoNumStartAtSpecified.Should().BeTrue();
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
        rtBody.AutoFitKind.Should().Be(TextAutoFitKind.Normal);
        rtBody.FontScalePPT.Should().Be(62500);
        rtBody.LnSpcReductionPPT.Should().Be(20000);
    }

    // ─── LA1: normAutofit vs spAutoFit vs noAutofit must not conflate ─────────

    /// <summary>
    /// LA1: a shape with <c>a:spAutoFit</c> (grow shape to fit text) must round-trip as
    /// <c>a:spAutoFit</c> — NOT be silently rewritten as <c>a:normAutofit</c> (shrink text)
    /// on save. Before the fix, the single AutoFit bool collapsed both modes and the writer
    /// always re-emitted normAutofit for AutoFit=true, permanently changing shape behavior.
    /// </summary>
    [Fact]
    public void LA1_SpAutoFit_RoundTrips_AsSpAutoFit_NotNormAutofit()
    {
        var body = new TextBody { AutoFitKind = TextAutoFitKind.Shape };
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Grows the shape", FontSizePt = 18 });
        body.Paragraphs.Add(para);

        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(MakeShapeWithText(body));

        using var ms = new System.IO.MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(p, ms);
        var bytes = ms.ToArray();

        // Inspect the raw written XML: must contain a:spAutoFit, must NOT contain a:normAutofit.
        using var zip = new System.IO.Compression.ZipArchive(
            new System.IO.MemoryStream(bytes), System.IO.Compression.ZipArchiveMode.Read);
        var slideEntry = zip.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        slideEntry.Should().NotBeNull("slide XML entry must exist in the PPTX");
        using var entryStream = slideEntry!.Open();
        var doc = System.Xml.Linq.XDocument.Load(entryStream);
        System.Xml.Linq.XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var bodyPr = doc.Descendants(a + "bodyPr").FirstOrDefault();
        bodyPr.Should().NotBeNull();

        bodyPr!.Element(a + "spAutoFit").Should().NotBeNull(
            "LA1: an spAutoFit shape must be written back out as a:spAutoFit");
        bodyPr.Element(a + "normAutofit").Should().BeNull(
            "LA1: an spAutoFit shape must NEVER be rewritten as a:normAutofit on save");

        // Re-read: must still be Shape.
        ms.Position = 0;
        var p2 = FreeP.Core.IO.PptxPackageReader.Read(ms);
        var rtBody = p2.Slides[0].Shapes[0].TextBody!;
        rtBody.AutoFitKind.Should().Be(TextAutoFitKind.Shape);
        rtBody.AutoFit.Should().BeFalse("AutoFit back-compat bool only reflects Normal, not Shape");
    }

    /// <summary>LA1: a:normAutofit still round-trips as a:normAutofit (Normal), not Shape.</summary>
    [Fact]
    public void LA1_NormAutofit_RoundTrips_AsNormAutofit_Kind()
    {
        var body = new TextBody { AutoFitKind = TextAutoFitKind.Normal, FontScalePPT = 80000 };
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "Shrinks the text", FontSizePt = 18 });
        body.Paragraphs.Add(para);

        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(MakeShapeWithText(body));

        using var ms = new System.IO.MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(p, ms);
        var bytes = ms.ToArray();

        using var zip = new System.IO.Compression.ZipArchive(
            new System.IO.MemoryStream(bytes), System.IO.Compression.ZipArchiveMode.Read);
        var slideEntry = zip.Entries.First(e =>
            e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        using var entryStream = slideEntry.Open();
        var doc = System.Xml.Linq.XDocument.Load(entryStream);
        System.Xml.Linq.XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var bodyPr = doc.Descendants(a + "bodyPr").First();

        bodyPr.Element(a + "normAutofit").Should().NotBeNull();
        bodyPr.Element(a + "spAutoFit").Should().BeNull();

        ms.Position = 0;
        var p2 = FreeP.Core.IO.PptxPackageReader.Read(ms);
        var rtBody = p2.Slides[0].Shapes[0].TextBody!;
        rtBody.AutoFitKind.Should().Be(TextAutoFitKind.Normal);
        rtBody.AutoFit.Should().BeTrue();
    }

    /// <summary>LA1: no autofit element present (or explicit a:noAutofit) reads back as None.</summary>
    [Fact]
    public void LA1_NoAutofit_RoundTrips_AsNoneKind()
    {
        var body = new TextBody { AutoFitKind = TextAutoFitKind.None };
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "No autofit", FontSizePt = 18 });
        body.Paragraphs.Add(para);

        var p = MakePresentation();
        p.Slides[0].Shapes.Clear();
        p.Slides[0].Shapes.Add(MakeShapeWithText(body));

        using var ms = new System.IO.MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(p, ms);
        ms.Position = 0;
        var p2 = FreeP.Core.IO.PptxPackageReader.Read(ms);

        var rtBody = p2.Slides[0].Shapes[0].TextBody!;
        rtBody.AutoFitKind.Should().Be(TextAutoFitKind.None);
        rtBody.AutoFit.Should().BeFalse();
    }

    [Fact]
    public void TextAutoFit_AllThreeModes_RoundTripThroughPptx()
    {
        foreach (var mode in new[] { TextAutoFitKind.None, TextAutoFitKind.Normal, TextAutoFitKind.Shape })
        {
            var body = new TextBody { AutoFitKind = mode };
            body.Paragraphs.Add(new Paragraph { Runs = { new Run { Text = "Autofit mode" } } });
            var presentation = MakePresentation();
            presentation.Slides[0].Shapes.Clear();
            presentation.Slides[0].Shapes.Add(MakeShapeWithText(body));

            using var stream = new System.IO.MemoryStream();
            FreeP.Core.IO.PptxPackageWriter.Write(presentation, stream);
            stream.Position = 0;
            var reopened = FreeP.Core.IO.PptxPackageReader.Read(stream);

            reopened.Slides[0].Shapes[0].TextBody!.AutoFitKind.Should().Be(mode);
        }
    }

    [Fact]
    public void PptxImport_BuBlip_ResolvesImageBulletIntoSharedRenderPlan()
    {
        var imageBytes = Minimal1x1Png();
        using var package = CreatePictureBulletPptx(imageBytes);

        var presentation = FreeP.Core.IO.PptxPackageReader.Read(package);

        var paragraph = presentation.Slides[0].Shapes[0].TextBody!.Paragraphs[0];
        paragraph.BulletKind.Should().Be(BulletKind.Image);
        paragraph.BulletImage.Should().NotBeNull();
        paragraph.BulletImage!.Bytes.Should().Equal(imageBytes);
        paragraph.BulletImage.ContentType.Should().Be("image/png");

        var ops = SlideCompositor.Compose(presentation, presentation.Slides[0]);
        var layout = ops.OfType<DrawOp.Shape>().Single().Text!;
        layout.Paragraphs[0].BulletKind.Should().Be(BulletKind.Image);
        layout.Paragraphs[0].BulletImage.Should().BeSameAs(paragraph.BulletImage);

        var plan = TextLayoutPlanner.PlanBodyText(
            layout,
            new LayoutRect(0, 0, 300, 160),
            new[] { new TextParagraphMeasure(0, 24, 0, 0) });
        plan.Paragraphs.Single().Bullet!.Value.Image.Should().BeSameAs(paragraph.BulletImage);
    }

    [Fact]
    public void RoundTrip_ImageBullet_WritesBuBlipMediaRelationshipAndReadsBack()
    {
        var imageBytes = Minimal1x1Png();
        var body = new TextBody();
        var para = new Paragraph
        {
            BulletKind = BulletKind.Image,
            BulletImage = new ImagePart
            {
                Bytes = imageBytes,
                ContentType = "image/png"
            },
            MarginLeftEmu = 342900,
            IndentEmu = -171450,
        };
        para.Runs.Add(new Run { Text = "Picture bullet", FontSizePt = 18 });
        body.Paragraphs.Add(para);

        var presentation = MakePresentation();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(MakeShapeWithText(body));

        using var ms = new System.IO.MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(presentation, ms);
        var bytes = ms.ToArray();

        using (var zip = new System.IO.Compression.ZipArchive(
            new System.IO.MemoryStream(bytes),
            System.IO.Compression.ZipArchiveMode.Read))
        {
            var slideXml = ReadZipText(zip, "ppt/slides/slide1.xml");
            var slideRels = ReadZipText(zip, "ppt/slides/_rels/slide1.xml.rels");
            slideXml.Should().Contain("<a:buBlip>");
            slideXml.Should().Contain("r:embed=\"rIdBulletImg1\"");
            slideRels.Should().Contain("Id=\"rIdBulletImg1\"");
            slideRels.Should().Contain("Target=\"../media/slide1_bullet1.png\"");
            zip.GetEntry("ppt/media/slide1_bullet1.png").Should().NotBeNull();
        }

        using var readStream = new System.IO.MemoryStream(bytes);
        var roundTripped = FreeP.Core.IO.PptxPackageReader.Read(readStream);
        var paragraph = roundTripped.Slides[0].Shapes[0].TextBody!.Paragraphs[0];
        paragraph.BulletKind.Should().Be(BulletKind.Image);
        paragraph.BulletImage.Should().NotBeNull();
        paragraph.BulletImage!.ContentType.Should().Be("image/png");
        paragraph.BulletImage.Bytes.Should().Equal(imageBytes);
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
            BulletColorFollowsText = true,
            BulletSizeFollowsText = true,
            BulletFontFollowsText = true,
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
        var buClrTxIdx = childNames.IndexOf("buClrTx");
        var buSzTxIdx = childNames.IndexOf("buSzTx");
        var buFontTxIdx = childNames.IndexOf("buFontTx");
        var buCharIdx = childNames.IndexOf("buChar");

        spcBefIdx.Should().BeGreaterThanOrEqualTo(0, "a:spcBef must be present");
        spcAftIdx.Should().BeGreaterThanOrEqualTo(0, "a:spcAft must be present");
        buClrTxIdx.Should().BeGreaterThanOrEqualTo(0, "a:buClrTx must be present");
        buSzTxIdx.Should().BeGreaterThanOrEqualTo(0, "a:buSzTx must be present");
        buFontTxIdx.Should().BeGreaterThanOrEqualTo(0, "a:buFontTx must be present");
        buCharIdx.Should().BeGreaterThanOrEqualTo(0, "a:buChar must be present");
        spcBefIdx.Should().BeLessThan(buClrTxIdx,
            "BU2: a:spcBef must come BEFORE bullet source elements per CT_TextParagraphProperties schema order");
        spcAftIdx.Should().BeLessThan(buClrTxIdx,
            "BU2: a:spcAft must come BEFORE bullet source elements per CT_TextParagraphProperties schema order");
        buClrTxIdx.Should().BeLessThan(buSzTxIdx, "bullet color source must precede bullet size source");
        buSzTxIdx.Should().BeLessThan(buFontTxIdx, "bullet size source must precede bullet font source");
        buFontTxIdx.Should().BeLessThan(buCharIdx, "bullet font source must precede bullet character");
        spcBefIdx.Should().BeLessThan(spcAftIdx,
            "a:spcBef must come before a:spcAft");
    }

    private static System.IO.MemoryStream CreatePictureBulletPptx(byte[] imageBytes)
    {
        var stream = new System.IO.MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(
                   stream,
                   System.IO.Compression.ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            WriteZipEntry(zip, "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="png" ContentType="image/png"/>
                  <Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/>
                  <Override PartName="/ppt/slides/slide1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>
                </Types>
                """);
            WriteZipEntry(zip, "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="ppt/presentation.xml"/>
                </Relationships>
                """);
            WriteZipEntry(zip, "ppt/_rels/presentation.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide1.xml"/>
                </Relationships>
                """);
            WriteZipEntry(zip, "ppt/presentation.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <p:presentation xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <p:sldSz cx="9144000" cy="5143500"/>
                  <p:sldIdLst>
                    <p:sldId id="256" r:id="rId1"/>
                  </p:sldIdLst>
                </p:presentation>
                """);
            WriteZipEntry(zip, "ppt/slides/_rels/slide1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdImage1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="../media/image1.png"/>
                </Relationships>
                """);
            WriteZipEntry(zip, "ppt/slides/slide1.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <p:cSld>
                    <p:spTree>
                      <p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>
                      <p:grpSpPr/>
                      <p:sp>
                        <p:nvSpPr><p:cNvPr id="2" name="Picture bullet text"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr>
                        <p:spPr>
                          <a:xfrm><a:off x="0" y="0"/><a:ext cx="3000000" cy="1000000"/></a:xfrm>
                          <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                        </p:spPr>
                        <p:txBody>
                          <a:bodyPr/>
                          <a:lstStyle/>
                          <a:p>
                            <a:pPr marL="342900" indent="-171450"><a:buBlip><a:blip r:embed="rIdImage1"/></a:buBlip></a:pPr>
                            <a:r><a:rPr sz="1800"/><a:t>Imported image bullet</a:t></a:r>
                          </a:p>
                        </p:txBody>
                      </p:sp>
                    </p:spTree>
                  </p:cSld>
                </p:sld>
                """);

            var imageEntry = zip.CreateEntry("ppt/media/image1.png");
            using var imageStream = imageEntry.Open();
            imageStream.Write(imageBytes, 0, imageBytes.Length);
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteZipEntry(
        System.IO.Compression.ZipArchive zip,
        string path,
        string text)
    {
        var entry = zip.CreateEntry(path);
        using var writer = new System.IO.StreamWriter(entry.Open(), System.Text.Encoding.UTF8);
        writer.Write(text);
    }

    private static string ReadZipText(System.IO.Compression.ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path) ?? throw new InvalidOperationException($"Missing ZIP entry: {path}");
        using var reader = new System.IO.StreamReader(entry.Open(), System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] Minimal1x1Png() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];
}
