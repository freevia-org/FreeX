using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using ModelAlignment = FreeW.Core.Model.TextAlignment;
using Drawing = Avalonia.Media.Drawing;
using DrawingGroup = Avalonia.Media.DrawingGroup;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewHeaderFooterCaretGeometryTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);
    private static readonly Color TextColor = Color.Parse("#7632A4");
    private static RunFormatting Formatting => RunFormatting.Default with
        { FontFamily = "Arial", FontSizePt = 18, Bold = true, ColorHex = "#7632A4" };

    public static IEnumerable<object[]> Breaks()
    {
        foreach (var footer in new[] { false, true })
        foreach (var separator in new[] { "\n", "\r\n", "\v", "\u2028", "\u2029" })
            yield return [footer, separator];
    }

    [Theory]
    [MemberData(nameof(Breaks))]
    public Task CaretAndSelectionFollowTheActualDrawnLineAndTypingPreservesSourceOffsets(bool footer, string separator) => OnUi(() =>
    {
        var source = new Run("AAAA" + separator + "B", Formatting);
        var paragraph = new Paragraph();
        paragraph.Runs.Add(source);
        var view = View(footer, paragraph);
        view.HeaderFooterUsesSharedLiteralLayout(paragraph).Should().BeTrue();
        var lineStart = 4 + separator.Length;
        view.PlaceCaretInHeaderFooter(footer, 0, lineStart);
        var lines = TextInk(view).OrderBy(bounds => bounds.Y).ToArray();
        lines.Should().HaveCount(2, "the actual drawing must contain two separate shaped text lines");
        var caret = view.HfCaretRectForTest!.Value;
        caret.X.Should().BeApproximately(lines[1].X, 0.1);
        caret.Y.Should().BeGreaterThan(lines[0].Y);
        caret.Top.Should().BeLessThanOrEqualTo(lines[1].Top + 0.1);
        caret.Bottom.Should().BeGreaterThanOrEqualTo(lines[1].Bottom - 0.1);
        view.PlaceCaretInHeaderFooter(footer, 0, 4);
        var before = view.HfCaretRectForTest!.Value;
        before.Y.Should().BeLessThan(caret.Y);
        before.X.Should().BeApproximately(lines[0].Right, 0.1);

        view.PlaceCaretInHeaderFooter(footer, 0, lineStart);
        Press(view, Key.Right, shift: true);
        view.SelectedText.Should().Be("B");
        var highlights = Paint(view).Where(item => item.Drawing is GeometryDrawing geometry
                && geometry.Brush is ISolidColorBrush brush && brush.Color == Color.FromArgb(0x55, 0x33, 0x99, 0xFF))
            .Select(item => item.Drawing.GetBounds().TransformToAABB(item.Transform)).ToArray();
        highlights.Should().ContainSingle();
        highlights[0].X.Should().BeApproximately(lines[1].X, 0.1);
        highlights[0].Right.Should().BeApproximately(lines[1].Right, 0.1);
        highlights[0].Top.Should().BeApproximately(caret.Top, 0.1);
        highlights[0].Height.Should().BeApproximately(caret.Height, 0.1);

        view.PlaceCaretInHeaderFooter(footer, 0, lineStart + 1);
        view.InsertText("X");
        Story(view, footer).PlainText.Should().Be("AAAA" + separator + "BX");
        var typedLine = TextInk(view).OrderBy(bounds => bounds.Y).Last();
        var typedCaret = view.HfCaretRectForTest!.Value;
        typedCaret.X.Should().BeApproximately(typedLine.Right, 0.1);
        typedCaret.Top.Should().BeApproximately(caret.Top, 0.1);
        view.Undo();
        Story(view, footer).PlainText.Should().Be("AAAA" + separator + "B");
        view.HfCaretRectForTest!.Value.X.Should().BeApproximately(lines[1].Right, 0.1);
    });

    [Theory]
    [InlineData(false, ModelAlignment.Center)]
    [InlineData(false, ModelAlignment.Right)]
    [InlineData(true, ModelAlignment.Center)]
    [InlineData(true, ModelAlignment.Right)]
    public Task ParagraphAlignmentUsesTheSameOriginForDrawingAndBothCaretLines(bool footer, ModelAlignment alignment) => OnUi(() =>
    {
        var paragraph = new Paragraph { Formatting = ParagraphFormatting.Default with { Alignment = alignment } };
        paragraph.Runs.Add(new Run("AAAA\nB", Formatting));
        var view = View(footer, paragraph);
        var lines = TextInk(view).OrderBy(bounds => bounds.Y).ToArray();
        lines.Should().HaveCount(2);
        view.PlaceCaretInHeaderFooter(footer, 0, 0);
        view.HfCaretRectForTest!.Value.X.Should().BeApproximately(lines[0].X, 0.1);
        view.PlaceCaretInHeaderFooter(footer, 0, 5);
        view.HfCaretRectForTest!.Value.X.Should().BeApproximately(lines[1].X, 0.1);
        view.HfCaretRectForTest!.Value.Y.Should().BeGreaterThan(lines[0].Y);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MultilineParagraphReservesItsDrawnHeightBeforeTheNextParagraph(bool footer) => OnUi(() =>
    {
        var first = new Paragraph();
        first.Runs.Add(new Run("AAAA\nB", Formatting));
        var second = new Paragraph();
        second.Runs.Add(new Run("CC", Formatting));
        var view = View(footer, first, second);
        var lines = TextInk(view).OrderBy(bounds => bounds.Y).ToArray();
        lines.Should().HaveCount(3);
        lines[2].Top.Should().BeGreaterThanOrEqualTo(lines[1].Bottom - 0.1);
        view.PlaceCaretInHeaderFooter(footer, 1, 0);
        view.HfCaretRectForTest!.Value.X.Should().BeApproximately(lines[2].X, 0.1);
        view.HfCaretRectForTest!.Value.Y.Should().BeGreaterThanOrEqualTo(lines[1].Bottom - 0.1);
    });

    [Fact]
    public Task FooterBottomEdgeDoesNotMoveWhenTheLiteralStoryGainsALine() => OnUi(() =>
    {
        DocumentView Make(string text)
        {
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run(text, Formatting));
            return View(true, paragraph);
        }
        var single = TextInk(Make("B")).Single();
        var multiple = TextInk(Make("AAAA\nB")).OrderBy(bounds => bounds.Y).ToArray();
        multiple.Should().HaveCount(2);
        multiple[^1].Bottom.Should().BeApproximately(single.Bottom, 0.1);
        multiple[0].Top.Should().BeLessThan(single.Top);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task KeyboardKeepsUtf16GraphemeBoundariesAcrossTheLiteralBreak(bool footer) => OnUi(() =>
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("AAAA\r\n😀e\u0301", Formatting));
        var view = View(footer, paragraph);
        view.PlaceCaretInHeaderFooter(footer, 0, 4);
        Press(view, Key.Right);
        view.AutomationSnapshot().CaretOffset.Should().Be(6);
        var start = view.HfCaretRectForTest!.Value;
        Press(view, Key.Right);
        view.AutomationSnapshot().CaretOffset.Should().Be(8);
        view.HfCaretRectForTest!.Value.X.Should().BeGreaterThan(start.X);
        view.HfCaretRectForTest!.Value.Y.Should().BeApproximately(start.Y, 0.1);
        Press(view, Key.Right);
        view.AutomationSnapshot().CaretOffset.Should().Be(10);
        Press(view, Key.Left, shift: true);
        view.SelectedText.Should().Be("e\u0301");
        paragraph.PlainText.Should().Be("AAAA\r\n😀e\u0301");
    });

    [Theory]
    [InlineData("style")]
    [InlineData("tab")]
    [InlineData("field")]
    public Task CapabilityDoesNotAdvertiseUnsupportedMultilineStories(string kind) => OnUi(() =>
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("AAAA\n", Formatting));
        paragraph.Runs.Add(kind switch
        {
            "style" => new Run("B", Formatting with { FontSizePt = 24, CharacterSpacingPt = 1 }),
            "tab" => new Run("\tB", Formatting),
            _ => Run.DateField("B", Formatting),
        });
        var original = paragraph.Runs.ToArray();
        var view = View(false, paragraph);
        view.HeaderFooterUsesSharedLiteralLayout(paragraph).Should().BeFalse();
        paragraph.Runs.Should().Equal(original);
        view.PlaceCaretInHeaderFooter(false, 0, 0);
        view.HfCaretRectForTest.Should().NotBeNull("the preserved field/story fallback remains available");
    });

    [Theory]
    [MemberData(nameof(Breaks))]
    public Task SecondLineMousePlacementUsesTheActualDrawnLine(bool footer, string separator) => OnUi(() =>
    {
        var paragraph = new Paragraph();
        var source = new Run("AAAA" + separator + "B", Formatting);
        paragraph.Runs.Add(source);
        var view = View(footer, paragraph);
        var secondLine = TextInk(view).OrderBy(bounds => bounds.Y).Last();

        view.HitTestHeaderFooterForTest(new Point(secondLine.Left + 0.01,
            secondLine.Top + secondLine.Height / 2)).Should().BeTrue();

        view.AutomationSnapshot().CaretOffset.Should().Be(4 + separator.Length);
        var caret = view.HfCaretRectForTest!.Value;
        caret.X.Should().BeApproximately(secondLine.Left, 0.1);
        caret.Top.Should().BeLessThanOrEqualTo(secondLine.Top + 0.1);
        caret.Bottom.Should().BeGreaterThanOrEqualTo(secondLine.Bottom - 0.1);
        view.InsertText("X");
        paragraph.PlainText.Should().Be("AAAA" + separator + "XB");
        paragraph.Runs.Select(run => run.Formatting.FontSizePt).Should().OnlyContain(size => size == 18);
        view.Undo();
        paragraph.PlainText.Should().Be("AAAA" + separator + "B");
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MouseHitPositionsStayOnUtf16TextElementBoundaries(bool footer) => OnUi(() =>
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("A\n😀e\u0301", Formatting));
        var view = View(footer, paragraph);
        var ink = TextInk(view);
        var firstY = ink.Min(bounds => bounds.Y);
        var secondLine = ink.Where(bounds => bounds.Y > firstY + 1).Aggregate((left, right) => left.Union(right));

        for (var sample = 0; sample <= 16; sample++)
        {
            var point = new Point(secondLine.Left + secondLine.Width * sample / 16,
                secondLine.Top + secondLine.Height / 2);
            view.HitTestHeaderFooterForTest(point).Should().BeTrue();
            view.AutomationSnapshot().CaretOffset.Should().BeOneOf(new[] { 2, 4, 6 },
                "mouse placement must not split a surrogate pair or a combining text element");
        }
        paragraph.PlainText.Should().Be("A\n😀e\u0301");
    });

    [Theory]
    [InlineData(false, "\n")]
    [InlineData(true, "\n")]
    [InlineData(false, "\r")]
    [InlineData(true, "\r")]
    public Task StyledRunBeginningWithALineBreakStillMeasuresAndActuallyDraws(bool footer, string separator) => OnUi(() =>
    {
        var first = new Run("A", Formatting);
        var formatting = Formatting with { FontSizePt = 24, Bold = false, Italic = true };
        var second = new Run(separator + "B", formatting);
        var paragraph = new Paragraph();
        paragraph.Runs.AddRange([first, second]);
        var view = View(footer, paragraph);

        view.HeaderFooterUsesSharedLiteralLayout(paragraph).Should().BeTrue();
        var drawn = TextInk(view).OrderBy(bounds => bounds.Y).ToArray();
        drawn.Should().HaveCount(2, "both styled runs must reach the actual renderer without a no-glyph measurement hang");
        drawn[1].Y.Should().BeGreaterThan(drawn[0].Y);
        drawn[1].Height.Should().BeGreaterThan(drawn[0].Height);
        view.PlaceCaretInHeaderFooter(footer, 0, 1 + separator.Length);
        var caret = view.HfCaretRectForTest!.Value;
        double.IsFinite(caret.X).Should().BeTrue();
        double.IsFinite(caret.Y).Should().BeTrue();
        caret.Height.Should().BeGreaterThan(0);
        caret.X.Should().BeApproximately(drawn[1].X, 0.1,
            "a break-only prefix has zero width in the styled paragraph's shared layout");
        view.HitTestHeaderFooterForTest(new Point(drawn[1].X + 0.01,
            drawn[1].Y + drawn[1].Height / 2)).Should().BeTrue();
        view.AutomationSnapshot().CaretOffset.Should().Be(1 + separator.Length);
        view.HfCaretRectForTest.Should().NotBeNull();
        Press(view, Key.Left, shift: true);
        view.SelectedText.Should().Be(separator);
        TextInk(view).Should().HaveCount(2, "a selected break-only substring must not enter glyph formatting either");
        paragraph.PlainText.Should().Be("A" + separator + "B");
        paragraph.Runs.Should().Equal(first, second);
        second.Formatting.Should().BeSameAs(formatting);
        view.HeaderFooterItemsFull.Should().ContainSingle().Which.Text.Should().Be("A" + separator + "B");
    });

    [Theory]
    [InlineData(false, "\n")]
    [InlineData(true, "\n")]
    [InlineData(false, "\r")]
    [InlineData(true, "\r")]
    public Task LegacyControlOnlySegmentHasZeroAdvanceWithoutDrawingMetricSampleText(bool footer, string separator) => OnUi(() =>
    {
        var first = new Run("L", Formatting);
        var control = new Run(separator, Formatting with { Bold = false, CharacterSpacingPt = 1 });
        var last = new Run("right", Formatting);
        var paragraph = new Paragraph();
        paragraph.Runs.AddRange([first, control, last]);
        var view = View(footer, paragraph);
        view.HeaderFooterUsesSharedLiteralLayout(paragraph).Should().BeFalse();

        view.PlaceCaretInHeaderFooter(footer, 0, 1 + separator.Length);
        view.HfCaretRectForTest.Should().NotBeNull();
        view.HfCaretRectForTest!.Value.Height.Should().BeGreaterThan(0);
        var actualDrawnText = string.Concat(Paint(view)
            .Where(item => item.Drawing is GlyphRunDrawing glyph
                && glyph.Foreground is ISolidColorBrush brush && brush.Color == TextColor)
            .Select(item => ((GlyphRunDrawing)item.Drawing).GlyphRun!.Characters.Span.ToString()));
        actualDrawnText.Should().Be("Lright", "Ag is a metrics sample only and must never be rendered");
        paragraph.PlainText.Should().Be("L" + separator + "right");
        paragraph.Runs.Should().Equal(first, control, last);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task GlyphlessTabSegmentKeepsItsAdvanceBeforeAStyledVisibleSuffix(bool footer) => OnUi(() =>
    {
        var tabFormatting = Formatting with { Bold = false };
        var tab = Run.DateField("\t", tabFormatting);
        tab.FieldLocked = true; // Locked cached field results are atomic and are not literal-tab split.
        var suffix = new Run("right", Formatting);
        var paragraph = new Paragraph();
        paragraph.Runs.AddRange([tab, suffix]);
        var view = View(footer, paragraph);
        var referenceSource = Run.DateField("\tright", tabFormatting);
        referenceSource.FieldLocked = true;
        var referenceParagraph = new Paragraph();
        referenceParagraph.Runs.Add(referenceSource);
        var reference = View(footer, referenceParagraph);
        var actualInk = TextInk(view).Single();
        var referenceSuffixX = RenderedTextStart(reference, "right");
        var start = view.HeaderFooterItemsFull.First().X;

        actualInk.X.Should().BeGreaterThan(start + 1,
            "the standalone tab segment has advance although it paints no glyph");
        actualInk.X.Should().BeApproximately(referenceSuffixX, 0.1,
            "a formatting boundary before the suffix must not discard the tab advance");
        view.PlaceCaretInHeaderFooter(footer, 0, 0);
        var before = view.HfCaretRectForTest!.Value;
        view.PlaceCaretInHeaderFooter(footer, 0, 1);
        var after = view.HfCaretRectForTest!.Value;
        after.X.Should().BeGreaterThan(before.X + 1);
        after.X.Should().BeApproximately(actualInk.X, 0.1);
        var drawnText = string.Concat(Paint(view)
            .Where(item => item.Drawing is GlyphRunDrawing glyph
                && glyph.Foreground is ISolidColorBrush brush && brush.Color == TextColor)
            .Select(item => ((GlyphRunDrawing)item.Drawing).GlyphRun!.Characters.Span.ToString()));
        drawnText.Should().Be("right", "a glyphless tab must reserve space without painting metric text");
        paragraph.Runs.Should().Equal(tab, suffix);
        tab.Text.Should().Be("\t");
        tab.FieldLocked.Should().BeTrue();
        tab.Formatting.Should().BeSameAs(tabFormatting);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task LiteralLeadingTabKeepsTheSameStopWithAStyledSuffix(bool footer) => OnUi(() =>
    {
        var tab = new Run("\t", Formatting with { Bold = false });
        var suffix = new Run("right", Formatting);
        var paragraph = new Paragraph();
        paragraph.Runs.AddRange([tab, suffix]);
        var referenceParagraph = new Paragraph();
        referenceParagraph.Runs.Add(new Run("\tright", Formatting));
        var view = View(footer, paragraph);
        var reference = View(footer, referenceParagraph);
        var actualInk = TextInk(view).Single();
        var referenceInk = TextInk(reference).Single();
        actualInk.X.Should().BeApproximately(referenceInk.X, 0.1);
        view.PlaceCaretInHeaderFooter(footer, 0, 1);
        view.HfCaretRectForTest!.Value.X.Should().BeApproximately(actualInk.X, 0.1);
        paragraph.Runs.Should().Equal(tab, suffix);
        paragraph.PlainText.Should().Be("\tright");
    });

    public static IEnumerable<object[]> ParagraphEnds()
    {
        foreach (var footer in new[] { false, true })
        foreach (var text in new[] { "AAAA", "AAAA\nB", "AAAA\r\nB", "😀e\u0301" })
            yield return [footer, text];
    }

    [Theory]
    [MemberData(nameof(ParagraphEnds))]
    public Task ParagraphEndInsertionMatchesTheActualLastVisibleGlyph(bool footer, string text) => OnUi(() =>
    {
        var paragraph = new Paragraph();
        var source = new Run(text, Formatting);
        paragraph.Runs.Add(source);
        var view = View(footer, paragraph);
        var ink = TextInk(view);
        var lastLine = text.Contains('\n') ? ink.OrderBy(bounds => bounds.Y).Last()
            : ink.Aggregate((left, right) => left.Union(right));

        view.PlaceCaretInHeaderFooter(footer, 0, text.Length);

        var caret = view.HfCaretRectForTest!.Value;
        caret.X.Should().BeApproximately(lastLine.Right, 0.1);
        caret.Top.Should().BeLessThanOrEqualTo(lastLine.Top + 0.1);
        caret.Bottom.Should().BeGreaterThanOrEqualTo(lastLine.Bottom - 0.1);
        view.AutomationSnapshot().CaretOffset.Should().Be(text.Length);
        paragraph.Runs.Single().Should().BeSameAs(source);
        source.Text.Should().Be(text);
    });

    [Theory]
    [InlineData(false, "\n")]
    [InlineData(true, "\n")]
    [InlineData(false, "\r\n")]
    [InlineData(true, "\r\n")]
    public Task TrailingEmptyLineInsertionMatchesTheNextLineActualGlyphOrigin(bool footer, string separator) => OnUi(() =>
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("AAAA" + separator, Formatting));
        var referenceParagraph = new Paragraph();
        referenceParagraph.Runs.Add(new Run("AAAA" + separator + "B", Formatting));
        var view = View(footer, paragraph);
        var reference = View(footer, referenceParagraph);
        var expected = TextInk(reference).OrderBy(bounds => bounds.Y).Last();

        view.PlaceCaretInHeaderFooter(footer, 0, paragraph.PlainText.Length);

        var caret = view.HfCaretRectForTest!.Value;
        caret.X.Should().BeApproximately(expected.X, 0.1,
            "actual backend lines: {0}", string.Join("; ", view.HeaderFooterBackendLinesForTest()));
        caret.Y.Should().BeApproximately(expected.Y, 0.1);
        view.AutomationSnapshot().CaretOffset.Should().Be(4 + separator.Length);
        view.AutomationSnapshot().Text.Should().Be("AAAA" + separator);
        var backend = view.HeaderFooterBackendLinesForTest();
        backend.Should().HaveCount(2);
        backend[^1].Start.Should().Be(paragraph.PlainText.Length);
        backend[^1].ContainsCaret.Should().BeTrue();
        view.HitTestHeaderFooterForTest(new Point(expected.X + expected.Width / 2,
            expected.Y + expected.Height / 2)).Should().BeTrue();
        view.AutomationSnapshot().CaretOffset.Should().Be(paragraph.PlainText.Length,
            "the backend paragraph terminator must not become a model/native text offset");
        // Native context is offered only by a visible, attached editor. Keep the geometry proof
        // above independent, then use the same real window/focus contract as the IME fixtures.
        var window = new Window { Width = 816, Height = 1400, Content = view };
        try
        {
            window.Show();
            view.Focus();
            view.PlaceCaretInHeaderFooter(footer, 0, paragraph.PlainText.Length);
            var request = new TextInputMethodClientRequestedEventArgs
                { RoutedEvent = DocumentView.TextInputMethodClientRequestedEvent };
            view.RaiseEvent(request);
            request.Client.Should().NotBeNull();
            request.Client!.SurroundingText.Should().Be("AAAA" + separator);
            request.Client.Selection.Start.Should().Be(paragraph.PlainText.Length);
            request.Client.Selection.End.Should().Be(paragraph.PlainText.Length);
        }
        finally
        {
            window.Close();
        }
        paragraph.PlainText.Should().Be("AAAA" + separator);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task StyledLiteralParagraphKeepsEachRunFontAndColorInTheSharedFlow(bool footer) => OnUi(() =>
    {
        var first = new Run("A", Formatting);
        var green = Formatting with { FontSizePt = 24, Bold = false, Italic = true, ColorHex = "#226644" };
        var second = new Run("\nright", green);
        var paragraph = new Paragraph();
        paragraph.Runs.AddRange([first, second]);
        var view = View(footer, paragraph);
        view.HeaderFooterUsesSharedLiteralLayout(paragraph).Should().BeTrue();
        var painted = Paint(view).Where(item => item.Drawing is GlyphRunDrawing glyph
            && glyph.Foreground is ISolidColorBrush brush
            && (brush.Color == TextColor || brush.Color == Color.Parse("#226644"))).ToArray();
        var before = painted.Single(item => ((ISolidColorBrush)((GlyphRunDrawing)item.Drawing).Foreground!).Color == TextColor);
        var after = painted.Single(item => ((ISolidColorBrush)((GlyphRunDrawing)item.Drawing).Foreground!).Color == Color.Parse("#226644"));
        var beforeBounds = before.Drawing.GetBounds().TransformToAABB(before.Transform);
        var afterBounds = after.Drawing.GetBounds().TransformToAABB(after.Transform);

        ((GlyphRunDrawing)before.Drawing).GlyphRun!.FontRenderingEmSize.Should().BeApproximately(24, 0.01);
        ((GlyphRunDrawing)after.Drawing).GlyphRun!.FontRenderingEmSize.Should().BeApproximately(32, 0.01);
        afterBounds.X.Should().BeApproximately(beforeBounds.X, 0.1);
        afterBounds.Y.Should().BeGreaterThan(beforeBounds.Y);
        view.HitTestHeaderFooterForTest(new Point(afterBounds.X + 0.01, afterBounds.Y + afterBounds.Height / 2))
            .Should().BeTrue();
        view.AutomationSnapshot().CaretOffset.Should().Be(2);
        paragraph.Runs.Should().Equal(first, second);
        second.Formatting.Should().BeSameAs(green);
        paragraph.PlainText.Should().Be("A\nright");
    });

    private static void Press(DocumentView view, Key key, bool shift = false)
    {
        var args = new KeyEventArgs
        {
            RoutedEvent = DocumentView.KeyDownEvent,
            Key = key,
            KeyModifiers = shift ? KeyModifiers.Shift : KeyModifiers.None,
        };
        view.RaiseKeyDownForContextMenuTests(args);
        args.Handled.Should().BeTrue();
    }

    private static DocumentView View(bool footer, params Paragraph[] paragraphs)
    {
        var document = TextDocument.CreateEmpty();
        var story = new HeaderFooter();
        story.Paragraphs.AddRange(paragraphs);
        if (footer) document.FinalSectionHeadersFooters.Footer = story;
        else document.FinalSectionHeadersFooters.Header = story;
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 1400));
        view.Arrange(new Rect(0, 0, 816, 1400));
        return view;
    }

    private static HeaderFooter Story(DocumentView view, bool footer) => footer
        ? view.Document.FinalSectionHeadersFooters.Footer! : view.Document.FinalSectionHeadersFooters.Header!;

    // Read the actual Render output, including nested drawing transforms, rather than independently
    // measuring a prefix with a second font/layout implementation.
    private static IEnumerable<(Drawing Drawing, Matrix Transform)> Paint(DocumentView view)
    {
        var group = new DrawingGroup();
        using (var context = group.Open()) view.Render(context);
        return Flatten(group, Matrix.Identity).ToArray();
    }

    private static IEnumerable<(Drawing Drawing, Matrix Transform)> Flatten(DrawingGroup group, Matrix parent)
    {
        var transform = (group.Transform?.Value ?? Matrix.Identity) * parent;
        foreach (var child in group.Children)
            if (child is DrawingGroup nested)
                foreach (var item in Flatten(nested, transform)) yield return item;
            else yield return (child, transform);
    }

    private static double RenderedTextStart(DocumentView view, string text)
    {
        var item = Paint(view).Single(item => item.Drawing is GlyphRunDrawing glyph
            && glyph.Foreground is ISolidColorBrush brush && brush.Color == TextColor
            && glyph.GlyphRun!.Characters.Span.ToString().Contains(text, StringComparison.Ordinal));
        var run = ((GlyphRunDrawing)item.Drawing).GlyphRun!;
        var index = run.Characters.Span.ToString().IndexOf(text, StringComparison.Ordinal);
        var distance = run.GetDistanceFromCharacterHit(new CharacterHit(index));
        return new Rect(run.BaselineOrigin.X + distance, run.Bounds.Y, 0, run.Bounds.Height)
            .TransformToAABB(item.Transform).X;
    }

    private static Rect[] TextInk(DocumentView view) => Paint(view)
        .Where(item => item.Drawing is GlyphRunDrawing glyph
            && glyph.Foreground is ISolidColorBrush brush && brush.Color == TextColor)
        .Select(item => item.Drawing.GetBounds().TransformToAABB(item.Transform)).ToArray();

    private static Task OnUi(Action action) => Session.Dispatch(action, CancellationToken.None);
}
