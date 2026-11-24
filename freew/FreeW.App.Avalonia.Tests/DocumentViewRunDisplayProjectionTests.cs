using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewRunDisplayProjectionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task LiteralBoundariesPreserveBodyAndCellNewlinePolicy(bool cell) => OnUiThread(() =>
    {
        var source = new Run("a\r\nb\rc", RunFormatting.Default with { Bold = true, FontSizePt = 17 });
        var paragraph = ParagraphWithPrefix(source);
        var view = View(paragraph, cell);

        var projection = view.ResolveRunDisplayProjection(paragraph, source, 1, 0, cell);

        projection.SourceRun.Should().BeSameAs(source);
        projection.SourceRunIndex.Should().Be(1);
        projection.IsAtomic.Should().BeFalse();
        projection.DisplayText.Should().Be(cell ? "a\nb\rc" : "a\r\nb\rc");
        int[] expectedBoundaries = cell ? [0, 1, 3, 4, 5, 6] : [0, 1, 2, 3, 4, 5, 6];
        projection.DisplayToModelBoundaries.Should().Equal(expectedBoundaries);
        projection.Tokens.Should().ContainSingle().Which.SourceRun.Should().BeSameAs(source);
        projection.Tokens[0].SourceRunIndex.Should().Be(1);
        projection.Tokens[0].Formatting.Bold.Should().BeTrue();
        projection.Tokens[0].Formatting.FontSizePt.Should().Be(17);
        source.Text.Should().Be("a\r\nb\rc");
    });

    [Fact]
    public Task ConsecutiveCrAndCrLfAreProjectedOnlyOnce() => OnUiThread(() =>
    {
        var source = new Run("\r\r\n");
        var paragraph = ParagraphWithPrefix(source);
        var view = View(paragraph, cell: true);

        var projection = view.ResolveRunDisplayProjection(paragraph, source, 1, 0, isTableCell: true);

        projection.DisplayText.Should().Be("\r\n", "a lone CR followed by CRLF represents two existing break stops");
        projection.DisplayToModelBoundaries.Should().Equal(0, 1, 3);
        projection.DisplayToModelBoundaries!.Count.Should().Be(projection.DisplayText.Length + 1);
    });

    [Fact]
    public Task CrossRunCrLfKeepsTheExistingPerRunCellBoundaryPolicy() => OnUiThread(() =>
    {
        var first = new Run("a\r");
        var second = new Run("\nb");
        var paragraph = new Paragraph();
        paragraph.Runs.AddRange([first, second]);
        var view = View(paragraph, cell: true);

        var left = view.ResolveRunDisplayProjection(paragraph, first, 0, 0, isTableCell: true);
        var right = view.ResolveRunDisplayProjection(paragraph, second, 1, 0, isTableCell: true);

        left.DisplayText.Should().Be("a\r");
        right.DisplayText.Should().Be("\nb");
        left.DisplayToModelBoundaries.Should().Equal(0, 1, 2);
        right.DisplayToModelBoundaries.Should().Equal(0, 1, 2);
        (left.DisplayText.Length + right.DisplayText.Length).Should().Be(4,
            "a resolver must not silently change cross-run offsets in only one consumer");
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task LiteralUnicodeKeepsUtf16OffsetsAndSourceFormatting(bool cell) => OnUiThread(() =>
    {
        var formatting = RunFormatting.Default with { Italic = true, ColorHex = "#225588" };
        var source = new Run("a😀e\u0301", formatting);
        var paragraph = ParagraphWithPrefix(source);
        var view = View(paragraph, cell);

        var projection = view.ResolveRunDisplayProjection(paragraph, source, 1, 0, cell);

        projection.DisplayText.Should().Be(source.Text);
        projection.DisplayToModelBoundaries.Should().Equal(Enumerable.Range(0, source.Text.Length + 1));
        projection.Formatting.Italic.Should().BeTrue();
        projection.Formatting.ColorHex.Should().Be("#225588");
        source.Formatting.Should().BeSameAs(formatting);
    });

    public static IEnumerable<object[]> Fields()
    {
        foreach (var cell in new[] { false, true })
        foreach (var complex in new[] { false, true })
        foreach (var code in new[] { false, true })
            yield return [cell, complex, code];
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public Task LockedFieldsKeepCachedResultsAndFieldCodeColorWithoutChangingTheModel(bool cell, bool complex, bool code) => OnUiThread(() =>
    {
        var formatting = RunFormatting.Default with { Bold = true, FontSizePt = 19, ColorHex = "#AA2244" };
        var source = complex
            ? Run.ComplexFieldRun(" DATE ", "cached result", showCode: code, formatting: formatting,
                sequence: new ComplexFieldSequenceMetadata(IsLocked: true))
            : Run.DateField("cached result", formatting);
        if (!complex)
        {
            source.FieldLocked = true;
            source.FieldCodeVisible = code;
        }
        var paragraph = ParagraphWithPrefix(source);
        var view = View(paragraph, cell);

        var projection = view.ResolveRunDisplayProjection(paragraph, source, 1, 0, cell);

        projection.DisplayText.Should().Be(code ? "{ DATE }" : "cached result");
        projection.IsAtomic.Should().BeTrue();
        projection.DisplayToModelBoundaries.Should().BeNull();
        projection.Formatting.ColorHex.Should().Be(code ? ComplexFieldDisplayPlanner.FieldCodeColorHex : "#AA2244");
        projection.Formatting.Bold.Should().BeTrue();
        projection.Formatting.FontSizePt.Should().Be(19);
        source.Text.Should().Be("cached result");
        source.Formatting.Should().BeSameAs(formatting);
        source.Formatting.ColorHex.Should().Be("#AA2244");
        paragraph.Runs[1].Should().BeSameAs(source);
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task NotesUseComputedNumberAndSuperscriptWhileKeepingAuthoredFormatting(bool cell, bool endnote) => OnUiThread(() =>
    {
        var formatting = RunFormatting.Default with
        {
            Bold = true, FontSizePt = 17, FontFamily = "Carlito", ColorHex = "#663399", VerticalAlign = VerticalAlign.Baseline
        };
        var first = endnote ? Run.EndnoteReference(12) : Run.FootnoteReference(12);
        var source = endnote ? Run.EndnoteReference(403, formatting) : Run.FootnoteReference(403, formatting);
        var paragraph = new Paragraph();
        paragraph.Runs.AddRange([first, source]);
        var document = Document(paragraph, cell);
        if (endnote)
        {
            document.Endnotes[12] = new Endnote(12, "First note");
            document.Endnotes[403] = new Endnote(403, "Second note");
        }
        else
        {
            document.Footnotes[12] = new Footnote(12, "First note");
            document.Footnotes[403] = new Footnote(403, "Second note");
        }
        var options = endnote ? document.EndnoteNumbering : document.FootnoteNumbering;
        options.NumberFormat = NoteNumberFormat.UpperRoman;
        options.StartAt = 5;
        var view = View(document);

        var projection = view.ResolveRunDisplayProjection(paragraph, source, 1, 0, cell);

        projection.DisplayText.Should().Be("VI");
        projection.IsAtomic.Should().BeTrue();
        projection.DisplayToModelBoundaries.Should().BeNull();
        projection.Formatting.VerticalAlign.Should().Be(VerticalAlign.Superscript);
        projection.Formatting.FontSizePt.Should().Be(17);
        projection.Formatting.FontFamily.Should().Be("Carlito");
        projection.Formatting.ColorHex.Should().Be("#663399");
        projection.Formatting.Bold.Should().BeTrue();
        source.Formatting.Should().BeSameAs(formatting);
        source.Formatting.VerticalAlign.Should().Be(VerticalAlign.Baseline);
        source.Text.Should().Be("403");
        projection.Tokens.Single().SourceRun.Should().BeSameAs(source);
    });

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "")]
    [InlineData(false, "stale equation cache")]
    [InlineData(true, "stale equation cache")]
    public Task EquationScriptsResolveStylesWithoutChangingCachesOrDiagnostics(bool cell, string cached) => OnUiThread(() =>
    {
        var equation = new Equation([MathRun.Superscript("x", "2")]);
        var source = new Run(cached, RunFormatting.Default with { FontSizePt = 18, Bold = true }) { Equation = equation };
        var paragraph = ParagraphWithPrefix(source);
        var view = View(paragraph, cell);
        var elements = view.EquationVisualElements.Count;
        var segments = view.EquationVisualSegments.Count;

        var projection = view.ResolveRunDisplayProjection(paragraph, source, 1, 0, cell);
        for (var index = 0; index < 10; index++)
            view.ResolveRunDisplayProjection(paragraph, source, 1, 0, cell);

        projection.DisplayText.Should().Be("x2");
        projection.Tokens.Select(token => token.Text).Should().Equal("x", "2");
        projection.Tokens.Select(token => token.DisplayStart).Should().Equal(0, 1);
        projection.Tokens[1].Formatting.VerticalAlign.Should().Be(VerticalAlign.Superscript);
        projection.Tokens[1].Formatting.FontSizePt.Should().Be(18, "the renderer applies the script scale once");
        projection.Tokens.Should().OnlyContain(token => token.SourceRun == source && token.SourceRunIndex == 1);
        projection.IsAtomic.Should().BeTrue();
        projection.DisplayToModelBoundaries.Should().BeNull();
        source.Text.Should().Be(cached);
        source.Equation.Should().BeSameAs(equation);
        equation.LinearText.Should().Be("x^2");
        view.EquationVisualElements.Count.Should().Be(elements);
        view.EquationVisualSegments.Count.Should().Be(segments);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FractionIsOneAtomicDisplayStopWithExplicitGeometry(bool cell) => OnUiThread(() =>
    {
        var equation = new Equation([MathRun.Fraction("a+b", "c")]);
        var source = new Run("") { Equation = equation };
        var paragraph = ParagraphWithPrefix(source);
        var view = View(paragraph, cell);

        var projection = view.ResolveRunDisplayProjection(paragraph, source, 1, 0, cell);

        projection.DisplayText.Should().Be("\uFFFC");
        var token = projection.Tokens.Should().ContainSingle().Subject;
        token.DisplayLength.Should().Be(1);
        token.EquationElement!.Kind.Should().Be(EquationVisualElementKind.Fraction);
        token.EquationElement.Numerator.Should().Be("a+b");
        token.EquationElement.Denominator.Should().Be("c");
        token.EquationElement.Should().BeSameAs(projection.EquationPlan!.Elements.Single());
        token.SourceRun.Should().BeSameAs(source);
        source.Text.Should().BeEmpty();
        source.Equation.Should().BeSameAs(equation);
    });

    [Fact]
    public Task MixedEquationElementsKeepOneContinuousDisplayOffsetStream() => OnUiThread(() =>
    {
        var source = new Run("")
        {
            Equation = new Equation([MathRun.PlainText("a"), MathRun.Fraction("1", "2"), MathRun.Superscript("x", "2")])
        };
        var paragraph = ParagraphWithPrefix(source);
        var view = View(paragraph, cell: true);

        var projection = view.ResolveRunDisplayProjection(paragraph, source, 1, 0, isTableCell: true);

        projection.DisplayText.Should().Be("a\uFFFCx2");
        projection.Tokens.Select(token => token.DisplayStart).Should().Equal(0, 1, 2, 3);
        projection.Tokens.Select(token => token.DisplayLength).Should().Equal(1, 1, 1, 1);
        projection.Tokens[1].EquationElement.Should().NotBeNull();
        projection.Tokens[3].Formatting.VerticalAlign.Should().Be(VerticalAlign.Superscript);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task EmbeddedObjectsKeepTheirIdentityButHaveNoEditableDisplayText(bool cell) => OnUiThread(() =>
    {
        var embedded = EmbeddedObject.Create([1, 2, 3], "Projection.Package", widthPt: 40, heightPt: 24);
        var source = new Run("cached label") { EmbeddedObject = embedded };
        var paragraph = ParagraphWithPrefix(source);
        var view = View(paragraph, cell);

        var projection = view.ResolveRunDisplayProjection(paragraph, source, 1, 0, cell);

        projection.DisplayText.Should().BeEmpty();
        projection.Tokens.Should().BeEmpty();
        projection.DisplayToModelBoundaries.Should().BeNull();
        projection.IsAtomic.Should().BeTrue();
        projection.SourceRun.Should().BeSameAs(source);
        source.EmbeddedObject.Should().BeSameAs(embedded);
        source.Text.Should().Be("cached label");
    });

    [Fact]
    public Task EmptyLiteralControlRetainsItsSingleModelBoundary() => OnUiThread(() =>
    {
        var source = Run.PlainTextControl("", tag: "empty");
        var paragraph = ParagraphWithPrefix(source);
        var view = View(paragraph, cell: true);

        var projection = view.ResolveRunDisplayProjection(paragraph, source, 1, 0, isTableCell: true);

        projection.DisplayText.Should().BeEmpty();
        projection.Tokens.Should().BeEmpty();
        projection.DisplayToModelBoundaries.Should().Equal(0);
        projection.IsAtomic.Should().BeFalse();
        projection.SourceRun.Control.Should().BeSameAs(source.Control);
    });

    [Fact]
    public Task HiddenFormattingDoesNotRemoveOffsetBearingText() => OnUiThread(() =>
    {
        var source = new Run("hidden", RunFormatting.Default with { Hidden = true });
        var paragraph = ParagraphWithPrefix(source);
        var view = View(paragraph, cell: true);

        var projection = view.ResolveRunDisplayProjection(paragraph, source, 1, 0, isTableCell: true);

        projection.IsHidden.Should().BeTrue();
        projection.DisplayText.Should().Be("hidden");
        projection.DisplayToModelBoundaries.Should().Equal(0, 1, 2, 3, 4, 5, 6);
        projection.Tokens.Single().Formatting.Hidden.Should().BeTrue();
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task RenderedNotesShareComputedTextFormattingAndSelectionOffsets(bool cell, bool endnote) => OnUiThread(() =>
    {
        var formatting = RunFormatting.Default with { FontSizePt = 17, Bold = true, ColorHex = "#663399" };
        var note = endnote ? Run.EndnoteReference(403, formatting) : Run.FootnoteReference(403, formatting);
        var paragraph = new Paragraph();
        paragraph.Runs.AddRange([note, new Run("tail", RunFormatting.Default with { FontSizePt = 11, Bold = false })]);
        var document = Document(paragraph, cell);
        if (endnote) document.Endnotes[403] = new Endnote(403, "Note text");
        else document.Footnotes[403] = new Footnote(403, "Note text");
        var view = View(document);
        Layout(view);

        view.GetPlacedForBlock(0).Select(glyph => glyph.Ch).Should().Equal("1tail");
        var placedFormatting = view.GetPlacedFormattingForBlock(0);
        placedFormatting[0].VerticalAlign.Should().Be(VerticalAlign.Superscript);
        placedFormatting[0].FontSizePt.Should().Be(17);
        placedFormatting[0].ColorHex.Should().Be("#663399");
        placedFormatting[1].FontSizePt.Should().Be(11);
        placedFormatting[1].Bold.Should().BeFalse();
        if (cell) view.SetCellTextSelectionForTest(0, 0, 0, 0, 1, 0, 0, 0, 5);
        else view.SetBodySelectionForTest(0, 1, 0, 5);
        view.SelectedText.Should().Be("tail");
        view.AutomationSnapshot().Text.Should().Be("1tail");
        view.AutomationSnapshot().CaretOffset.Should().Be(5);
        note.Text.Should().Be("403");
        paragraph.Runs[0].Should().BeSameAs(note);
        note.Formatting.Should().BeSameAs(formatting);
    });

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "")]
    [InlineData(false, "stale cache")]
    [InlineData(true, "stale cache")]
    public Task RenderedFractionHasGeometryAndOneSharedCaretStop(bool cell, string cache) => OnUiThread(() =>
    {
        var source = new Run(cache, RunFormatting.Default with { FontSizePt = 18 })
            { Equation = new Equation([MathRun.Fraction("a+b", "c")]) };
        var paragraph = new Paragraph();
        paragraph.Runs.AddRange([source, new Run("z")]);
        var view = View(paragraph, cell);
        Layout(view);

        var glyphs = view.GetPlacedForBlock(0);
        glyphs.Select(glyph => glyph.Ch).Should().Equal('\uFFFC', 'z');
        glyphs[0].W.Should().BeGreaterThan(10);
        glyphs[0].LineHeight.Should().BeGreaterThan(18 * 4.0 / 3);
        glyphs[1].X.Should().BeApproximately(glyphs[0].X + glyphs[0].W, 0.01);
        view.EquationVisualElements.Should().ContainSingle().Which.Kind.Should().Be(EquationVisualElementKind.Fraction);
        if (cell) view.SetCellTextSelectionForTest(0, 0, 0, 0, 1, 0, 0, 0, 2);
        else view.SetBodySelectionForTest(0, 1, 0, 2);
        view.SelectedText.Should().Be("z");
        view.AutomationSnapshot().Text.Should().Be("\uFFFCz");
        source.Text.Should().Be(cache);
        paragraph.Runs[0].Should().BeSameAs(source);
    });

    [Theory]
    [InlineData("field")]
    [InlineData("equation")]
    [InlineData("note")]
    public Task AutoFitUsesTheSameResolvedGeometryInsteadOfCachedRunText(string kind) => OnUiThread(() =>
    {
        DocumentView Make(string cache)
        {
            var source = kind switch
            {
                "field" => Run.DateField(cache),
                "note" => Run.FootnoteReference(403),
                _ => new Run(cache) { Equation = new Equation([MathRun.Fraction("long numerator", "denominator")]) },
            };
            if (kind == "field") source.FieldCodeVisible = true;
            var paragraph = new Paragraph();
            paragraph.Runs.AddRange([source, new Run(" tail")]);
            var document = Document(paragraph, cell: true);
            ((Table)document.Blocks[0]).AutoFit = AutoFitMode.Contents;
            document.Footnotes[403] = new Footnote(403, "Note content");
            var view = View(document);
            Layout(view);
            source.Text.Should().Be(kind == "note" ? "403" : cache);
            return view;
        }
        var empty = Make("");
        var stale = Make(new string('W', 100));
        var width = empty.TableCellRects.Single().Rect.Width;
        width.Should().BeApproximately(stale.TableCellRects.Single().Rect.Width, 0.01);
        var glyphs = empty.GetPlacedForBlock(0);
        width.Should().BeGreaterThan(glyphs.Sum(glyph => glyph.W));
        width.Should().BeLessThan(glyphs.Sum(glyph => glyph.W) + 40,
            "the column should fit the displayed stream and cell margins rather than expand to page width");
    });

    [Fact]
    public Task MixedCellObjectsEquationsAndHardBreaksKeepGeometryAndExactDisplayOffsets() => OnUiThread(() =>
    {
        var embedded = new Run("") { EmbeddedObject = EmbeddedObject.Create([1], "Projection.Package", widthPt: 24, heightPt: 24) };
        var equation = new Run("") { Equation = new Equation([MathRun.Fraction("1", "2")]) };
        var paragraph = new Paragraph();
        paragraph.Runs.AddRange([new Run("a"), embedded, equation, new Run("\r\nb")]);
        var view = View(paragraph, cell: true);
        Layout(view);

        var glyphs = view.GetCellPlaced(0, 0, 0, 0);
        var a = glyphs.Single(glyph => glyph.Ch == 'a');
        var fraction = glyphs.Single(glyph => glyph.Ch == '\uFFFC');
        var b = glyphs.Single(glyph => glyph.Ch == 'b');
        fraction.ParaOffset.Should().Be(1);
        b.ParaOffset.Should().Be(3);
        b.Y.Should().BeGreaterThan(a.Y);
        glyphs.Single(glyph => glyph.Sentinel).ParaOffset.Should().Be(4);
        view.EmbeddedObjectRenderItems.Should().ContainSingle().Which.RunIndex.Should().Be(1);
        view.AutomationSnapshot().Text.Should().Be("a\uFFFC\nb");
        paragraph.Runs[1].Should().BeSameAs(embedded);
        paragraph.Runs[2].Should().BeSameAs(equation);
    });

    [Fact]
    public Task TableFieldCodeKeepsItsResolvedStyleInsteadOfTheFirstRunStyle() => OnUiThread(() =>
    {
        var source = Run.DateField("", RunFormatting.Default with { FontSizePt = 19, Bold = false, ColorHex = "#AA2244" });
        source.FieldCodeVisible = true;
        var paragraph = new Paragraph();
        paragraph.Runs.AddRange([new Run("a", RunFormatting.Default with { FontSizePt = 10, Bold = true }), source]);
        var view = View(paragraph, cell: true);
        Layout(view);

        view.GetPlacedForBlock(0).Select(glyph => glyph.Ch).Should().Equal("a{ DATE }");
        var formats = view.GetPlacedFormattingForBlock(0);
        formats[0].FontSizePt.Should().Be(10);
        formats[0].Bold.Should().BeTrue();
        formats.Skip(1).Should().OnlyContain(format => format.FontSizePt == 19 && format.Bold == false
            && format.ColorHex == ComplexFieldDisplayPlanner.FieldCodeColorHex);
        source.Formatting.ColorHex.Should().Be("#AA2244");
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FlowBreakChromeDoesNotDiscardColocatedSourceText(bool cell) => OnUiThread(() =>
    {
        var source = new Run("after") { IsPageBreak = true };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(source);
        var view = View(paragraph, cell);
        Layout(view);

        view.ResolveRunDisplayProjection(paragraph, source, 0, 0, cell).DisplayText.Should().Be("after");
        view.GetPlacedForBlock(0).Select(glyph => glyph.Ch).Should().Equal("after");
        view.AutomationSnapshot().Text.Should().Be("after");
        source.IsPageBreak.Should().BeTrue();
        source.Text.Should().Be("after");
        paragraph.Runs.Single().Should().BeSameAs(source);
    });

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public Task CellHardBreakGeometryCaretSelectionAndAccessibilityShareTheDisplayBoundary(string separator) => OnUiThread(() =>
    {
        var source = new Run("A" + separator + "B");
        var paragraph = new Paragraph();
        paragraph.Runs.Add(source);
        var view = View(paragraph, cell: true);
        Layout(view);
        var expectedSeparator = separator == "\r\n" ? "\n" : separator;
        var glyphs = view.GetCellPlaced(0, 0, 0, 0);
        var before = glyphs.Single(glyph => glyph.Ch == 'A');
        var after = glyphs.Single(glyph => glyph.Ch == 'B');

        after.Y.Should().BeGreaterThan(before.Y);
        after.X.Should().BeApproximately(before.X, 0.01);
        after.ParaOffset.Should().Be(2);
        glyphs.Single(glyph => glyph.Sentinel).ParaOffset.Should().Be(3);
        view.PlaceCaretInCell(0, 0, 0, 0, 2);
        view.CaretRectForTest.Should().NotBeNull();
        view.CaretRectForTest!.Value.X.Should().BeApproximately(after.X, 0.01);
        view.CaretRectForTest!.Value.Y.Should().BeApproximately(after.Y, 0.01);
        view.AutomationSnapshot().Text.Should().Be("A" + expectedSeparator + "B");
        view.AutomationSnapshot().CaretOffset.Should().Be(2);
        view.SetCellTextSelectionForTest(0, 0, 0, 0, 1, 0, 0, 0, 2);
        view.SelectedText.Should().Be(expectedSeparator);
        var snapshot = view.AutomationSnapshot();
        snapshot.GetText(snapshot.Selection!).Should().Be(expectedSeparator);
        snapshot.CaretOffset.Should().Be(2);
        source.Text.Should().Be("A" + separator + "B");
        paragraph.Runs.Single().Should().BeSameAs(source);
    });

    private static void Layout(DocumentView view)
    {
        view.Measure(new Size(816, 4000));
        view.Arrange(new Rect(0, 0, 816, 4000));
    }

    private static Paragraph ParagraphWithPrefix(Run source)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.AddRange([new Run("prefix"), source]);
        return paragraph;
    }

    private static TextDocument Document(Paragraph paragraph, bool cell)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        if (cell)
        {
            var table = Table.Create(1, 1);
            table.Rows[0].Cells[0].Paragraphs.Clear();
            table.Rows[0].Cells[0].Paragraphs.Add(paragraph);
            document.Blocks.Add(table);
        }
        else document.Blocks.Add(paragraph);
        return document;
    }

    private static DocumentView View(Paragraph paragraph, bool cell) => View(Document(paragraph, cell));

    private static DocumentView View(TextDocument document)
    {
        var view = new DocumentView();
        view.LoadDocument(document);
        return view;
    }

    private static Task OnUiThread(Action action) => Session.Dispatch(action, CancellationToken.None);
}
