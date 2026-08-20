namespace FreeW.Core.Model.Tests;

public sealed class TableLayoutOperationsSetColumnWidthTests
{
    [Fact]
    public void SetColumnWidthUsesGridColumnMappingInARowWithAMergedCell()
    {
        // Row 0: a merged title cell spanning grid columns 0-1, plus a plain cell at grid column 2.
        var mergedRow = new TableRow();
        mergedRow.Cells.Add(new TableCell("Title") { GridSpan = 2 });
        mergedRow.Cells.Add(new TableCell("Plain"));

        // Row 1: three separate cells, one per grid column (no merge).
        var plainRow = new TableRow();
        plainRow.Cells.Add(new TableCell("A"));
        plainRow.Cells.Add(new TableCell("B"));
        plainRow.Cells.Add(new TableCell("C"));

        var table = new Table();
        table.Rows.Add(mergedRow);
        table.Rows.Add(plainRow);

        // Setting grid column 1's width must land on whichever cell actually occupies grid column 1
        // in each row: the merged title cell in row 0 (it spans columns 0-1), and the "B" cell in row 1.
        var result = TableLayoutOperations.SetColumnWidth(table, columnIndex: 1, widthPt: 45.0);

        result.Should().BeTrue();

        mergedRow.Cells[0].WidthPt.Should().Be(45.0,
            "the merged cell spans grid column 1 (StartColumn=0, GridSpan=2) so it is the cell that occupies that column");
        mergedRow.Cells[1].WidthPt.Should().BeNull(
            "the plain cell after the merge sits at grid column 2, not the column being set");

        plainRow.Cells[0].WidthPt.Should().BeNull("cell A is grid column 0, untouched by a column-1 width change");
        plainRow.Cells[1].WidthPt.Should().Be(45.0, "cell B is grid column 1");
        plainRow.Cells[2].WidthPt.Should().BeNull("cell C is grid column 2, untouched by a column-1 width change");
    }

    [Fact]
    public void SetColumnWidthStillAppliesToTheCorrectCellInAUniformTableWithNoMerges()
    {
        var table = Table.Create(rows: 2, columns: 2);

        var result = TableLayoutOperations.SetColumnWidth(table, columnIndex: 1, widthPt: 30.0);

        result.Should().BeTrue();
        foreach (var row in table.Rows)
        {
            row.Cells[0].WidthPt.Should().BeNull("column 0 was not the one whose width changed");
            row.Cells[1].WidthPt.Should().Be(30.0, "column 1 is the one whose width changed");
        }
    }
}
