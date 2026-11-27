using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Free.Shared.Pdf;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using DrawingGroup = Avalonia.Media.DrawingGroup;
using ModelAlignment = FreeW.Core.Model.TextAlignment;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewHeaderFooterTableVisualTests
{
    private static readonly HeadlessUnitTestSession Session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);
    private static readonly RunFormatting Format = RunFormatting.Default with { FontFamily = "Arial", FontSizePt = 16, ColorHex = "#7632A4" };
    private const double Scale = 96d / 72;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SingleTablePaintsAuthoredFillAndStyledBordersInBothSurfaces(bool footer) => Run(() =>
    {
        var table = Table.Create(1, 1); table.DefaultCellMargins = new(4, 6, 8, 10);
        table.Rows[0].HeightPt = 60; table.Rows[0].HeightRule = TableRowHeightRule.Exact;
        var cell = table.Rows[0].Cells[0]; SetText(cell, "Visible", Format with { ColorHex = "#FFFFFF" });
        cell.ShadingColorHex = "#943634"; cell.Borders = new CellBorders
        { Top = new(BorderLineStyle.Double, "#123456", 2), Bottom = new(BorderLineStyle.Dashed, "#654321", 1), NoLeft = true, NoRight = true };
        var (view, _) = View(table, footer);
        var drawings = Paint(view).ToArray();
        var fill = drawings.Where(item => item.Drawing is GeometryDrawing g && g.Brush is ISolidColorBrush b && b.Color == Color.Parse("#943634"))
            .Select(item => item.Drawing.GetBounds().TransformToAABB(item.Transform)).Should().ContainSingle().Which;
        fill.Height.Should().BeApproximately(60 * Scale, 0.001);
        drawings.Count(item => item.Drawing is GeometryDrawing g && g.Pen?.Brush is ISolidColorBrush b && b.Color == Color.Parse("#123456")).Should().Be(2);
        var pdf = view.BuildPdfContent(); var red = pdf.Pages.SelectMany(page => page.Ops).OfType<PdfFillRect>()
            .Should().ContainSingle(op => op.Color == new PdfColor(148, 54, 52)).Which;
        red.Height.Should().BeApproximately(60, 0.001);
        pdf.Pages.SelectMany(page => page.Ops).OfType<PdfPath>().Should().Contain(op => op.StrokeDash != null && op.StrokeColor == new PdfColor(101, 67, 33));
        var text = Flatten(pdf.Pages.SelectMany(page => page.Ops)).OfType<PdfText>().Should().ContainSingle(op => op.Text == "Visible").Which;
        text.Color.Should().Be(new PdfColor(255, 255, 255)); text.FontFamily.Should().Be("Arial");
    });

    [Theory]
    [InlineData(TableCellVerticalAlignment.Top, 4)]
    [InlineData(TableCellVerticalAlignment.Center, 24)]
    [InlineData(TableCellVerticalAlignment.Bottom, 44)]
    public Task AuthoredValignUsesTheSamePaddedRowForCaretAndPdf(TableCellVerticalAlignment alignment, double expectedOffsetPt) => Run(() =>
    {
        var table = Table.Create(1, 1); table.DefaultCellMargins = new(4, 6, 8, 10);
        table.Rows[0].HeightPt = 72; table.Rows[0].HeightRule = TableRowHeightRule.Exact;
        var cell = table.Rows[0].Cells[0]; cell.VerticalAlignment = alignment;
        var paragraph = SetText(cell, "Valign", Format);
        paragraph.Formatting = ParagraphFormatting.Default with { LineRule = LineSpacingRule.Exact, LineHeightPt = 20, SpaceAfterPt = 0 };
        var (view, _) = View(table); view.PlaceCaretInHeaderFooter(false, 0, 0);
        var item = view.HeaderFooterItemsFull.Single();
        var caret = view.HfCaretRectForTest!.Value;
        var page = view.RenderedPageOriginForTest(0);
        caret.X.Should().BeApproximately(page.X + (36 + 6) * Scale, 0.01);
        caret.Y.Should().BeApproximately(page.Y + (18 + expectedOffsetPt) * Scale, 0.01);
        item.Y.Should().BeApproximately(caret.Y, 0.01);
        var ink = TextInk(view).Should().ContainSingle().Which;
        var text = Flatten(view.BuildPdfContent().Pages[0].Ops).OfType<PdfText>().Should().ContainSingle(op => op.Text == "Valign").Which;
        text.X.Should().BeApproximately((ink.X - page.X) / Scale, 0.1);
        view.HitTestHeaderFooterForTest(new Point(caret.X + 0.01, caret.Y + 5)).Should().BeTrue();
        view.HeaderFooterCaretInfo!.Value.Offset.Should().Be(0);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task WrappedMixedStyleSourceRetainsCaretHitSelectionAndRunIdentity(bool footer) => Run(() =>
    {
        var table = Table.Create(1, 1); table.AutoFit = AutoFitMode.Fixed; table.PreferredWidthPt = 92;
        table.DefaultCellMargins = new(2, 4, 2, 4);
        var cell = table.Rows[0].Cells[0];
        var paragraph = SetText(cell, "Alpha beta ", Format); paragraph.Runs.Add(new Run("gamma delta", Format with { Bold = true, Italic = true }));
        paragraph.Formatting = ParagraphFormatting.Default with { LineRule = LineSpacingRule.Exact, LineHeightPt = 24, SpaceAfterPt = 7 };
        var sources = paragraph.Runs.ToArray(); var (view, _) = View(table, footer);
        var lines = TextInk(view).Select(bounds => bounds.Y).Distinct().Order().ToArray();
        lines.Length.Should().BeGreaterThan(1, "the narrow cell must wrap its original paragraph");
        view.PlaceCaretInHeaderFooter(footer, 0, paragraph.PlainText.Length);
        var caret = view.HfCaretRectForTest!.Value; caret.Y.Should().BeGreaterThan(lines[0]);
        view.HitTestHeaderFooterForTest(new Point(caret.X - 0.01, caret.Y + caret.Height / 2)).Should().BeTrue();
        view.HeaderFooterCaretInfo!.Value.Offset.Should().Be(paragraph.PlainText.Length);
        view.PlaceCaretInHeaderFooter(footer, 0, 0);
        view.RaiseKeyDownForContextMenuTests(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right, KeyModifiers = KeyModifiers.Shift });
        view.SelectedText.Should().Be("A");
        Paint(view).Any(item => item.Drawing is GeometryDrawing g && g.Brush is ISolidColorBrush b && b.Color == Color.FromArgb(0x55, 0x33, 0x99, 0xFF)).Should().BeTrue();
        var exported = Flatten(view.BuildPdfContent().Pages.SelectMany(page => page.Ops)).OfType<PdfText>().Where(op => op.Color == new PdfColor(118, 50, 164)).ToArray();
        string.Concat(exported.Select(op => op.Text)).Should().Be(paragraph.PlainText);
        exported.Should().Contain(op => op.Face == PdfFontFace.BoldItalic);
        paragraph.Runs.Should().Equal(sources); cell.Paragraphs[0].Should().BeSameAs(paragraph);
        view.PlaceCaretInHeaderFooter(footer, 0, paragraph.PlainText.Length); view.InsertText("X");
        cell.Paragraphs[0].PlainText.Should().EndWith("X"); view.Undo(); cell.Paragraphs[0].PlainText.Should().Be("Alpha beta gamma delta");
    });

    [Theory]
    [InlineData(false, false, PdfFontFace.Regular)]
    [InlineData(true, false, PdfFontFace.Bold)]
    [InlineData(false, true, PdfFontFace.Italic)]
    [InlineData(true, true, PdfFontFace.BoldItalic)]
    public Task OrdinaryHeaderPdfUsesActualAlignmentFontAndFace(bool bold, bool italic, PdfFontFace face) => Run(() =>
    {
        var document = Document(); var paragraph = new Paragraph(); paragraph.Runs.Add(new Run("Ordinary", Format with { Bold = bold, Italic = italic }));
        paragraph.Formatting = ParagraphFormatting.Default with { Alignment = ModelAlignment.Right };
        document.FinalSectionHeadersFooters.Header = new HeaderFooter(); document.Header!.Paragraphs.Add(paragraph);
        var view = Layout(document); var ink = TextInk(view).Should().ContainSingle().Which;
        var origin = view.RenderedPageOriginForTest(0);
        var text = Flatten(view.BuildPdfContent().Pages[0].Ops).OfType<PdfText>().Should().ContainSingle(op => op.Text == "Ordinary").Which;
        text.Face.Should().Be(face); text.FontFamily.Should().Be("Arial");
        text.X.Should().BeApproximately((ink.Left - origin.X) / Scale, 0.1);
        paragraph.Runs.Should().ContainSingle();
    });


    [Fact]
    public Task StyledControlSegmentsKeepParagraphAlignmentAndOriginalTargets() => Run(() =>
    {
        var table = Table.Create(1, 1); table.DefaultCellMargins = new(4, 6, 4, 6);
        var paragraph = SetText(table.Rows[0].Cells[0], "Left", Format);
        paragraph.Runs.Add(new Run("Right", Format with { Bold = true }) { Control = new ContentControl(ContentControlKind.PlainText, Tag: "test") });
        paragraph.Formatting = ParagraphFormatting.Default with { Alignment = ModelAlignment.Right, SpaceAfterPt = 0 };
        var (view, _) = View(table);
        string.Concat(view.HeaderFooterItemsFull.Select(item => item.Text)).Should().Be("LeftRight");
        var ink = TextInk(view).OrderBy(bounds => bounds.X).ToArray(); ink.Should().HaveCount(2);
        var origin = view.RenderedPageOriginForTest(0);
        ink.Last().Right.Should().BeApproximately(origin.X + (360 - 36 - 6) * Scale, 0.1);
        var texts = Flatten(view.BuildPdfContent().Pages[0].Ops).OfType<PdfText>().Where(op => op.Text == "Left" || op.Text == "Right").ToArray();
        texts.Should().HaveCount(2); texts[0].Face.Should().Be(PdfFontFace.Regular); texts[1].Face.Should().Be(PdfFontFace.Bold);
        view.PlaceCaretInHeaderFooter(false, 0, 4); view.InsertText("X");
        paragraph.PlainText.Should().Be("LeftXRight");
    });


    [Fact]
    public Task EmptyCellPaddingIsAnEditableOriginalParagraph() => Run(() =>
    {
        var table = Table.Create(1, 1); table.DefaultCellMargins = new(8, 10, 8, 10);
        var paragraph = SetText(table.Rows[0].Cells[0], "", Format);
        var (view, _) = View(table);
        var origin = view.RenderedPageOriginForTest(0);
        view.HitTestHeaderFooterForTest(new Point(origin.X + 37 * Scale, origin.Y + 19 * Scale)).Should().BeTrue();
        view.HeaderFooterCaretInfo!.Value.Offset.Should().Be(0);
        view.InsertText("Empty becomes text");
        table.Rows[0].Cells[0].Paragraphs.Single().Should().BeSameAs(paragraph);
        paragraph.PlainText.Should().Be("Empty becomes text");
    });

    [Fact]
    public Task WrappedUnicodeHitTestingNeverSplitsSourceGraphemes() => Run(() =>
    {
        var table = Table.Create(1, 1); table.AutoFit = AutoFitMode.Fixed; table.PreferredWidthPt = 80;
        var paragraph = SetText(table.Rows[0].Cells[0], "A😀e\u0301 beta gamma delta", Format);
        var source = paragraph.Runs.Single(); var (view, _) = View(table);
        var legal = System.Globalization.StringInfo.ParseCombiningCharacters(paragraph.PlainText).Append(paragraph.PlainText.Length).ToArray();
        foreach (var offset in legal)
        {
            view.PlaceCaretInHeaderFooter(false, 0, offset);
            var caret = view.HfCaretRectForTest!.Value;
            view.HitTestHeaderFooterForTest(new Point(caret.X + 0.01, caret.Y + caret.Height / 2)).Should().BeTrue();
            legal.Should().Contain(view.HeaderFooterCaretInfo!.Value.Offset);
        }
        table.Rows[0].Cells[0].Paragraphs.Single().Runs.Single().Should().BeSameAs(source);
        source.Text.Should().Be("A😀e\u0301 beta gamma delta");
    });


    [Fact]
    public Task StyledControlSoftBreakUsesAuthoredLeadingInPaintCaretAndPdf() => Run(() =>
    {
        var table = Table.Create(1, 1);
        var paragraph = SetText(table.Rows[0].Cells[0], "A\nB", Format);
        paragraph.Runs.Single().Control = new ContentControl(ContentControlKind.PlainText, Tag: "multiline");
        paragraph.Formatting = ParagraphFormatting.Default with { LineRule = LineSpacingRule.Exact, LineHeightPt = 30, SpaceAfterPt = 0 };
        var (view, _) = View(table);
        var ink = TextInk(view).OrderBy(bounds => bounds.Y).ToArray();
        ink.Should().HaveCount(2);
        (ink[1].Y - ink[0].Y).Should().BeApproximately(30 * Scale, 0.1);
        view.PlaceCaretInHeaderFooter(false, 0, 2);
        var caret = view.HfCaretRectForTest!.Value;
        caret.Y.Should().BeGreaterThan(ink[0].Bottom);
        view.HitTestHeaderFooterForTest(new Point(caret.X + 0.01, caret.Y + caret.Height / 2)).Should().BeTrue();
        view.HeaderFooterCaretInfo!.Value.Offset.Should().Be(2);
        var text = Flatten(view.BuildPdfContent().Pages[0].Ops).OfType<PdfText>().Where(op => op.Text == "A" || op.Text == "B").ToArray();
        text.Should().HaveCount(2); (text[0].Y - text[1].Y).Should().BeApproximately(30, 0.01);
    });

    private static Paragraph SetText(TableCell cell, string text, RunFormatting format)
    { cell.Paragraphs.Clear(); var p = new Paragraph(); p.Runs.Add(new Run(text, format)); p.Formatting = ParagraphFormatting.Default with { SpaceAfterPt = 0 }; cell.Paragraphs.Add(p); return p; }
    private static (DocumentView View, TextDocument Document) View(Table table, bool footer = false)
    {
        var document = Document(); var story = new HeaderFooter { Table = table };
        foreach (var row in table.Rows) foreach (var cell in row.Cells) story.Paragraphs.AddRange(cell.Paragraphs);
        if (footer) document.FinalSectionHeadersFooters.Footer = story; else document.FinalSectionHeadersFooters.Header = story;
        return (Layout(document), document);
    }
    private static TextDocument Document()
    { var d = TextDocument.CreateEmpty(); d.Page.WidthPt = 360; d.Page.HeightPt = 480; d.Page.MarginLeftPt = d.Page.MarginRightPt = d.Page.MarginTopPt = d.Page.MarginBottomPt = 36; d.Page.HeaderDistancePt = d.Page.FooterDistancePt = 18; return d; }
    private static DocumentView Layout(TextDocument document)
    { var view = new DocumentView(); view.LoadDocument(document); view.Measure(new Size(816, 1400)); view.Arrange(new Rect(0, 0, 816, 1400)); return view; }
    private static IEnumerable<PdfDrawOp> Flatten(IEnumerable<PdfDrawOp> ops)
    { foreach (var op in ops) { if (op is PdfClipGroup clip) foreach (var child in Flatten(clip.Ops)) yield return child; else if (op is PdfOpacityGroup opacity) foreach (var child in Flatten(opacity.Ops)) yield return child; else yield return op; } }
    private static IEnumerable<(Drawing Drawing, Matrix Transform)> Paint(DocumentView view)
    { var group = new DrawingGroup(); using (var c = group.Open()) view.Render(c); return FlattenDrawing(group, Matrix.Identity).ToArray(); }
    private static IEnumerable<(Drawing Drawing, Matrix Transform)> FlattenDrawing(DrawingGroup group, Matrix parent)
    { var transform = (group.Transform?.Value ?? Matrix.Identity) * parent; foreach (var child in group.Children) if (child is DrawingGroup nested) foreach (var item in FlattenDrawing(nested, transform)) yield return item; else yield return (child, transform); }
    private static Rect[] TextInk(DocumentView view) => Paint(view).Where(item => item.Drawing is GlyphRunDrawing glyph && glyph.Foreground is ISolidColorBrush brush && brush.Color == Color.Parse("#7632A4")).Select(item => item.Drawing.GetBounds().TransformToAABB(item.Transform)).ToArray();
    private static Task Run(Action action) => Session.Dispatch(action, CancellationToken.None);
}
