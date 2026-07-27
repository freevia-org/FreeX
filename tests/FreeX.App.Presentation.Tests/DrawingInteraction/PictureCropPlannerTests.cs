using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.DrawingInteraction;

namespace FreeX.App.Presentation.Tests.DrawingInteraction;

public sealed class PictureCropPlannerTests
{
    private static readonly LayoutRect Picture = new(100, 50, 200, 100);

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
        PictureCropPlanner.HitTestHandle(new LayoutPoint(x, y), Picture)
            .Should().Be(expected);
    }

    [Fact]
    public void HitTestHandle_DoesNotStealObjectResizeEdge()
    {
        PictureCropPlanner.HitTestHandle(new LayoutPoint(Picture.Right, Picture.Bottom), Picture)
            .Should().Be(PictureCropHandle.None);
    }

    [Fact]
    public void HitTestHandle_ReturnsNoneForEmptyRect()
    {
        PictureCropPlanner.HitTestHandle(new LayoutPoint(0, 0), new LayoutRect(0, 0, 0, 0))
            .Should().Be(PictureCropHandle.None);
    }

    [Fact]
    public void CalculateCrop_LeftHandleAdjustsLeftCropByHorizontalRatio()
    {
        var result = PictureCropPlanner.CalculateCrop(
            PictureCropHandle.CropW,
            new PictureCropRatios(0.10, 0.05, 0.20, 0.15),
            Picture,
            new LayoutPoint(110, 100),
            new LayoutPoint(130, 100));

        result.Should().Be(new PictureCropRatios(0.20, 0.05, 0.20, 0.15));
    }

    [Fact]
    public void CalculateCrop_RightHandleAdjustsRightCropByInverseHorizontalRatio()
    {
        var result = PictureCropPlanner.CalculateCrop(
            PictureCropHandle.CropE,
            new PictureCropRatios(0.10, 0.05, 0.20, 0.15),
            Picture,
            new LayoutPoint(290, 100),
            new LayoutPoint(270, 100));

        result.Should().Be(new PictureCropRatios(0.10, 0.05, 0.30, 0.15));
    }

    [Fact]
    public void CalculateCrop_CornerHandleAdjustsBothAxes()
    {
        var result = PictureCropPlanner.CalculateCrop(
            PictureCropHandle.CropSE,
            new PictureCropRatios(0.10, 0.05, 0.20, 0.15),
            Picture,
            new LayoutPoint(290, 140),
            new LayoutPoint(270, 120));

        result.Should().Be(new PictureCropRatios(0.10, 0.05, 0.30, 0.35));
    }

    [Fact]
    public void CalculateCrop_ClampsToMinimumVisibleArea()
    {
        var result = PictureCropPlanner.CalculateCrop(
            PictureCropHandle.CropW,
            new PictureCropRatios(0.20, 0, 0.40, 0),
            Picture,
            new LayoutPoint(110, 100),
            new LayoutPoint(500, 100));

        result.Left.Should().BeApproximately(0.59, 0.0001);
        result.Right.Should().Be(0.40);
        (result.Left + result.Right).Should().BeLessThan(1);
    }

    [Fact]
    public void CalculateCrop_CornerDragClampsBothAxesAndKeepsVisibleArea()
    {
        var result = PictureCropPlanner.CalculateCrop(
            PictureCropHandle.CropNW,
            new PictureCropRatios(0.45, 0.45, 0.45, 0.45),
            Picture,
            new LayoutPoint(110, 60),
            new LayoutPoint(500, 500));

        (result.Left + result.Right).Should().BeLessThanOrEqualTo(0.99);
        (result.Top + result.Bottom).Should().BeLessThanOrEqualTo(0.99);
        result.Left.Should().BeGreaterThanOrEqualTo(0);
        result.Top.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void CalculateCrop_NoneHandleReturnsStartCrop()
    {
        var start = new PictureCropRatios(0.10, 0.05, 0.20, 0.15);
        PictureCropPlanner.CalculateCrop(
            PictureCropHandle.None, start, Picture, new LayoutPoint(110, 100), new LayoutPoint(130, 100))
            .Should().Be(start);
    }

    [Fact]
    public void CalculateVisibleCropRect_MapsRatiosIntoPictureRect()
    {
        PictureCropPlanner.CalculateVisibleCropRect(
                Picture,
                new PictureCropRatios(0.10, 0.20, 0.30, 0.40))
            .Should().Be(new LayoutRect(120, 70, 120, 40));
    }

    [Fact]
    public void GetHandleCenters_ReturnsEightInnerHandles()
    {
        var centers = PictureCropPlanner.GetHandleCenters(Picture);

        centers.Should().HaveCount(8);
        centers.Should().Contain(c => c.Handle == PictureCropHandle.CropNW
            && c.Center == new LayoutPoint(110, 60));
    }
}
