using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

public sealed class CellTextOrientationLayoutPlannerTests
{
    [Theory]
    [InlineData(-91, 0)]
    [InlineData(-90, -90)]
    [InlineData(45, 45)]
    [InlineData(90, 90)]
    [InlineData(91, 0)]
    [InlineData(255, 0)]
    public void NormalizeRotationForDisplay_UsesSupportedExcelRange(int rotation, int expected)
    {
        CellTextOrientationLayoutPlanner.NormalizeRotationForDisplay(rotation).Should().Be(expected);
    }

    [Fact]
    public void PrepareDisplayText_StacksExcelVerticalText()
    {
        CellTextOrientationLayoutPlanner.HasTextOrientation(255).Should().BeTrue();
        CellTextOrientationLayoutPlanner.PrepareDisplayText("Sample", 255).Should().Be("S\na\nm\np\nl\ne");
        CellTextOrientationLayoutPlanner.PrepareDisplayText("Sample", 90).Should().Be("Sample");
    }

    [Fact]
    public void CalculateLayout_UsesRotationBoundsForAlignment()
    {
        var layout = CellTextOrientationLayoutPlanner.CalculateLayout(
            new CellTextLayoutRect(10, 20, 100, 40),
            textWidth: 30,
            textHeight: 10,
            HorizontalAlignment.Left,
            VerticalAlignment.Center,
            isNumeric: false,
            indentPixels: 0,
            textRotation: 90);

        layout.IsRotated.Should().BeTrue();
        layout.TransformAngle.Should().Be(-90);
        layout.Bounds.Width.Should().BeApproximately(10, 0.001);
        layout.Bounds.Height.Should().BeApproximately(30, 0.001);
        layout.Bounds.Left.Should().BeApproximately(12, 0.001);
        layout.Bounds.Top.Should().BeApproximately(25, 0.001);
    }

    [Fact]
    public void CalculateLayout_RightAlignsGeneralNumericText()
    {
        var layout = CellTextOrientationLayoutPlanner.CalculateLayout(
            new CellTextLayoutRect(10, 20, 100, 40),
            textWidth: 30,
            textHeight: 10,
            HorizontalAlignment.General,
            VerticalAlignment.Bottom,
            isNumeric: true,
            indentPixels: 0,
            textRotation: 0);

        layout.TextPoint.X.Should().Be(78);
        layout.TextPoint.Y.Should().Be(49);
        layout.Bounds.Should().Be(new CellTextLayoutRect(78, 49, 30, 10));
    }

    [Fact]
    public void ShouldClip_UsesLayoutBoundsAndWrappedTextHeight()
    {
        var clipRect = new CellTextLayoutRect(0, 0, 50, 20);
        var inside = new CellTextOrientationLayout(
            new CellTextLayoutPoint(2, 2),
            new CellTextLayoutRect(2, 2, 20, 10),
            TransformAngle: 0);
        var overflowing = new CellTextOrientationLayout(
            new CellTextLayoutPoint(2, 2),
            new CellTextLayoutRect(2, 2, 60, 10),
            TransformAngle: 0);

        CellTextOrientationLayoutPlanner.ShouldClip(wrapText: false, clipRect, textHeight: 10, inside).Should().BeFalse();
        CellTextOrientationLayoutPlanner.ShouldClip(wrapText: false, clipRect, textHeight: 10, overflowing).Should().BeTrue();
        CellTextOrientationLayoutPlanner.ShouldClip(wrapText: true, clipRect, textHeight: 24, inside).Should().BeTrue();
    }
}
