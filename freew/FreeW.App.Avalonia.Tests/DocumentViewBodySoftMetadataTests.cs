using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Free.Shared.Pdf;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using Xunit.Abstractions;
using DrawingGroup = Avalonia.Media.DrawingGroup;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewBodySoftMetadataTests
{
    private static readonly HeadlessUnitTestSession Session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);
    private static readonly RunFormatting Format = RunFormatting.Default with { FontFamily = "Arial", FontSizePt = 24 };
    private static readonly Lazy<string> ApplicationIdentity = new(() =>
    {
        var assembly = typeof(DocumentView).Assembly;
        return $"Loaded application: {assembly.Location}; SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location)))}; Version={assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}";
    });

    public DocumentViewBodySoftMetadataTests(ITestOutputHelper output) => output.WriteLine(ApplicationIdentity.Value);

    [Theory]
    [InlineData("a😀b")]
    [InlineData("ae\u035Cb")]
    [InlineData("a👩‍💻b")]
    public Task PublicHyperlinkKeepsWholePaintedTextAndUndoRedo(string text) => Session.Dispatch(() =>
    {
        var paragraph = new Paragraph(); paragraph.Runs.Add(new Run(text, Format));
        var view = View(paragraph);
        view.SetBodySelectionForTest(0, 0, 0, text.Length);
        view.InsertHyperlink(text, "https://example.invalid");
        Verify();
        view.Undo(); Layout(view);
        paragraph.PlainText.Should().Be(text);
        paragraph.Runs.Should().OnlyContain(run => run.HyperlinkUrl == null);
        view.Redo(); Layout(view); Verify();

        void Verify()
        {
            Layout(view);
            view.BodyUsesShapedLiteralText(paragraph).Should().BeTrue();
            var originals = paragraph.Runs.ToArray();
            var glyphs = Glyphs(Render(view)).ToArray();
            glyphs.Should().NotBeEmpty();
            glyphs.Select(glyph => glyph.GlyphRun!.Characters.Span.ToString()).Should().OnlyContain(value => Valid(value), "the real painter must not submit isolated surrogate halves");
            glyphs.Where(glyph => glyph.Foreground is ISolidColorBrush brush && brush.Color == Color.Parse("#0563C1")).Should().NotBeEmpty();
            paragraph.PlainText.Should().Be(text);
            paragraph.Runs.Should().Equal(originals);
            var pdf = view.BuildPdfContent();
            var ops = pdf.Pages.SelectMany(page => page.Ops).OfType<PdfText>().ToArray();
            string.Concat(ops.Select(op => op.Text)).Should().Be(text);
            ops.Should().OnlyContain(op => Valid(op.Text) && op.Color == new PdfColor(5, 99, 193));
            pdf.Pages.SelectMany(page => page.LinkOverlays ?? []).Should().ContainSingle().Which.Uri.Should().Be("https://example.invalid");
            pdf.Pages.SelectMany(page => page.Ops).OfType<PdfLine>().Should().NotBeEmpty();
        }
    }, CancellationToken.None);

    [Theory]
    [InlineData("a😀b")]
    [InlineData("ae\u035Cb")]
    [InlineData("a👩‍💻b")]
    public Task PublicCommentRetainsReferenceOneGraphemeAnchorAndReviewTint(string text) => Session.Dispatch(() =>
    {
        var paragraph = new Paragraph(); paragraph.Runs.Add(new Run(text, Format));
        var view = View(paragraph);
        var stops = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
        view.SetBodySelectionForTest(0, stops[1], 0, stops[2]);
        var id = view.AddComment("Synthetic note", "Reviewer", "R");
        id.Should().NotBeNull(); Layout(view);
        view.BodyUsesShapedLiteralText(paragraph).Should().BeTrue();
        var originals = paragraph.Runs.ToArray();
        paragraph.Runs.Should().ContainSingle(run => run.IsCommentReference && run.Text.Length == 0 && run.CommentId == id);
        var anchors = view.CommentAnchorGlyphs();
        anchors.Should().ContainSingle().Which.CommentId.Should().Be(id!.Value);
        view.MoveCaretToBlockForTest(0, text.Length);
        var active = Rectangles(Render(view), Color.FromArgb(0x33, 0xFF, 0xC1, 0x07)).ToArray();
        active.Should().ContainSingle();
        active[0].X.Should().BeApproximately(anchors[0].Rect.X, 0.01);
        active[0].Width.Should().BeApproximately(anchors[0].Rect.Width, 0.01);
        Glyphs(Render(view)).Select(glyph => glyph.GlyphRun!.Characters.Span.ToString()).Should().OnlyContain(value => Valid(value));
        view.Undo(); Layout(view);
        view.Document.Comments.Should().NotContainKey(id.Value);
        paragraph.PlainText.Should().Be(text);
        view.Redo(); Layout(view);
        view.Document.Comments.Should().ContainKey(id.Value);
        view.BodyUsesShapedLiteralText(paragraph).Should().BeTrue();
        originals = paragraph.Runs.ToArray();
        view.SetCommentResolved(id.Value, true).Should().BeTrue(); Layout(view);
        Rectangles(Render(view), Color.FromArgb(0x33, 0xFF, 0xC1, 0x07)).Should().BeEmpty();
        Rectangles(Render(view), Color.FromArgb(0x1F, 0x9E, 0x9E, 0x9E)).Should().ContainSingle();
        view.ApplyShowMarkupComments(false); Layout(view);
        Rectangles(Render(view), Color.FromArgb(0x1F, 0x9E, 0x9E, 0x9E)).Should().BeEmpty();
        view.CommentHighlightGlyphsForTest.Should().BeEmpty();
        paragraph.Runs.Should().Equal(originals);
        paragraph.PlainText.Should().Be(text);
        string.Concat(view.BuildPdfContent().Pages.SelectMany(page => page.Ops).OfType<PdfText>().Select(op => op.Text)).Should().Be(text);
    }, CancellationToken.None);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public Task EqualSoftMetadataAcrossSplitSourceGraphemeIsShapedWithoutRewritingRuns(int caseId) => Session.Dispatch(() =>
    {
        var pieces = caseId switch { 0 => new[] { "a\uD83D", "\uDE00b" }, 1 => new[] { "ae", "\u035Cb" }, _ => new[] { "a👩", "\u200D", "💻b" } };
        var paragraph = new Paragraph();
        foreach (var piece in pieces) paragraph.Runs.Add(new Run(piece, Format)
        { HyperlinkUrl = "https://same.invalid", HyperlinkTooltip = "Same target", CommentId = 17 });
        var originals = paragraph.Runs.ToArray(); var text = string.Concat(pieces);
        var view = View(paragraph);
        view.BodyUsesShapedLiteralText(paragraph).Should().BeTrue();
        Glyphs(Render(view)).Select(glyph => glyph.GlyphRun!.Characters.Span.ToString()).Should().OnlyContain(value => Valid(value));
        var anchors = view.CommentAnchorGlyphs();
        anchors.Should().HaveCount(StringInfo.ParseCombiningCharacters(text).Length);
        var overlays = view.BuildPdfContent().Pages.SelectMany(page => page.LinkOverlays ?? []).ToArray();
        overlays.Should().ContainSingle().Which.Tooltip.Should().Be("Same target");
        var tint = Rectangles(Render(view), Color.FromArgb(0x33, 0xFF, 0xC1, 0x07)).ToArray();
        tint.Should().ContainSingle("split raw runs with the same comment must not double-composite the tint");
        paragraph.Runs.Should().Equal(originals); paragraph.PlainText.Should().Be(text);
    }, CancellationToken.None);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TextlessCommentReferenceHasNoVisibleStyleOrGlyph(bool atStart) => Session.Dispatch(() =>
    {
        var paragraph = new Paragraph(); var source = new Run("a😀b", Format);
        var marker = Run.CommentReference(17);
        marker.Formatting = RunFormatting.Default with { FontSizePt = 8, VerticalAlign = VerticalAlign.Superscript };
        if (atStart) paragraph.Runs.Add(marker);
        paragraph.Runs.Add(source);
        if (!atStart) paragraph.Runs.Add(marker);
        var originals = paragraph.Runs.ToArray();
        var view = View(paragraph);
        view.BodyUsesShapedLiteralText(paragraph).Should().BeTrue();
        Glyphs(Render(view)).Select(glyph => glyph.GlyphRun!.Characters.Span.ToString()).Should().OnlyContain(value => Valid(value));
        view.MoveCaretToBlockForTest(0, 4);
        view.CaretRectForTest!.Value.Height.Should().BeGreaterThanOrEqualTo(24);
        string.Concat(view.BuildPdfContent().Pages.SelectMany(page => page.Ops).OfType<PdfText>().Select(op => op.Text)).Should().Be("a😀b");
        paragraph.Runs.Should().Equal(originals);
    }, CancellationToken.None);

    [Fact]
    public Task ACommentReferenceCarryingVisibleTextIsNotAdmittedAsATextlessMarker() => Session.Dispatch(() =>
    {
        var paragraph = new Paragraph(); paragraph.Runs.Add(new Run("a😀b", Format));
        var marker = Run.CommentReference(17); marker.Text = "x"; paragraph.Runs.Add(marker);
        var view = View(new Paragraph("ASCII"));
        view.BodyUsesShapedLiteralText(paragraph).Should().BeFalse();
        paragraph.PlainText.Should().Be("a😀bx");
    }, CancellationToken.None);

    [Theory]
    [InlineData("url")]
    [InlineData("anchor")]
    [InlineData("tooltip")]
    [InlineData("comment")]
    [InlineData("comment-reference")]
    public Task SemanticTransitionInsideGraphemeRetainsTheSeparateRichPath(string difference) => Session.Dispatch(() =>
    {
        var paragraph = new Paragraph();
        var left = new Run("a\uD83D", Format) { HyperlinkUrl = "https://same.invalid", CommentId = 17, HyperlinkTooltip = "Same" };
        var right = new Run("\uDE00b", Format) { HyperlinkUrl = left.HyperlinkUrl, CommentId = left.CommentId, HyperlinkTooltip = left.HyperlinkTooltip };
        paragraph.Runs.Add(left);
        if (difference == "comment-reference") paragraph.Runs.Add(Run.CommentReference(17));
        paragraph.Runs.Add(right);
        switch (difference)
        {
            case "url": right.HyperlinkUrl = "https://different.invalid"; break;
            case "anchor": right.HyperlinkAnchor = "Other"; break;
            case "tooltip": right.HyperlinkTooltip = "Other"; break;
            case "comment": right.CommentId = 18; break;
        }
        var originals = paragraph.Runs.ToArray();
        var view = View(new Paragraph("ASCII"));
        view.BodyUsesShapedLiteralText(paragraph).Should().BeFalse();
        paragraph.Runs.Should().Equal(originals); paragraph.PlainText.Should().Be("a😀b");
    }, CancellationToken.None);

    [Fact]
    public Task AdjacentLinksRetainIndependentTargetsTooltipsAndTightHitRegions() => Session.Dispatch(() =>
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("p", Format));
        paragraph.Runs.Add(new Run("😀", Format) { HyperlinkUrl = "https://first.invalid", HyperlinkTooltip = "First" });
        paragraph.Runs.Add(new Run("e\u035C", Format) { HyperlinkUrl = "https://second.invalid", HyperlinkTooltip = "Second" });
        paragraph.Runs.Add(new Run("👩‍💻", Format) { HyperlinkAnchor = "Target", HyperlinkTooltip = "Internal" });
        paragraph.Runs.Add(new Run("q", Format));
        paragraph.BookmarkNames.Add("Target");
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary("target", BookmarkBoundaryKind.Start, RunIndex: 4, Name: "Target"));
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary("target", BookmarkBoundaryKind.End, RunIndex: 5));
        var view = View(paragraph);
        view.BodyUsesShapedLiteralText(paragraph).Should().BeTrue();
        var originals = paragraph.Runs.ToArray();
        var pdf = view.BuildPdfContent(); var overlays = pdf.Pages.SelectMany(page => page.LinkOverlays ?? []).ToArray();
        overlays.Should().HaveCount(3);
        overlays.Select(link => link.Uri).Should().Equal("https://first.invalid", "https://second.invalid", null);
        overlays.Select(link => link.Tooltip).Should().Equal("First", "Second", "Internal");
        overlays[2].DestinationName.Should().Be("Target");
        pdf.Pages.SelectMany(page => page.NamedDestinations ?? []).Should().ContainSingle().Which.Name.Should().Be("Target");
        var expected = new[] { (1, 3, "https://first.invalid", (string?)null), (3, 5, "https://second.invalid", (string?)null), (5, 10, (string?)null, "Target") };
        foreach (var (start, end, url, anchor) in expected)
        {
            view.MoveCaretToBlockForTest(0, start); var left = view.CaretRectForTest!.Value;
            view.MoveCaretToBlockForTest(0, end); var right = view.CaretRectForTest!.Value;
            var hit = HitLink(view, new Point((left.X + right.X) / 2, left.Y + left.Height / 2));
            hit.Should().NotBeNull(); hit!.Value.Url.Should().Be(url); hit.Value.Anchor.Should().Be(anchor);
            var overlay = overlays[Array.FindIndex(expected, item => item.Item1 == start)];
            (overlay.Width * 4 / 3).Should().BeApproximately(right.X - left.X, 0.01);
        }
        view.MoveCaretToBlockForTest(0, 0); var first = view.CaretRectForTest!.Value;
        HitLink(view, new Point(first.X + 0.01, first.Y + first.Height / 2)).Should().BeNull();
        view.MoveCaretToBlockForTest(0, 11); var last = view.CaretRectForTest!.Value;
        HitLink(view, new Point(last.X - 0.01, last.Y + last.Height / 2)).Should().BeNull();
        paragraph.Runs.Should().Equal(originals); paragraph.PlainText.Should().Be("p😀e\u035C👩‍💻q");
    }, CancellationToken.None);

    [Theory]
    [InlineData(0d)]
    [InlineData(2d)]
    [InlineData(-2d)]
    public Task WrappedSoftMetadataClipsAnnotationsAndTintToVisibleLines(double tracking) => Session.Dispatch(() =>
    {
        var text = "a😀b a😀b\r\n\r\ne\u035C\n";
        var paragraph = new Paragraph(); paragraph.Runs.Add(new Run(text, Format with { CharacterSpacingPt = tracking })
        { HyperlinkUrl = "https://wrapped.invalid", CommentId = 17 });
        var view = View(paragraph, narrow: true);
        view.BodyUsesShapedLiteralText(paragraph).Should().BeTrue();
        var lines = view.BodyTextLinesForTest; lines.Should().HaveCountGreaterThan(3);
        var pdf = view.BuildPdfContent(); var links = pdf.Pages.SelectMany(page => page.LinkOverlays ?? []).ToArray();
        var visible = lines.Where(line => text.Substring(line.Start, Math.Min(text.Length - line.Start, line.Length)).Any(character => !IsBreak(character))).ToArray();
        links.Should().HaveCount(visible.Length);
        links.Should().OnlyContain(link => link.Width > 0 && link.Height > 0 && link.Uri == "https://wrapped.invalid");
        var tint = Rectangles(Render(view), Color.FromArgb(0x33, 0xFF, 0xC1, 0x07)).ToArray();
        tint.Should().HaveCount(visible.Length);
        foreach (var line in lines)
        {
            var painted = visible.Any(item => item.Start == line.Start);
            tint.Any(rect => Math.Abs(rect.Y - line.Bounds.Y) < 0.01).Should().Be(painted);
        }
        var pieces = pdf.Pages.SelectMany(page => page.Ops).OfType<PdfText>().ToArray();
        pieces.Should().OnlyContain(piece => Valid(piece.Text) && !piece.Text.Any(IsBreak));
        string.Concat(pieces.Select(piece => piece.Text)).Should().Be(string.Concat(text.Where(character => !IsBreak(character))));
        paragraph.PlainText.Should().Be(text);
    }, CancellationToken.None);

    [Fact]
    public Task ChangedDisplayTextRecomputesAdmissionWhenItJoinsANeighbouringGrapheme() => Session.Dispatch(() =>
    {
        var paragraph = new Paragraph(); paragraph.Runs.Add(new Run("ae😀b", Format));
        var view = View(paragraph);
        view.SetBodySelectionForTest(0, 2, 0, 4);
        view.InsertHyperlink("\u035C", "https://mark.invalid");
        paragraph.PlainText.Should().Be("ae\u035Cb");
        view.BodyUsesShapedLiteralText(paragraph).Should().BeFalse("the new link starts inside the joined e+mark grapheme");
        view.Undo(); Layout(view);
        paragraph.PlainText.Should().Be("ae😀b");
        view.BodyUsesShapedLiteralText(paragraph).Should().BeTrue();
    }, CancellationToken.None);

    [Fact]
    public Task ExplicitHyperlinkColourAndBaseFormattingRemainAuthoredValues() => Session.Dispatch(() =>
    {
        var formatting = Format with { ColorHex = "#9B2226", Underline = false };
        var source = new Run("a😀b", formatting) { HyperlinkUrl = "https://colour.invalid" };
        var paragraph = new Paragraph(); paragraph.Runs.Add(source);
        var view = View(paragraph);
        view.BodyUsesShapedLiteralText(paragraph).Should().BeTrue();
        Glyphs(Render(view)).Where(glyph => glyph.Foreground is ISolidColorBrush brush && brush.Color == Color.Parse("#9B2226")).Should().NotBeEmpty();
        var pdf = view.BuildPdfContent();
        pdf.Pages.SelectMany(page => page.Ops).OfType<PdfText>().Should().OnlyContain(text => text.Color == new PdfColor(155, 34, 38));
        pdf.Pages.SelectMany(page => page.Ops).OfType<PdfLine>().Should().NotBeEmpty();
        source.Formatting.Should().BeSameAs(formatting);
        source.Formatting.Underline.Should().BeFalse("display underline is not authored formatting");
        paragraph.Runs.Should().ContainSingle().Which.Should().BeSameAs(source);
        view.MoveCaretToBlockForTest(0, 1);
        view.GetCaretFormatting().Run.ColorHex.Should().Be("#9B2226");
        view.GetCaretFormatting().Run.Underline.Should().BeFalse();
    }, CancellationToken.None);

    [Fact]
    public Task LinkAndCommentRegionsFollowActualColumnsAndPages() => Session.Dispatch(() =>
    {
        var text = string.Join('\n', Enumerable.Repeat("a😀b", 32));
        var paragraph = new Paragraph(); paragraph.Runs.Add(new Run(text, Format)
        { HyperlinkUrl = "https://columns.invalid", CommentId = 17 });
        paragraph.Formatting = paragraph.Formatting with { LineRule = LineSpacingRule.Exact, LineHeightPt = 40 };
        var document = TextDocument.CreateEmpty(); document.Blocks.Clear(); document.Blocks.Add(paragraph);
        document.Page.WidthPt = 360; document.Page.HeightPt = 300;
        document.Page.MarginLeftPt = document.Page.MarginRightPt = document.Page.MarginTopPt = document.Page.MarginBottomPt = 20;
        document.Page.ColumnCount = 2; document.Page.ColumnSpacingPt = 18;
        var view = new DocumentView(); view.LoadDocument(document); Layout(view);
        view.BodyUsesShapedLiteralText(paragraph).Should().BeTrue();
        var lines = view.BodyTextLinesForTest;
        lines.Should().HaveCount(32);
        var second = view.LayoutColumnBand(1);
        lines.Should().Contain(line => line.Bounds.X >= second.Left);
        lines.Should().Contain(line => line.Bounds.X < second.Left);
        foreach (var line in lines)
            HitLink(view, new Point(line.Bounds.X + line.Bounds.Width / 2, line.Bounds.Y + line.Bounds.Height / 2))!.Value.Url.Should().Be("https://columns.invalid");
        view.CommentAnchorGlyphs().Should().HaveCount(96);
        Rectangles(Render(view), Color.FromArgb(0x33, 0xFF, 0xC1, 0x07)).Should().HaveCount(32);
        var pdf = view.BuildPdfContent();
        pdf.Pages.Should().HaveCount(3);
        pdf.Pages.Select(page => page.LinkOverlays?.Count ?? 0).Should().Equal(12, 12, 8);
        pdf.Pages.SelectMany(page => page.LinkOverlays ?? []).Should().HaveCount(32);
        foreach (var page in pdf.Pages)
        {
            (page.LinkOverlays ?? []).Should().OnlyContain(link => link.X >= 0 && link.Y >= 0
                && link.X + link.Width <= page.WidthPoints && link.Y + link.Height <= page.HeightPoints);
            for (var index = 0; index < page.LinkOverlays!.Count; index++)
            {
                var link = page.LinkOverlays[index];
                link.X.Should().BeApproximately(20 + index / 6 * 169, 0.01);
                link.Y.Should().BeApproximately(20 + index % 6 * 40, 0.01);
                link.Height.Should().BeApproximately(40, 0.01);
            }
        }
        paragraph.PlainText.Should().Be(text);
    }, CancellationToken.None);

    private static DocumentView View(Paragraph paragraph, bool narrow = false)
    {
        var document = TextDocument.CreateEmpty(); document.Blocks.Clear(); document.Blocks.Add(paragraph);
        if (narrow)
        {
            document.Page.WidthPt = 240; document.Page.MarginLeftPt = document.Page.MarginRightPt = 75;
            paragraph.Formatting = paragraph.Formatting with { IndentLeftPt = 45 };
        }
        var view = new DocumentView(); view.LoadDocument(document); Layout(view); return view;
    }
    private static void Layout(DocumentView view) { view.Measure(new Size(816, 4000)); view.Arrange(new Rect(0, 0, 816, 4000)); }
    private static DrawingGroup Render(DocumentView view) { var group = new DrawingGroup(); using (var context = group.Open()) view.Render(context); return group; }
    private static IEnumerable<Drawing> Flatten(DrawingGroup group) => group.Children.SelectMany(drawing => drawing is DrawingGroup child ? Flatten(child) : [drawing]);
    private static IEnumerable<GlyphRunDrawing> Glyphs(DrawingGroup group) => Flatten(group).OfType<GlyphRunDrawing>();
    private static IEnumerable<Rect> Rectangles(DrawingGroup group, Color color) => Flatten(group).OfType<GeometryDrawing>()
        .Where(drawing => drawing.Brush is ISolidColorBrush brush && brush.Color == color).Select(drawing => drawing.Geometry!.Bounds);
    private static DocumentView.LinkInfo? HitLink(DocumentView view, Point point)
    {
        object?[] arguments = [point, null];
        var method = typeof(DocumentView).GetMethod("TryHitTestHyperlink", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (bool)method.Invoke(view, arguments)! ? (DocumentView.LinkInfo)arguments[1]! : null;
    }
    private static bool Valid(string text)
    {
        for (var index = 0; index < text.Length; index++)
            if (char.IsHighSurrogate(text[index])) { if (++index >= text.Length || !char.IsLowSurrogate(text[index])) return false; }
            else if (char.IsLowSurrogate(text[index])) return false;
        return true;
    }
    private static bool IsBreak(char character) => character is '\r' or '\n' or '\v' or '\u2028' or '\u2029';
}
