namespace FreeW.Core.Model.Tests;

public sealed class TableGridProjectionTests
{
    [Fact]
    public void ProjectRowNormalizesMalformedSpansAndMapsBothDirections()
    {
        var row = new TableRow();
        row.Cells.Add(new TableCell { GridSpan = 0 });
        row.Cells.Add(new TableCell { GridSpan = 3 });
        row.Cells.Add(new TableCell { GridSpan = -4 });

        var projection = TableGridProjection.ProjectRow(row);

        projection.Select(cell => (cell.CellIndex, cell.StartColumn, cell.Span))
            .Should().Equal((0, 0, 1), (1, 1, 3), (2, 4, 1));
        TableGridProjection.RowWidth(row).Should().Be(5);
        TableGridProjection.StartColumn(row, 2).Should().Be(4);
        TableGridProjection.At(row, 3)?.CellIndex.Should().Be(1);
        TableGridProjection.At(row, 5).Should().BeNull();
        TableGridProjection.StartingAt(row, 2).Should().BeNull();
    }

    [Fact]
    public void TableWidthUsesWidestLogicalRow()
    {
        var table = new Table();
        var narrow = new TableRow();
        narrow.Cells.Add(new TableCell { GridSpan = 1 });
        var wide = new TableRow();
        wide.Cells.Add(new TableCell { GridSpan = 2 });
        wide.Cells.Add(new TableCell { GridSpan = 3 });
        table.Rows.Add(narrow);
        table.Rows.Add(wide);

        TableGridProjection.TableWidth(table).Should().Be(5);
    }
}
