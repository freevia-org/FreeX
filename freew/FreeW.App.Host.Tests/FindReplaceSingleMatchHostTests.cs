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
}
