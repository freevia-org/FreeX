using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class ZoomSelectionPlannerTests
{
    [Fact]
    public void CalculateFitWholePercent_RoundsSharedSelectionFitForCommandApplication()
    {
        ZoomSelectionPlanner.CalculateFitWholePercent(
                gridWidth: 813,
                gridHeight: 359,
                selectedColumns: 6,
                selectedRows: 7)
            .Should()
            .Be(169);
    }

    [Theory]
    [InlineData(10, 10, 100, 100, 10)]
    [InlineData(10000, 10000, 1, 1, 400)]
    public void CalculateFitWholePercent_ClampsToSupportedZoomRange(
        double gridWidth,
        double gridHeight,
        uint selectedColumns,
        uint selectedRows,
        int expected)
    {
        ZoomSelectionPlanner.CalculateFitWholePercent(gridWidth, gridHeight, selectedColumns, selectedRows)
            .Should()
            .Be(expected);
    }
}
