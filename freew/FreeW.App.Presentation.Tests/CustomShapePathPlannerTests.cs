using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class CustomShapePathPlannerTests
{
    [Fact]
    public void Build_scales_lines_beziers_closure_and_multiple_figures()
    {
        var geometry = new CustomGeometry { Width = 100, Height = 200 };
        geometry.Segments.AddRange(
        [
            new CustomSegment(CustomSegmentKind.MoveTo, new CustomPoint(0, 0)),
            new CustomSegment(CustomSegmentKind.LineTo, new CustomPoint(50, 100)),
            new CustomSegment(
                CustomSegmentKind.CubicBezierTo,
                new CustomPoint(100, 200),
                new CustomPoint(60, 120),
                new CustomPoint(80, 160)),
            new CustomSegment(CustomSegmentKind.Close),
            new CustomSegment(CustomSegmentKind.MoveTo, new CustomPoint(25, 50)),
            new CustomSegment(CustomSegmentKind.LineTo, new CustomPoint(75, 150)),
        ]);

        var figures = CustomShapePathPlanner.Build(
            geometry,
            new CustomShapePathBounds(10, 20, 200, 400));

        figures.Should().HaveCount(2);
        figures[0].Start.Should().Be(new CustomShapePathPoint(10, 20));
        figures[0].IsClosed.Should().BeTrue();
        figures[0].Commands.Should().Equal(
            new CustomShapePathCommand(
                CustomShapePathCommandKind.LineTo,
                new CustomShapePathPoint(110, 220)),
            new CustomShapePathCommand(
                CustomShapePathCommandKind.CubicBezierTo,
                new CustomShapePathPoint(210, 420),
                new CustomShapePathPoint(130, 260),
                new CustomShapePathPoint(170, 340)));
        figures[1].Start.Should().Be(new CustomShapePathPoint(60, 120));
        figures[1].Commands.Single().Point.Should().Be(new CustomShapePathPoint(160, 320));
        figures[1].IsClosed.Should().BeFalse();
    }

    [Fact]
    public void Build_can_invert_y_for_pdf_projection()
    {
        var geometry = CustomGeometry.RectanglePoly(gridW: 100, gridH: 100);

        var figure = CustomShapePathPlanner.Build(
            geometry,
            new CustomShapePathBounds(5, 10, 200, 300, InvertY: true)).Single();

        figure.Start.Should().Be(new CustomShapePathPoint(5, 310));
        figure.Commands[1].Point.Should().Be(new CustomShapePathPoint(205, 10));
        figure.IsClosed.Should().BeTrue();
    }

    [Fact]
    public void Build_ignores_segments_before_a_move_and_rejects_invalid_geometry_dimensions()
    {
        var geometry = new CustomGeometry { Width = 0, Height = 100 };
        geometry.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, new CustomPoint(0, 0)));
        CustomShapePathPlanner.Build(geometry, new CustomShapePathBounds(0, 0, 10, 10))
            .Should().BeEmpty();

        geometry = new CustomGeometry { Width = 100, Height = 100 };
        geometry.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, new CustomPoint(10, 10)));
        geometry.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, new CustomPoint(20, 20)));
        CustomShapePathPlanner.Build(geometry, new CustomShapePathBounds(0, 0, 10, 10))
            .Single().Commands.Should().BeEmpty();
    }
}
