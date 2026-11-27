using Free.Shared.AppServices;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

/// <summary>
/// WPF reference behaviour for interactive Find Next and single Replace: Replace with formatting formats
/// the written text in a table cell and a header/footer exactly as Replace All does, and Find Next finds
/// and selects a match that runs across paragraph marks. The Avalonia host is held to the same cases in
/// FreeW.App.Avalonia.Tests.FindReplaceSingleMatchTests.
/// </summary>
public sealed class FindReplaceSingleMatchHostTests
{
    private static readonly FindFormat BoldRed = new() { Bold = true, ColorHex = "FF0000" };

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

    private static DocumentView Load(Action<TextDocument> configure, params Block[] blocks)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var block in blocks)
            document.Blocks.Add(block);
        configure(document);

        var view = new DocumentView();
        view.LoadModel(document);
        return view;
    }

    private static void WithDialog(DocumentView view, string find, string replace, Action<FindReplaceDialog> act)
    {
        var dialog = new FindReplaceDialog(null!, view, FindReplaceOpenMode.Replace);
        try
        {
            dialog.Show();
            dialog.SetFindTextForTest(find);
            dialog.SetReplaceTextForTest(replace);
            act(dialog);
        }
        finally
        {
            dialog.Close();
        }
    }

    private static void AssertFormatted(Paragraph paragraph, string text)
    {
        var run = paragraph.Runs.Single(candidate => candidate.Text == text);
        run.Formatting.Bold.Should().BeTrue("the Replace with formatting applies to the written text");
        FindFormat.SameColor(run.Formatting.ColorHex, "#FF0000").Should().BeTrue();
        paragraph.Runs.Where(candidate => candidate.Text != text)
            .Should().OnlyContain(candidate => !candidate.Formatting.Bold, "text around the match stays as it was");
    }

    [StaFact]
    public void Replace_WithReplaceFormatting_FormatsTheWrittenTextInATableCell()
    {
        var view = Load(_ => { }, new Paragraph("intro"), TableOf("see cat here"));

        WithDialog(view, "cat", "tiger", dialog =>
        {
            dialog.SetFormatForTest(FindReplaceDialogFieldKind.Replace, BoldRed);
            dialog.FindNextForTest();
            dialog.ReplaceForTest();
        });

        view.CommitToModel();
        var paragraph = view.Model.Blocks.OfType<Table>().Single().Rows[0].Cells[0].Paragraphs[0];
        paragraph.PlainText.Should().Be("see tiger here");
        AssertFormatted(paragraph, "tiger");
    }

    [StaFact]
    public void Replace_WithReplaceFormatting_InACellParagraphWithTabs_FormatsTheWrittenText()
    {
        // The tab is one character in the model and a tab-stop element on the surface; the rendered
        // selection must still land on the same model offsets.
        var view = Load(_ => { }, new Paragraph("intro"), TableOf("a\tb\tsee cat here"));

        WithDialog(view, "cat", "tiger", dialog =>
        {
            dialog.SetFormatForTest(FindReplaceDialogFieldKind.Replace, BoldRed);
            dialog.FindNextForTest();
            dialog.ReplaceForTest();
        });

        view.CommitToModel();
        var paragraph = view.Model.Blocks.OfType<Table>().Single().Rows[0].Cells[0].Paragraphs[0];
        paragraph.PlainText.Should().Be("a\tb\tsee tiger here");
        AssertFormatted(paragraph, "tiger");
    }

    [StaFact]
    public void Replace_WithReplaceFormatting_AfterATabInASecondCellParagraph_FormatsTheWrittenText()
    {
        var view = Load(_ => { }, TableOf("first", "x\ty cat z"));

        WithDialog(view, "cat", "tiger", dialog =>
        {
            dialog.SetFormatForTest(FindReplaceDialogFieldKind.Replace, BoldRed);
            dialog.FindNextForTest();
            dialog.ReplaceForTest();
        });

        view.CommitToModel();
        var cell = view.Model.Blocks.OfType<Table>().Single().Rows[0].Cells[0];
        cell.Paragraphs[0].PlainText.Should().Be("first");
        cell.Paragraphs[1].PlainText.Should().Be("x\ty tiger z");
        AssertFormatted(cell.Paragraphs[1], "tiger");
    }

    [StaFact]
    public void Replace_WithReplaceFormatting_InAListParagraphOfACell_FormatsTheWrittenText()
    {
        var item = new Paragraph("item cat") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet } };
        var table = TableOf("lead");
        table.Rows[0].Cells[0].Paragraphs.Add(item);
        table.Rows[0].Cells[0].Paragraphs.Add(new Paragraph("tail"));
        var view = Load(_ => { }, table);

        WithDialog(view, "cat", "tiger", dialog =>
        {
            dialog.SetFormatForTest(FindReplaceDialogFieldKind.Replace, BoldRed);
            dialog.FindNextForTest();
            dialog.ReplaceForTest();
        });

        view.CommitToModel();
        var cell = view.Model.Blocks.OfType<Table>().Single().Rows[0].Cells[0];
        cell.Paragraphs.Select(paragraph => paragraph.PlainText).Should().Equal("lead", "item tiger", "tail");
        AssertFormatted(cell.Paragraphs[1], "tiger");
    }

    [StaTheory]
    [InlineData(ListKind.None, "item cat here")]
    [InlineData(ListKind.Bullet, "item cat here")]
    [InlineData(ListKind.Number, "item cat here")]
    [InlineData(ListKind.MultiLevel, "item cat here")]
    [InlineData(ListKind.Bullet, "a\tb\titem cat here")]
    [InlineData(ListKind.Number, "a\tb\titem cat here")]
    public void FindNext_InABodyListParagraph_SelectsTheMatchAtItsModelOffsets_AndReplaceFormatsIt(ListKind kind, string text)
    {
        var item = new Paragraph(text) { Formatting = ParagraphFormatting.Default with { ListKind = kind } };
        var view = Load(_ => { }, new Paragraph("intro"), item, new Paragraph("tail"));
        var expectedStart = text.IndexOf("cat", StringComparison.Ordinal);

        WithDialog(view, "cat", "tiger", dialog =>
        {
            dialog.SetFormatForTest(FindReplaceDialogFieldKind.Replace, BoldRed);
            dialog.FindNextForTest();

            var range = view.FindReplaceSelectionRange();
            range.Should().NotBeNull();
            range!.Value.Start.BlockIndex.Should().Be(1);
            range.Value.Start.Offset.Should().Be(expectedStart, "the list marker and tab chrome are not model characters");
            range.Value.End.Offset.Should().Be(expectedStart + 3);

            dialog.ReplaceForTest();
        });

        view.CommitToModel();
        var paragraphs = view.Model.Blocks.OfType<Paragraph>().ToList();
        paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().Equal("intro", text.Replace("cat", "tiger", StringComparison.Ordinal), "tail");
        AssertFormatted(paragraphs[1], "tiger");
    }

    [StaFact]
    public void FindNext_DoesNotSeeTextInANestedTable_BecauseTheSurfaceHostsItReadOnly()
    {
        // A nested table is rendered inside a read-only hosted editor (the outer editor never places a
        // caret there), so Find Next / single Replace can never be positioned in one; Replace All
        // reaches it through the model. This pins why the nested-table case needs no surface mapping.
        var outer = TableOf("outer text");
        outer.Rows[0].Cells[0].NestedTables.Add(TableOf("nested cat"));
        var view = Load(_ => { }, outer);

        view.BuildFindReplaceProjection(includeFormatting: false).Text.Should().NotContain("nested");
    }

    [StaFact]
    public void Replace_WithReplaceFormatting_FormatsTheWrittenTextInTheHeader()
    {
        var view = Load(document => document.Header = new HeaderFooter("see cat here"), new Paragraph("nothing relevant"));

        WithDialog(view, "cat", "tiger", dialog =>
        {
            dialog.SetFormatForTest(FindReplaceDialogFieldKind.Replace, BoldRed);
            dialog.FindNextForTest();
            dialog.ReplaceForTest();
        });

        var header = view.Model.Header!.Paragraphs[0];
        header.PlainText.Should().Be("see tiger here");
        AssertFormatted(header, "tiger");
    }

    [StaFact]
    public void Replace_WithReplaceFormatting_FormatsTheWrittenTextInTheFooter()
    {
        var view = Load(document => document.Footer = new HeaderFooter("see cat here"), new Paragraph("nothing relevant"));

        WithDialog(view, "cat", "tiger", dialog =>
        {
            dialog.SetFormatForTest(FindReplaceDialogFieldKind.Replace, BoldRed);
            dialog.FindNextForTest();
            dialog.ReplaceForTest();
        });

        var footer = view.Model.Footer!.Paragraphs[0];
        footer.PlainText.Should().Be("see tiger here");
        AssertFormatted(footer, "tiger");
    }

    [StaFact]
    public void FindNext_MatchesAcrossAParagraphMark_AndReplaceJoinsTheParagraphs()
    {
        var view = Load(_ => { }, new Paragraph("alpha"), new Paragraph("beta"), new Paragraph("gamma"));

        WithDialog(view, "a^pb", "-", dialog =>
        {
            dialog.FindNextForTest();
            view.Selection.IsEmpty.Should().BeFalse("the match across the paragraph mark is selected");
            dialog.ReplaceForTest();
        });

        view.CommitToModel();
        view.Model.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal("alph-eta", "gamma");
    }

    private static Paragraph CommentedParagraph(int id, string before, string after)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(before));
        paragraph.Runs.Add(Run.CommentReference(id));
        paragraph.Runs.Add(new Run(after));
        return paragraph;
    }

    private static void AddComments(TextDocument document, params int[] ids)
    {
        foreach (var id in ids)
            document.Comments[id] = new Comment(id, "Please revise", "Ann Reviewer", "AR");
    }

    [StaFact]
    public void Replace_ConsumesACommentMark_LikeReplaceAll_AndMovesOnToTheNext()
    {
        var view = Load(
            document => AddComments(document, 1, 2),
            CommentedParagraph(1, "one ", " two"),
            CommentedParagraph(2, "three ", " four"));

        WithDialog(view, "^a", "|", dialog =>
        {
            dialog.FindNextForTest();
            dialog.ReplaceForTest();
            view.CommitToModel();
            var afterFirst = view.Model.Blocks.OfType<Paragraph>().ToList();
            afterFirst[0].PlainText.Should().Be("one | two", "a single Replace consumes the comment mark it is on");
            afterFirst[0].Runs.Should().NotContain(run => run.IsCommentReference);
            afterFirst[1].Runs.Should().ContainSingle(run => run.IsCommentReference, "only the match was replaced");

            dialog.ReplaceForTest();
        });

        view.CommitToModel();
        var paragraphs = view.Model.Blocks.OfType<Paragraph>().ToList();
        paragraphs.Select(paragraph => paragraph.PlainText).Should().Equal("one | two", "three | four");
        paragraphs.SelectMany(paragraph => paragraph.Runs).Should().NotContain(run => run.IsCommentReference);
    }

    [StaFact]
    public void FindNext_OnACommentMark_AdvancesToTheNextOneInsteadOfRepeatingIt()
    {
        var view = Load(
            document => AddComments(document, 1, 2),
            CommentedParagraph(1, "one ", " two"),
            CommentedParagraph(2, "three ", " four"));

        // Replacing after two Find Nexts must act on the SECOND mark, not the first again.
        WithDialog(view, "^a", string.Empty, dialog =>
        {
            dialog.FindNextForTest();
            dialog.FindNextForTest();
            dialog.ReplaceForTest();
        });

        view.CommitToModel();
        var paragraphs = view.Model.Blocks.OfType<Paragraph>().ToList();
        paragraphs[0].Runs.Should().ContainSingle(run => run.IsCommentReference, "the first mark was stepped past");
        paragraphs[1].Runs.Should().NotContain(run => run.IsCommentReference);
        paragraphs[1].PlainText.Should().Be("three  four");
    }
}
