using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class CharacterSpacingLayoutTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Theory]
    [InlineData(1.5)]
    [InlineData(-1.5)]
    public Task ExpandedAndCondensedSpacingMoveTheBodyCaretByTheAuthoredAdvance(double spacingPt) =>
        Session.Dispatch(() =>
        {
            const string text = "AB C";
            var normal = BodyView(text, 0);
            var spaced = BodyView(text, spacingPt);

            var delta = EndCaretX(spaced, text.Length) - EndCaretX(normal, text.Length);

            delta.Should().BeApproximately(text.Length * spacingPt * 4 / 3, 0.01);
            spaced.Document.PlainText.Should().Be(normal.Document.PlainText);
        }, CancellationToken.None);

    [Theory]
    [InlineData("A😀B")]
    [InlineData("Ae\u0301B")]
    [InlineData("A👩‍💻B")]
    public Task SpacingIsAddedOncePerTextElementRatherThanPerUtf16Unit(string text) => Session.Dispatch(() =>
    {
        var normal = BodyView(text, 0);
        var expanded = BodyView(text, 2);

        var delta = EndCaretX(expanded, text.Length) - EndCaretX(normal, text.Length);

        delta.Should().BeApproximately(3 * 2 * 4.0 / 3, 0.01);
    }, CancellationToken.None);

    [Fact]
    public Task FormatCharactersDoNotAcquireExtraAdvance() => Session.Dispatch(() =>
    {
        const string text = "A\u2060B\u00ADC";
        var normal = BodyView(text, 0);
        var expanded = BodyView(text, 2);

        (EndCaretX(expanded, text.Length) - EndCaretX(normal, text.Length))
            .Should().BeApproximately(3 * 2 * 4.0 / 3, 0.01);
    }, CancellationToken.None);

    [Fact]
    public Task MixedRunsUseTheirOwnSpacingAndHiddenTextAddsNone() => Session.Dispatch(() =>
    {
        DocumentView View(double spacing)
        {
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run("AB", Format(spacing)));
            paragraph.Runs.Add(new Run("hidden", Format(40) with { Hidden = true }));
            paragraph.Runs.Add(new Run("CD", Format(0)));
            return ViewFor(paragraph);
        }
        var normal = View(0);
        var expanded = View(2);

        (EndCaretX(expanded, 10) - EndCaretX(normal, 10)).Should().BeApproximately(2 * 2 * 4.0 / 3, 0.01);
    }, CancellationToken.None);

    [Theory]
    [InlineData(1.5)]
    [InlineData(-1.5)]
    public Task SpacingRespectsTheExistingTabStop(double spacing) => Session.Dispatch(() =>
    {
        const string text = "A\tB";
        var normal = BodyView(text, 0);
        var spaced = BodyView(text, spacing);

        EndCaretX(spaced, 2).Should().BeApproximately(EndCaretX(normal, 2), 0.01,
            "tab alignment owns its stop regardless of the text advance before it");
        (EndCaretX(spaced, text.Length) - EndCaretX(normal, text.Length))
            .Should().BeApproximately(spacing * 4 / 3, 0.01);
    }, CancellationToken.None);

    [Fact]
    public Task SpacingParticipatesInLineWrappingAndPagination() => Session.Dispatch(() =>
    {
        var text = string.Concat(Enumerable.Repeat("alpha beta gamma delta ", 60));
        var normal = BodyView(text, 0, narrowPage: true);
        var expanded = BodyView(text, 2, narrowPage: true);
        // A small condensation can retain exactly the same legal word breaks. Use a
        // larger authored value so another whole word fits within this narrow page.
        var condensed = BodyView(text, -3, narrowPage: true);
        static int Lines(DocumentView view) => view.ReviewGlyphsForTest.Select(glyph => glyph.Rect.Y).Distinct().Count();

        Lines(expanded).Should().BeGreaterThan(Lines(normal));
        Lines(condensed).Should().BeLessThan(Lines(normal));
        expanded.PageCount.Should().BeGreaterThan(normal.PageCount);
        condensed.PageCount.Should().BeLessThan(normal.PageCount);
        expanded.Document.PlainText.Should().Be(normal.Document.PlainText);
        condensed.Document.PlainText.Should().Be(normal.Document.PlainText);
    }, CancellationToken.None);

    [Theory]
    [InlineData(1.5)]
    [InlineData(-1.5)]
    public Task TableCellCaretUsesTheSameAuthoredSpacing(double spacing) => Session.Dispatch(() =>
    {
        const string text = "AB C";
        DocumentView View(double value)
        {
            var table = Table.Create(1, 1);
            var paragraph = table.Rows[0].Cells[0].Paragraphs[0];
            paragraph.Runs.Clear();
            paragraph.Runs.Add(new Run(text, Format(value)));
            return ViewFor(table);
        }
        var normal = View(0);
        var spaced = View(spacing);
        normal.PlaceCaretInCell(0, 0, 0, 0, text.Length);
        spaced.PlaceCaretInCell(0, 0, 0, 0, text.Length);

        (spaced.CaretRectForTest!.Value.X - normal.CaretRectForTest!.Value.X)
            .Should().BeApproximately(text.Length * spacing * 4 / 3, 0.01);
    }, CancellationToken.None);

    [Fact]
    public Task AutoFitToContentsReservesTheExpandedCharacterSpacing() => Session.Dispatch(() =>
    {
        const string text = "Character spacing";
        DocumentView View(double spacing)
        {
            var table = Table.Create(1, 1);
            table.AutoFit = AutoFitMode.Contents;
            var paragraph = table.Rows[0].Cells[0].Paragraphs[0];
            paragraph.Runs.Clear();
            paragraph.Runs.Add(new Run(text, Format(spacing)));
            return ViewFor(table);
        }
        var normal = View(0);
        var expanded = View(2);

        (expanded.TableCellRects.Single().Rect.Width - normal.TableCellRects.Single().Rect.Width)
            .Should().BeApproximately(text.Length * 2 * 4.0 / 3, 0.01);
    }, CancellationToken.None);

    [Theory]
    [InlineData("e", "\u0301")]
    [InlineData("👩", "‍💻")]
    public Task AutoFitDoesNotAddSpacingInsideATextElementSplitAcrossRuns(string start, string continuation) =>
        Session.Dispatch(() =>
        {
            DocumentView View(double spacing)
            {
                var table = Table.Create(1, 1);
                table.AutoFit = AutoFitMode.Contents;
                var paragraph = table.Rows[0].Cells[0].Paragraphs[0];
                paragraph.Runs.Clear();
                paragraph.Runs.Add(new Run("long prefix " + start, Format(spacing)));
                paragraph.Runs.Add(new Run(continuation, Format(spacing) with { Italic = true }));
                paragraph.Runs.Add(new Run(" suffix", Format(0)));
                return ViewFor(table);
            }
            var normal = View(0);
            var expanded = View(2);

            (expanded.TableCellRects.Single().Rect.Width - normal.TableCellRects.Single().Rect.Width)
                .Should().BeApproximately(13 * 2 * 4.0 / 3, 0.01,
                    "the continuation run belongs to the preceding grapheme and contributes no extra spacing");
        }, CancellationToken.None);

    private static RunFormatting Format(double spacing) => RunFormatting.Default with
    {
        FontFamily = "Arial",
        FontSizePt = 11,
        CharacterSpacingPt = spacing,
    };

    private static DocumentView BodyView(string text, double spacing, bool narrowPage = false)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(text, Format(spacing)));
        return ViewFor(paragraph, narrowPage);
    }

    private static DocumentView ViewFor(Block block, bool narrowPage = false)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(block);
        if (narrowPage)
        {
            document.Page.WidthPt = 220;
            document.Page.HeightPt = 160;
            document.Page.MarginLeftPt = document.Page.MarginRightPt = 20;
            document.Page.MarginTopPt = document.Page.MarginBottomPt = 20;
        }
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 12000));
        return view;
    }

    private static double EndCaretX(DocumentView view, int offset)
    {
        view.MoveCaretToBlockForTest(0, offset);
        view.CaretRectForTest.Should().NotBeNull();
        return view.CaretRectForTest!.Value.X;
    }
}
