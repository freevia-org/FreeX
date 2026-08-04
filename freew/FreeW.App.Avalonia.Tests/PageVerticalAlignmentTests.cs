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

    [Fact]
    public async Task Justified_multi_column_alignment_uses_column_order_and_shifts_owned_objects_and_caret()
    {
        double topBodyY = 0;
        double justifiedBodyY = 0;
        double topObjectY = 0;
        double justifiedObjectY = 0;
        double gap = 0;
        double caretTop = 0;
        (int Block, int Offset)? hit = null;

        await Session.Dispatch(() =>
        {
            var topView = new DocumentView();
            topView.LoadDocument(MultiColumnDocument(PageVerticalAlignment.Top));
            topView.Measure(new Size(816, 4000));
            var topBody = topView.GetPlacedForBlock(1).First();
            topBodyY = topBody.Y;
            topObjectY = topView.InlineImageRects.Last().Y;

            var justifiedView = new DocumentView();
            justifiedView.LoadDocument(MultiColumnDocument(PageVerticalAlignment.Justified));
            justifiedView.Measure(new Size(816, 4000));
            var justifiedBody = justifiedView.GetPlacedForBlock(1).First();
            justifiedBodyY = justifiedBody.Y;
            justifiedObjectY = justifiedView.InlineImageRects.Last().Y;
            gap = justifiedView.BodyPageVerticalJustifiedGapsForTest.Single();

            hit = justifiedView.TestHitTest(new Point(
                justifiedBody.X + Math.Max(0.1, justifiedBody.W / 4),
                justifiedBody.Y + justifiedBody.LineHeight / 2));
            justifiedView.MoveCaretToBlockForTest(1, 0);
            caretTop = justifiedView.CaretTop;
        }, CancellationToken.None);

        gap.Should().BeGreaterThan(0);
        (justifiedBodyY - topBodyY).Should().BeApproximately(gap, 0.01,
            "the first block after the column-0 block boundary receives one gap");
        (justifiedObjectY - topObjectY).Should().BeApproximately(2 * gap, 0.01,
            "the image-only block in column 1 follows both column-order boundaries");
        hit.Should().Be((1, 0));
        caretTop.Should().BeApproximately(justifiedBodyY, 0.01,
            "caret geometry must consume the shifted glyph geometry");
    }

    [Fact]
    public async Task Justified_floating_groups_use_anchor_flow_column_for_all_geometry()
    {
        await Session.Dispatch(() =>
        {
            foreach (var anchorInSecondColumn in new[] { false, true })
            {
                var targetBlock = anchorInSecondColumn ? 3 : 1;
                var top = MeasureFloatingAnchorCase(
                    FloatingAnchorDocument(PageVerticalAlignment.Top, anchorInSecondColumn),
                    targetBlock);
                var justified = MeasureFloatingAnchorCase(
                    FloatingAnchorDocument(PageVerticalAlignment.Justified, anchorInSecondColumn),
                    targetBlock);
                var expectedOffset = justified.AnchorGlyphY - top.AnchorGlyphY;

                expectedOffset.Should().BeGreaterThan(0,
                    $"anchorInSecondColumn={anchorInSecondColumn}, topAnchorY={top.AnchorGlyphY}, justifiedAnchorY={justified.AnchorGlyphY}");
                (justified.GroupRect.Y - top.GroupRect.Y).Should().BeApproximately(expectedOffset, 0.01,
                    "the floating group must follow its anchor paragraph, not its visual column");
                (justified.ChildRect.Y - top.ChildRect.Y).Should().BeApproximately(expectedOffset, 0.01,
                    "nested group children must inherit the root group's single owner offset");
                (justified.SnapshotRect.Y - top.SnapshotRect.Y).Should().BeApproximately(expectedOffset, 0.01,
                    "the shared floating snapshot must use the anchor paragraph offset");
                (justified.WrapRect.Y - top.WrapRect.Y).Should().BeApproximately(expectedOffset, 0.01,
                    "wrap exclusion geometry must use the anchor paragraph offset");
                (justified.SelectedRect.Y - top.GroupRect.Y).Should().BeApproximately(expectedOffset, 0.01,
                    "selection geometry must stay aligned with the floating group");
                (justified.SelectedChildRect.Y - top.ChildRect.Y).Should().BeApproximately(expectedOffset, 0.01,
                    "child selection geometry must stay aligned with the nested child");

                var visualBand = top.View.LayoutColumnBand(anchorInSecondColumn ? 0 : 1);
                top.GroupRect.X.Should().BeInRange(
                    visualBand.Left - 2,
                    visualBand.Left + visualBand.Width + 2,
                    anchorInSecondColumn
                        ? "the inverse case must place a column-2 anchor visually into column 1"
                        : "the forward case must place a column-1 anchor visually into column 2");
            }
        }, CancellationToken.None);
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

    private static TextDocument MultiColumnDocument(PageVerticalAlignment alignment)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Page.WidthPt = 612;
        document.Page.HeightPt = 792;
        document.Page.MarginLeftPt = 72;
        document.Page.MarginRightPt = 72;
        document.Page.MarginTopPt = 72;
        document.Page.MarginBottomPt = 72;
        document.Page.ColumnCount = 2;
        document.Page.ColumnSpacingPt = 36;
        document.Page.VerticalAlignment = alignment;

        var filler = new Paragraph();
        filler.Runs.Add(new Run(string.Empty, RunFormatting.Default)
        {
            Image = new InlineImage([1], 100, 600) { Wrapping = ImageWrapping.Inline }
        });
        document.Blocks.Add(filler);
        document.Blocks.Add(new Paragraph(string.Join(
            ' ',
            Enumerable.Repeat(
                "This paragraph continues through the first column and into the next column.",
                8))));

        var imageOnlyBlock = new Paragraph();
        imageOnlyBlock.Runs.Add(new Run(string.Empty, RunFormatting.Default)
        {
            Image = new InlineImage([1], 100, 20) { Wrapping = ImageWrapping.Inline }
        });
        document.Blocks.Add(imageOnlyBlock);
        return document;
    }

    private static FloatingAnchorSample MeasureFloatingAnchorCase(
        TextDocument document,
        int targetBlock)
    {
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 4000));

        var anchorGlyph = view.GetPlacedForBlock(targetBlock).First();
        var groupRect = view.FloatingGroupRects.Single().Rect;
        var childRect = view.FloatingGroupChildRectForPathForTest(targetBlock, 1, [0, 0])!.Value;
        var snapshotRect = view.FloatingSnapshotRectsForTest.Single().Rect;
        var wrapRect = view.WrapExclusionZones.Single().Rect;

        view.SelectFloating(targetBlock, 1);
        var selectedRect = view.SelectedFloatingInfo!.Value.Rect;
        view.SelectFloatingGroupChildForTest(childRect.Center).Should().BeTrue();
        var selectedChildRect = view.SelectedFloatingGroupChildInfo!.Value.Rect;

        return new FloatingAnchorSample(
            view,
            anchorGlyph.Y,
            groupRect,
            childRect,
            snapshotRect,
            wrapRect,
            selectedRect,
            selectedChildRect);
    }

    private static TextDocument FloatingAnchorDocument(
        PageVerticalAlignment alignment,
        bool anchorInSecondColumn)
    {
        var document = MultiColumnDocument(alignment);
        document.Blocks.Clear();

        var filler = new Paragraph();
        filler.Runs.Add(new Run(string.Empty, RunFormatting.Default)
        {
            Image = new InlineImage([1], 100, 100) { Wrapping = ImageWrapping.Inline }
        });
        document.Blocks.Add(filler);

        if (anchorInSecondColumn)
        {
            document.Blocks.Add(new Paragraph("first column"));
            document.Blocks.Add(new Paragraph { Runs = { Run.ColumnBreak() } });
        }

        var anchor = new Paragraph(string.Join(
            ' ',
            Enumerable.Repeat(
                "This anchored paragraph continues through the first column and into the next column.",
                20)));
        anchor.Runs.Add(Run.FromDrawingGroup(CreateCrossColumnGroup(anchorInSecondColumn)));
        document.Blocks.Add(anchor);

        var trailing = new Paragraph("Trailing flow content.");
        document.Blocks.Add(trailing);
        return document;
    }

    private static DrawingGroup CreateCrossColumnGroup(bool anchorInSecondColumn)
    {
        var inner = new DrawingGroup { WidthPt = 96, HeightPt = 56 };
        inner.Children.Add(new Shape(ShapeKind.Rectangle, 48, 28));
        inner.ChildOffsets.Add((12, 12));

        var outer = new DrawingGroup
        {
            WidthPt = 280,
            HeightPt = 80,
            Placement = new FloatingPlacement
            {
                Wrapping = ImageWrapping.Square,
                HorizontalAnchor = HorizontalAnchor.Page,
                VerticalAnchor = VerticalAnchor.Paragraph,
                HorizontalOffsetPt = anchorInSecondColumn ? 84 : 360,
                VerticalOffsetPt = 0
            }
        };
        outer.Children.Add(inner);
        outer.ChildOffsets.Add((24, 20));
        outer.Children.Add(new Shape(ShapeKind.Ellipse, 64, 36));
        outer.ChildOffsets.Add((176, 48));
        return outer;
    }

    private sealed record FloatingAnchorSample(
        DocumentView View,
        double AnchorGlyphY,
        Rect GroupRect,
        Rect ChildRect,
        Rect SnapshotRect,
        Rect WrapRect,
        Rect SelectedRect,
        Rect SelectedChildRect);
}
