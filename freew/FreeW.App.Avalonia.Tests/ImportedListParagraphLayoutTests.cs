using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class ImportedListParagraphLayoutTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);
    private const double DipPerPoint = 96.0 / 72;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ImportedMarkersWithDifferentDigitWidthsShareTheAuthoredHangingStart(bool shaped) => Run(() =>
    {
        var document = Document();
        var text = shaped ? "item😀" : "item";
        document.Blocks.Add(Item(text, 36));
        var tenth = Item(text, 36);
        tenth.Formatting = tenth.Formatting with { ListStartOverride = 10 };
        document.Blocks.Add(tenth);
        var view = Layout(document);
        var markers = view.RenderedMarkerPositionsForTest;
        markers.Select(marker => marker.Text).Should().Equal("1.", "10.");
        foreach (var marker in markers)
            (marker.X - view.LayoutColumnBand(0).Left).Should().BeApproximately(18 * DipPerPoint, 0.01,
                "the hanging position anchors the marker's left edge independently of its digit count");
        foreach (var block in new[] { 0, 1 })
            (view.GetPlacedForBlock(block)[0].X - view.LayoutColumnBand(0).Left)
                .Should().BeApproximately(36 * DipPerPoint, 0.01);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ImportedListWrapKeepsAllTextAtTheNumberingIndent(bool shaped) => Run(() =>
    {
        var document = Document();
        document.Page.WidthPt = 240;
        var paragraph = Item(string.Join(" ", Enumerable.Repeat(shaped ? "word😀" : "words", 20)), 36);
        document.Blocks.Add(paragraph);
        var original = paragraph.Formatting;
        var view = Layout(document);
        view.BodyUsesShapedLiteralText(paragraph).Should().Be(shaped);
        var lines = view.GetPlacedForBlock(0).GroupBy(glyph => glyph.Y).ToArray();
        lines.Length.Should().BeGreaterThan(1);
        foreach (var line in lines)
            (line.Min(glyph => glyph.X) - view.LayoutColumnBand(0).Left)
                .Should().BeApproximately(36 * DipPerPoint, 0.01,
                    "the level's absolute text indent includes the hanging marker area");
        view.AllRenderedMarkerTextsForTest.Should().Equal("1.");
        paragraph.Formatting.Should().Be(original);
    });

    [Theory]
    [InlineData(0, 0)]
    [InlineData(72, 72)]
    public Task DirectIndentOverridesImportedNumberingWithoutAnExtraListInset(double direct, double expected) => Run(() =>
    {
        var document = Document();
        var item = Item("direct", 36);
        item.Formatting = item.Formatting with { IndentLeftPt = direct, IndentLeftIsSet = true };
        document.Blocks.Add(item);
        var view = Layout(document);
        (view.GetPlacedForBlock(0)[0].X - view.LayoutColumnBand(0).Left)
            .Should().BeApproximately(expected * DipPerPoint, 0.01);
    });

    [Fact]
    public Task PreservedNumberingUsesItsImportedGeometryWithoutChangingItsMarker() => Run(() =>
    {
        var document = Document();
        document.Preserved.OriginalNumbering = System.Xml.Linq.XElement.Parse(
            """
            <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:abstractNum w:abstractNumId="1"><w:lvl w:ilvl="0"><w:start w:val="1"/>
                <w:numFmt w:val="upperRoman"/><w:lvlText w:val="%1."/></w:lvl></w:abstractNum>
              <w:num w:numId="4"><w:abstractNumId w:val="1"/></w:num>
            </w:numbering>
            """);
        var item = Item("preserved", 36);
        item.Formatting = item.Formatting with { ListKind = ListKind.None };
        item.PreservedNumbering = new PreservedNumbering(4, 0);
        document.Blocks.Add(item);
        var view = Layout(document);
        (view.GetPlacedForBlock(0)[0].X - view.LayoutColumnBand(0).Left)
            .Should().BeApproximately(36 * DipPerPoint, 0.01);
        view.AllRenderedMarkerTextsForTest.Should().Equal("I.");
    });

    [Fact]
    public Task NewNativeListKeepsItsDefaultIndentAndNumbering() => Run(() =>
    {
        var document = Document();
        document.Blocks.Add(new Paragraph("one") { Formatting = Exact() with { ListKind = ListKind.Number } });
        document.Blocks.Add(new Paragraph("two") { Formatting = Exact() with { ListKind = ListKind.Number } });
        var view = Layout(document);
        (view.GetPlacedForBlock(0)[0].X - view.LayoutColumnBand(0).Left).Should().BeApproximately(24, 0.01);
        view.AllRenderedMarkerTextsForTest.Should().Equal("1.", "2.");
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task InheritedContextualSpacingRemovesTheGapOnlyBetweenMatchingStyles(bool shaped) => Run(() =>
    {
        var document = Document();
        AddListStyle(document);
        var text = shaped ? "item😀" : "item";
        document.Blocks.Add(Item(text, 36));
        document.Blocks.Add(Item(text, 72));
        document.Blocks.Add(new Paragraph(text) { Formatting = Exact() });
        var first = ((Paragraph)document.Blocks[0]).Formatting;
        var view = Layout(document);
        var one = view.GetPlacedForBlock(0)[0];
        var two = view.GetPlacedForBlock(1)[0];
        var other = view.GetPlacedForBlock(2)[0];
        (two.Y - one.Y).Should().BeApproximately(12 * DipPerPoint, 0.01,
            "ListParagraph's contextual spacing suppresses its inherited 10pt gap");
        (other.Y - two.Y).Should().BeApproximately(22 * DipPerPoint, 0.01,
            "spacing after the final list item remains before a different style");
        ((Paragraph)document.Blocks[0]).Formatting.Should().Be(first);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ContextualKeepWithNextUsesTheSameGapForReservationAndPlacement(bool shaped) => Run(() =>
    {
        var document = Document();
        document.Page.HeightPt = 144;
        document.Page.MarginTopPt = 36;
        document.Page.MarginBottomPt = 36;
        AddListStyle(document);
        // Four 12pt lines leave 24pt for the final pair. An obsolete 10pt gap reservation
        // would move the pair to the next page even though its real contextual gap is zero.
        document.Blocks.Add(new Paragraph("filler\nfiller\nfiller\nfiller") { Formatting = Exact() with
        {
            SpaceAfterPt = 0, SpaceAfterIsSet = true,
        } });
        var text = shaped ? "item😀" : "item";
        var first = Item(text, 36);
        first.Formatting = first.Formatting with { KeepWithNext = true };
        document.Blocks.Add(first);
        document.Blocks.Add(Item(text, 36));
        var view = Layout(document);
        view.PageCount.Should().Be(1, "the actual two-line contextual pair fits the remaining space");
        (view.GetPlacedForBlock(2)[0].Y - view.GetPlacedForBlock(1)[0].Y)
            .Should().BeApproximately(12 * DipPerPoint, 0.01);
    });

    private static TextDocument Document()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.DefaultRun = RunFormatting.Default with { FontFamily = "Arial", FontSizePt = 11 };
        document.Page.WidthPt = 360;
        document.Page.MarginLeftPt = 36;
        document.Page.MarginRightPt = 36;
        return document;
    }

    private static ParagraphFormatting Exact() => new()
    {
        LineRule = LineSpacingRule.Exact, LineHeightPt = 12, LineSpacingIsSet = true,
        SpaceAfterPt = 0,
    };

    private static Paragraph Item(string text, double left) => new(text)
    {
        StyleId = "ListParagraph", Formatting = Exact() with
        {
            ListKind = ListKind.Number, ListLevelIndentLeftPt = left,
            ListLevelFirstLineIndentPt = -18,
        },
    };

    private static void AddListStyle(TextDocument document) => document.Styles["ListParagraph"] = new DocumentStyle
    {
        Id = "ListParagraph", Name = "List Paragraph", Paragraph = Exact() with
        {
            ContextualSpacing = true, SpaceAfterPt = 10, SpaceAfterIsSet = true,
        },
    };

    private static DocumentView Layout(TextDocument document)
    {
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(900, 3000));
        return view;
    }

    private static Task Run(Action action) => Session.Dispatch(action, CancellationToken.None);
}
