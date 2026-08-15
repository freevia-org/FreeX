using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class TableCellVerticalLayoutPlannerTests
{
    [Theory]
    [InlineData(TableCellVerticalAlignment.Top, 0)]
    [InlineData(TableCellVerticalAlignment.Center, 25)]
    [InlineData(TableCellVerticalAlignment.Bottom, 50)]
    public void ResolveContentOffset_UsesWordCellAlignment(
        TableCellVerticalAlignment alignment,
        double expectedOffset)
    {
        TableCellVerticalLayoutPlanner.ResolveContentOffset(
                alignment,
                regionHeightDip: 100,
                contentHeightDip: 40,
                verticalPaddingDip: 5)
            .Should().Be(expectedOffset);
    }

    [Fact]
    public void ResolveContentOffset_ClampsOverflowAndInvalidMeasurements()
    {
        TableCellVerticalLayoutPlanner.ResolveContentOffset(
                TableCellVerticalAlignment.Bottom,
                regionHeightDip: 20,
                contentHeightDip: 80,
                verticalPaddingDip: 5)
            .Should().Be(0);

        TableCellVerticalLayoutPlanner.ResolveContentOffset(
                TableCellVerticalAlignment.Center,
                regionHeightDip: double.NaN,
                contentHeightDip: double.PositiveInfinity,
                verticalPaddingDip: -10)
            .Should().Be(0);
    }

    [Fact]
    public void ResolveRegionHeight_IncludesConsecutiveVerticalMergeRowsAtGridColumn()
    {
        var table = new Table();
        table.Rows.Add(Row(Cell(span: 2, VerticalMergeState.Restart), Cell()));
        table.Rows.Add(Row(Cell(span: 2, VerticalMergeState.Continue), Cell()));
        table.Rows.Add(Row(Cell(span: 2, VerticalMergeState.Continue), Cell()));
        table.Rows.Add(Row(Cell(span: 2), Cell()));

        TableCellVerticalLayoutPlanner.ResolveRegionHeight(
                table,
                [20, 30, 40, 50],
                rowIndex: 0,
                gridColumn: 0)
            .Should().Be(90);

        TableCellVerticalLayoutPlanner.ResolveRegionHeight(
                table,
                [20, 30, 40, 50],
                rowIndex: 3,
                gridColumn: 0)
            .Should().Be(50);
    }

    [Fact]
    public void ResolveRegionHeight_RejectsMissingMeasurementsAndSafelyHandlesInvalidAddress()
    {
        var table = new Table();
        table.Rows.Add(Row(Cell()));
        table.Rows.Add(Row(Cell()));

        var act = () => TableCellVerticalLayoutPlanner.ResolveRegionHeight(
            table,
            [20],
            rowIndex: 0,
            gridColumn: 0);

        act.Should().Throw<ArgumentException>();
        TableCellVerticalLayoutPlanner.ResolveRegionHeight(
                table,
                [20, 30],
                rowIndex: 5,
                gridColumn: 0)
            .Should().Be(0);
    }

    [Fact]
    public void AvaloniaRenderer_DelegatesVerticalCellGeometryToSharedPlanner()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "Editing",
            "DocumentView.cs"));

        source.Should().Contain("TableCellVerticalLayoutPlanner.ResolveRegionHeight(")
            .And.Contain("TableCellVerticalLayoutPlanner.ResolveContentOffset(")
            .And.NotContain("AV-TBL5-VRENDER-VMERGE: pre-compute every row's height up front")
            .And.NotContain("vertical render: the Avalonia table renderer currently top-anchors");
    }

    private static TableRow Row(params TableCell[] cells)
    {
        var row = new TableRow();
        foreach (var cell in cells)
            row.Cells.Add(cell);
        return row;
    }

    private static TableCell Cell(
        int span = 1,
        VerticalMergeState verticalMerge = VerticalMergeState.None) =>
        new()
        {
            GridSpan = span,
            VerticalMerge = verticalMerge,
        };
}
