using System.Windows;
using FluentAssertions;

namespace FreeX.App.UI.Tests;

public sealed class GridPictureCropPlannerTests
{
    private static readonly Rect Picture = new(100, 50, 200, 100);

    [Theory]
    [InlineData(110, 60, PictureCropHandle.CropNW)]
    [InlineData(200, 60, PictureCropHandle.CropN)]
    [InlineData(290, 60, PictureCropHandle.CropNE)]
    [InlineData(290, 100, PictureCropHandle.CropE)]
    [InlineData(290, 140, PictureCropHandle.CropSE)]
    [InlineData(200, 140, PictureCropHandle.CropS)]
    [InlineData(110, 140, PictureCropHandle.CropSW)]
    [InlineData(110, 100, PictureCropHandle.CropW)]
    public void HitTestHandle_ReturnsInnerCropHandles(double x, double y, PictureCropHandle expected)
    {
        GridPictureCropPlanner.HitTestHandle(new Point(x, y), Picture)
            .Should().Be(expected);
    }

    [Fact]
    public void HitTestHandle_DoesNotStealObjectResizeEdge()
    {
        GridPictureCropPlanner.HitTestHandle(new Point(Picture.Right, Picture.Bottom), Picture)
            .Should().Be(PictureCropHandle.None);
    }

    [Fact]
    public void CalculateCrop_LeftHandleAdjustsLeftCropByHorizontalRatio()
    {
        var result = GridPictureCropPlanner.CalculateCrop(
            PictureCropHandle.CropW,
            new PictureCropRatios(0.10, 0.05, 0.20, 0.15),
            Picture,
            new Point(110, 100),
            new Point(130, 100));

        result.Should().Be(new PictureCropRatios(0.20, 0.05, 0.20, 0.15));
    }

    [Fact]
    public void CalculateCrop_RightHandleAdjustsRightCropByInverseHorizontalRatio()
    {
        var result = GridPictureCropPlanner.CalculateCrop(
            PictureCropHandle.CropE,
            new PictureCropRatios(0.10, 0.05, 0.20, 0.15),
            Picture,
            new Point(290, 100),
            new Point(270, 100));

        result.Should().Be(new PictureCropRatios(0.10, 0.05, 0.30, 0.15));
    }

    [Fact]
    public void CalculateCrop_CornerHandleAdjustsBothAxes()
    {
        var result = GridPictureCropPlanner.CalculateCrop(
            PictureCropHandle.CropSE,
            new PictureCropRatios(0.10, 0.05, 0.20, 0.15),
            Picture,
            new Point(290, 140),
            new Point(270, 120));

        result.Should().Be(new PictureCropRatios(0.10, 0.05, 0.30, 0.35));
    }

    [Fact]
    public void CalculateCrop_ClampsToMinimumVisibleArea()
    {
        var result = GridPictureCropPlanner.CalculateCrop(
            PictureCropHandle.CropW,
            new PictureCropRatios(0.20, 0, 0.40, 0),
            Picture,
            new Point(110, 100),
            new Point(500, 100));

        result.Left.Should().BeApproximately(0.59, 0.0001);
        result.Right.Should().Be(0.40);
        (result.Left + result.Right).Should().BeLessThan(1);
    }

    [Fact]
    public void CalculateVisibleCropRect_MapsRatiosIntoPictureRect()
    {
        GridPictureCropPlanner.CalculateVisibleCropRect(
                Picture,
                new PictureCropRatios(0.10, 0.20, 0.30, 0.40))
            .Should().Be(new Rect(120, 70, 120, 40));
    }
}
