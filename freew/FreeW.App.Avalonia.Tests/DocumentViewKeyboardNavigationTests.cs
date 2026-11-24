using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewKeyboardNavigationTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public Task OptionWordNavigationAndShiftSelectionUseTheBodyStory() => OnUiThread(() =>
    {
        var view = CreateView(new Paragraph("alpha   beta"));
        view.MoveCaretToBlockForTest(0, 0);

        Press(view, Key.Right, KeyModifiers.Alt | KeyModifiers.Shift);
        view.SelectedText.Should().Be("alpha");
        Press(view, Key.Right, KeyModifiers.Alt | KeyModifiers.Shift);
        view.SelectedText.Should().Be("alpha   beta");
        Press(view, Key.Left);
        view.CaretPositionForTest.Should().Be((0, 0));
        view.SelectedText.Should().BeEmpty();
        Press(view, Key.Right, KeyModifiers.Control);
        view.CaretPositionForTest.Should().Be((0, 8));
    });

    [Fact]
    public Task PlainArrowsCollapseReverseSelectionsToTheDirectionalEdge() => OnUiThread(() =>
    {
        var view = CreateView(new Paragraph("abcdef"));
        view.SetBodySelectionForTest(0, 5, 0, 2);
        Press(view, Key.Right);
        view.CaretPositionForTest.Should().Be((0, 5));
        view.SetBodySelectionForTest(0, 2, 0, 5);
        Press(view, Key.Left);
        view.CaretPositionForTest.Should().Be((0, 2));
    });

    [Fact]
    public Task PlainArrowAndShiftArrowPreserveWholeEmojiAndCombiningCharacters() => OnUiThread(() =>
    {
        var text = "a👩‍💻e\u0301z";
        var view = CreateView(new Paragraph(text));
        view.MoveCaretToBlockForTest(0, 1);

        Press(view, Key.Right, KeyModifiers.Shift);
        view.CaretPositionForTest.Should().Be((0, "a👩‍💻".Length));
        view.SelectedText.Should().Be("👩‍💻");
        Press(view, Key.Right);
        Press(view, Key.Right, KeyModifiers.Shift);
        view.SelectedText.Should().Be("e\u0301");
        view.PlainText.Should().Be(text);
    });

    [Theory]
    [InlineData(2, 6)]
    [InlineData(3, 6)]
    [InlineData(5, 6)]
    [InlineData(7, 8)]
    public Task PlainRightArrowRecoversFromAnInteriorPointerOffset(int offset, int expected) => OnUiThread(() =>
    {
        var view = CreateView(new Paragraph("a👩‍💻e\u0301z"));
        view.MoveCaretToBlockForTest(0, offset);

        Press(view, Key.Right);

        view.CaretPositionForTest.Should().Be((0, expected));
    });

    [Fact]
    public Task CommandAndHomeEndUseTheCurrentWrappedVisualLine() => OnUiThread(() =>
    {
        var text = string.Join(" ", Enumerable.Repeat("wrapped words", 35));
        var view = CreateView(new Paragraph(text));
        var glyphs = view.GetPlacedForBlock(0);
        var rows = glyphs.Select((glyph, index) => (glyph.Y, Offset: index))
            .GroupBy(glyph => glyph.Y).OrderBy(row => row.Min(glyph => glyph.Offset)).ToArray();
        rows.Length.Should().BeGreaterThan(2);
        var rowStart = rows[1].Min(glyph => glyph.Offset);
        var rowEnd = rows[1].Max(glyph => glyph.Offset) + 1;
        view.MoveCaretToBlockForTest(0, rowStart + 2);

        Press(view, Key.Left, KeyModifiers.Meta | KeyModifiers.Shift);
        view.SelectedText.Should().Be(text.Substring(rowStart, 2));
        view.CaretPositionForTest.Should().Be((0, rowStart));
        Press(view, Key.End);
        view.CaretPositionForTest.Should().Be((0, rowEnd));
        view.MoveCaretToBlockForTest(0, rowStart + 2);
        Press(view, Key.Right, KeyModifiers.Meta);
        view.CaretPositionForTest.Should().Be((0, rowEnd));
    });

    [Fact]
    public Task CommandParagraphAndDocumentEdgesRemainDistinct() => OnUiThread(() =>
    {
        var view = CreateView(new Paragraph("first"), new Paragraph("second"), new Paragraph("last"));
        view.MoveCaretToBlockForTest(1, 3);

        Press(view, Key.Up, KeyModifiers.Meta);
        view.CaretPositionForTest.Should().Be((1, 0));
        Press(view, Key.Home, KeyModifiers.Meta);
        view.CaretPositionForTest.Should().Be((0, 0));
        Press(view, Key.End, KeyModifiers.Control | KeyModifiers.Shift);
        view.SelectedText.Should().Be("first\nsecond\nlast");
        view.CaretPositionForTest.Should().Be((2, 4));
    });

    [Fact]
    public Task ControlDownUsesTheWindowsNextParagraphStartBinding() => OnUiThread(() =>
    {
        var view = CreateView(new Paragraph("first paragraph"), new Paragraph("second"));
        view.MoveCaretToBlockForTest(0, 3);

        Press(view, Key.Down, KeyModifiers.Control | KeyModifiers.Shift);
        view.CaretPositionForTest.Should().Be((1, 0));
        view.SelectedText.Should().Be("st paragraph\n");
        view.MoveCaretToBlockForTest(0, 3);
        Press(view, Key.Down, KeyModifiers.Meta);
        view.CaretPositionForTest.Should().Be((0, "first paragraph".Length));
    });

    [Theory]
    [InlineData(Key.Home, "alp")]
    [InlineData(Key.End, "ha beta")]
    public Task ShiftHomeEndInACellPreserveTheOriginalAnchor(Key key, string expected) => OnUiThread(() =>
    {
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0] = new TableCell("alpha beta");
        var view = CreateView(table);
        view.SetCellTextSelectionForTest(0, 0, 0, 0, 3, 0, 0, 0, 3);

        Press(view, key, KeyModifiers.Shift);

        view.SelectedText.Should().Be(expected);
    });

    [Fact]
    public Task DocumentEdgesFromCellsReachTheDocumentRatherThanOnlyTheCell() => OnUiThread(() =>
    {
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0] = new TableCell("cell text");
        var view = CreateView(new Paragraph("before"), table, new Paragraph("after"));
        view.SetCellTextSelectionForTest(1, 0, 0, 0, 3, 0, 0, 0, 3);

        Press(view, Key.Home, KeyModifiers.Control);
        view.CaretPositionForTest.Should().Be((0, 0));
        Press(view, Key.End, KeyModifiers.Meta);
        view.CaretPositionForTest.Should().Be((2, 5));
    });

    [Fact]
    public Task ShapeTextNavigationCrossesFormattedRunsAndKeepsSelectionInTheShape() => OnUiThread(() =>
    {
        var shape = new Shape(ShapeKind.TextBox, 144, 108, "#FFFFFF")
        {
            Placement = new FloatingPlacement { Wrapping = ImageWrapping.InFront },
        };
        var shapeParagraph = new Paragraph();
        shapeParagraph.Runs.Add(new Run("alpha "));
        shapeParagraph.Runs.Add(new Run("beta", RunFormatting.Default with { Bold = true }));
        shape.TextParagraphs.Add(shapeParagraph);
        shape.TextParagraphs.Add(new Paragraph("next paragraph"));
        var body = new Paragraph("body must remain unchanged");
        body.Runs.Add(new Run(shape.PlainText) { Shape = shape });
        var view = CreateView(body);
        view.SelectFloating(0, 1);
        view.EnterSelectedShapeTextEditing().Should().BeTrue();
        view.SelectShapeTextRangeForTest(0, 0, 0);

        Press(view, Key.Right, KeyModifiers.Alt | KeyModifiers.Shift);
        view.SelectedText.Should().Be("alpha");
        Press(view, Key.End, KeyModifiers.Meta | KeyModifiers.Shift);
        view.SelectedText.Should().Be("alpha beta\nnext paragraph");
        view.SelectAll();
        view.SelectedText.Should().Be("alpha beta\nnext paragraph");
        view.ShapeTextCaretInfo!.Value.TextParagraphIndex.Should().Be(1);
        var bodyCaret = view.CaretPositionForTest;
        Press(view, Key.Up);
        view.ShapeTextCaretInfo.Should().NotBeNull();
        view.CaretPositionForTest.Should().Be(bodyCaret,
            "vertical text-box navigation must not move the hidden body caret");
        body.Runs[0].Text.Should().Be("body must remain unchanged");
    });

    [Theory]
    [InlineData("field")]
    [InlineData("complex-field")]
    [InlineData("equation")]
    [InlineData("footnote")]
    [InlineData("endnote")]
    public Task StructuralBodyParagraphsUseResolvedCaretOffsets(string kind) => OnUiThread(() =>
    {
        var run = kind switch
        {
            "field" => Run.DateField(cached: ""),
            "complex-field" => Run.ComplexFieldRun(" DATE ", ""),
            "equation" => new Run("") { Equation = Equation.FromText("x^2") },
            "footnote" => Run.FootnoteReference(403),
            _ => Run.EndnoteReference(507),
        };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(run);
        paragraph.Runs.Add(new Run(" suffix"));
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);
        document.Footnotes[403] = new Footnote(403, "footnote text");
        document.Endnotes[507] = new Endnote(507, "endnote text");
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 4000));
        var displayEnd = view.GetPlacedForBlock(0).Count;
        var original = document.PlainText;
        var originalEquationElements = view.EquationVisualElements.Count;
        var originalEquationSegments = view.EquationVisualSegments.Count;
        view.MoveCaretToBlockForTest(0, displayEnd - 2);

        Press(view, Key.Right, KeyModifiers.Meta);
        view.CaretPositionForTest.Should().Be((0, displayEnd));
        view.CaretRectForTest.Should().NotBeNull();
        Press(view, Key.Left);
        view.CaretPositionForTest.Should().Be((0, displayEnd - 1));
        Press(view, Key.End, KeyModifiers.Control);
        view.CaretPositionForTest.Should().Be((0, displayEnd));
        document.PlainText.Should().Be(original);
        view.EquationVisualElements.Count.Should().Be(originalEquationElements,
            "navigation must not append render-only equation diagnostics");
        view.EquationVisualSegments.Count.Should().Be(originalEquationSegments);
    });

    [Fact]
    public Task FieldCellLineEndUsesItsResolvedEndSentinel() => OnUiThread(() =>
    {
        var table = Table.Create(1, 1);
        var paragraph = table.Rows[0].Cells[0].Paragraphs[0];
        paragraph.Runs.Clear();
        paragraph.Runs.Add(Run.DateField(cached: ""));
        paragraph.Runs.Add(new Run(" suffix"));
        var view = CreateView(table);
        var glyphs = view.GetCellPlaced(0, 0, 0, 0);
        var displayEnd = glyphs.Where(glyph => !glyph.Sentinel).Max(glyph => glyph.ParaOffset) + 1;
        glyphs.Single(glyph => glyph.Sentinel).ParaOffset.Should().Be(displayEnd);
        view.SetCellTextSelectionForTest(0, 0, 0, 0, displayEnd - 2, 0, 0, 0, displayEnd - 2);

        Press(view, Key.End);

        view.CellCaretInfo!.Value.Offset.Should().Be(displayEnd);
        view.CaretRectForTest.Should().NotBeNull(
            "a cell field's cached text must not determine the displayed end caret");
        Press(view, Key.Left);
        view.CellCaretInfo!.Value.Offset.Should().Be(displayEnd - 1);
    });

    [Fact]
    public Task LocalCharacterMotionDoesNotAllocateAddressesForTheEntireDocument() => OnUiThread(() =>
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        for (var index = 0; index < 4096; index++)
            document.Blocks.Add(new Paragraph("local movement"));
        var view = new DocumentView();
        view.LoadDocument(document);
        view.MoveCaretToBlockForTest(0, 1);
        Press(view, Key.Right);
        var before = GC.GetAllocatedBytesForCurrentThread();

        Press(view, Key.Right);

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(64 * 1024,
            "ordinary character movement must not build an address record for every document paragraph");
        view.CaretPositionForTest.Should().Be((0, 3));
    });

    private static DocumentView CreateView(params Block[] blocks)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.AddRange(blocks);
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 4000));
        return view;
    }

    private static void Press(DocumentView view, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var args = new KeyEventArgs { Key = key, KeyModifiers = modifiers, RoutedEvent = DocumentView.KeyDownEvent };
        view.RaiseKeyDownForContextMenuTests(args);
        args.Handled.Should().BeTrue();
    }

    private static Task OnUiThread(Action action) => Session.Dispatch(action, CancellationToken.None);
}
