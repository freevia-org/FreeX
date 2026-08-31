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

    [Fact]
    public void PointQueries_DenseRowMatchMaterializedProjection()
    {
        const int cellCount = 512;
        var row = new TableRow();
        for (var index = 0; index < cellCount; index++)
            row.Cells.Add(new TableCell { GridSpan = index % 5 - 1 });

        var projection = TableGridProjection.ProjectRow(row);
        projection.Should().HaveCount(cellCount);
        foreach (var expected in projection)
        {
            TableGridProjection.StartColumn(row, expected.CellIndex).Should().Be(expected.StartColumn);
            TableGridProjection.At(row, expected.StartColumn).Should().Be(expected);
            TableGridProjection.At(row, expected.EndColumnExclusive - 1).Should().Be(expected);
            TableGridProjection.StartingAt(row, expected.StartColumn).Should().Be(expected);
            TableGridProjection.InsertionIndex(row, expected.StartColumn).Should().Be(expected.CellIndex);
        }

        TableGridProjection.At(row, -1).Should().BeNull();
        TableGridProjection.At(row, TableGridProjection.RowWidth(row)).Should().BeNull();
        TableGridProjection.StartingAt(row, 4).Should().BeNull();
        TableGridProjection.InsertionIndex(row, -1).Should().Be(0);
        TableGridProjection.InsertionIndex(row, TableGridProjection.RowWidth(row)).Should().Be(cellCount);
    }

    [Fact]
    public void PointQueries_SourceGuardAvoidsMaterializedRowProjection()
    {
        var source = TestWorkspaceFileLocator.ReadAllText("freew", "FreeW.Core.Model", "TableGridProjection.cs");

        source.Should().Contain("var projected = new TableGridCellProjection(cell, cellIndex, startColumn, span);")
            .And.NotContain("return ProjectRow(row)[cellIndex].StartColumn;")
            .And.NotContain("foreach (var projected in ProjectRow(row))");
    }
}
