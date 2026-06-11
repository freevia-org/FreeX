using System.Windows;
using FluentAssertions;

namespace FreeX.App.UI.Tests;

public sealed class GridObjectDragPlannerTests
{
    private static readonly Rect Start = new(100, 100, 200, 100);

    [Fact]
    public void CalculateDragRect_Move_TranslatesRectWithoutResizing()
    {
        var result = GridObjectDragPlanner.CalculateDragRect(
            ObjectDragKind.Move, Start, new Point(150, 150), new Point(180, 130));

        result.Should().Be(new Rect(130, 80, 200, 100));
    }

    [Fact]
    public void CalculateDragRect_ResizeSE_MovesBottomRightAndKeepsTopLeft()
    {
        var result = GridObjectDragPlanner.CalculateDragRect(
            ObjectDragKind.ResizeSE, Start, new Point(300, 200), new Point(340, 260));

        result.Left.Should().Be(100);
        result.Top.Should().Be(100);
        result.Width.Should().Be(240);
        result.Height.Should().Be(160);
    }

    [Fact]
    public void CalculateDragRect_ResizeE_OnlyChangesWidth()
    {
        var result = GridObjectDragPlanner.CalculateDragRect(
            ObjectDragKind.ResizeE, Start, new Point(300, 150), new Point(330, 175));

        result.Should().Be(new Rect(100, 100, 230, 100));
    }

    [Fact]
    public void CalculateDragRect_ResizeS_OnlyChangesHeight()
    {
        var result = GridObjectDragPlanner.CalculateDragRect(
            ObjectDragKind.ResizeS, Start, new Point(200, 200), new Point(175, 250));

        result.Should().Be(new Rect(100, 100, 200, 150));
    }

    [Fact]
    public void CalculateDragRect_ResizeW_MovesLeftEdgeAndKeepsRight()
    {
        var result = GridObjectDragPlanner.CalculateDragRect(
            ObjectDragKind.ResizeW, Start, new Point(100, 150), new Point(70, 175));

        result.Left.Should().Be(70);
        result.Top.Should().Be(100);
        result.Width.Should().Be(230);
        result.Height.Should().Be(100);
        result.Right.Should().Be(Start.Right);
    }

    [Fact]
    public void CalculateDragRect_ResizeN_MovesTopEdgeAndKeepsBottom()
    {
        var result = GridObjectDragPlanner.CalculateDragRect(
            ObjectDragKind.ResizeN, Start, new Point(200, 100), new Point(225, 60));

        result.Left.Should().Be(100);
        result.Top.Should().Be(60);
        result.Width.Should().Be(200);
        result.Height.Should().Be(140);
        result.Bottom.Should().Be(Start.Bottom);
    }

    [Fact]
    public void CalculateDragRect_ResizeNW_MovesTopLeftAndKeepsBottomRight()
    {
        var result = GridObjectDragPlanner.CalculateDragRect(
            ObjectDragKind.ResizeNW, Start, new Point(100, 100), new Point(70, 80));

        result.Left.Should().Be(70);
        result.Top.Should().Be(80);
        result.Width.Should().Be(230);
        result.Height.Should().Be(120);
        result.Right.Should().Be(Start.Right);
        result.Bottom.Should().Be(Start.Bottom);
    }

    [Fact]
    public void CalculateDragRect_ResizeNE_MovesTopRightAndKeepsBottomLeft()
    {
        var result = GridObjectDragPlanner.CalculateDragRect(
            ObjectDragKind.ResizeNE, Start, new Point(300, 100), new Point(340, 80));

        result.Left.Should().Be(100);
        result.Top.Should().Be(80);
        result.Width.Should().Be(240);
        result.Height.Should().Be(120);
        result.Bottom.Should().Be(Start.Bottom);
    }

    [Fact]
    public void CalculateDragRect_ResizeSW_MovesBottomLeftAndKeepsTopRight()
    {
        var result = GridObjectDragPlanner.CalculateDragRect(
            ObjectDragKind.ResizeSW, Start, new Point(100, 200), new Point(70, 240));

        result.Left.Should().Be(70);
        result.Top.Should().Be(100);
        result.Width.Should().Be(230);
        result.Height.Should().Be(140);
        result.Right.Should().Be(Start.Right);
    }

    [Fact]
    public void CalculateDragTransform_ResizeE_CrossesFixedLeftEdgeAndReportsHorizontalFlip()
    {
        var result = GridObjectDragPlanner.CalculateDragTransform(
            ObjectDragKind.ResizeE, Start, new Point(300, 150), new Point(0, 150), minimumSize: 8);

        result.Rect.Should().Be(new Rect(0, 100, 100, 100));
        result.CrossedHorizontally.Should().BeTrue();
        result.CrossedVertically.Should().BeFalse();
    }

    [Fact]
    public void CalculateDragTransform_ResizeW_CrossesFixedRightEdgeAndReportsHorizontalFlip()
    {
        var result = GridObjectDragPlanner.CalculateDragTransform(
            ObjectDragKind.ResizeW, Start, new Point(100, 150), new Point(500, 150), minimumSize: 8);

        result.Rect.Should().Be(new Rect(300, 100, 200, 100));
        result.CrossedHorizontally.Should().BeTrue();
        result.CrossedVertically.Should().BeFalse();
    }

    [Fact]
    public void CalculateDragTransform_ResizeN_CrossesFixedBottomEdgeAndReportsVerticalFlip()
    {
        var result = GridObjectDragPlanner.CalculateDragTransform(
            ObjectDragKind.ResizeN, Start, new Point(200, 100), new Point(200, 400), minimumSize: 8);

        result.Rect.Should().Be(new Rect(100, 200, 200, 200));
        result.CrossedHorizontally.Should().BeFalse();
        result.CrossedVertically.Should().BeTrue();
    }

    [Fact]
    public void CalculateDragTransform_ResizeNW_CrossesBothAxesAndReportsBothFlips()
    {
        var result = GridObjectDragPlanner.CalculateDragTransform(
            ObjectDragKind.ResizeNW, Start, new Point(100, 100), new Point(900, 900), minimumSize: 8);

        result.Rect.Should().Be(new Rect(300, 200, 600, 700));
        result.CrossedHorizontally.Should().BeTrue();
        result.CrossedVertically.Should().BeTrue();
    }

    [Fact]
    public void CalculateDragTransform_ResizeSE_CrossesBothAxesForFreeLineEndpointMovement()
    {
        var result = GridObjectDragPlanner.CalculateDragTransform(
            ObjectDragKind.ResizeSE, Start, new Point(300, 200), new Point(0, 0), minimumSize: 8);

        result.Rect.Should().Be(new Rect(0, 0, 100, 100));
        result.CrossedHorizontally.Should().BeTrue();
        result.CrossedVertically.Should().BeTrue();
    }

    [Fact]
    public void CalculateDragTransform_CrossedHandleMaintainsMinimumSizeAroundFixedEdge()
    {
        var result = GridObjectDragPlanner.CalculateDragTransform(
            ObjectDragKind.ResizeE, Start, new Point(300, 150), new Point(95, 150), minimumSize: 8);

        result.Rect.Should().Be(new Rect(92, 100, 8, 100));
        result.CrossedHorizontally.Should().BeTrue();
    }

    [Fact]
    public void CalculateDragRect_NoneReturnsStartRect()
    {
        GridObjectDragPlanner.CalculateDragRect(
            ObjectDragKind.None, Start, new Point(0, 0), new Point(50, 50))
            .Should().Be(Start);
    }

    [Fact]
    public void CalculateDragRect_RotateReturnsStartRect()
    {
        GridObjectDragPlanner.CalculateDragRect(
            ObjectDragKind.Rotate, Start, new Point(0, 0), new Point(50, 50))
            .Should().Be(Start);
    }

    [Theory]
    [InlineData(100, 100, ObjectDragKind.ResizeNW)] // top-left corner
    [InlineData(200, 100, ObjectDragKind.ResizeN)]  // top-center edge
    [InlineData(300, 100, ObjectDragKind.ResizeNE)] // top-right corner
    [InlineData(300, 150, ObjectDragKind.ResizeE)]  // right-center edge
    [InlineData(300, 200, ObjectDragKind.ResizeSE)] // bottom-right corner
    [InlineData(200, 200, ObjectDragKind.ResizeS)]  // bottom-center edge
    [InlineData(100, 200, ObjectDragKind.ResizeSW)] // bottom-left corner
    [InlineData(100, 150, ObjectDragKind.ResizeW)]  // left-center edge
    public void HitTestHandle_ReturnsCorrectKindForEachHandle(double x, double y, ObjectDragKind expected)
    {
        GridObjectDragPlanner.HitTestHandle(new Point(x, y), Start)
            .Should().Be(expected);
    }

    [Fact]
    public void HitTestHandle_ReturnsMoveOverBody()
    {
        GridObjectDragPlanner.HitTestHandle(new Point(200, 150), Start)
            .Should().Be(ObjectDragKind.Move);
    }

    [Fact]
    public void HitTestHandle_ReturnsNoneOutsideObject()
    {
        GridObjectDragPlanner.HitTestHandle(new Point(500, 500), Start)
            .Should().Be(ObjectDragKind.None);
    }

    [Fact]
    public void HitTestHandle_ReturnsNoneForEmptyRect()
    {
        GridObjectDragPlanner.HitTestHandle(new Point(0, 0), Rect.Empty)
            .Should().Be(ObjectDragKind.None);
    }

    [Theory]
    [InlineData(100, 0, 0)]     // pointer straight up
    [InlineData(200, 100, 90)]  // pointer straight right
    [InlineData(100, 200, 180)] // pointer straight down
    [InlineData(0, 100, 270)]   // pointer straight left
    public void CalculateRotationDegrees_ReturnsCardinalAngles(double px, double py, double expected)
    {
        var center = new Point(100, 100);
        var degrees = GridObjectDragPlanner.CalculateRotationDegrees(center, new Point(px, py));

        degrees.Should().BeApproximately(expected, 0.0001);
    }

    [Theory]
    [InlineData(200, 0, 45)]    // up-right
    [InlineData(200, 200, 135)] // down-right
    [InlineData(0, 200, 225)]   // down-left
    [InlineData(0, 0, 315)]     // up-left
    public void CalculateRotationDegrees_ReturnsDiagonalAngles(double px, double py, double expected)
    {
        var center = new Point(100, 100);
        var degrees = GridObjectDragPlanner.CalculateRotationDegrees(center, new Point(px, py));

        degrees.Should().BeApproximately(expected, 0.0001);
    }

    [Fact]
    public void CalculateRotationDegrees_ReturnsZeroWhenPointerAtCenter()
    {
        var center = new Point(100, 100);
        GridObjectDragPlanner.CalculateRotationDegrees(center, center)
            .Should().Be(0);
    }
}
