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
}
