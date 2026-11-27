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
}
