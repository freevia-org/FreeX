using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowMaskGeometryPlannerTests
{
    [Fact]
    public void BuildRandomBars_UsesStableNonSequentialOrderAndCompleteBands()
    {
        var first = SlideShowMaskGeometryPlanner.BuildRandomBars(960, 540, 8, horizontal: true);
        var second = SlideShowMaskGeometryPlanner.BuildRandomBars(960, 540, 8, horizontal: true);

        first.Select(bar => bar.Order).Should().Equal(second.Select(bar => bar.Order));
        first.Select(bar => bar.Order).Should().Equal(6, 0, 4, 1, 7, 3, 5, 2);
        first.Select(bar => bar.Geometry.Open.Height).Should().OnlyContain(height => height > 0);
        first.Select(bar => bar.Geometry.Open.Width).Should().OnlyContain(width => width > 0);
        first.Select(bar => bar.Geometry.Closed.Height).Should().OnlyContain(height => height == 0);
        first.Select(bar => bar.Geometry.Closed.Width).Should().OnlyContain(width => width == 960);
    }

    [Fact]
    public void BuildBlindsBandAndCheckerboardCell_PreserveNativeCoordinates()
    {
        var blinds = SlideShowMaskGeometryPlanner.BuildBlindsBand(960, 540, 3, 1, horizontal: true);
        blinds.Closed.Should().Be(new SlideShowMaskRect(0, 180, 960, 0));
        blinds.Open.Should().Be(new SlideShowMaskRect(0, 180, 960, 180));

        var cell = SlideShowMaskGeometryPlanner.BuildCheckerboardCell(
            960, 540, rowCount: 2, columnCount: 3, row: 1, column: 2, horizontal: false);
        cell.Closed.Should().Be(new SlideShowMaskRect(640, 270, 320, 0));
        cell.Open.Should().Be(new SlideShowMaskRect(640, 270, 320, 270));
        SlideShowMaskGeometryPlanner.IsSecondCheckerboardPhase(1, 2).Should().BeTrue();
    }

    [Fact]
    public void BuildCenterMasks_ClampProgressAndKeepShapeOrder()
    {
        SlideShowMaskGeometryPlanner.BuildCircle(960, 540, 2)
            .Should().Be(new SlideShowMaskEllipse(new SlideShowMaskPoint(480, 270), 480, 270));

        SlideShowMaskGeometryPlanner.BuildDiamond(960, 540, 0)
            .Should().Equal(
                new SlideShowMaskPoint(480, 270),
                new SlideShowMaskPoint(480, 270),
                new SlideShowMaskPoint(480, 270),
                new SlideShowMaskPoint(480, 270));

        var plus = SlideShowMaskGeometryPlanner.BuildPlusRects(960, 540, 0.5);
        plus.Closed.Should().Be(new SlideShowMaskRect(240, 0, 480, 540));
        plus.Open.Should().Be(new SlideShowMaskRect(0, 135, 960, 270));
    }

    [Fact]
    public void BuildStripsAndSweeps_PreserveCompletionAndArcContracts()
    {
        var strips = SlideShowMaskGeometryPlanner.BuildStrips(960, 540, 0.5, 2, slopeDown: true);
        strips.IsFullyOpen.Should().BeFalse();
        strips.Polygons.Should().HaveCount(2);
        strips.Polygons[0].Points.Should().Equal(
            new SlideShowMaskPoint(-540, 0),
            new SlideShowMaskPoint(-30, 0),
            new SlideShowMaskPoint(510, 540),
            new SlideShowMaskPoint(0, 540));

        SlideShowMaskGeometryPlanner.BuildWedge(960, 540, 0).IsCollapsed.Should().BeTrue();
        var wedge = SlideShowMaskGeometryPlanner.BuildWedge(960, 540, 0.5);
        wedge.Arcs.Should().ContainSingle();
        wedge.Arcs[0].SweepDegrees.Should().Be(180);
        wedge.Arcs[0].IsLargeArc.Should().BeFalse();

        var wheel = SlideShowMaskGeometryPlanner.BuildWheel(960, 540, 0.5, 4);
        wheel.Arcs.Should().HaveCount(4);
        wheel.Arcs.Select(arc => arc.SweepDegrees).Should().AllSatisfy(sweep => sweep.Should().Be(45));
        SlideShowMaskGeometryPlanner.BuildWheel(960, 540, 1, 4).IsFullyOpen.Should().BeTrue();
    }
}
