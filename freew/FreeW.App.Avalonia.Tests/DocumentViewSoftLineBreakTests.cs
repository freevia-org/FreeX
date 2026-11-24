using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewSoftLineBreakTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    public static IEnumerable<object[]> BreaksAndSpacing()
    {
        foreach (var separator in new[] { "\n", "\r", "\r\n", "\v", "\u2028", "\u2029" })
        foreach (var spacing in new[] { 0.0, 1.5, -1.5 })
            yield return [separator, spacing];
    }

    [Theory]
    [MemberData(nameof(BreaksAndSpacing))]
    public Task SoftBreakCreatesTwoLinesWithoutChangingTextOrSpacing(string separator, double spacing) =>
        Session.Dispatch(() =>
        {
            var text = "AB" + separator + "AB";
            var view = View(Paragraph(text, spacing));
            var glyphs = view.ReviewGlyphsForTest;

            glyphs.Select(glyph => glyph.Ch).Should().Equal('A', 'B', 'A', 'B');
            glyphs.Select(glyph => glyph.Offset).Should().Equal(0, 1, 2 + separator.Length, 3 + separator.Length);
            glyphs.Select(glyph => glyph.Rect.Y).Distinct().Should().HaveCount(2);
            var before = Caret(view, 0, 2);
            var after = Caret(view, 0, 2 + separator.Length);
            (after.Y - before.Y).Should().BeApproximately(20 * 4.0 / 3, 0.01);
            after.X.Should().BeApproximately(Caret(view, 0, 0).X, 0.01);
            Caret(view, 0, text.Length).X.Should().BeApproximately(before.X, 0.01,
                "line controls must not acquire character spacing or change the following line's origin");
            view.Document.PlainText.Should().Be(text);

            view.MoveCaretToBlockForTest(0, 0);
            Press(view, Key.End);
            view.CaretPositionForTest.Should().Be((0, 2));
            Press(view, Key.Right);
            view.CaretPositionForTest.Should().Be((0, 2 + separator.Length),
                "CRLF is one text element and the visual line end precedes the whole separator");
        }, CancellationToken.None);

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    [InlineData("\v")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public Task LeadingConsecutiveAndTrailingBreaksKeepEmptyLineCaretStops(string separator) =>
        Session.Dispatch(() =>
        {
            var text = separator + separator + "AB" + separator;
            var view = View(Paragraph(text));
            var offsets = new[] { 0, separator.Length, separator.Length * 2, text.Length };
            var carets = offsets.Select(offset => Caret(view, 0, offset)).ToArray();

            for (var index = 1; index < carets.Length; index++)
            {
                (carets[index].Y - carets[index - 1].Y).Should().BeApproximately(20 * 4.0 / 3, 0.01);
                carets[index].X.Should().BeApproximately(carets[0].X, 0.01);
            }
            view.ReviewGlyphsForTest.Should().HaveCount(2);
            view.Document.PlainText.Should().Be(text);
        }, CancellationToken.None);

    [Fact]
    public Task CrLfAcrossFormattingRunsStillProducesOneBoundary() => Session.Dispatch(() =>
    {
        var paragraph = Paragraph("A\r");
        paragraph.Runs.Add(new Run("\nB", RunFormatting.Default with { Bold = true, FontSizePt = 22 }));
        var view = View(paragraph);

        (Caret(view, 0, 3).Y - Caret(view, 0, 1).Y).Should().BeApproximately(20 * 4.0 / 3, 0.01);
        view.ReviewGlyphsForTest.Select(glyph => glyph.Ch).Should().Equal('A', 'B');
        view.MoveCaretToBlockForTest(0, 0);
        Press(view, Key.End);
        view.CaretPositionForTest.Should().Be((0, 1));
        Press(view, Key.Right);
        view.CaretPositionForTest.Should().Be((0, 3));
    }, CancellationToken.None);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task KeepWithNextCountsEverySoftLineInTheFollowingParagraph(bool keepWithNext) => Session.Dispatch(() =>
    {
        var filler = Paragraph("filler\nfiller");
        var heading = Paragraph("heading");
        heading.Formatting = heading.Formatting with { KeepWithNext = keepWithNext };
        var body = Paragraph("one\n\nthree");
        body.Formatting = body.Formatting with { KeepLinesTogether = true };
        var document = Document(filler, heading, body);
        // Keep the physical page above the renderer's 400-DIP minimum. A 120-point
        // page is expanded by that floor, so it does not create the intended boundary.
        // These margins leave exactly 100 points: filler consumes 40, the heading 20,
        // and the following three lines need 60 together (including the blank line).
        document.Page.HeightPt = 400;
        document.Page.MarginTopPt = document.Page.MarginBottomPt = 150;
        var view = View(document);

        view.MoveCaretToBlockForTest(1, 0);
        view.CaretPageIndex.Should().Be(keepWithNext ? 1 : 0);
        view.MoveCaretToBlockForTest(2, 0);
        view.CaretPageIndex.Should().Be(1);
        view.MoveCaretToBlockForTest(2, body.PlainText.Length);
        view.CaretPageIndex.Should().Be(1);
        view.PageCount.Should().Be(2);
    }, CancellationToken.None);

    [Fact]
    public Task HiddenSoftControlsDoNotCreateVisibleLines() => Session.Dispatch(() =>
    {
        var paragraph = Paragraph("AB");
        paragraph.Runs.Add(new Run("\r\n\v\u2028", RunFormatting.Default with { Hidden = true }));
        paragraph.Runs.Add(new Run("CD"));
        var view = View(paragraph);

        Caret(view, 0, 0).Y.Should().BeApproximately(Caret(view, 0, paragraph.PlainText.Length).Y, 0.01);
        view.Document.PlainText.Should().Be("AB\r\n\v\u2028CD");
    }, CancellationToken.None);

    [Fact]
    public Task EmptyFinalLineInheritsTheSoftBreakFontHeight() => Session.Dispatch(() =>
    {
        var paragraph = Paragraph("\n\n");
        paragraph.Formatting = paragraph.Formatting with { LineRule = LineSpacingRule.Multiple, LineSpacing = 1 };
        paragraph.Runs[0] = new Run("\n\n", RunFormatting.Default with { FontFamily = "Arial", FontSizePt = 32 });
        var view = View(paragraph);
        var first = Caret(view, 0, 0);
        var middle = Caret(view, 0, 1);
        var last = Caret(view, 0, 2);

        middle.Height.Should().BeApproximately(first.Height, 0.01);
        last.Height.Should().BeApproximately(first.Height, 0.01);
        (middle.Y - first.Y).Should().BeApproximately(first.Height, 0.01);
        (last.Y - middle.Y).Should().BeApproximately(middle.Height, 0.01);
    }, CancellationToken.None);

    private static Paragraph Paragraph(string text, double spacing = 0)
    {
        var paragraph = new Paragraph
        {
            Formatting = ParagraphFormatting.Default with
            {
                LineRule = LineSpacingRule.Exact,
                LineHeightPt = 20,
                SpaceBeforePt = 0, SpaceBeforeIsSet = true,
                SpaceAfterPt = 0, SpaceAfterIsSet = true,
                WidowControl = false, WidowControlIsSet = true,
            },
        };
        paragraph.Runs.Add(new Run(text, RunFormatting.Default with
        {
            FontFamily = "Arial", FontSizePt = 11, CharacterSpacingPt = spacing,
        }));
        return paragraph;
    }

    private static TextDocument Document(params Block[] blocks)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.AddRange(blocks);
        return document;
    }

    private static DocumentView View(params Block[] blocks) => View(Document(blocks));

    private static DocumentView View(TextDocument document)
    {
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 4000));
        return view;
    }

    private static Rect Caret(DocumentView view, int block, int offset)
    {
        view.MoveCaretToBlockForTest(block, offset);
        view.CaretRectForTest.Should().NotBeNull();
        return view.CaretRectForTest!.Value;
    }

    private static void Press(DocumentView view, Key key)
    {
        var args = new KeyEventArgs { Key = key, RoutedEvent = DocumentView.KeyDownEvent };
        view.RaiseKeyDownForContextMenuTests(args);
        args.Handled.Should().BeTrue();
    }
}
