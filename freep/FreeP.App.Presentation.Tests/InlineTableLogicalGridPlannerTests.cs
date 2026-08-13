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
    public void LayoutMetricsOwnTracksSpacingRowsAlignmentAndCellPlacement()
    {
        var table = Table(1, 3);
        table.RichTextCellSpacingPt = 6;
        table.RichTextLeftIndentPt = 3;
        table.Rows[0].HorizontalAlignment = TableRowHorizontalAlignment.Center;
        table.Rows[0].Cells.Clear();
        table.Rows[0].Cells.Add(new TableCell { GridSpan = 2 });
        table.Rows[0].Cells.Add(new TableCell());

        var layout = InlineTableLogicalGridPlan.CreateLayout(
            table,
            availableWidthDip: 224);

        layout.Columns.Select(column => (column.WidthDip, column.TrackWidthDip))
            .Should().Equal((48, 56), (48, 56), (48, 48));
        layout.CellSpacingDip.Should().Be(8);
        layout.LeftIndentDip.Should().Be(4);
        layout.ContentWidthDip.Should().Be(160);
        layout.WidthDip.Should().Be(164);
        layout.AvailableWidthDip.Should().Be(224);
        layout.Rows.Should().ContainSingle();
        layout.Rows[0].ContentWidthDip.Should().Be(160);
        layout.Rows[0].HorizontalOffsetDip.Should().Be(30);
        layout.Rows[0].HeightDip.Should().Be(24);
        layout.FrameAlignment.Should().Be(TableRowHorizontalAlignment.Center);
        layout.Cells.Select(cell =>
                (cell.ColumnIndex, cell.ColumnSpan, cell.RowSpan, cell.Bounds))
            .Should().Equal(
                (0, 2, 1, new Free.Shared.AppServices.TableGridRect(34, 0, 104, 24)),
                (2, 1, 1, new Free.Shared.AppServices.TableGridRect(146, 0, 48, 24)));
        layout.Cells[0].TrailingSpacingDip.Should().Be(8);
        layout.HitTest(100, 12).Should().Be(layout.Cells[0]);
        layout.HitTest(86, 12).Should().BeNull("cell-spacing gaps are not editable slots");
    }

    [Fact]
    public void LayoutMetricsNormalizeMalformedDimensionsAndClampSpans()
    {
        var table = Table(2, 2);
        table.ColumnWidthsEmu[0] = 0;
        table.ColumnWidthsEmu[1] = -1;
        table.RichTextCellSpacingPt = -10;
        table.RichTextLeftIndentPt = 900;
        table.Rows[0].HeightEmu = 9525;
        table.Rows[0].HeightRule = TableRowHeightRule.AtLeast;
        table.Rows[1].HeightEmu = 0;
        table.Rows[1].HeightRule = TableRowHeightRule.Exact;
        table.Rows[0].Cells[0].GridSpan = int.MaxValue;
        table.Rows[0].Cells[0].RowSpan = int.MaxValue;
        table.Rows[0].Cells[1].HMerge = true;
        table.Rows[1].Cells[0].VMerge = true;
        table.Rows[1].Cells[1].VMerge = true;

        var layout = InlineTableLogicalGridPlan.CreateLayout(
            table,
            availableWidthDip: double.NegativeInfinity);

        layout.Columns.Select(column => column.WidthDip).Should().Equal(24, 24);
        layout.CellSpacingDip.Should().Be(0);
        layout.LeftIndentDip.Should().Be(1000);
        layout.WidthDip.Should().Be(1048);
        layout.AvailableWidthDip.Should().Be(1048);
        layout.Rows.Select(row => row.HeightDip).Should().Equal(20, 24);
        layout.Rows[0].UsesMinimumHeight.Should().BeTrue();
        layout.Rows[1].UsesMinimumHeight.Should().BeFalse();
        layout.Cells.Should().ContainSingle();
        layout.Cells[0].ColumnSpan.Should().Be(2);
        layout.Cells[0].RowSpan.Should().Be(2);
        layout.Cells[0].Bounds.Should().Be(
            new Free.Shared.AppServices.TableGridRect(1000, 0, 48, 44));
        layout.ResolveCell(1, 1).Should().Be(layout.Cells[0]);
        layout.HitTest(1030, 30).Should().Be(layout.Cells[0]);
    }

    [Fact]
    public void EmptyTableGetsStableDefaultGeometry()
    {
        var layout = InlineTableLogicalGridPlan.CreateLayout(new TableShape());

        layout.Columns.Should().ContainSingle();
        layout.Columns[0].WidthDip.Should().Be(72);
        layout.Rows.Should().ContainSingle();
        layout.Rows[0].HeightDip.Should().Be(24);
        layout.WidthDip.Should().Be(72);
        layout.HeightDip.Should().Be(24);
        layout.Cells.Should().BeEmpty();
        layout.HitTest(10, 10).Should().BeNull();
    }

    [Fact]
    public void RendererAdapters_ConsumeSharedLayoutWithoutOwningSpanArithmetic()
    {
        var wpf = TestWorkspaceFileLocator.ReadAllText(
            "freep", "FreeP.App.Rendering.Wpf", "TextBodyFlowDocumentConverter.cs");
        var avaloniaPlanner = TestWorkspaceFileLocator.ReadAllText(
            "freep", "FreeP.App.Rendering.Avalonia", "AvaloniaInlineTableLayoutPlanner.cs");
        var avaloniaSurface = TestWorkspaceFileLocator.ReadAllText(
            "freep", "FreeP.App.Rendering.Avalonia", "AvaloniaRichTextEditingSurface.cs");

        wpf.Should().Contain("InlineTableLogicalGridPlan.CreateLayout(");
        avaloniaSurface.Should().Contain("InlineTableLogicalGridPlan.CreateLayout(");
        avaloniaPlanner.Should().Contain("InlineTableLayoutPlan layout");
        wpf.Should().NotContain("private static double GetHorizontalOffset(");
        avaloniaPlanner.Should().NotContain("internal static double GetHorizontalOffset(");

        var rendererSources = wpf + avaloniaPlanner + avaloniaSurface;
        rendererSources.Should().NotContain("table.ColumnWidthsEmu");
        rendererSources.Should().NotContain("table.RichTextCellSpacingPt");
        rendererSources.Should().NotContain("table.RichTextLeftIndentPt");
        rendererSources.Should().NotContain("cell.GridSpan");
        rendererSources.Should().NotContain("cell.RowSpan");
        rendererSources.Should().NotContain("row.HeightEmu");
        rendererSources.Should().NotContain("table.Rows.Sum(row =>");
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
