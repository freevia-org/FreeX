using FluentAssertions;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.GridInteraction;

public sealed class GridSelectionLayoutPlannerTests
{
    [Fact]
    public void CalculateVisibleSelectionLayout_ReportsRectAndAllFourEdgesWhenFullyVisible()
    {
        var sheet = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));

        var layout = GridSelectionLayoutPlanner.CalculateVisibleSelectionLayout(
            CreateViewport(),
            range,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18);

        layout.Should().NotBeNull();
        var value = layout!.Value;
        // Columns 2..3 span x [40,120) + 30 header => [70,150]; rows 2..3 span y [20,60) + 18 => [38,78].
        value.Rect.Should().Be(new GridRect(70, 38, 80, 40));
        value.HasTopEdge.Should().BeTrue();
        value.HasBottomEdge.Should().BeTrue();
        value.HasLeftEdge.Should().BeTrue();
        value.HasRightEdge.Should().BeTrue();
    }

    [Fact]
    public void CalculateVisibleSelectionLayout_OmitsEdgesScrolledOutOfView()
    {
        var sheet = SheetId.New();
        // Range starts at row 1/col 1 but only rows 3+/cols 3+ are visible, so top/left edges are off-screen.
        var range = new GridRange(
            new CellAddress(sheet, 1, 1),
            new CellAddress(sheet, 4, 4));
        var viewport = new ViewportModel(
            [],
            [new RowMetric(3, 20, 0), new RowMetric(4, 20, 20)],
            [new ColMetric(3, 40, 0), new ColMetric(4, 40, 40)]);

        var layout = GridSelectionLayoutPlanner.CalculateVisibleSelectionLayout(
            viewport,
            range,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18);

        layout.Should().NotBeNull();
        var value = layout!.Value;
        value.HasTopEdge.Should().BeFalse();
        value.HasLeftEdge.Should().BeFalse();
        value.HasBottomEdge.Should().BeTrue();
        value.HasRightEdge.Should().BeTrue();
    }

    [Fact]
    public void CalculateVisibleSelectionLayout_ReturnsNullWhenNoMetricsIntersectRange()
    {
        var sheet = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheet, 90, 90),
            new CellAddress(sheet, 95, 95));

        GridSelectionLayoutPlanner.CalculateVisibleSelectionLayout(
                CreateViewport(),
                range,
                rowHeaderWidth: 30,
                columnHeaderHeight: 18)
            .Should()
            .BeNull();
    }

    [Fact]
    public void CalculateVisibleSelectionLayout_ScalesMetricGeometryWithoutScalingHeaders()
    {
        var sheet = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));

        var layout = GridSelectionLayoutPlanner.CalculateVisibleSelectionLayout(
            CreateViewport(),
            range,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            metricScale: 1.5);

        layout.Should().NotBeNull();
        layout!.Value.Rect.Should().Be(new GridRect(90, 48, 120, 60));
    }

    private static ViewportModel CreateViewport() =>
        new(
            [],
            [
                new RowMetric(1, 20, 0),
                new RowMetric(2, 20, 20),
                new RowMetric(3, 20, 40),
                new RowMetric(4, 20, 60),
                new RowMetric(5, 20, 80)
            ],
            [
                new ColMetric(1, 40, 0),
                new ColMetric(2, 40, 40),
                new ColMetric(3, 40, 80),
                new ColMetric(4, 40, 120),
                new ColMetric(5, 40, 160)
            ]);
}
