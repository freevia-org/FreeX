using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// STA coverage for <see cref="DocumentView.SortSelectedParagraphs(SortKind, bool, bool, bool)"/> and
/// <see cref="DocumentView.SortCaretTableRows"/>: load a model, place a selection/caret, sort, and assert
/// the model's block/row order changes through the (reversible) command bus. These need STA + a
/// Dispatcher for the RichTextBox/FlowDocument, so they run as <c>[StaFact]</c>.
/// </summary>
public sealed class SortSelectionTests
{
    private static DocumentView ViewWith(TextDocument doc)
    {
        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static TextDocument DocOfParagraphs(params string[] texts)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        foreach (var text in texts)
            doc.Blocks.Add(new Paragraph(text));
        return doc;
    }

    // Select from the first rendered paragraph's start to the last paragraph's end so both selection
    // endpoints resolve to WPF paragraphs (ContentEnd lands past the last paragraph and would not).
    private static void SelectAllParagraphs(DocumentView view)
    {
        var paragraphs = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().ToList();
        view.Selection.Select(paragraphs[0].ContentStart, paragraphs[^1].ContentEnd);
    }

    [StaFact]
    public void SortSelectedParagraphs_Ascending_ReordersSelectedBlocks()
    {
        var view = ViewWith(DocOfParagraphs("Charlie", "alpha", "Bravo"));

        // Select the whole body, then sort A→Z.
        SelectAllParagraphs(view);
        view.SortSelectedParagraphs(SortKind.Text, ascending: true, caseSensitive: false, hasHeaderRow: false);

        view.Model.Blocks.OfType<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("alpha", "Bravo", "Charlie");
    }

    [StaFact]
    public void SortSelectedParagraphs_Descending_ReversesOrder()
    {
        var view = ViewWith(DocOfParagraphs("alpha", "Charlie", "Bravo"));

        SelectAllParagraphs(view);
        view.SortSelectedParagraphs(SortKind.Text, ascending: false, caseSensitive: false, hasHeaderRow: false);

        view.Model.Blocks.OfType<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("Charlie", "Bravo", "alpha");
    }

    [StaFact]
    public void SortSelectedParagraphs_Number_OrdersNumerically()
    {
        var view = ViewWith(DocOfParagraphs("10", "2", "1"));

        SelectAllParagraphs(view);
        view.SortSelectedParagraphs(SortKind.Number, ascending: true, caseSensitive: false, hasHeaderRow: false);

        view.Model.Blocks.OfType<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("1", "2", "10");
    }

    [StaFact]
    public void SortSelectedParagraphs_HasHeaderRow_PinsFirstParagraph()
    {
        var view = ViewWith(DocOfParagraphs("Name", "Charlie", "alpha", "Bravo"));

        SelectAllParagraphs(view);
        view.SortSelectedParagraphs(SortKind.Text, ascending: true, caseSensitive: false, hasHeaderRow: true);

        view.Model.Blocks.OfType<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("Name", "alpha", "Bravo", "Charlie");
    }

    [StaFact]
    public void SortSelectedParagraphs_Undo_RestoresOriginalOrder()
    {
        var view = ViewWith(DocOfParagraphs("Charlie", "alpha", "Bravo"));

        SelectAllParagraphs(view);
        view.SortSelectedParagraphs(SortKind.Text, ascending: true, caseSensitive: false, hasHeaderRow: false);
        view.Commands.Undo();

        view.Model.Blocks.OfType<Paragraph>().Select(p => p.PlainText)
            .Should().Equal("Charlie", "alpha", "Bravo");
    }

    [StaFact]
    public void SortCaretTableRows_SortsRowsByCaretColumn()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(0, 0);
        table.Rows.Add(TableRowOf("3", "Cherry"));
        table.Rows.Add(TableRowOf("1", "Apple"));
        table.Rows.Add(TableRowOf("2", "Banana"));
        doc.Blocks.Add(table);

        var view = ViewWith(doc);

        // Place the caret in column 1 of the first rendered cell-row so the sort key is the fruit name.
        var firstRowSecondCell = FirstTableSecondColumnStart(view);
        view.CaretPosition = firstRowSecondCell;
        view.SortCaretTableRows(SortKind.Text, ascending: true, caseSensitive: false, hasHeaderRow: false);

        var resultTable = view.Model.Blocks.OfType<Table>().Single();
        resultTable.Rows.Select(r => r.Cells[1].PlainText).Should().Equal("Apple", "Banana", "Cherry");
        // The companion (rank) column travels with its row.
        resultTable.Rows.Select(r => r.Cells[0].PlainText).Should().Equal("1", "2", "3");
    }

    [StaFact]
    public void SortCaretTableRows_UsesModelCellIndexAfterMergedCell()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(0, 0);
        table.Rows.Add(MergedTableRowOf("3", "Cherry"));
        table.Rows.Add(MergedTableRowOf("1", "Apple"));
        table.Rows.Add(MergedTableRowOf("2", "Banana"));
        doc.Blocks.Add(table);

        var view = ViewWith(doc);
        view.CaretPosition = FirstTableSecondColumnStart(view);
        view.SortCaretTableRows(SortKind.Text, ascending: true, caseSensitive: false, hasHeaderRow: false);

        var resultTable = view.Model.Blocks.OfType<Table>().Single();
        resultTable.Rows.Select(r => r.Cells[1].PlainText).Should().Equal("Apple", "Banana", "Cherry");
        resultTable.Rows.Select(r => r.Cells[0].PlainText).Should().Equal("1", "2", "3");
    }

    // The content start of the second cell of the first rendered table row, so the caret's column is 1.
    private static System.Windows.Documents.TextPointer FirstTableSecondColumnStart(DocumentView view)
    {
        var table = view.Document.Blocks.OfType<System.Windows.Documents.Table>().First();
        var firstRow = table.RowGroups[0].Rows[0];
        return firstRow.Cells[1].ContentStart;
    }

    private static TableRow TableRowOf(params string[] cells)
    {
        var row = new TableRow();
        foreach (var text in cells)
            row.Cells.Add(new TableCell(text));
        return row;
    }

    private static TableRow MergedTableRowOf(string rank, string value)
    {
        var row = new TableRow();
        row.Cells.Add(new TableCell(rank) { GridSpan = 2 });
        row.Cells.Add(new TableCell(value));
        return row;
    }
}
