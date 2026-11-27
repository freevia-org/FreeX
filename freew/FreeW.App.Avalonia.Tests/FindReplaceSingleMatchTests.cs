using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FluentAssertions;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Interactive Find Next / single Replace must do what Replace All and Word do: a Replace with formatting
/// formats the written text inside a table cell and a header/footer (not only in the body), and Find Next
/// finds and selects a match that runs across paragraph marks (<c>^p</c>), in the body and in a table cell.
/// Each case drives the production path (DocumentView.FindNext / ReplaceNext -> the shared planners).
/// </summary>
public sealed class FindReplaceSingleMatchTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    private static readonly FindFormat BoldRed = new() { Bold = true, ColorHex = "FF0000" };

    private static DocumentView Load(TextDocument document)
    {
        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(800, 2000));
        return view;
    }

    private static TextDocument DocumentOf(params Block[] blocks)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var block in blocks)
            document.Blocks.Add(block);
        return document;
    }

    private static Table TableOf(params string[] cellParagraphs)
    {
        var cell = new TableCell();
        foreach (var text in cellParagraphs)
            cell.Paragraphs.Add(new Paragraph(text));
        var row = new TableRow();
        row.Cells.Add(cell);
        var table = new Table();
        table.Rows.Add(row);
        return table;
    }

    private static FindReplaceReplaceRequest Request(string find, string replace, FindFormat? replaceFormat = null) =>
        new(find, replace, new FindReplaceSearchOptions(), ReplaceFormat: replaceFormat);

    private static void AssertFormatted(Paragraph paragraph, string text)
    {
        var run = paragraph.Runs.Single(candidate => candidate.Text == text);
        run.Formatting.Bold.Should().BeTrue("the Replace with formatting applies to the written text");
        FindFormat.SameColor(run.Formatting.ColorHex, "#FF0000").Should().BeTrue();
        paragraph.Runs.Where(candidate => candidate.Text != text)
            .Should().OnlyContain(candidate => !candidate.Formatting.Bold, "text around the match stays as it was");
    }

    // ---- single Replace with formatting ------------------------------------------------------

    [Fact]
    public async Task ReplaceNext_WithReplaceFormatting_FormatsTheWrittenTextInATableCell()
    {
        var found = false;
        var replaced = false;
        Paragraph? cellParagraph = null;
        var ran = await OnUiThread(() =>
        {
            var view = Load(DocumentOf(new Paragraph("intro"), TableOf("see cat here"), new Paragraph("outro")));

            found = view.FindNext("cat");
            replaced = view.ReplaceNext(Request("cat", "tiger", BoldRed));

            cellParagraph = ((Table)view.Document.Blocks[1]).Rows[0].Cells[0].Paragraphs[0];
        });
        ran.Should().BeTrue();

        found.Should().BeTrue();
        replaced.Should().BeFalse("there is no further match after the only one was replaced");
        cellParagraph!.PlainText.Should().Be("see tiger here");
        AssertFormatted(cellParagraph, "tiger");
    }

    [Fact]
    public async Task ReplaceNext_WithEmptyReplacementAndFormatting_ReformatsTheFoundTextInATableCell()
    {
        Paragraph? cellParagraph = null;
        var ran = await OnUiThread(() =>
        {
            var view = Load(DocumentOf(new Paragraph("intro"), TableOf("see cat here")));

            view.FindNext("cat");
            view.ReplaceNext(Request("cat", string.Empty, BoldRed));

            cellParagraph = ((Table)view.Document.Blocks[1]).Rows[0].Cells[0].Paragraphs[0];
        });
        ran.Should().BeTrue();

        cellParagraph!.PlainText.Should().Be("see cat here");
        AssertFormatted(cellParagraph, "cat");
    }

    [Fact]
    public async Task ReplaceNext_WithReplaceFormatting_FormatsTheWrittenTextInTheHeader()
    {
        Paragraph? header = null;
        var ran = await OnUiThread(() =>
        {
            var document = DocumentOf(new Paragraph("nothing relevant here"));
            document.Header = new HeaderFooter("see cat here");
            var view = Load(document);

            view.FindNext("cat").Should().BeTrue();
            view.ReplaceNext(Request("cat", "tiger", BoldRed));

            header = view.Document.Header!.Paragraphs[0];
        });
        ran.Should().BeTrue();

        header!.PlainText.Should().Be("see tiger here");
        AssertFormatted(header, "tiger");
    }

    [Fact]
    public async Task ReplaceNext_WithReplaceFormatting_FormatsTheWrittenTextInTheFooter()
    {
        Paragraph? footer = null;
        var ran = await OnUiThread(() =>
        {
            var document = DocumentOf(new Paragraph("nothing relevant here"));
            document.Footer = new HeaderFooter("see cat here");
            var view = Load(document);

            view.FindNext("cat").Should().BeTrue();
            view.ReplaceNext(Request("cat", "tiger", BoldRed));

            footer = view.Document.Footer!.Paragraphs[0];
        });
        ran.Should().BeTrue();

        footer!.PlainText.Should().Be("see tiger here");
        AssertFormatted(footer, "tiger");
    }

    [Fact]
    public async Task ReplaceNext_InATableCell_UndoesAsOneStep()
    {
        string? afterReplace = null;
        string? afterUndo = null;
        var ran = await OnUiThread(() =>
        {
            var view = Load(DocumentOf(new Paragraph("intro"), TableOf("see cat here")));
            string CellText() => ((Table)view.Document.Blocks[1]).Rows[0].Cells[0].Paragraphs[0].PlainText;

            view.FindNext("cat");
            view.ReplaceNext(Request("cat", "tiger", BoldRed));
            afterReplace = CellText();
            view.Undo();
            afterUndo = CellText();
        });
        ran.Should().BeTrue();

        afterReplace.Should().Be("see tiger here");
        afterUndo.Should().Be("see cat here", "the text and its formatting are one undoable Replace");
    }

    // ---- Find Next across paragraph marks ----------------------------------------------------

    [Fact]
    public async Task FindNext_MatchesAcrossAParagraphMark_AndSelectsIt()
    {
        var found = false;
        string? selected = null;
        var ran = await OnUiThread(() =>
        {
            var view = Load(DocumentOf(new Paragraph("alpha"), new Paragraph("beta"), new Paragraph("gamma")));

            found = view.FindNext("a^pb");
            selected = view.SelectedText;
        });
        ran.Should().BeTrue();

        found.Should().BeTrue("a paragraph mark is matchable, as in Word's Find (^p)");
        selected.Should().StartWith("a").And.EndWith("b").And.HaveLength(3, "the mark between them is selected");
    }

    [Fact]
    public async Task FindNext_ParagraphMarkAlone_SelectsTheMarkAndAdvancesToTheNextOne()
    {
        var firstFound = false;
        var secondFound = false;
        var thirdFound = true;
        var ran = await OnUiThread(() =>
        {
            var view = Load(DocumentOf(new Paragraph("one"), new Paragraph("two"), new Paragraph("three")));

            firstFound = view.FindNext("^p");
            secondFound = view.FindNext("^p");

            // Two marks exist between three paragraphs; the walk wraps back to the first, it never stalls.
            thirdFound = view.FindNext("^p");
        });
        ran.Should().BeTrue();

        firstFound.Should().BeTrue();
        secondFound.Should().BeTrue();
        thirdFound.Should().BeTrue("Find Next wraps to the first mark again");
    }

    [Fact]
    public async Task FindNext_MatchesAcrossAParagraphMark_InsideATableCell()
    {
        var found = false;
        string? selected = null;
        var ran = await OnUiThread(() =>
        {
            var view = Load(DocumentOf(new Paragraph("intro"), TableOf("alpha", "beta")));

            found = view.FindNext("a^pb");
            selected = view.SelectedText;
        });
        ran.Should().BeTrue();

        found.Should().BeTrue();
        selected.Should().StartWith("a").And.EndWith("b").And.HaveLength(3);
    }

    [Fact]
    public async Task FindNext_AParagraphMarkAtTheEndOfAFlow_IsNotMatched()
    {
        var found = true;
        var ran = await OnUiThread(() =>
        {
            // The last paragraph of the document has no following paragraph to run into.
            var view = Load(DocumentOf(new Paragraph("alpha"), new Paragraph("beta")));

            found = view.FindNext("beta^p");
        });
        ran.Should().BeTrue();

        found.Should().BeFalse("the final paragraph mark is not a paragraph-spanning match");
    }

    [Fact]
    public async Task ReplaceNext_OfAMatchAcrossParagraphMarks_InsideATableCell_JoinsTheCellParagraphs()
    {
        string[]? cellTexts = null;
        var ran = await OnUiThread(() =>
        {
            var view = Load(DocumentOf(new Paragraph("intro"), TableOf("alpha", "beta")));

            view.FindNext("a^pb").Should().BeTrue();
            view.ReplaceNext(Request("a^pb", "-", BoldRed));

            cellTexts = ((Table)view.Document.Blocks[1]).Rows[0].Cells[0].Paragraphs
                .Select(paragraph => paragraph.PlainText).ToArray();
        });
        ran.Should().BeTrue();

        cellTexts.Should().Equal("alph-eta");
    }

    [Fact]
    public async Task ReplaceNext_OfAMatchAcrossParagraphMarks_JoinsTheParagraphsAndMovesOn()
    {
        string[]? texts = null;
        var ran = await OnUiThread(() =>
        {
            var view = Load(DocumentOf(
                new Paragraph("end here end"), new Paragraph("next end"), new Paragraph("last")));

            view.FindNext("end^p").Should().BeTrue();
            view.ReplaceNext(Request("end^p", "END. "));
            view.ReplaceNext(Request("end^p", "END. "));

            texts = view.Document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText).ToArray();
        });
        ran.Should().BeTrue();

        texts.Should().Equal("end here END. next END. last");
    }

    [Fact]
    public async Task ReplaceNext_WithAPageBreakCode_WritesABreakRunNotTheControlCharacter()
    {
        Paragraph[]? paragraphs = null;
        var ran = await OnUiThread(() =>
        {
            var view = Load(DocumentOf(new Paragraph("one; two")));

            view.FindNext("; ").Should().BeTrue();
            view.ReplaceNext(Request("; ", "^m"));

            paragraphs = view.Document.Blocks.OfType<Paragraph>().ToArray();
        });
        ran.Should().BeTrue();

        string.Concat(paragraphs!.Select(paragraph => paragraph.PlainText)).Should().Be("onetwo");
        paragraphs!.SelectMany(paragraph => paragraph.Runs).Should().ContainSingle(run => run.IsPageBreak);
        paragraphs!.SelectMany(paragraph => paragraph.Runs).Should().NotContain(run => run.Text.Contains('\f'));
    }

    [Fact]
    public async Task ReplaceNext_WithNumericCodes_WritesThoseCharacters_AndFindWhatReadsThem()
    {
        string? text = null;
        var ran = await OnUiThread(() =>
        {
            var view = Load(DocumentOf(new Paragraph("Terms (c) apply — always")));

            view.FindNext("(c)").Should().BeTrue();
            view.ReplaceNext(Request("(c)", "^0169"));
            view.FindNext("^u8212").Should().BeTrue();
            view.ReplaceNext(Request("^u8212", "^0150"));

            text = ((Paragraph)view.Document.Blocks[0]).PlainText;
        });
        ran.Should().BeTrue();

        text.Should().Be("Terms © apply – always");
    }

    // ---- graphics and comment marks (no text extent) -----------------------------------------

    private static Run Image(byte id) => Run.FromImage(new InlineImage([id], 10, 10));

    private static Paragraph Marked(params Run[] runs)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.AddRange(runs);
        return paragraph;
    }

    [Fact]
    public async Task FindNext_OnGraphicsAndCommentMarks_ParksTheCaretOnEachObjectInTurn()
    {
        var graphics = new List<(int Block, int Offset)>();
        var comments = new List<(int Block, int Offset)>();
        var found = new List<bool>();
        var ran = await OnUiThread(() =>
        {
            var document = DocumentOf(Marked(
                new Run("a"), Image(1), new Run("b"), Run.CommentReference(1), Image(2), new Run("c")));
            document.Comments[1] = new Comment(1, "Please revise", "Ann Reviewer", "AR");
            var view = Load(document);

            for (var i = 0; i < 3; i++)
            {
                found.Add(view.FindNext("^g"));
                graphics.Add(view.CaretPosition);
            }

            for (var i = 0; i < 2; i++)
            {
                found.Add(view.FindNext("^a"));
                comments.Add(view.CaretPosition);
            }
        });
        ran.Should().BeTrue();

        found.Should().OnlyContain(hit => hit);
        // Two graphics and a comment mark at plain offsets 1, 2 and 2: Find Next walks them in turn, and wraps.
        graphics.Should().Equal((0, 1), (0, 2), (0, 1));
        comments.Should().Equal((0, 2), (0, 2));
    }

    [Fact]
    public async Task ReplaceNext_OnAGraphic_ConsumesItLikeReplaceAll_AndMovesOnToTheNextOne()
    {
        string? text = null;
        byte[] remaining = [];
        var results = new List<bool>();
        var ran = await OnUiThread(() =>
        {
            var view = Load(DocumentOf(Marked(
                new Run("a"), Image(1), new Run("b"), Image(2), new Run("c"))));

            view.FindNext("^g").Should().BeTrue();
            results.Add(view.ReplaceNext(Request("^g", "[x]")));
            var afterFirst = (Paragraph)view.Document.Blocks[0];
            afterFirst.Runs.Count(run => run.Image != null).Should().Be(1, "a single Replace consumes only the graphic it is on");
            results.Add(view.ReplaceNext(Request("^g", "[x]")));

            var paragraph = (Paragraph)view.Document.Blocks[0];
            text = paragraph.PlainText;
            remaining = paragraph.Runs.Where(run => run.Image != null).Select(run => run.Image!.Bytes[0]).ToArray();
        });
        ran.Should().BeTrue();

        results.Should().Equal(true, false);
        text.Should().Be("a[x]b[x]c");
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task ReplaceNext_OnACommentMark_ConsumesIt()
    {
        Paragraph? paragraph = null;
        var ran = await OnUiThread(() =>
        {
            var document = DocumentOf(Marked(new Run("a"), Run.CommentReference(1), new Run("b")));
            document.Comments[1] = new Comment(1, "Please revise", "Ann Reviewer", "AR");
            var view = Load(document);

            view.FindNext("^a").Should().BeTrue();
            view.ReplaceNext(Request("^a", "|"));

            paragraph = (Paragraph)view.Document.Blocks[0];
        });
        ran.Should().BeTrue();

        paragraph!.PlainText.Should().Be("a|b");
        paragraph.Runs.Should().NotContain(run => run.IsCommentReference);
    }

    [Fact]
    public async Task ReplaceNext_AfterFindNextOnAGraphic_ForADifferentSearch_LeavesTheGraphicAlone()
    {
        int graphics = 0;
        var ran = await OnUiThread(() =>
        {
            var view = Load(DocumentOf(Marked(new Run("a"), Image(1), new Run("b"))));

            view.FindNext("^g").Should().BeTrue();
            view.ReplaceNext(Request("b", "X"));

            graphics = ((Paragraph)view.Document.Blocks[0]).Runs.Count(run => run.Image != null);
        });
        ran.Should().BeTrue();

        graphics.Should().Be(1, "the parked caret is the match of the ^g search only");
    }

    [Fact]
    public async Task FindNext_AndReplaceNext_ReachAGraphicInATableCellAndInTheHeader()
    {
        Paragraph? cellParagraph = null;
        Paragraph? header = null;
        var ran = await OnUiThread(() =>
        {
            var cell = new TableCell();
            cell.Paragraphs.Add(Marked(new Run("x"), Image(1), new Run("y")));
            var row = new TableRow();
            row.Cells.Add(cell);
            var table = new Table();
            table.Rows.Add(row);
            var document = DocumentOf(new Paragraph("intro"), table);
            document.Header = new HeaderFooter();
            document.Header.Paragraphs.Add(Marked(new Run("H"), Image(2), new Run("I")));
            var view = Load(document);

            view.FindNext("^g").Should().BeTrue();
            view.ReplaceNext(Request("^g", "[cell]"));
            view.FindNext("^g").Should().BeTrue();
            view.ReplaceNext(Request("^g", "[hdr]"));

            cellParagraph = ((Table)view.Document.Blocks[1]).Rows[0].Cells[0].Paragraphs[0];
            header = view.Document.Header!.Paragraphs[0];
        });
        ran.Should().BeTrue();

        cellParagraph!.PlainText.Should().Be("x[cell]y");
        cellParagraph.Runs.Should().NotContain(run => run.Image != null);
        header!.PlainText.Should().Be("H[hdr]I");
        header.Runs.Should().NotContain(run => run.Image != null);
    }
}
