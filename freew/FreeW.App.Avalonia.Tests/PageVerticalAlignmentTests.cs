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
}
