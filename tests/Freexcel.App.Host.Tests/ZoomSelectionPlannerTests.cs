using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class ZoomSelectionPlannerTests
{
    [Fact]
    public void CalculateFitPercent_UsesSmallerWidthOrHeightFit()
    {
        ZoomSelectionPlanner.CalculateFitPercent(
                gridWidth: 800,
                gridHeight: 300,
                selectedColumns: 5,
                selectedRows: 10)
            .Should()
            .Be(150);
    }

    [Theory]
    [InlineData(10, 10, 100, 100, 10)]
    [InlineData(10000, 10000, 1, 1, 400)]
    public void CalculateFitPercent_ClampsToSupportedZoomRange(
        double gridWidth,
        double gridHeight,
        uint selectedColumns,
        uint selectedRows,
        double expected)
    {
        ZoomSelectionPlanner.CalculateFitPercent(gridWidth, gridHeight, selectedColumns, selectedRows)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void CalculateDialogZoomPercent_UsesFitPercentWhenDialogRequestsFitSelection()
    {
        ZoomSelectionPlanner.CalculateDialogZoomPercent(
                new ZoomDialogResult(125, FitSelection: true),
                gridWidth: 800,
                gridHeight: 300,
                selectedColumns: 5,
                selectedRows: 10)
            .Should()
            .Be(150);
    }
}
