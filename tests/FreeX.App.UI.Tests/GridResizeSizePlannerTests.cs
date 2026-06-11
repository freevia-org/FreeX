using System.IO;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Calc;

namespace FreeX.App.UI.Tests;

public sealed class GridResizeSizePlannerTests
{
    [Theory]
    [InlineData(-12)]
    [InlineData(0)]
    [InlineData(double.NaN)]
    public void ClampColumnSize_AllowsExcelZeroWidthHideInsteadOfMinimumVisualWidth(double requestedPixels)
    {
        GridResizeSizePlanner.ClampColumnSize(requestedPixels)
            .Should()
            .Be(GridResizeSizePlanner.MinimumSizePixels);
    }

    [Theory]
    [InlineData(-12)]
    [InlineData(0)]
    [InlineData(double.NaN)]
    public void ClampRowSize_AllowsExcelZeroHeightHideInsteadOfMinimumVisualHeight(double requestedPixels)
    {
        GridResizeSizePlanner.ClampRowSize(requestedPixels)
            .Should()
            .Be(GridResizeSizePlanner.MinimumSizePixels);
    }

    [Fact]
    public void ClampColumnSize_CapsAtCurrentCommandBridgeExcelMaximum()
    {
        GridResizeSizePlanner.ClampColumnSize(GridResizeSizePlanner.MaximumColumnSizePixels + 100)
            .Should()
            .Be(GridResizeSizePlanner.MaximumColumnSizePixels);
        GridResizeSizePlanner.MaximumColumnSizePixels
            .Should()
            .Be(ColumnWidthPixelMapper.MaximumColumnWidthPixels);
    }

    [Fact]
    public void ClampRowSize_CapsAtExcelMaximumHeight()
    {
        GridResizeSizePlanner.ClampRowSize(GridResizeSizePlanner.MaximumRowSizePixels + 100)
            .Should()
            .Be(GridResizeSizePlanner.MaximumRowSizePixels);
    }

    [Fact]
    public void GridViewResizeDrag_UsesPlannerForPreviewAndCommitSizes()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.UI", "GridView.Input.cs"));

        source.Should().Contain("GridResizeSizePlanner.ClampColumnSize(_resizeSizeStart + (pos.X - _resizeDragStart))");
        source.Should().Contain("GridResizeSizePlanner.ClampRowSize(_resizeSizeStart + (pos.Y - _resizeDragStart))");
        source.Should().Contain("GridResizeSizePlanner.ClampColumnSize(_resizeSizeStart + delta)");
        source.Should().Contain("GridResizeSizePlanner.ClampRowSize(_resizeSizeStart + delta)");
        source.Should().Contain("_resizeDragStart = _resizeLinePos;");
        source.Should().Contain("GridResizeSizePlanner.CalculateLinePosition(_resizeSizeStart, _resizeDragStart, newWidth)");
        source.Should().NotContain("Math.Max(MinCellSize");
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
}
