using System.Globalization;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Free.Shared.Pdf;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using DrawingGroup = Avalonia.Media.DrawingGroup;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewBodyTextSpanTests
{
    private static readonly HeadlessUnitTestSession Session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);
    private static readonly string[][] SourcePieceCases =
    {
        new[] { "a😀b" }, new[] { "ae\u0301b" }, new[] { "a👩‍💻b" },
        new[] { "a\uD83D", "\uDE00b" }, new[] { "a\uD83D", "", "\uDE00b" }, new[] { "ae", "\u0301b" }, new[] { "a👩", "\u200d", "💻b" },
    };
    // xUnit serializes theory arguments. Unpaired source pieces must be created after
    // transport, otherwise its string serializer replaces the surrogate halves.
    public static IEnumerable<object[]> SourcePieces => Enumerable.Range(0, SourcePieceCases.Length).Select(index => new object[] { index });

    [Theory]
    [MemberData(nameof(SourcePieces))]
    public Task RealConsumerShapesCompleteTextAndKeepsSourceRunPieces(int caseIndex) => Session.Dispatch(() =>
    {
        var pieces = SourcePieceCases[caseIndex];
        var paragraph = Paragraph(pieces);
        var originals = paragraph.Runs.ToArray();
        var view = View(paragraph);
        view.BodyUsesShapedLiteralText(paragraph).Should().BeTrue();
        var text = string.Concat(pieces);
        var group = new DrawingGroup(); using (var context = group.Open()) view.Render(context);
        var strings = GlyphText(group).ToArray();
        strings.Should().NotBeEmpty();
        strings.Should().OnlyContain(value => Valid(value), "the actual body painter must never submit a surrogate half");
        paragraph.PlainText.Should().Be(text);
        paragraph.Runs.Should().Equal(originals);
        string.Concat(view.BuildPdfContent().Pages.SelectMany(page => page.Ops).OfType<PdfText>().Select(op => op.Text)).Should().Be(text);
        var stops = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
        var line = view.BodyTextLinesForTest.Should().ContainSingle().Which;
        for (var index = 0; index <= 32; index++)
        {
            view.HitBodyTextForTest(new Point(line.Bounds.X + line.Bounds.Width * index / 32, line.Bounds.Y + line.Bounds.Height / 2), out var offset).Should().BeTrue();
            offset.Should().BeOneOf(stops);
        }
    }, CancellationToken.None);

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\v")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public Task ExplicitSoftBreaksKeepRawOffsetsAndTrailingEmptyLine(string separator) => Session.Dispatch(() =>
    {
        var text = "a😀" + separator + separator + "b" + separator;
        var paragraph = Paragraph([text]);
        var view = View(paragraph);
        var lines = view.BodyTextLinesForTest;
        lines.Should().HaveCount(4);
        view.MoveCaretToBlockForTest(0, text.Length);
        var caretValue = view.CaretRectForTest;
        caretValue.Should().NotBeNull();
        var caret = caretValue!.Value;
        caret.Y.Should().BeApproximately(lines[^1].Bounds.Y, 0.01);
        view.AutomationSnapshot().CaretOffset.Should().Be(text.Length);
        paragraph.PlainText.Should().Be(text);
        var exported = string.Concat(view.BuildPdfContent().Pages.SelectMany(page => page.Ops).OfType<PdfText>().Select(op => op.Text));
        exported.Should().Be("a😀b");
    }, CancellationToken.None);

    [Fact]
    public Task NarrowWrappingNeverSplitsTextElementsAndPreservesTypingUndo() => Session.Dispatch(() =>
    {
        var paragraph = Paragraph(["a👩‍💻b a😀b ae\u0301b"]);
        var source = paragraph.Runs[0];
        var view = View(paragraph, narrow: true);
        var text = paragraph.PlainText;
        var stops = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
        view.BodyTextLinesForTest.Should().HaveCountGreaterThan(1);
        foreach (var line in view.BodyTextLinesForTest)
        {
            line.Start.Should().BeOneOf(stops);
            Math.Min(text.Length, line.Start + line.Length).Should().BeOneOf(stops);
        }
        view.MoveCaretToBlockForTest(0, 6);
        view.InsertText("X");
        paragraph.PlainText.Should().Be("a👩‍💻Xb a😀b ae\u0301b");
        view.Undo();
        paragraph.Runs.Should().ContainSingle().Which.Should().BeSameAs(source);
        view.AutomationSnapshot().CaretOffset.Should().Be(6);
    }, CancellationToken.None);

    [Fact]
    public Task AsciiBaselineAndUnsupportedRichParagraphsDoNotClaimTheNewPath() => Session.Dispatch(() =>
    {
        var ascii = Paragraph(["AV To baseline"]);
        var view = View(ascii);
        view.BodyUsesShapedLiteralText(ascii).Should().BeFalse();
        var rich = Paragraph(["a😀b"]);
        rich.Runs.Add(Run.DateField("date"));
        view.LoadDocument(TextDocument.CreateEmpty());
        view.BodyUsesShapedLiteralText(rich).Should().BeFalse();
    }, CancellationToken.None);

    [Theory]
    [InlineData("surrogate-style")]
    [InlineData("combining-style")]
    [InlineData("nested-revision")]
    public Task UnsupportedGraphemeStyleOrRevisionDoesNotClaimTheLiteralPath(string kind) => Session.Dispatch(() =>
    {
        var view = View(Paragraph(["ASCII"]));
        var paragraph = Paragraph(kind == "combining-style" ? ["ae", "\u0301b"] : ["a\uD83D", "\uDE00b"]);
        if (kind == "nested-revision") paragraph.Runs[0].NestedRevision = new NestedRevisionInfo(RevisionKind.Inserted, "Editor", null);
        else paragraph.Runs[1].Formatting = paragraph.Runs[1].Formatting with { Bold = true };
        var originals = paragraph.Runs.ToArray();
        view.BodyUsesShapedLiteralText(paragraph).Should().BeFalse();
        paragraph.Runs.Should().Equal(originals);
    }, CancellationToken.None);

    [Fact]
    public Task WrappedTrailingHitHomeEndAndHistoryKeepTheActualVisualLine() => Session.Dispatch(() =>
    {
        var paragraph = Paragraph(["a😀b"]);
        var view = View(paragraph, narrow: true);
        var first = view.BodyTextLinesForTest[0];
        var edge = first.Start + first.Length;
        view.PlaceBodyHitForTest(new Point(first.Bounds.Right - 0.01, first.Bounds.Y + first.Bounds.Height / 2)).Should().BeTrue();
        view.AutomationSnapshot().CaretOffset.Should().Be(edge);
        view.CaretRectForTest!.Value.Y.Should().BeApproximately(first.Bounds.Y, 0.01);
        Send(Key.Home);
        view.AutomationSnapshot().CaretOffset.Should().Be(0);
        Send(Key.End);
        view.AutomationSnapshot().CaretOffset.Should().Be(edge);
        view.CaretRectForTest!.Value.Y.Should().BeApproximately(first.Bounds.Y, 0.01);
        view.InsertText("X");
        paragraph.PlainText.Should().Be("a😀b".Insert(edge, "X"));
        view.Undo();
        Layout();
        paragraph.PlainText.Should().Be("a😀b");
        view.AutomationSnapshot().CaretOffset.Should().Be(edge);
        view.CaretRectForTest!.Value.Y.Should().BeApproximately(first.Bounds.Y, 0.01);
        view.Redo();
        Layout();
        view.AutomationSnapshot().CaretOffset.Should().Be(edge + 1);
        view.CaretRectForTest!.Value.Y.Should().BeGreaterThan(first.Bounds.Y);

        void Send(Key key) => view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        void Layout() { view.Measure(new Size(816, 4000)); view.Arrange(new Rect(0, 0, 816, 4000)); }
    }, CancellationToken.None);

    [Fact]
    public Task ReflowDiscardsAnAffinityWhoseSourceLineChanged() => Session.Dispatch(() =>
    {
        var paragraph = Paragraph(["a😀b"]);
        var view = View(paragraph, narrow: true);
        var first = view.BodyTextLinesForTest[0];
        var edge = first.Start + first.Length;
        view.PlaceBodyHitForTest(new Point(first.Bounds.Right - 0.01, first.Bounds.Y + first.Bounds.Height / 2)).Should().BeTrue();
        paragraph.Formatting = paragraph.Formatting with { IndentLeftPt = 0 };
        Refresh();
        _ = view.CaretRectForTest; // Validate against the wider line, whose source end changed.
        paragraph.Formatting = paragraph.Formatting with { IndentLeftPt = 45 };
        Refresh();
        view.AutomationSnapshot().CaretOffset.Should().Be(edge);
        view.CaretRectForTest!.Value.Y.Should().BeApproximately(view.BodyTextLinesForTest[1].Bounds.Y, 0.01);
        // Explicit independent placement at the same source offset uses the canonical next line.
        view.MoveCaretToBlockForTest(0, edge);
        view.CaretRectForTest!.Value.Y.Should().BeApproximately(view.BodyTextLinesForTest[1].Bounds.Y, 0.01);
        void Refresh() { view.InvalidateAfterExternalMutation(); view.Measure(new Size(816, 4000)); view.Arrange(new Rect(0, 0, 816, 4000)); }
    }, CancellationToken.None);

    [Theory]
    [InlineData("a😀\U0001E900b")]
    [InlineData("a😀\uFB1Db")]
    [InlineData("a😀\u200Fb")]
    public Task DirectionalScriptsOutsideTheBasicHebrewArabicRangeRetainTheirSeparatePath(string text) => Session.Dispatch(() =>
    {
        var paragraph = Paragraph([text]);
        var view = View(paragraph);
        view.BodyUsesShapedLiteralText(paragraph).Should().BeFalse();
        paragraph.PlainText.Should().Be(text);
    }, CancellationToken.None);

    [Fact]
    public Task WrappedSelectionAndRunBackgroundPaintOnlyIntersectingSourceLines() => Session.Dispatch(() =>
    {
        var paragraph = Paragraph(["a😀b"]);
        var view = View(paragraph, narrow: true);
        var text = paragraph.PlainText;
        var first = view.BodyTextLinesForTest[0];
        var edge = first.Start + first.Length;
        paragraph.Runs.Clear();
        paragraph.Runs.Add(new Run(text[..edge], RunFormatting.Default with { FontFamily = "Arial", FontSizePt = 24, HighlightColorHex = "#FFFF00" }));
        paragraph.Runs.Add(new Run(text[edge..], RunFormatting.Default with { FontFamily = "Arial", FontSizePt = 24 }));
        view.InvalidateAfterExternalMutation();
        view.Measure(new Size(816, 4000)); view.Arrange(new Rect(0, 0, 816, 4000));
        view.SetBodySelectionForTest(0, 0, 0, edge);
        view.SelectedText.Should().Be(text[..edge]);
        var drawing = new DrawingGroup(); using (var context = drawing.Open()) view.Render(context);
        var selection = Rectangles(drawing, Color.FromArgb(0x55, 0x33, 0x99, 0xFF)).ToArray();
        var shading = Rectangles(drawing, Colors.Yellow).ToArray();
        selection.Should().NotBeEmpty(); shading.Should().NotBeEmpty();
        foreach (var line in view.BodyTextLinesForTest)
        {
            var intersects = Math.Max(0, line.Start) < Math.Min(edge, line.Start + line.Length);
            selection.Any(rect => Math.Abs(rect.Y - line.Bounds.Y) < 0.01).Should().Be(intersects);
            shading.Any(rect => Math.Abs(rect.Y - line.Bounds.Y) < 0.01).Should().Be(intersects);
        }
        paragraph.PlainText.Should().Be(text);
        static IEnumerable<Rect> Rectangles(DrawingGroup group, Color color) => group.Children.SelectMany(item => item switch
        {
            DrawingGroup child => Rectangles(child, color),
            GeometryDrawing { Brush: ISolidColorBrush brush, Geometry: { } geometry } when brush.Color == color => new[] { geometry.Bounds },
            _ => [],
        });
    }, CancellationToken.None);

    [Theory]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public Task VerticalNavigationUsesAdjacentShapedLinesRatherThanTheVisualCaretHeight(double spacing) => Session.Dispatch(() =>
    {
        var paragraph = Paragraph(["a😀b a😀b a😀b"]);
        paragraph.Formatting = paragraph.Formatting with { LineRule = LineSpacingRule.Multiple, LineSpacing = spacing };
        var view = View(paragraph, narrow: true);
        var lines = view.BodyTextLinesForTest;
        lines.Should().HaveCountGreaterThan(2);
        view.PlaceBodyHitForTest(new Point(lines[0].Bounds.X + 0.01, lines[0].Bounds.Y + lines[0].Bounds.Height / 2)).Should().BeTrue();
        var x = view.CaretRectForTest!.Value.X;
        foreach (var (key, index) in new[] { (Key.Down, 1), (Key.Down, 2), (Key.Up, 1), (Key.Up, 0) })
        {
            view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
            view.CaretPositionForTest.Offset.Should().Be(lines[index].Start);
            view.CaretRectForTest!.Value.Y.Should().BeApproximately(lines[index].Bounds.Y, 0.01);
            view.CaretRectForTest!.Value.X.Should().BeApproximately(x, 0.01);
        }
    }, CancellationToken.None);

    [Theory]
    [InlineData(24)]
    [InlineData(-24)]
    public Task WrappedVerticalNavigationPreservesColumnXAcrossFirstAndHangingIndent(double indent) => Session.Dispatch(() =>
    {
        var paragraph = Paragraph(["a😀b a😀b a😀b"]);
        paragraph.Formatting = paragraph.Formatting with { FirstLineIndentPt = indent, LineRule = LineSpacingRule.Multiple, LineSpacing = 2 };
        var view = View(paragraph, narrow: true);
        var lines = view.BodyTextLinesForTest;
        lines.Should().HaveCountGreaterThan(1);
        var click = indent < 0 ? lines[0].Bounds.Right - 0.01 : lines[0].Bounds.X + 0.01;
        view.PlaceBodyHitForTest(new Point(click, lines[0].Bounds.Y + lines[0].Bounds.Height / 2)).Should().BeTrue();
        var before = view.CaretPositionForTest;
        var x = view.CaretRectForTest!.Value.X;
        view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
        view.CaretRectForTest!.Value.Y.Should().BeApproximately(lines[1].Bounds.Y, 0.01);
        view.CaretRectForTest!.Value.X.Should().BeApproximately(x, 0.01);
        view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Up });
        view.CaretPositionForTest.Should().Be(before);
        view.CaretRectForTest!.Value.Y.Should().BeApproximately(lines[0].Bounds.Y, 0.01);
        view.CaretRectForTest!.Value.X.Should().BeApproximately(x, 0.01);
    }, CancellationToken.None);

    [Fact]
    public Task ShapedColumnCannotStealEqualHeightConventionalOrCellHitsInTheOtherColumn() => Session.Dispatch(() =>
    {
        (DocumentView View, Paragraph Shaped, Paragraph Plain, Paragraph Cell, Rect Glyph, Rect CellCaret)? fixture = null;
        // Build the required physical collision using the active font metrics rather than guessing
        // how many explicit lines fill a column on every supported headless/native backend.
        foreach (var count in Enumerable.Range(7, 35))
        {
            var shaped = Paragraph([string.Join("\n", Enumerable.Repeat("a😀b", count))]);
            var plain = Paragraph(["conventional"]);
            plain.Runs[0].Formatting = RunFormatting.Default with { FontFamily = "Arial", FontSizePt = 11 };
            var table = Table.Create(1, 1);
            var cell = table.Rows[0].Cells[0].Paragraphs[0];
            cell.Runs.Clear(); cell.Runs.Add(new Run("cell"));
            var document = TextDocument.CreateEmpty(); document.Blocks.Clear();
            document.Blocks.Add(shaped); document.Blocks.Add(plain); document.Blocks.Add(table);
            document.Page.WidthPt = 360; document.Page.HeightPt = 220;
            document.Page.MarginLeftPt = document.Page.MarginRightPt = document.Page.MarginTopPt = document.Page.MarginBottomPt = 20;
            document.Page.ColumnCount = 2; document.Page.ColumnSpacingPt = 18;
            var candidate = new DocumentView(); candidate.LoadDocument(document);
            candidate.Measure(new Size(816, 4000)); candidate.Arrange(new Rect(0, 0, 816, 4000));
            var column = candidate.LayoutColumnBand(1);
            var glyph = candidate.ReviewGlyphsForTest.First(item => item.Block == 1).Rect;
            candidate.PlaceCaretInCell(2, 0, 0, 0, 0);
            var target = candidate.CaretRectForTest!.Value;
            if (glyph.X >= column.Left && target.X >= column.Left
                && candidate.BodyTextLinesForTest.Any(item => item.Bounds.X < column.Left && glyph.Y >= item.Bounds.Y && glyph.Y < item.Bounds.Bottom)
                && candidate.BodyTextLinesForTest.Any(item => item.Bounds.X < column.Left && target.Y >= item.Bounds.Y && target.Y < item.Bounds.Bottom))
            { fixture = (candidate, shaped, plain, cell, glyph, target); break; }
            candidate.LoadDocument(TextDocument.CreateEmpty());
        }
        fixture.Should().NotBeNull("the fixture must have real same-Y ownership collisions in both columns");
        var (view, body, conventional, cellParagraph, plainGlyph, cellCaret) = fixture!.Value;
        var originalBody = body.PlainText;
        view.MoveCaretToBlockForTest(0, 0);
        view.PlaceBodyHitForTest(new Point(plainGlyph.X + 0.01, plainGlyph.Y + plainGlyph.Height / 2)).Should().BeTrue();
        view.CaretPositionForTest.Block.Should().Be(1);
        view.InsertText("X"); conventional.PlainText.Should().Be("Xconventional");
        view.MoveCaretToBlockForTest(0, 0);
        view.PlaceBodyHitForTest(new Point(cellCaret.X + 0.01, cellCaret.Y + cellCaret.Height / 2)).Should().BeTrue();
        view.CaretPositionForTest.Block.Should().Be(2);
        view.InsertText("X"); cellParagraph.PlainText.Should().Be("Xcell");
        body.PlainText.Should().Be(originalBody);
    }, CancellationToken.None);

    [Theory]
    [InlineData("A😀B", 2)]
    [InlineData("A😀B", -2)]
    [InlineData("Ae\u035CB", 2)]
    [InlineData("Ae\u035CB", -2)]
    [InlineData("A👩‍💻B", 2)]
    [InlineData("A👩‍💻B", -2)]
    public Task UniformTrackingUsesWholeShapedTextElements(string text, double tracking) => Session.Dispatch(() =>
    {
        var untracked = View(Paragraph([text]));
        untracked.MoveCaretToBlockForTest(0, text.Length);
        var normalEnd = untracked.CaretRectForTest!.Value.X;
        var paragraph = Paragraph([text]);
        paragraph.Runs[0].Formatting = paragraph.Runs[0].Formatting with { CharacterSpacingPt = tracking };
        var view = View(paragraph);
        view.BodyUsesShapedLiteralText(paragraph).Should().BeTrue();
        view.MoveCaretToBlockForTest(0, text.Length);
        (view.CaretRectForTest!.Value.X - normalEnd).Should().BeApproximately(3 * tracking * 4 / 3, 0.01);
        paragraph.PlainText.Should().Be(text);
    }, CancellationToken.None);

    [Fact]
    public Task MixedTrackingAndAuthoredLigaturesRetainTheirSeparateLayoutPolicy() => Session.Dispatch(() =>
    {
        var view = View(Paragraph(["ASCII"]));
        var mixed = Paragraph(["a😀", "b"]);
        mixed.Runs[0].Formatting = mixed.Runs[0].Formatting with { CharacterSpacingPt = 2 };
        view.BodyUsesShapedLiteralText(mixed).Should().BeFalse();
        var ligature = Paragraph(["affi😀b"]);
        ligature.Runs[0].Formatting = ligature.Runs[0].Formatting with { CharacterSpacingPt = 2, Ligatures = LigatureMode.Standard };
        view.BodyUsesShapedLiteralText(ligature).Should().BeFalse();
    }, CancellationToken.None);

    [Theory]
    [InlineData(2)]
    [InlineData(-2)]
    public Task TrackedNarrowWrapsKeepRawGraphemeBoundaries(double tracking) => Session.Dispatch(() =>
    {
        var text = "a😀b ae\u035Cb a👩‍💻b";
        var paragraph = Paragraph([text]);
        paragraph.Runs[0].Formatting = paragraph.Runs[0].Formatting with { CharacterSpacingPt = tracking };
        var view = View(paragraph, narrow: true);
        var stops = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
        view.BodyTextLinesForTest.Should().HaveCountGreaterThan(1);
        foreach (var line in view.BodyTextLinesForTest)
        {
            line.Start.Should().BeOneOf(stops);
            Math.Min(text.Length, line.Start + line.Length).Should().BeOneOf(stops);
        }
        paragraph.PlainText.Should().Be(text);
    }, CancellationToken.None);

    [Theory]
    [InlineData(2)]
    [InlineData(-2)]
    [InlineData(-100)]
    public Task TrackedPdfKeepsWholeSourceElementsStylesAndBackendInsertionPositions(double tracking) => Session.Dispatch(() =>
    {
        var paragraph = Paragraph(["a", "😀\r\n\r\n", "be\u035C\n"]);
        foreach (var run in paragraph.Runs) run.Formatting = run.Formatting with { CharacterSpacingPt = tracking };
        paragraph.Runs[2].Formatting = paragraph.Runs[2].Formatting with { Bold = true, ColorHex = "#9B2226" };
        var originals = paragraph.Runs.ToArray();
        var text = paragraph.PlainText;
        var view = View(paragraph);
        var exported = view.BuildPdfContent().Pages.SelectMany(page => page.Ops).OfType<PdfText>().ToArray();
        exported.Select(item => item.Text).Should().Equal("a", "😀", "b", "e\u035C");
        exported.Should().OnlyContain(item => Valid(item.Text) && !item.Text.Any(char.IsControl));
        string.Concat(exported.Select(item => item.Text)).Should().Be("a😀be\u035C");
        var starts = StringInfo.ParseCombiningCharacters(text).Where(start => text[start] is not ('\r' or '\n')).ToArray();
        view.MoveCaretToBlockForTest(0, 0); var firstX = view.CaretRectForTest!.Value.X;
        for (var index = 0; index < exported.Length; index++)
        {
            view.MoveCaretToBlockForTest(0, starts[index]);
            ((exported[index].X - exported[0].X) * 4 / 3).Should().BeApproximately(view.CaretRectForTest!.Value.X - firstX, 0.01);
            exported[index].FontFamily.Should().NotBeNullOrEmpty();
            exported[index].Face.Should().Be(index < 2 ? PdfFontFace.Regular : PdfFontFace.Bold);
            var source = index < 2 ? paragraph.Runs[index] : paragraph.Runs[2];
            exported[index].Color.Should().Be(index < 2 ? exported[0].Color : exported[2].Color);
            source.Formatting.CharacterSpacingPt.Should().Be(tracking);
        }
        exported[0].Color.Should().NotBe(exported[2].Color);
        paragraph.Runs.Should().Equal(originals); paragraph.PlainText.Should().Be(text);
    }, CancellationToken.None);

    private static Paragraph Paragraph(IEnumerable<string> pieces)
    {
        var paragraph = new Paragraph();
        foreach (var piece in pieces) paragraph.Runs.Add(new Run(piece, RunFormatting.Default with { FontFamily = "Arial", FontSizePt = 24, ColorHex = "#172554", Bold = piece.Length == 0 }));
        return paragraph;
    }
    private static DocumentView View(Paragraph paragraph, bool narrow = false)
    {
        var document = TextDocument.CreateEmpty(); document.Blocks.Clear(); document.Blocks.Add(paragraph);
        if (narrow)
        {
            document.Page.WidthPt = 240; document.Page.MarginLeftPt = document.Page.MarginRightPt = 75;
            paragraph.Formatting = paragraph.Formatting with { IndentLeftPt = 45 };
        }
        var view = new DocumentView(); view.LoadDocument(document);
        view.Measure(new Size(816, 4000)); view.Arrange(new Rect(0, 0, 816, 4000));
        return view;
    }
    private static IEnumerable<string> GlyphText(DrawingGroup group) => group.Children.SelectMany(drawing => drawing switch
    {
        DrawingGroup child => GlyphText(child), GlyphRunDrawing glyph => new[] { glyph.GlyphRun!.Characters.Span.ToString() }, _ => [],
    });
    private static bool Valid(string text)
    {
        for (var index = 0; index < text.Length; index++)
            if (char.IsHighSurrogate(text[index])) { if (++index >= text.Length || !char.IsLowSurrogate(text[index])) return false; }
            else if (char.IsLowSurrogate(text[index])) return false;
        return true;
    }
}
