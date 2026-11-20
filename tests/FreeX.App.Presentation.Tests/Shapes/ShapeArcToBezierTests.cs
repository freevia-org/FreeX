using FluentAssertions;
using Free.Shared.Drawing;

namespace FreeX.App.Presentation.Tests.Shapes;

public sealed class ShapeArcToBezierTests
{
    [Theory]
    [InlineData(false, true, 1)]
    [InlineData(false, false, 1)]
    [InlineData(true, true, 3)]
    [InlineData(true, false, 3)]
    public void Convert_RespectsSweepAndLargeArc(bool largeArc, bool clockwise, int expectedSegments)
    {
        var start = new LayoutPoint(1, 0);
        var end = new LayoutPoint(0, 1);
        var result = ShapeArcToBezier.Convert(start, ShapeSegment.ArcTo(end, 1, 1, clockwise, largeArc));

        result.Should().HaveCount(expectedSegments);
        result.Should().OnlyContain(segment => segment.Kind == ShapeSegmentKind.CubicBezier);
        result[^1].End.Should().Be(end);
        // For the short clockwise quarter of the unit circle, the initial tangent points down.
        if (!largeArc && clockwise)
        {
            result[0].Control1.X.Should().BeApproximately(1, 1e-12);
            result[0].Control1.Y.Should().BeApproximately(0.5522847498307936, 1e-12);
            result[0].Control2.X.Should().BeApproximately(0.5522847498307936, 1e-12);
            result[0].Control2.Y.Should().BeApproximately(1, 1e-12);
        }
    }

    [Fact]
    public void Convert_NormalizesTooSmallRadiiAndPreservesEndpoint()
    {
        var result = ShapeArcToBezier.Convert(new LayoutPoint(0, 0),
            ShapeSegment.ArcTo(new LayoutPoint(4, 0), 1, 1, sweepClockwise: true));
        result.Should().HaveCount(2);
        result[0].End.X.Should().BeApproximately(2, 1e-12);
        Math.Abs(result[0].End.Y).Should().BeApproximately(2, 1e-12);
        result[^1].End.Should().Be(new LayoutPoint(4, 0));
    }

    [Fact]
    public void Convert_HandlesDegenerateAndNegativeRadii()
    {
        var start = new LayoutPoint(2, 3);
        var end = new LayoutPoint(8, 7);
        ShapeArcToBezier.Convert(start, ShapeSegment.ArcTo(start, 2, 3, true)).Should().BeEmpty();
        ShapeArcToBezier.Convert(start, ShapeSegment.ArcTo(end, 0, 3, true)).Should().Equal(ShapeSegment.LineTo(end));
        ShapeArcToBezier.Convert(start, ShapeSegment.ArcTo(end, -5, -3, true))
            .Should().Equal(ShapeArcToBezier.Convert(start, ShapeSegment.ArcTo(end, 5, 3, true)));
    }

    [Fact]
    public void Convert_RejectsNonArcAndNonFiniteInputs()
    {
        Action nonArc = () => ShapeArcToBezier.Convert(default, ShapeSegment.LineTo(new LayoutPoint(1, 1)));
        Action nonFinite = () => ShapeArcToBezier.Convert(default, ShapeSegment.ArcTo(new LayoutPoint(1, 1), double.NaN, 2, true));
        nonArc.Should().Throw<ArgumentException>();
        nonFinite.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Convert_DoesNotEmitInfiniteControlPointsForExtremeFiniteInput()
    {
        foreach (var clockwise in new[] { false, true })
        {
            var arc = ShapeSegment.ArcTo(new LayoutPoint(1e308, -1e308), 1e308, 1e308, clockwise, true);
            try
            {
                var result = ShapeArcToBezier.Convert(new LayoutPoint(1e308, 1e308), arc);
                result.Should().OnlyContain(segment => double.IsFinite(segment.Control1.X) && double.IsFinite(segment.Control1.Y) &&
                    double.IsFinite(segment.Control2.X) && double.IsFinite(segment.Control2.Y) &&
                    double.IsFinite(segment.End.X) && double.IsFinite(segment.End.Y));
            }
            catch (ArgumentOutOfRangeException)
            {
                // An unrepresentable curve may be rejected, but must not emit invalid geometry.
            }
        }
    }
}
