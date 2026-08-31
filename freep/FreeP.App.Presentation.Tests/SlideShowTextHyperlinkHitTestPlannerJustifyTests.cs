namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Covers freep-hyperlinks-actions F3: the slideshow run-hyperlink hit test must stretch
/// inter-word gaps on non-final Justify/Distributed lines the same way
/// TextBodyFlowDocumentConverter's TextAlignment.Justify stretches the real rendering,
/// so the clickable hot-zone lines up with where the hyperlink text actually renders.
/// </summary>
public sealed class SlideShowTextHyperlinkHitTestPlannerJustifyTests
{
    /// <summary>
    /// Builds a shape whose text body wraps to exactly two lines under a 75dip-wide,
    /// zero-inset text box at an 18pt font (fontSizeDip = 24, default glyph = 13.2dip,
    /// space = 8.4dip):
    ///   Line 1: "Sec " (plain, 48.0dip) + "cd" (hyperlink run, 26.4dip) = 74.4dip natural
    ///           width against a 75.0dip line box -- one word-gap, not the last line.
    ///   Line 2: "Zzzz" (plain filler that forces the line-1/line-2 break).
    /// Real (Justify) rendering stretches that one gap by the full 0.6dip shortfall, so the
    /// "cd" hyperlink actually renders at [48.6, 75.0]dip instead of its unstretched
    /// [48.0, 74.4]dip span.
    /// </summary>
    private static SlideShape BuildTwoLineShape(TextAlign? align)
    {
        var link = new Hyperlink { Url = "https://example.com/cd" };
        var paragraph = new Paragraph { Align = align };
        paragraph.Runs.Add(new Run { Text = "Sec ", FontSizePt = 18 });
        paragraph.Runs.Add(new Run { Text = "cd", FontSizePt = 18, Hyperlink = link });
        paragraph.Runs.Add(new Run { Text = "Zzzz", FontSizePt = 18 });

        var body = new TextBody
        {
            InsetLeftPt = 0,
            InsetRightPt = 0,
            InsetTopPt = 0,
            InsetBottomPt = 0,
        };
        body.Paragraphs.Add(paragraph);

        return new SlideShape
        {
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 714_375,   // 75.0dip * 9525 EMU/dip
            ExtentCyEmu = 1_905_000, // 200dip tall: comfortably fits two 28.8dip lines
            TextBody = body,
        };
    }

    /// <summary>Same geometry, but the paragraph never wraps, so its single line is the
    /// (trivially) last line of the paragraph -- Justify must NOT stretch it.</summary>
    private static SlideShape BuildSingleLineShape(TextAlign? align)
    {
        var link = new Hyperlink { Url = "https://example.com/cd" };
        var paragraph = new Paragraph { Align = align };
        paragraph.Runs.Add(new Run { Text = "Sec ", FontSizePt = 18 });
        paragraph.Runs.Add(new Run { Text = "cd", FontSizePt = 18, Hyperlink = link });

        var body = new TextBody
        {
            InsetLeftPt = 0,
            InsetRightPt = 0,
            InsetTopPt = 0,
            InsetBottomPt = 0,
        };
        body.Paragraphs.Add(paragraph);

        return new SlideShape
        {
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 714_375,   // 75.0dip wide -- content (74.4dip) fits on one line
            ExtentCyEmu = 1_905_000,
            TextBody = body,
        };
    }

    [Theory]
    [InlineData(TextAlign.Justify)]
    [InlineData(TextAlign.Distributed)]
    public void HitTest_StretchesHyperlinkSpanOnNonFinalJustifiedLine(TextAlign align)
    {
        var shape = BuildTwoLineShape(align);

        // 74.7dip is beyond the unstretched span's right edge (74.4dip) but inside the
        // justified span's stretched right edge (75.0dip): only the fixed hit test should
        // find the "cd" hyperlink here.
        var hit = SlideShowTextHyperlinkHitTestPlanner.HitTest(shape, new SlideShowPoint(74.7, 14));

        hit.Should().NotBeNull("the 'cd' run visibly renders out to 75.0dip once its line is justified")
            .And.BeSameAs(shape.TextBody!.Paragraphs[0].Runs[1].Hyperlink);
    }

    [Fact]
    public void HitTest_LeftAlignedLineIsNotStretched()
    {
        // Sibling/no-regression case: the same geometry under the ordinary (Left) alignment
        // must keep behaving exactly as before -- no stretch, so the point just past the
        // natural span edge is correctly a miss.
        var shape = BuildTwoLineShape(align: TextAlign.Left);

        var hit = SlideShowTextHyperlinkHitTestPlanner.HitTest(shape, new SlideShowPoint(74.7, 14));

        hit.Should().BeNull("Left-aligned text never stretches, so 74.7dip falls past the natural 74.4dip span edge");
    }

    [Fact]
    public void HitTest_DoesNotStretchTheFinalLineOfAJustifiedParagraph()
    {
        // Real Justify rendering (and TextAlignment.Justify) never stretches the last line
        // of a paragraph. A single-line paragraph's only line IS the last line, so it must
        // render (and hit-test) exactly like Left alignment even though Align is Justify.
        var shape = BuildSingleLineShape(align: TextAlign.Justify);

        var hit = SlideShowTextHyperlinkHitTestPlanner.HitTest(shape, new SlideShowPoint(74.7, 14));

        hit.Should().BeNull("the final line of a justified paragraph is left-flush, not stretched");
    }

    [Theory]
    [InlineData(TextAlign.Justify)]
    [InlineData(TextAlign.Distributed)]
    public void HitTest_StillMissesWellPastTheStretchedSpan(TextAlign align)
    {
        // Guards against over-correction: a click clearly outside even the fully-stretched
        // line box must still miss.
        var shape = BuildTwoLineShape(align);

        var hit = SlideShowTextHyperlinkHitTestPlanner.HitTest(shape, new SlideShowPoint(90, 14));

        hit.Should().BeNull("90dip is past the entire 75.0dip line box, stretched or not");
    }
}
