using System.Text;
using FreeP.App.Compositor;
using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// The non-compositor consumers of paragraph spacing must resolve
/// Paragraph.SpaceBeforePercent/SpaceAfterPercent too — the .pptx writer's cached-autofit
/// estimate, the RTF clipboard export, the in-canvas editing overlay and the slide-show
/// hyperlink hit test each read the points fields only, so percent-authored spacing silently
/// counted as zero in all four.
/// </summary>
public sealed class ParagraphPercentSpacingExportTests
{
    private const double PtToDip = 96.0 / 72.0;
    private const long EmuPerPoint = 12_700;

    private static double LineHeightPt(double fontSizePt) =>
        fontSizePt * ParagraphSpacingMetrics.LineHeightFactor;

    private static Paragraph MakeParagraph(string text, double fontSizePt, Action<Paragraph> configure)
    {
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = text, FontSizePt = fontSizePt });
        configure(para);
        return para;
    }

    // ─── .pptx writer: cached normAutofit scale estimate ──────────────────────

    /// <summary>
    /// Four 18pt single-line paragraphs measure ~86pt of text, which fits a ~120pt box. Adding a
    /// full line of percent space-before to each takes the block to ~173pt, so the writer must
    /// cache a shrink — reading only SpaceBeforePt left the file claiming the text fits.
    /// </summary>
    private static TextBody MakeAutoFitBody(Action<Paragraph> configure)
    {
        var body = new TextBody { Wrap = true };
        for (int index = 0; index < 4; index++)
            body.Paragraphs.Add(MakeParagraph("Line", 18, configure));
        return body;
    }

    private const long AutoFitExtentCxEmu = (long)((200 + 14.4) * EmuPerPoint);
    private const long AutoFitExtentCyEmu = (long)((120 + 7.2) * EmuPerPoint);

    [Fact]
    public void RecomputeNormalAutoFitScale_WithoutSpacing_LeavesCachedScaleAlone()
    {
        var (fontScalePpt, _) = PptxPackageWriter.RecomputeNormalAutoFitScale(
            MakeAutoFitBody(_ => { }), AutoFitExtentCxEmu, AutoFitExtentCyEmu);

        fontScalePpt.Should().BeNull(
            "the authored text alone fits the box, so there is no evidence of overflow");
    }

    [Fact]
    public void RecomputeNormalAutoFitScale_PercentSpacing_CountsTowardOverflow()
    {
        var (fontScalePpt, _) = PptxPackageWriter.RecomputeNormalAutoFitScale(
            MakeAutoFitBody(para =>
            {
                para.SpaceBeforePercent = 100;
            }),
            AutoFitExtentCxEmu,
            AutoFitExtentCyEmu);

        fontScalePpt.Should().NotBeNull(
            "percent space-before overflows the box and must drive the cached autofit scale");
        fontScalePpt!.Value.Should().BeLessThan(100_000);
    }

    [Fact]
    public void RecomputeNormalAutoFitScale_PointsAndPercentSpacing_AgreeWhenEquivalent()
    {
        var percent = PptxPackageWriter.RecomputeNormalAutoFitScale(
            MakeAutoFitBody(para => para.SpaceBeforePercent = 100),
            AutoFitExtentCxEmu,
            AutoFitExtentCyEmu);
        var points = PptxPackageWriter.RecomputeNormalAutoFitScale(
            MakeAutoFitBody(para => para.SpaceBeforePt = LineHeightPt(18)),
            AutoFitExtentCxEmu,
            AutoFitExtentCyEmu);

        percent.FontScalePpt.Should().Be(points.FontScalePpt,
            "100% of one line and its equivalent in points must estimate identically");
    }

    [Fact]
    public void RecomputeNormalAutoFitScale_LineSpacingPercent_CountsTowardOverflow()
    {
        var (fontScalePpt, _) = PptxPackageWriter.RecomputeNormalAutoFitScale(
            MakeAutoFitBody(para => para.LineSpacingPercent = 200),
            AutoFitExtentCxEmu,
            AutoFitExtentCyEmu);

        fontScalePpt.Should().NotBeNull(
            "double line spacing doubles the text height and must drive the cached autofit scale");
        fontScalePpt!.Value.Should().BeLessThan(100_000);
    }

    // ─── RTF clipboard export ─────────────────────────────────────────────────

    [Fact]
    public void SerializeRtf_PercentSpacing_ResolvesToPointsBeforeExport()
    {
        const double fontSizePt = 20;
        var body = new TextBody();
        body.Paragraphs.Add(MakeParagraph("Percent spaced", fontSizePt, para =>
        {
            para.SpaceBeforePercent = 100;
            para.SpaceAfterPercent = 50;
        }));

        var rtf = ExternalRichTextClipboardPlanner.SerializeRtf(
            new InCanvasRichClipboardPayload(body, InCanvasTextEditPlanner.ExtractPlainText(body)));

        // RTF has no percentage spacing, so it must carry the resolved points as twips.
        int expectedBeforeTwips = (int)Math.Round(1.00 * LineHeightPt(fontSizePt) * 20);
        int expectedAfterTwips = (int)Math.Round(0.50 * LineHeightPt(fontSizePt) * 20);
        var source = Encoding.ASCII.GetString(rtf);
        source.Should().Contain($"\\sb{expectedBeforeTwips}");
        source.Should().Contain($"\\sa{expectedAfterTwips}");

        var restored = ExternalRichTextClipboardPlanner.TryParseRtf(rtf);
        restored.Should().NotBeNull();
        restored!.Body.Paragraphs[0].SpaceBeforePt.Should().BeApproximately(
            LineHeightPt(fontSizePt), 0.05,
            "percent space-before must survive an RTF export as its resolved point value");
        restored.Body.Paragraphs[0].SpaceAfterPt.Should().BeApproximately(
            0.50 * LineHeightPt(fontSizePt), 0.05);
    }

    // ─── XAML clipboard export ────────────────────────────────────────────────

    [Fact]
    public void SerializeXamlPackage_RoundTripsParagraphSpacingAndMarginInDips()
    {
        const double fontSizePt = 20;
        const long marginLeftEmu = 457_200; // 0.5", i.e. 48 DIP
        var body = new TextBody();
        body.Paragraphs.Add(MakeParagraph("Percent spaced", fontSizePt, para =>
        {
            para.MarginLeftEmu = marginLeftEmu;
            para.SpaceBeforePercent = 100;
            para.SpaceAfterPt = 6;
        }));

        var package = ExternalXamlClipboardPlanner.SerializeXamlPackage(
            new InCanvasRichClipboardPayload(body, InCanvasTextEditPlanner.ExtractPlainText(body)));
        var restored = ExternalXamlClipboardPlanner.TryParseXamlPackage(package);

        restored.Should().NotBeNull();
        var paragraph = restored!.Body.Paragraphs[0];
        paragraph.MarginLeftEmu.Should().Be(marginLeftEmu,
            "Margin is authored in XAML device-independent pixels, not EMU");
        paragraph.SpaceBeforePt.Should().BeApproximately(LineHeightPt(fontSizePt), 0.05,
            "percent space-before must survive a XAML export as its resolved point value");
        paragraph.SpaceAfterPt.Should().BeApproximately(6, 0.05,
            "paragraph spacing was never written to XAML at all before");
    }

    // ─── In-canvas editing overlay ────────────────────────────────────────────

    [Fact]
    public void InCanvasRichTextVisualPlanner_PercentSpacing_MatchesRenderedSpacing()
    {
        const double fontSizePt = 20;
        var body = new TextBody();
        body.Paragraphs.Add(MakeParagraph("Percent spaced", fontSizePt, para =>
        {
            para.SpaceBeforePercent = 150;
            para.SpaceAfterPercent = 50;
        }));

        var plan = InCanvasRichTextVisualPlanner.Create(body);

        plan.Paragraphs[0].SpaceBeforeDip.Should().BeApproximately(
            1.50 * LineHeightPt(fontSizePt) * PtToDip, 1e-9,
            "the editing overlay must place text where the renderer draws it");
        plan.Paragraphs[0].SpaceAfterDip.Should().BeApproximately(
            0.50 * LineHeightPt(fontSizePt) * PtToDip, 1e-9);
    }

    // ─── Slide-show hyperlink hit testing ─────────────────────────────────────

    [Fact]
    public void HitTest_PercentSpaceBefore_MovesHyperlinkHotspotDown()
    {
        const double fontSizePt = 20;
        // One 20pt line is 32 DIP tall, so a full line of space-before shifts the second
        // paragraph's hotspot from 32..64 DIP down to 64..96 DIP below the text-area top.
        const double lineHeightDip = 32;

        static SlideShape MakeShape(double? spaceBeforePercent)
        {
            var link = new Hyperlink { Url = "https://example.com/" };
            var body = new TextBody { Wrap = true, Anchor = VerticalAnchor.Top };
            body.Paragraphs.Add(MakeParagraph("First", fontSizePt, _ => { }));

            var linked = new Paragraph { SpaceBeforePercent = spaceBeforePercent };
            linked.Runs.Add(new Run { Text = "Link", FontSizePt = fontSizePt, Hyperlink = link });
            body.Paragraphs.Add(linked);

            return new SlideShape
            {
                Id = 1,
                Kind = SlideShapeKind.AutoShape,
                AutoShapeKind = DrawingShapeKind.Rectangle,
                OffsetXEmu = 0,
                OffsetYEmu = 0,
                ExtentCxEmu = 4572000,
                ExtentCyEmu = 4572000,
                TextBody = body
            };
        }

        // Default insets: 9.14 DIP horizontal, 4.57 DIP vertical.
        const double textLeftDip = 9.14;
        const double textTopDip = 4.57;
        double unshiftedY = textTopDip + lineHeightDip + 8;
        double shiftedY = textTopDip + lineHeightDip * 2 + 8;
        double probeX = textLeftDip + 5;

        SlideShowTextHyperlinkHitTestPlanner
            .HitTest(MakeShape(null), new SlideShowPoint(probeX, unshiftedY))
            .Should().NotBeNull("without space-before the link sits directly under the first line");
        SlideShowTextHyperlinkHitTestPlanner
            .HitTest(MakeShape(100), new SlideShowPoint(probeX, unshiftedY))
            .Should().BeNull("a full line of percent space-before pushes the link past this point");
        SlideShowTextHyperlinkHitTestPlanner
            .HitTest(MakeShape(100), new SlideShowPoint(probeX, shiftedY))
            .Should().NotBeNull("the link hotspot must follow the percent space-before");
    }
}
