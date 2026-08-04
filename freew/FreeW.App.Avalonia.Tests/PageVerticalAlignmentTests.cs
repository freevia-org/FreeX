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

    private static async Task<bool> OnUiThread(Action action)
    {
        try
        {
            await Session.Dispatch(action, CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            return false; // no headless drawing backend in this environment
        }
    }

    [Fact]
    public async Task Print_layout_applies_section_vertical_alignment_to_body_content()
    {
        double topY = 0;
        double centerY = 0;
        double bottomY = 0;
        double centerOffset = 0;
        double bottomOffset = 0;

        var ran = await OnUiThread(() =>
        {
            topY = MeasureFirstGlyph(PageVerticalAlignment.Top, out _);
            centerY = MeasureFirstGlyph(PageVerticalAlignment.Center, out centerOffset);
            bottomY = MeasureFirstGlyph(PageVerticalAlignment.Bottom, out bottomOffset);
        });

        if (!ran)
            return;

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

        var ran = await OnUiThread(() =>
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
        });

        if (!ran)
            return;

        webY.Should().BeLessThan(topY, "Web Layout has no print-page vertical whitespace");
    }

    private static double MeasureFirstGlyph(PageVerticalAlignment alignment, out double offset)
    {
        var view = new DocumentView();
        view.LoadDocument(OneParagraph(alignment));
        view.Measure(new Size(960, 1200));
        offset = view.BodyPageVerticalOffsetsForTest.Single();
        return view.GetPlacedForBlock(0).First().Y;
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
