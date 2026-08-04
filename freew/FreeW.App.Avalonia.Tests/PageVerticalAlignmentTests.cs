using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using System.Threading;

namespace FreeW.App.Avalonia.Tests;

public sealed class PageVerticalAlignmentTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Print_layout_applies_section_vertical_alignment_to_body_content()
    {
        double topY = 0;
        double centerY = 0;
        double bottomY = 0;
        double centerOffset = 0;
        double bottomOffset = 0;

        await Session.Dispatch(() =>
        {
            topY = MeasureFirstGlyph(PageVerticalAlignment.Top, out _);
            centerY = MeasureFirstGlyph(PageVerticalAlignment.Center, out centerOffset);
            bottomY = MeasureFirstGlyph(PageVerticalAlignment.Bottom, out bottomOffset);
        }, CancellationToken.None);

        centerOffset.Should().BeGreaterThan(0);
        bottomOffset.Should().BeGreaterThan(centerOffset);
        (centerY - topY).Should().BeApproximately(centerOffset, 0.01);
        (bottomY - topY).Should().BeApproximately(bottomOffset, 0.01);
    }

    [Fact]
    public async Task Continuous_view_keeps_body_at_top_for_page_vertical_alignment()
    {
        double topY = 0;
        double webY = 0;

        await Session.Dispatch(() =>
        {
            var doc = OneParagraph(PageVerticalAlignment.Bottom);
            var print = new DocumentView();
            print.LoadDocument(doc);
            print.Measure(new Size(960, 1200));
            topY = print.GetPlacedForBlock(0).First().Y;

            var web = new DocumentView();
            web.LoadDocument(doc);
            web.ViewMode = DocumentViewMode.WebLayout;
            web.Measure(new Size(960, 1200));
            webY = web.GetPlacedForBlock(0).First().Y;
        }, CancellationToken.None);

        webY.Should().BeLessThan(topY, "Web Layout has no print-page vertical whitespace");
    }

    [Fact]
    public async Task Bottom_alignment_uses_image_bottom_and_shifts_image_rect()
    {
        Rect topRect = default;
        Rect shortRect = default;
        Rect tallRect = default;
        double shortOffset = 0;
        double tallOffset = 0;

        await Session.Dispatch(() =>
        {
            (topRect, _) = MeasureInlineImage(PageVerticalAlignment.Top, 100);
            (shortRect, shortOffset) = MeasureInlineImage(PageVerticalAlignment.Bottom, 100);
            (tallRect, tallOffset) = MeasureInlineImage(PageVerticalAlignment.Bottom, 250);
        }, CancellationToken.None);

        (shortRect.Y - topRect.Y).Should().BeApproximately(shortOffset, 0.01);
        (shortOffset - tallOffset).Should().BeApproximately(
            tallRect.Height - shortRect.Height,
            0.01,
            "Bottom alignment must measure the image's true bottom when no glyph geometry exists");
    }

    [Fact]
    public async Task Justified_alignment_distributes_page_space_between_body_blocks_and_hit_tests_shifted_text()
    {
        double firstTop = 0;
        double secondTop = 0;
        double thirdTop = 0;
        double gap = 0;
        (int Block, int Offset)? hit = null;

        await Session.Dispatch(() =>
        {
            var topDocument = ThreeParagraphs(PageVerticalAlignment.Top);
            var topView = new DocumentView();
            topView.LoadDocument(topDocument);
            topView.Measure(new Size(960, 1200));
            firstTop = topView.GetPlacedForBlock(0).First().Y;
            var topSecond = topView.GetPlacedForBlock(1).First().Y;

            var justifiedView = new DocumentView();
            justifiedView.LoadDocument(ThreeParagraphs(PageVerticalAlignment.Justified));
            justifiedView.Measure(new Size(960, 1200));
            secondTop = justifiedView.GetPlacedForBlock(1).First().Y;
            thirdTop = justifiedView.GetPlacedForBlock(2).First().Y;
            gap = justifiedView.BodyPageVerticalJustifiedGapsForTest.Single();

            var secondGlyph = justifiedView.GetPlacedForBlock(1).First();
            hit = justifiedView.TestHitTest(new Point(
                secondGlyph.X + Math.Max(0.1, secondGlyph.W / 2),
                secondGlyph.Y + secondGlyph.LineHeight / 2));

            (secondTop - topSecond).Should().BeApproximately(gap, 0.01);
            (thirdTop - secondTop).Should().BeGreaterThan(gap);
            firstTop.Should().BeApproximately(topView.GetPlacedForBlock(0).First().Y, 0.01);
        }, CancellationToken.None);

        gap.Should().BeGreaterThan(0);
        hit.Should().Be((1, 1));
    }

    private static double MeasureFirstGlyph(PageVerticalAlignment alignment, out double offset)
    {
        var view = new DocumentView();
        view.LoadDocument(OneParagraph(alignment));
        view.Measure(new Size(960, 1200));
        offset = alignment == PageVerticalAlignment.Top
            ? 0
            : view.BodyPageVerticalOffsetsForTest.Single();
        return view.GetPlacedForBlock(0).First().Y;
    }

    private static (Rect Rect, double Offset) MeasureInlineImage(PageVerticalAlignment alignment, double heightPt)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(string.Empty, RunFormatting.Default)
        {
            Image = new InlineImage([1], 100, heightPt) { Wrapping = ImageWrapping.Inline }
        });
        document.Blocks.Add(paragraph);
        document.Page.VerticalAlignment = alignment;

        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(960, 1200));
        return (view.InlineImageRects.Single(),
            alignment == PageVerticalAlignment.Top ? 0 : view.BodyPageVerticalOffsetsForTest.Single());
    }

    private static TextDocument OneParagraph(PageVerticalAlignment alignment)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("A short body paragraph."));
        document.Page.VerticalAlignment = alignment;
        return document;
    }

    private static TextDocument ThreeParagraphs(PageVerticalAlignment alignment)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("First paragraph."));
        document.Blocks.Add(new Paragraph("Second paragraph."));
        document.Blocks.Add(new Paragraph("Third paragraph."));
        document.Page.VerticalAlignment = alignment;
        return document;
    }
}
