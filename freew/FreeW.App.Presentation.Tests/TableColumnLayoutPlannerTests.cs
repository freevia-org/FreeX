using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class TableColumnLayoutPlannerTests
{
    [Fact]
    public void AllocateColumnWidthsPreservesDeclarationsAndDistributesMissingWidth()
    {
        var table = Table.Create(1, 3);
        table.ColumnWidthsPt.AddRange([60, 0, 0]);

        TableColumnLayoutPlanner.AllocateColumnWidths(table, 3, availableWidthDip: 200)
            .Should().Equal(80, 60, 60);
    }

    [Fact]
    public void AllocateColumnWidthsScalesOversizedAuthoredGeometryToAvailableWidth()
    {
        var table = Table.Create(1, 2);
        table.ColumnWidthsPt.AddRange([150, 150]);

        TableColumnLayoutPlanner.AllocateColumnWidths(table, 2, availableWidthDip: 200)
            .Should().Equal(100, 100);
    }

    [Fact]
    public void ContentAutoFitAppliesMeasuredSingleAndSpanningRequirements()
    {
        var table = Table.Create(1, 3);
        table.AutoFit = AutoFitMode.Contents;
        table.Rows[0].Cells[0].GridSpan = 2;
        var measurements = new[]
        {
            new TableCellContentMeasurement(0, 0, 100),
            new TableCellContentMeasurement(0, 1, 30),
            new TableCellContentMeasurement(0, 2, 500)
        };

        var widths = TableColumnLayoutPlanner.BuildContentAutoFitWidths(
            table,
            availableWidthDip: 500,
            measurements)!;

        widths.Take(2).Sum().Should().BeGreaterThanOrEqualTo(128);
        widths[2].Should().Be(44);
        widths.Sum().Should().BeLessThanOrEqualTo(500);
    }

    [Fact]
    public void ContentAutoFitRejectsVerticalTextAndHonorsPreferredTableWidth()
    {
        var table = Table.Create(1, 2);
        table.AutoFit = AutoFitMode.Contents;
        table.Rows[0].Cells[0].TextDirection = CellTextDirection.Rotate90;

        TableColumnLayoutPlanner.BuildContentAutoFitWidths(table, 400, [])
            .Should().BeNull();

        table.Rows[0].Cells[0].TextDirection = CellTextDirection.Horizontal;
        table.PreferredWidthPt = 150;
        var widths = TableColumnLayoutPlanner.BuildContentAutoFitWidths(
            table,
            availableWidthDip: 400,
            [new(0, 0, 100), new(0, 1, 50)])!;
        widths.Sum().Should().BeApproximately(200, 0.001);
        TableColumnLayoutPlanner.ResolveTableWidthDip(table).Should().BeApproximately(200, 0.001);
    }
}
