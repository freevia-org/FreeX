using FluentAssertions;
using FreeX.App.Presentation.Drawing;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class FormControlRenderGeometryPlannerTests
{
    [Fact]
    public void GetGlyphRect_ClampsAndVerticallyCentersGlyph()
    {
        var rect = new LayoutRect(10, 20, 80, 30);

        FormControlRenderPlanner.GetGlyphRect(rect, maximumSize: 13)
            .Should().Be(new LayoutRect(11, 28.5, 13, 13));
    }

    [Fact]
    public void GetSpinnerButtonLayout_PreservesStackedButtonGeometry()
    {
        var layout = FormControlRenderPlanner.GetSpinnerButtonLayout(
            new LayoutRect(10, 20, 80, 31),
            maximumButtonWidth: 17);

        layout.FirstButton.Should().Be(new LayoutRect(10, 20, 17, 15.5));
        layout.SecondButton.Should().Be(new LayoutRect(10, 35.5, 17, 15.5));
        layout.FirstDirection.Should().Be(FormControlTriangleDirection.Up);
        layout.SecondDirection.Should().Be(FormControlTriangleDirection.Down);
    }

    [Fact]
    public void GetScrollBarButtonLayout_UsesHorizontalEndButtons()
    {
        var layout = FormControlRenderPlanner.GetScrollBarButtonLayout(new LayoutRect(10, 20, 80, 12));

        layout.FirstButton.Should().Be(new LayoutRect(10, 20, 12, 12));
        layout.SecondButton.Should().Be(new LayoutRect(78, 20, 12, 12));
        layout.FirstDirection.Should().Be(FormControlTriangleDirection.Left);
        layout.SecondDirection.Should().Be(FormControlTriangleDirection.Right);
    }

    [Fact]
    public void GetScrollBarButtonLayout_UsesVerticalEndButtons()
    {
        var layout = FormControlRenderPlanner.GetScrollBarButtonLayout(new LayoutRect(10, 20, 12, 80));

        layout.FirstButton.Should().Be(new LayoutRect(10, 20, 12, 12));
        layout.SecondButton.Should().Be(new LayoutRect(10, 88, 12, 12));
        layout.FirstDirection.Should().Be(FormControlTriangleDirection.Up);
        layout.SecondDirection.Should().Be(FormControlTriangleDirection.Down);
    }

    [Fact]
    public void GetGroupBoxLayout_PreservesFrameAndHostCaptionHeight()
    {
        var layout = FormControlRenderPlanner.GetGroupBoxLayout(
            new LayoutRect(10, 20, 80, 30),
            captionHeight: 21);

        layout.Frame.Should().Be(new LayoutRect(11, 27, 78, 22));
        layout.Caption.Should().Be(new LayoutRect(10, 20, 80, 21));
    }

    [Fact]
    public void GetListRowSeparatorYCoordinates_UsesSharedRowHeightAndBottomInset()
    {
        FormControlRenderPlanner.GetListRowSeparatorYCoordinates(new LayoutRect(10, 20, 80, 62))
            .Should().Equal(35, 50, 65, 80);
    }

    [Theory]
    [InlineData(FormControlTriangleDirection.Up, 5, 2)]
    [InlineData(FormControlTriangleDirection.Down, 5, 8)]
    [InlineData(FormControlTriangleDirection.Left, 2, 5)]
    [InlineData(FormControlTriangleDirection.Right, 8, 5)]
    public void GetTriangleLayout_PointsFirstVertexInRequestedDirection(
        FormControlTriangleDirection direction,
        double expectedX,
        double expectedY)
    {
        var triangle = FormControlRenderPlanner.GetTriangleLayout(new LayoutRect(0, 0, 10, 10), direction);

        triangle.First.Should().Be(new LayoutPoint(expectedX, expectedY));
    }
}
