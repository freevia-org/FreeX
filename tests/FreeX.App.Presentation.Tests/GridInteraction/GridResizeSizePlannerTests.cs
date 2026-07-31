using FluentAssertions;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Calc;
using FreeX.Core.Commands;

namespace FreeX.App.Presentation.Tests.GridInteraction;

public sealed class GridResizeSizePlannerTests
{
    [Theory]
    [InlineData(-12)]
    [InlineData(0)]
    [InlineData(double.NaN)]
    public void ClampColumnSize_AllowsZeroWidthHideInsteadOfMinimumVisualWidth(double requestedPixels)
    {
        GridResizeSizePlanner.ClampColumnSize(requestedPixels)
            .Should()
            .Be(GridResizeSizePlanner.MinimumSizePixels);
    }

    [Theory]
    [InlineData(-12)]
    [InlineData(0)]
    [InlineData(double.NaN)]
    public void ClampRowSize_AllowsZeroHeightHideInsteadOfMinimumVisualHeight(double requestedPixels)
    {
        GridResizeSizePlanner.ClampRowSize(requestedPixels)
            .Should()
            .Be(GridResizeSizePlanner.MinimumSizePixels);
    }

    [Fact]
    public void ClampColumnSize_CapsAtMaximumColumnWidth()
    {
        GridResizeSizePlanner.ClampColumnSize(GridResizeSizePlanner.MaximumColumnSizePixels + 100)
            .Should()
            .Be(GridResizeSizePlanner.MaximumColumnSizePixels);
        GridResizeSizePlanner.MaximumColumnSizePixels
            .Should()
            .Be(ColumnWidthPixelMapper.MaximumColumnWidthPixels);
    }

    [Fact]
    public void ClampRowSize_CapsAtMaximumHeight()
    {
        GridResizeSizePlanner.ClampRowSize(GridResizeSizePlanner.MaximumRowSizePixels + 100)
            .Should()
            .Be(GridResizeSizePlanner.MaximumRowSizePixels);

        // R105: MaximumRowSizePixels clamps a pixel-space drag delta (it feeds
        // SetRowHeightCommand's pixel height directly), so it must be Excel's 409.5-point row-height
        // ceiling converted to pixels at 96 DPI (409.5 * 96/72 = 546) -- the same value
        // AutoFitSizingService.MaximumRowHeight already uses and SetRowHeightCommand's own guard
        // enforces (R102). This constant was previously the raw 409.5 (a points value), which
        // silently capped interactive drag-resize below heights the command itself legally accepts.
        GridResizeSizePlanner.MaximumRowSizePixels
            .Should()
            .Be(AutoFitSizingService.MaximumRowHeight)
            .And.Be(409.5 * (96.0 / 72.0));
    }

    [Fact]
    public void ClampColumnSize_PassesThroughValuesWithinRange()
    {
        GridResizeSizePlanner.ClampColumnSize(123.5).Should().Be(123.5);
    }

    [Fact]
    public void ClampRowSize_PassesThroughValuesWithinRange()
    {
        GridResizeSizePlanner.ClampRowSize(72.25).Should().Be(72.25);
    }

    [Fact]
    public void CalculateLinePosition_TracksPointerWhenResizeIsUnclamped()
    {
        const double originalSize = 64;
        const double originalEdge = 140;
        const double pointer = 173.5;

        var resizedSize = GridResizeSizePlanner.ClampColumnSize(originalSize + pointer - originalEdge);

        GridResizeSizePlanner.CalculateLinePosition(originalSize, originalEdge, resizedSize)
            .Should()
            .Be(pointer);
    }

    [Fact]
    public void CalculateLinePosition_AnchorsToEdgeWhenSizeIsClampedToZero()
    {
        // Dragging far left clamps the column to zero width; the guide line snaps to the size start.
        var resizedSize = GridResizeSizePlanner.ClampColumnSize(-50);

        GridResizeSizePlanner.CalculateLinePosition(sizeStartPixels: 64, dragEdgeStart: 140, resizedSizePixels: resizedSize)
            .Should()
            .Be(140 - 64);
    }
}
