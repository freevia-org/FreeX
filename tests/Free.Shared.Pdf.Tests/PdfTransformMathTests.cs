using FluentAssertions;
using Free.Shared.Pdf;

namespace Free.Shared.Pdf.Tests;

public sealed class PdfTransformMathTests
{
    [Fact]
    public void IsFiniteAffineMatrix_accepts_finite_components()
    {
        PdfTransformMath.IsFiniteAffineMatrix(
                double.MinValue,
                -1,
                0,
                1,
                double.MaxValue,
                -0.0)
            .Should().BeTrue();
    }

    public static TheoryData<double> NonFiniteValues => new()
    {
        double.NaN,
        double.PositiveInfinity,
        double.NegativeInfinity,
    };

    [Theory]
    [MemberData(nameof(NonFiniteValues))]
    public void IsFiniteAffineMatrix_rejects_a_non_finite_component_in_every_position(double value)
    {
        for (var index = 0; index < 6; index++)
        {
            var components = new[] { 1d, 0d, 0d, 1d, 0d, 0d };
            components[index] = value;

            PdfTransformMath.IsFiniteAffineMatrix(
                    components[0],
                    components[1],
                    components[2],
                    components[3],
                    components[4],
                    components[5])
                .Should().BeFalse($"component {index} is {value}");
        }
    }

    [Theory]
    [InlineData(1, 0, 0, 1, 1)]
    [InlineData(3, 4, 0, 12, 8.5)]
    [InlineData(-1, 0, 0, -1, 1)]
    [InlineData(0, 2, -2, 0, 2)]
    public void EstimateUniformScale_averages_axis_magnitudes(
        double m11,
        double m12,
        double m21,
        double m22,
        double expected)
    {
        PdfTransformMath.EstimateUniformScale(m11, m12, m21, m22)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(double.NaN, 0, 0, 1)]
    [InlineData(double.PositiveInfinity, 0, 0, 1)]
    [InlineData(double.MaxValue, double.MaxValue, 0, 1)]
    public void EstimateUniformScale_uses_fallback_for_degenerate_non_finite_or_overflowed_axes(
        double m11,
        double m12,
        double m21,
        double m22)
    {
        PdfTransformMath.EstimateUniformScale(m11, m12, m21, m22, fallbackScale: 0.75)
            .Should().Be(0.75);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void EstimateUniformScale_rejects_an_invalid_fallback(double fallbackScale)
    {
        var action = () => PdfTransformMath.EstimateUniformScale(1, 0, 0, 1, fallbackScale);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ResolveCanvasCoordinate_maps_only_the_unset_nan_sentinel_to_origin()
    {
        PdfTransformMath.ResolveCanvasCoordinate(double.NaN).Should().Be(0);
        PdfTransformMath.ResolveCanvasCoordinate(-42.5).Should().Be(-42.5);
        PdfTransformMath.ResolveCanvasCoordinate(0).Should().Be(0);
        PdfTransformMath.ResolveCanvasCoordinate(73.25).Should().Be(73.25);
        PdfTransformMath.ResolveCanvasCoordinate(double.PositiveInfinity).Should().Be(double.PositiveInfinity);
        PdfTransformMath.ResolveCanvasCoordinate(double.NegativeInfinity).Should().Be(double.NegativeInfinity);
    }
}
