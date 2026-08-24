using Free.Shared.Ribbon;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using Free.Shared.AppServices;
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
        var clipboard = new FixedTextClipboard("Z\nQ");
        var view = Load(model, clipboard);
        var row = RenderedTable(view).RowGroups[0].Rows[0];
        var first = row.Cells[0].Blocks.OfType<WpfParagraph>().Single();
        var last = row.Cells[2].Blocks.OfType<WpfParagraph>().Single();

        view.Selection.Select(Position(first, 1), Position(last, 1));
        view.PastePlainText();

        view.CommitToModel();
        var resultTable = view.Model.Blocks.OfType<Table>().Single();
        resultTable.Rows[0].Cells.Select(c => c.PlainText).Should()
            .Equal("Z\nQ", string.Empty, string.Empty);
    }

    [StaFact]
    public void SelectTable_selects_every_rendered_cell_without_mutating_the_model()
    {
        var view = Load(SelectionTableDocument());
        var rows = RenderedTable(view).RowGroups[0].Rows;
        view.CaretPosition = rows[1].Cells[1].Blocks.FirstBlock!.ContentStart;

        view.SelectTable();

        view.Selection.Text.Should().ContainAll("A", "B", "C", "D", "E", "F");
        view.Model.Blocks.OfType<Table>().Single().Rows.SelectMany(row => row.Cells)
            .Select(cell => cell.PlainText).Should().Equal("A", "B", "C", "D", "E", "F");
    }

    [StaFact]
    public void SelectTableRow_selects_only_the_caret_row()
    {
        var view = Load(SelectionTableDocument());
        var rows = RenderedTable(view).RowGroups[0].Rows;
        view.CaretPosition = rows[1].Cells[1].Blocks.FirstBlock!.ContentStart;

        view.SelectTableRow();

        view.Selection.Text.Should().ContainAll("D", "E", "F");
        view.Selection.Text.Should().NotContainAny("A", "B", "C");
    }

    [StaFact]
    public void SelectTableColumn_selects_only_the_caret_column()
    {
        var view = Load(SelectionTableDocument());
        var rows = RenderedTable(view).RowGroups[0].Rows;
        view.CaretPosition = rows[0].Cells[1].Blocks.FirstBlock!.ContentStart;

        view.SelectTableColumn();

        view.Selection.Text.Should().ContainAll("B", "E");
        view.Selection.Text.Should().NotContainAny("A", "C", "D", "F");
    }

    [StaFact]
    public void SelectTableCell_selects_only_the_caret_cell()
    {
        var view = Load(SelectionTableDocument());
        var rows = RenderedTable(view).RowGroups[0].Rows;
        view.CaretPosition = rows[1].Cells[1].Blocks.FirstBlock!.ContentStart;

        view.SelectTableCell();

        view.Selection.Text.Should().Contain("E");
        view.Selection.Text.Should().NotContainAny("A", "B", "C", "D", "F");
    }

    [StaTheory]
    [InlineData("freew.table-select-table", "ABCDEF", "")]
    [InlineData("freew.table-select-row", "DEF", "ABC")]
    [InlineData("freew.table-select-col", "BE", "ACDF")]
    [InlineData("freew.table-select-cell", "E", "ABCDF")]
    public void Table_selection_ribbon_commands_execute_the_native_selection_route(
        string commandId,
        string included,
        string excluded)
    {
        var view = Load(SelectionTableDocument());
        var rows = RenderedTable(view).RowGroups[0].Rows;
        view.CaretPosition = rows[1].Cells[1].Blocks.FirstBlock!.ContentStart;
        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());
        registry.TryGet(commandId, out var command).Should().BeTrue();

        command!.Execute(RibbonCommandContext.Empty);

        foreach (var character in included)
            view.Selection.Text.Should().Contain(character.ToString());
        foreach (var character in excluded)
            view.Selection.Text.Should().NotContain(character.ToString());
    }

    private static DocumentView Load(TextDocument document, IPlatformClipboard? clipboard = null)
    {
        var view = new DocumentView(clipboard);
        view.LoadModel(document);
        return view;
    }

    private sealed class FixedTextClipboard(string text) : IPlatformClipboard
    {
        public ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
            PlatformClipboardReadRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardReadResult<PlatformClipboardContent>.Success(
                new PlatformClipboardContent(Text: text)));

        public ValueTask<PlatformClipboardWriteResult> WriteAsync(
            PlatformClipboardContent content,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardWriteResult.Success());

        public ValueTask<PlatformClipboardWriteResult> ClearAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PlatformClipboardWriteResult.Success());
    }

    private static TextDocument TableDocument(int rows, int columns)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(Table.Create(rows, columns));
        return document;
    }

    private static TextDocument SelectionTableDocument()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var table = Table.Create(2, 3);
        var labels = new[] { "A", "B", "C", "D", "E", "F" };
        var labelIndex = 0;
        foreach (var row in table.Rows)
        {
            for (var column = 0; column < row.Cells.Count; column++)
                row.Cells[column] = new TableCell(labels[labelIndex++]);
        }
        document.Blocks.Add(table);
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
