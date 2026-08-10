using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class InlineTableLogicalGridPlannerTests
{
    [Fact]
    public void MergedCellsProduceOneAnchorPerLogicalCellInVisualOrder()
    {
        var table = Table(2, 3);
        table.Rows[0].Cells[0].GridSpan = 2;
        table.Rows[0].Cells[0].RowSpan = 2;
        table.Rows[0].Cells[1].HMerge = true;
        table.Rows[1].Cells[0].VMerge = true;
        table.Rows[1].Cells[1].VMerge = true;

        var plan = InlineTableLogicalGridPlan.Create(table);

        plan.Cells.Select(cell =>
                (cell.RowIndex, cell.ColumnIndex, cell.SourceCellIndex))
            .Should().Equal(
                (0, 0, 0),
                (0, 2, 2),
                (1, 2, 2));
        plan.ResolveCell(0, 1).Should().Be(plan.Cells[0]);
        plan.ResolveCell(1, 0).Should().Be(plan.Cells[0]);
        plan.ResolveCell(1, 1).Should().Be(plan.Cells[0]);
    }

    [Fact]
    public void CompactGridSpanUsesLogicalColumnAndSourceIndex()
    {
        var table = Table(1, 3);
        table.Rows[0].Cells.Clear();
        table.Rows[0].Cells.Add(new TableCell { GridSpan = 2 });
        table.Rows[0].Cells.Add(new TableCell());

        var plan = InlineTableLogicalGridPlan.Create(table);

        plan.Cells.Select(cell =>
                (cell.RowIndex, cell.ColumnIndex, cell.SourceCellIndex))
            .Should().Equal((0, 0, 0), (0, 2, 1));
        plan.ResolveCell(0, 1).Should().Be(plan.Cells[0]);
    }

    [Fact]
    public void NavigationMovesBackwardAndStaysBoundedAtBothEnds()
    {
        var plan = InlineTableLogicalGridPlan.Create(Table(1, 2));

        plan.TryGetAdjacent(plan.Cells[1], backwards: true, out var previous)
            .Should().BeTrue();
        previous.Should().Be(plan.Cells[0]);
        plan.TryGetAdjacent(plan.Cells[0], backwards: true, out _)
            .Should().BeFalse();
        plan.TryGetAdjacent(plan.Cells[1], backwards: false, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void AppendRowCopiesHeightAndCreatesOneCellPerColumn()
    {
        var table = Table(1, 3);
        table.Rows[0].HeightEmu = 228600;
        table.Rows[0].HeightRule = TableRowHeightRule.AtLeast;
        table.Rows[0].HorizontalAlignment = TableRowHorizontalAlignment.Right;

        var row = InlineTableLogicalGridPlan.CreateAppendRow(table);

        row.HeightEmu.Should().Be(228600);
        row.HeightRule.Should().Be(TableRowHeightRule.AtLeast);
        row.HorizontalAlignment.Should().Be(TableRowHorizontalAlignment.Right);
        row.Cells.Should().HaveCount(3);
        row.Cells.Should().OnlyContain(cell =>
            cell.TextBody != null && cell.GridSpan == 1 && cell.RowSpan == 1
            && !cell.HMerge && !cell.VMerge);
    }

    [Theory]
    [InlineData(TableRowHorizontalAlignment.Left, 0)]
    [InlineData(TableRowHorizontalAlignment.Center, 30)]
    [InlineData(TableRowHorizontalAlignment.Right, 60)]
    public void RowHorizontalLayout_AccountsForGridSpansAndAlignment(
        TableRowHorizontalAlignment alignment,
        double expectedOffset)
    {
        var row = new TableRow { HorizontalAlignment = alignment };
        row.Cells.Add(new TableCell { GridSpan = 2 });
        row.Cells.Add(new TableCell());

        var layout = InlineTableLogicalGridPlan.ResolveRowHorizontalLayout(
            row,
            [10, 20, 30],
            availableWidth: 120);

        layout.RowWidth.Should().Be(60);
        layout.Offset.Should().Be(expectedOffset);
    }

    [Fact]
    public void RendererAdapters_UseSharedRowHorizontalLayout()
    {
        var wpf = TestWorkspaceFileLocator.ReadAllText(
            "freep", "FreeP.App.Rendering.Wpf", "TextBodyFlowDocumentConverter.cs");
        var avalonia = TestWorkspaceFileLocator.ReadAllText(
            "freep", "FreeP.App.Rendering.Avalonia", "AvaloniaInlineTableLayoutPlanner.cs");

        (wpf + avalonia).Should().Contain("InlineTableLogicalGridPlan.ResolveRowHorizontalLayout(");
        wpf.Should().NotContain("private static double GetHorizontalOffset(");
        avalonia.Should().NotContain("internal static double GetHorizontalOffset(");
    }

    private static TableShape Table(int rows, int columns)
    {
        var table = new TableShape();
        for (int column = 0; column < columns; column++)
            table.ColumnWidthsEmu.Add(457200);
        for (int row = 0; row < rows; row++)
        {
            var modelRow = new TableRow { HeightEmu = 228600 };
            for (int column = 0; column < columns; column++)
                modelRow.Cells.Add(new TableCell { TextBody = new TextBody() });
            table.Rows.Add(modelRow);
        }

        return table;
    }
}
