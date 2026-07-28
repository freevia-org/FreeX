using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.DrawingInteraction;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DrawingInteraction;

public sealed class ObjectDragPlannerTests
{
    private static readonly LayoutRect Start = new(100, 100, 200, 100);

    [Fact]
    public void CalculateDragRect_Move_TranslatesRectWithoutResizing()
    {
        var result = ObjectDragPlanner.CalculateDragRect(
            ObjectDragKind.Move, Start, new LayoutPoint(150, 150), new LayoutPoint(180, 130));

        result.Should().Be(new LayoutRect(130, 80, 200, 100));
    }

    [Fact]
    public void ShouldCommitMove_OnlyWhenAnchorChanges()
    {
        var sheet = new SheetId(Guid.NewGuid());
        var anchor = new CellAddress(sheet, 4, 5);

        ObjectDragPlanner.ShouldCommitMove(anchor, anchor).Should().BeFalse();
        ObjectDragPlanner.ShouldCommitMove(anchor, new CellAddress(sheet, 4, 6)).Should().BeTrue();
    }

    [Fact]
    public void ShouldCommitResize_RejectsNoOpAndAcceptsGeometryOrFlipChanges()
    {
        ObjectDragPlanner.ShouldCommitResize(Start, Start, false, false, false, false).Should().BeFalse();
        ObjectDragPlanner.ShouldCommitResize(
            Start,
            new LayoutRect(100, 100, 201.1, 100),
            false,
            false,
            false,
            false).Should().BeTrue();
        ObjectDragPlanner.ShouldCommitResize(Start, Start, false, false, true, false).Should().BeTrue();
    }

    [Theory]
    [InlineData(ObjectDragKind.ResizeW, 276, 100, 24, 100)]
    [InlineData(ObjectDragKind.ResizeN, 100, 182, 200, 18)]
    public void ClampResizeToMinimums_PreservesOppositeEdgeForWestAndNorthHandles(
        ObjectDragKind kind,
        double expectedLeft,
        double expectedTop,
        double expectedWidth,
        double expectedHeight)
    {
        var transform = ObjectDragPlanner.CalculateDragTransform(
            kind,
            Start,
            kind == ObjectDragKind.ResizeW ? new LayoutPoint(100, 150) : new LayoutPoint(150, 100),
            kind == ObjectDragKind.ResizeW ? new LayoutPoint(295, 150) : new LayoutPoint(150, 190),
            minimumSize: 18);

        ObjectDragPlanner.ClampResizeToMinimums(kind, transform, 24, 18)
            .Should().Be(new LayoutRect(expectedLeft, expectedTop, expectedWidth, expectedHeight));
    }

    [Fact]
    public void CalculateDragRect_ResizeSE_MovesBottomRightAndKeepsTopLeft()
    {
        var result = ObjectDragPlanner.CalculateDragRect(
            ObjectDragKind.ResizeSE, Start, new LayoutPoint(300, 200), new LayoutPoint(340, 260));

        result.Left.Should().Be(100);
        result.Top.Should().Be(100);
        result.Width.Should().Be(240);
        result.Height.Should().Be(160);
    }

    [Fact]
    public void CalculateDragRect_ResizeE_OnlyChangesWidth()
    {
        var result = ObjectDragPlanner.CalculateDragRect(
            ObjectDragKind.ResizeE, Start, new LayoutPoint(300, 150), new LayoutPoint(330, 175));

        result.Should().Be(new LayoutRect(100, 100, 230, 100));
    }

    [Fact]
    public void CalculateDragRect_ResizeS_OnlyChangesHeight()
    {
        var result = ObjectDragPlanner.CalculateDragRect(
            ObjectDragKind.ResizeS, Start, new LayoutPoint(200, 200), new LayoutPoint(175, 250));

        result.Should().Be(new LayoutRect(100, 100, 200, 150));
    }

    [Fact]
    public void CalculateDragRect_ResizeW_MovesLeftEdgeAndKeepsRight()
    {
        var result = ObjectDragPlanner.CalculateDragRect(
            ObjectDragKind.ResizeW, Start, new LayoutPoint(100, 150), new LayoutPoint(70, 175));

        result.Left.Should().Be(70);
        result.Top.Should().Be(100);
        result.Width.Should().Be(230);
        result.Height.Should().Be(100);
        result.Right.Should().Be(Start.Right);
    }

    [Fact]
    public void CalculateDragRect_ResizeN_MovesTopEdgeAndKeepsBottom()
    {
        var result = ObjectDragPlanner.CalculateDragRect(
            ObjectDragKind.ResizeN, Start, new LayoutPoint(200, 100), new LayoutPoint(225, 60));

        result.Left.Should().Be(100);
        result.Top.Should().Be(60);
        result.Width.Should().Be(200);
        result.Height.Should().Be(140);
        result.Bottom.Should().Be(Start.Bottom);
    }

    [Fact]
    public void CalculateDragRect_ResizeNW_MovesTopLeftAndKeepsBottomRight()
    {
        var result = ObjectDragPlanner.CalculateDragRect(
            ObjectDragKind.ResizeNW, Start, new LayoutPoint(100, 100), new LayoutPoint(70, 80));

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
        var result = ObjectDragPlanner.CalculateDragRect(
            ObjectDragKind.ResizeNE, Start, new LayoutPoint(300, 100), new LayoutPoint(340, 80));

        result.Left.Should().Be(100);
        result.Top.Should().Be(80);
        result.Width.Should().Be(240);
        result.Height.Should().Be(120);
        result.Bottom.Should().Be(Start.Bottom);
    }

    [Fact]
    public void CalculateDragRect_ResizeSW_MovesBottomLeftAndKeepsTopRight()
    {
        var result = ObjectDragPlanner.CalculateDragRect(
            ObjectDragKind.ResizeSW, Start, new LayoutPoint(100, 200), new LayoutPoint(70, 240));

        result.Left.Should().Be(70);
        result.Top.Should().Be(100);
        result.Width.Should().Be(230);
        result.Height.Should().Be(140);
        result.Right.Should().Be(Start.Right);
    }

    [Fact]
    public void CalculateDragTransform_ResizeE_CrossesFixedLeftEdgeAndReportsHorizontalFlip()
    {
        var result = ObjectDragPlanner.CalculateDragTransform(
            ObjectDragKind.ResizeE, Start, new LayoutPoint(300, 150), new LayoutPoint(0, 150), minimumSize: 8);

        result.Rect.Should().Be(new LayoutRect(0, 100, 100, 100));
        result.CrossedHorizontally.Should().BeTrue();
        result.CrossedVertically.Should().BeFalse();
    }

    [Fact]
    public void CalculateDragTransform_ResizeW_CrossesFixedRightEdgeAndReportsHorizontalFlip()
    {
        var result = ObjectDragPlanner.CalculateDragTransform(
            ObjectDragKind.ResizeW, Start, new LayoutPoint(100, 150), new LayoutPoint(500, 150), minimumSize: 8);

        result.Rect.Should().Be(new LayoutRect(300, 100, 200, 100));
        result.CrossedHorizontally.Should().BeTrue();
        result.CrossedVertically.Should().BeFalse();
    }

    [Fact]
    public void CalculateDragTransform_ResizeN_CrossesFixedBottomEdgeAndReportsVerticalFlip()
    {
        var result = ObjectDragPlanner.CalculateDragTransform(
            ObjectDragKind.ResizeN, Start, new LayoutPoint(200, 100), new LayoutPoint(200, 400), minimumSize: 8);

        result.Rect.Should().Be(new LayoutRect(100, 200, 200, 200));
        result.CrossedHorizontally.Should().BeFalse();
        result.CrossedVertically.Should().BeTrue();
    }

    [Fact]
    public void CalculateDragTransform_ResizeNW_CrossesBothAxesAndReportsBothFlips()
    {
        var result = ObjectDragPlanner.CalculateDragTransform(
            ObjectDragKind.ResizeNW, Start, new LayoutPoint(100, 100), new LayoutPoint(900, 900), minimumSize: 8);

        result.Rect.Should().Be(new LayoutRect(300, 200, 600, 700));
        result.CrossedHorizontally.Should().BeTrue();
        result.CrossedVertically.Should().BeTrue();
    }

    [Fact]
    public void CalculateDragTransform_ResizeSE_CrossesBothAxesForFreeLineEndpointMovement()
    {
        var result = ObjectDragPlanner.CalculateDragTransform(
            ObjectDragKind.ResizeSE, Start, new LayoutPoint(300, 200), new LayoutPoint(0, 0), minimumSize: 8);

        result.Rect.Should().Be(new LayoutRect(0, 0, 100, 100));
        result.CrossedHorizontally.Should().BeTrue();
        result.CrossedVertically.Should().BeTrue();
    }

    [Fact]
    public void CalculateDragTransform_CrossedHandleMaintainsMinimumSizeAroundFixedEdge()
    {
        var result = ObjectDragPlanner.CalculateDragTransform(
            ObjectDragKind.ResizeE, Start, new LayoutPoint(300, 150), new LayoutPoint(95, 150), minimumSize: 8);

        result.Rect.Should().Be(new LayoutRect(92, 100, 8, 100));
        result.CrossedHorizontally.Should().BeTrue();
    }

    [Fact]
    public void CalculateDragTransform_ResizeE_ClampsToMinimumSizeWithoutCrossing()
    {
        var result = ObjectDragPlanner.CalculateDragTransform(
            ObjectDragKind.ResizeE, Start, new LayoutPoint(300, 150), new LayoutPoint(105, 150), minimumSize: 8);

        result.Rect.Should().Be(new LayoutRect(100, 100, 8, 100));
        result.CrossedHorizontally.Should().BeFalse();
    }

    [Fact]
    public void CalculateDragRect_NoneReturnsStartRect()
    {
        ObjectDragPlanner.CalculateDragRect(
            ObjectDragKind.None, Start, new LayoutPoint(0, 0), new LayoutPoint(50, 50))
            .Should().Be(Start);
    }

    [Fact]
    public void CalculateDragRect_RotateReturnsStartRect()
    {
        ObjectDragPlanner.CalculateDragRect(
            ObjectDragKind.Rotate, Start, new LayoutPoint(0, 0), new LayoutPoint(50, 50))
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
        ObjectDragPlanner.HitTestHandle(new LayoutPoint(x, y), Start)
            .Should().Be(expected);
    }

    [Fact]
    public void HitTestHandle_ReturnsRotateOverGrip()
    {
        // Grip sits RotationGripOffset above the top-center handle.
        var grip = new LayoutPoint(200, 100 - ObjectDragPlanner.RotationGripOffset);
        ObjectDragPlanner.HitTestHandle(grip, Start)
            .Should().Be(ObjectDragKind.Rotate);
    }

    [Fact]
    public void HitTestHandle_ReturnsMoveOverBody()
    {
        ObjectDragPlanner.HitTestHandle(new LayoutPoint(200, 150), Start)
            .Should().Be(ObjectDragKind.Move);
    }

    [Fact]
    public void HitTestHandle_ReturnsNoneOutsideObject()
    {
        ObjectDragPlanner.HitTestHandle(new LayoutPoint(500, 500), Start)
            .Should().Be(ObjectDragKind.None);
    }

    [Fact]
    public void HitTestHandle_ReturnsNoneForEmptyRect()
    {
        ObjectDragPlanner.HitTestHandle(new LayoutPoint(0, 0), new LayoutRect(0, 0, 0, 0))
            .Should().Be(ObjectDragKind.None);
    }

    [Fact]
    public void HitTestHandle_RotatedObject_InverseRotatesPointerToFindCorner()
    {
        // Square centered at (200, 150). Rotating 90 degrees clockwise maps the original
        // top-left (NW) corner's screen position; the inverse rotation in HitTestHandle must
        // recover ResizeNW. The unrotated NW corner is (100, 100); rotate it +90 about the
        // center to get where it sits on screen.
        var rotated = ObjectDragPlanner.RotateHandleCenter(ObjectDragKind.ResizeNW, Start, 90);

        ObjectDragPlanner.HitTestHandle(rotated, Start, rotationDegrees: 90)
            .Should().Be(ObjectDragKind.ResizeNW);
    }

    [Fact]
    public void HitTestHandle_RotatedObject_UnrotatedPointerMissesCorner()
    {
        // The same unrotated NW corner position should NOT be the NW handle once the object is
        // rotated, because the handle has moved on screen.
        ObjectDragPlanner.HitTestHandle(new LayoutPoint(100, 100), Start, rotationDegrees: 90)
            .Should().NotBe(ObjectDragKind.ResizeNW);
    }

    [Fact]
    public void RotateHandleCenter_NoRotation_ReturnsUnrotatedCenter()
    {
        ObjectDragPlanner.RotateHandleCenter(ObjectDragKind.ResizeNE, Start, 0)
            .Should().Be(new LayoutPoint(300, 100));
    }

    [Fact]
    public void RotatePointAroundCenter_RoundTripsWithInverseRotation()
    {
        var point = new LayoutPoint(123, 45);
        var rotated = ObjectDragPlanner.RotatePointAroundCenter(point, Start, 37);
        var restored = ObjectDragPlanner.RotatePointAroundCenter(rotated, Start, -37);

        restored.X.Should().BeApproximately(point.X, 0.0001);
        restored.Y.Should().BeApproximately(point.Y, 0.0001);
    }

    [Theory]
    [InlineData(100, 0, 0)]     // pointer straight up
    [InlineData(200, 100, 90)]  // pointer straight right
    [InlineData(100, 200, 180)] // pointer straight down
    [InlineData(0, 100, 270)]   // pointer straight left
    public void CalculateRotationDegrees_ReturnsCardinalAngles(double px, double py, double expected)
    {
        var center = new LayoutPoint(100, 100);
        var degrees = ObjectDragPlanner.CalculateRotationDegrees(center, new LayoutPoint(px, py));

        degrees.Should().BeApproximately(expected, 0.0001);
    }

    [Theory]
    [InlineData(200, 0, 45)]    // up-right
    [InlineData(200, 200, 135)] // down-right
    [InlineData(0, 200, 225)]   // down-left
    [InlineData(0, 0, 315)]     // up-left
    public void CalculateRotationDegrees_ReturnsDiagonalAngles(double px, double py, double expected)
    {
        var center = new LayoutPoint(100, 100);
        var degrees = ObjectDragPlanner.CalculateRotationDegrees(center, new LayoutPoint(px, py));

        degrees.Should().BeApproximately(expected, 0.0001);
    }

    [Fact]
    public void CalculateRotationDegrees_ReturnsZeroWhenPointerAtCenter()
    {
        var center = new LayoutPoint(100, 100);
        ObjectDragPlanner.CalculateRotationDegrees(center, center)
            .Should().Be(0);
    }

    [Fact]
    public void CalculateRotationDelta_QuarterTurnClockwiseIsPositiveNinety()
    {
        var center = new LayoutPoint(100, 100);
        var startGrip = new LayoutPoint(100, 0);   // straight up
        var currentGrip = new LayoutPoint(200, 100); // straight right

        ObjectDragPlanner.CalculateRotationDelta(center, startGrip, currentGrip)
            .Should().BeApproximately(90, 0.0001);
    }

    [Fact]
    public void CalculateRotationDelta_QuarterTurnCounterClockwiseIsNegativeNinety()
    {
        var center = new LayoutPoint(100, 100);
        var startGrip = new LayoutPoint(100, 0);  // straight up
        var currentGrip = new LayoutPoint(0, 100); // straight left

        ObjectDragPlanner.CalculateRotationDelta(center, startGrip, currentGrip)
            .Should().BeApproximately(-90, 0.0001);
    }

    [Fact]
    public void CalculateRotationDelta_NormalizesToShortestSignedArc()
    {
        var center = new LayoutPoint(100, 100);
        var startGrip = new LayoutPoint(0, 100);  // straight left (270 degrees)
        var currentGrip = new LayoutPoint(100, 0); // straight up (0 degrees)

        // 0 - 270 = -270 raw; shortest signed arc is +90.
        ObjectDragPlanner.CalculateRotationDelta(center, startGrip, currentGrip)
            .Should().BeApproximately(90, 0.0001);
    }

    [Fact]
    public void CalculateRotationDelta_SameGripIsZero()
    {
        var center = new LayoutPoint(100, 100);
        var grip = new LayoutPoint(100, 0);

        ObjectDragPlanner.CalculateRotationDelta(center, grip, grip)
            .Should().Be(0);
    }

    [Fact]
    public void CalculateRotationDelta_ReturnsZeroWhenGripAtCenter()
    {
        var center = new LayoutPoint(100, 100);

        ObjectDragPlanner.CalculateRotationDelta(center, center, new LayoutPoint(200, 100))
            .Should().Be(0);
    }
}
