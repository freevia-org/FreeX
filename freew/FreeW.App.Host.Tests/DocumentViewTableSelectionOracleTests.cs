using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfTable = System.Windows.Documents.Table;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Runtime evidence for the native WPF RichTextBox selection route used as the Avalonia parity
/// oracle. These tests intentionally mutate the live FlowDocument through TextSelection.Text, then
/// commit it back to the FreeW model so the observed behavior is not inferred from model helpers.
/// </summary>
public sealed class DocumentViewTableSelectionOracleTests
{
    [StaFact]
    public void Native_wpf_delete_within_cell_across_paragraphs_joins_text_and_keeps_one_paragraph()
    {
        var model = TableDocument(1, 1);
        var cell = model.Blocks.OfType<Table>().Single().Rows[0].Cells[0];
        cell.Paragraphs.Clear();
        cell.Paragraphs.Add(new Paragraph("AB"));
        cell.Paragraphs.Add(new Paragraph("CD"));
        var view = Load(model);
        var wpfCell = RenderedTable(view).RowGroups[0].Rows[0].Cells[0];
        var paragraphs = wpfCell.Blocks.OfType<WpfParagraph>().ToList();

        view.Selection.Select(Position(paragraphs[0], 1), Position(paragraphs[1], 1));
        view.Selection.Text.Should().NotBeEmpty("the oracle must select the cross-paragraph text");
        view.Selection.Text = string.Empty;
        view.Selection.Text.Should().BeEmpty("WPF must delete the selected FlowDocument text");
        LiveText(wpfCell.Blocks).Should().Be("AD", "the live WPF cell should show the deletion");
        view.CommitToModel();

        var resultCell = view.Model.Blocks.OfType<Table>().Single().Rows[0].Cells[0];
        resultCell.Paragraphs.Should().ContainSingle();
        resultCell.PlainText.Should().Be("AD");
    }

    [StaFact]
    public void Native_wpf_cross_cell_selection_normalizes_to_whole_cells_and_preserves_structure()
    {
        var model = TableDocument(1, 3);
        var table = model.Blocks.OfType<Table>().Single();
        table.Rows[0].Cells[0] = new TableCell("Axx");
        table.Rows[0].Cells[1] = new TableCell("Bmiddle");
        table.Rows[0].Cells[2] = new TableCell("Cyy");
        var view = Load(model);
        var row = RenderedTable(view).RowGroups[0].Rows[0];
        var first = row.Cells[0].Blocks.OfType<WpfParagraph>().Single();
        var last = row.Cells[2].Blocks.OfType<WpfParagraph>().Single();

        view.Selection.Select(Position(first, 1), Position(last, 1));
        view.Selection.Text.Should().Be("Axx\tBmiddle\tCyy\r\n");
        view.Selection.Text = string.Empty;
        view.Selection.Text.Should().BeEmpty("WPF must delete the selected FlowDocument text");
        row.Cells.Select(cell => LiveText(cell.Blocks)).Should().OnlyContain(text => text == string.Empty);
        view.CommitToModel();

        var resultTable = view.Model.Blocks.OfType<Table>().Single();
        resultTable.Rows[0].Cells.Should().HaveCount(3);
        resultTable.Rows[0].Cells.Select(c => c.PlainText).Should().OnlyContain(text => text == string.Empty);
    }

    [StaFact]
    public void Native_wpf_cross_cell_typing_replaces_touched_cells_with_text_in_the_first_cell()
    {
        var model = TableDocument(1, 3);
        var table = model.Blocks.OfType<Table>().Single();
        table.Rows[0].Cells[0] = new TableCell("Axx");
        table.Rows[0].Cells[1] = new TableCell("Bmiddle");
        table.Rows[0].Cells[2] = new TableCell("Cyy");
        var view = Load(model);
        var row = RenderedTable(view).RowGroups[0].Rows[0];
        var first = row.Cells[0].Blocks.OfType<WpfParagraph>().Single();
        var last = row.Cells[2].Blocks.OfType<WpfParagraph>().Single();

        view.Selection.Select(Position(first, 1), Position(last, 1));
        view.Selection.Text.Should().Be("Axx\tBmiddle\tCyy\r\n");
        view.Selection.Text = "Z";
        view.CommitToModel();

        var resultTable = view.Model.Blocks.OfType<Table>().Single();
        resultTable.Rows[0].Cells.Select(c => c.PlainText).Should()
            .Equal("Z", string.Empty, string.Empty);
    }

    [StaFact]
    public void Native_wpf_cross_cell_multiline_selection_text_replacement_keeps_text_in_first_cell()
    {
        var model = TableDocument(1, 3);
        var table = model.Blocks.OfType<Table>().Single();
        table.Rows[0].Cells[0] = new TableCell("Axx");
        table.Rows[0].Cells[1] = new TableCell("Bmiddle");
        table.Rows[0].Cells[2] = new TableCell("Cyy");
        var view = Load(model);
        var row = RenderedTable(view).RowGroups[0].Rows[0];
        var first = row.Cells[0].Blocks.OfType<WpfParagraph>().Single();
        var last = row.Cells[2].Blocks.OfType<WpfParagraph>().Single();

        view.Selection.Select(Position(first, 1), Position(last, 1));
        view.Selection.Text.Should().Be("Axx\tBmiddle\tCyy\r\n");
        view.Selection.Text = "Z\nQ";
        view.CommitToModel();

        var resultTable = view.Model.Blocks.OfType<Table>().Single();
        resultTable.Rows[0].Cells.Select(c => c.PlainText).Should()
            .Equal("Z\nQ", string.Empty, string.Empty);
    }

    [StaFact]
    public void Native_wpf_cross_cell_multiline_paste_uses_the_real_paste_route()
    {
        var model = TableDocument(1, 3);
        var table = model.Blocks.OfType<Table>().Single();
        table.Rows[0].Cells[0] = new TableCell("Axx");
        table.Rows[0].Cells[1] = new TableCell("Bmiddle");
        table.Rows[0].Cells[2] = new TableCell("Cyy");
        var view = Load(model);
        var row = RenderedTable(view).RowGroups[0].Rows[0];
        var first = row.Cells[0].Blocks.OfType<WpfParagraph>().Single();
        var last = row.Cells[2].Blocks.OfType<WpfParagraph>().Single();

        view.Selection.Select(Position(first, 1), Position(last, 1));
        System.Windows.Clipboard.SetText("Z\nQ");
        try
        {
            view.PastePlainText();
        }
        finally
        {
            System.Windows.Clipboard.Clear();
        }

        view.CommitToModel();
        var resultTable = view.Model.Blocks.OfType<Table>().Single();
        resultTable.Rows[0].Cells.Select(c => c.PlainText).Should()
            .Equal("Z\nQ", string.Empty, string.Empty);
    }

    private static DocumentView Load(TextDocument document)
    {
        var view = new DocumentView();
        view.LoadModel(document);
        return view;
    }

    private static TextDocument TableDocument(int rows, int columns)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(Table.Create(rows, columns));
        return document;
    }

    private static WpfTable RenderedTable(DocumentView view) =>
        view.Document.Blocks.OfType<WpfTable>().Single();

    private static System.Windows.Documents.TextPointer Position(WpfParagraph paragraph, int offset)
    {
        var textStart = paragraph.ContentStart;
        while (textStart is not null
            && textStart.GetPointerContext(System.Windows.Documents.LogicalDirection.Forward)
                != System.Windows.Documents.TextPointerContext.Text)
        {
            textStart = textStart.GetNextContextPosition(System.Windows.Documents.LogicalDirection.Forward);
        }

        return textStart?.GetPositionAtOffset(offset, System.Windows.Documents.LogicalDirection.Forward)
            ?? throw new InvalidOperationException("WPF did not expose the requested text position.");
    }

    private static string LiveText(System.Windows.Documents.BlockCollection blocks) =>
        string.Join("|", blocks.OfType<WpfParagraph>()
            .Select(paragraph => new System.Windows.Documents.TextRange(
                paragraph.ContentStart, paragraph.ContentEnd).Text.TrimEnd('\r', '\n')));
}
