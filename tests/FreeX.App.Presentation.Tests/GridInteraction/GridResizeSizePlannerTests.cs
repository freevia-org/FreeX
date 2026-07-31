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
    }

    [Fact]
    public void R106_MaximumColumnSizePixels_IsIndependentlyPinnedToPixelValue()
    {
        // R106: GridResizeSizePlanner.MaximumColumnSizePixels is declared as a plain const alias of
        // ColumnWidthPixelMapper.MaximumColumnWidthPixels (`public const double MaximumColumnSizePixels =
        // ColumnWidthPixelMapper.MaximumColumnWidthPixels;`), so an assertion of the form
        // `MaximumColumnSizePixels.Should().Be(ColumnWidthPixelMapper.MaximumColumnWidthPixels)` compares
        // that compile-time constant to itself and can never fail for any value it holds -- exactly the
        // row-sibling blind spot that let GridResizeSizePlanner.MaximumRowSizePixels silently hold a
        // points-space value instead of the required pixel value for many rounds before R105 caught it
        // (see ClampRowSize_CapsAtMaximumHeight below). Pin the column ceiling to independently-derived
        // values instead: the raw literal, and ColumnWidthToPixels applied to Excel's 255-character
        // maximum column width (255 * 7 + 5 = 1790), so a future points/pixels unit mismatch on the
        // column axis would be caught here.
        GridResizeSizePlanner.MaximumColumnSizePixels
            .Should()
            .Be(1790.0)
            .And.Be(ColumnWidthPixelMapper.ColumnWidthToPixels(ColumnWidthPixelMapper.MaximumColumnWidth))
            .And.Be(ColumnWidthPixelMapper.MaximumColumnWidthPixels);
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
