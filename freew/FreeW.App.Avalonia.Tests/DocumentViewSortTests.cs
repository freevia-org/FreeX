using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewSortTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Table_sort_adapts_grid_column_after_merged_cell_to_model_cell_index()
    {
        await Session.Dispatch(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var table = Table.Create(0, 0);
            table.Rows.Add(Row("3", "Cherry"));
            table.Rows.Add(Row("1", "Apple"));
            table.Rows.Add(Row("2", "Banana"));
            document.Blocks.Add(table);

            var view = new DocumentView();
            view.LoadDocument(document);
            // Each first cell spans grid columns 0-1, so the second model cell starts at grid column 2.
            view.PlaceCaretInCell(0, row: 0, col: 2, paraIdx: 0, offset: 0);
            view.SortCaretTableRows(
                SortKind.Text,
                ascending: true,
                caseSensitive: false,
                hasHeaderRow: false);

            var sorted = document.Blocks.Should().ContainSingle().Which.Should().BeOfType<Table>().Which;
            sorted.Rows.Select(row => row.Cells[1].PlainText).Should().Equal("Apple", "Banana", "Cherry");
            sorted.Rows.Select(row => row.Cells[0].PlainText).Should().Equal("1", "2", "3");
        }, CancellationToken.None);
    }

    private static TableRow Row(string rank, string value)
    {
        var row = new TableRow();
        row.Cells.Add(new TableCell(rank) { GridSpan = 2 });
        row.Cells.Add(new TableCell(value));
        return row;
    }
}
