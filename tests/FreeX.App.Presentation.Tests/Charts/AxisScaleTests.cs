using FluentAssertions;
using FreeX.App.Presentation.Charts;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class AxisScaleTests
{
    private static readonly PlotRect Plot = new(0, 0, 200, 100);

    [Theory]
    [InlineData(0, 10, 7, 1)]      // range 10 / 7 ≈ 1.43 -> normalized < 1.5 -> nice 1
    [InlineData(0, 100, 7, 10)]    // range 100 / 7 ≈ 14.3 -> normalized 1.43 -> nice 10
    [InlineData(0, 1, 5, 0.2)]     // range 1 / 5 = 0.2 -> nice 0.2
    [InlineData(0, 50, 5, 10)]     // range 50 / 5 = 10 -> nice 10
    [InlineData(0, 8, 4, 2)]       // range 8 / 4 = 2 -> nice 2
    [InlineData(0, 35, 5, 10)]     // range 35 / 5 = 7 -> normalized 7 -> nice 10
    [InlineData(0, 21, 5, 5)]      // range 21 / 5 = 4.2 -> normalized in [3,7) -> nice 5
    public void CalculateNiceStep_picks_round_numbers(double min, double max, int target, double expected)
    {
        AxisScale.CalculateNiceStep(max - min, target).Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void CalculateNiceStep_handles_nonpositive_range()
    {
        AxisScale.CalculateNiceStep(0, 7).Should().Be(1);
        AxisScale.CalculateNiceStep(-5, 7).Should().Be(1);
    }

    [Fact]
    public void ValueAxis_baselines_at_zero_for_all_positive_data()
    {
        var scale = AxisScale.CreateValueAxis(20, 80, Plot, AxisSide.Left);
        scale.Minimum.Should().Be(0, "value axes include the zero line when all data is positive");
        scale.Maximum.Should().BeGreaterThanOrEqualTo(80);
    }

    [Fact]
    public void ValueAxis_extends_below_zero_for_negative_data()
    {
        var scale = AxisScale.CreateValueAxis(-30, 50, Plot, AxisSide.Left);
        scale.Minimum.Should().BeLessThanOrEqualTo(-30);
        scale.Maximum.Should().BeGreaterThanOrEqualTo(50);
    }

    [Fact]
    public void ValueAxis_rounds_bounds_out_to_the_major_step()
    {
        var scale = AxisScale.CreateValueAxis(0, 95, Plot, AxisSide.Left);
        // range 95 / 7 ≈ 13.6 -> step 10; max rounds up to 100.
        scale.MajorStep.Should().Be(10);
        scale.Maximum.Should().Be(100);
    }

    [Fact]
    public void ExplicitBounds_and_step_override_auto_values()
    {
        var scale = AxisScale.CreateValueAxis(0, 100, Plot, AxisSide.Left,
            explicitMin: 5, explicitMax: 105, explicitStep: 25);
        scale.Minimum.Should().Be(5);
        scale.Maximum.Should().Be(105);
        scale.MajorStep.Should().Be(25);
    }

    [Fact]
    public void HorizontalAxis_transforms_min_to_left_and_max_to_right()
    {
        var scale = AxisScale.CreateValueAxis(0, 100, Plot, AxisSide.Bottom, explicitMin: 0, explicitMax: 100);
        scale.Transform(0).Should().BeApproximately(Plot.Left, 1e-9);
        scale.Transform(100).Should().BeApproximately(Plot.Right, 1e-9);
        scale.Transform(50).Should().BeApproximately((Plot.Left + Plot.Right) / 2, 1e-9);
    }

    [Fact]
    public void VerticalAxis_transforms_max_to_top_and_min_to_bottom()
    {
        var scale = AxisScale.CreateValueAxis(0, 100, Plot, AxisSide.Left, explicitMin: 0, explicitMax: 100);
        scale.Transform(0).Should().BeApproximately(Plot.Bottom, 1e-9, "smallest value sits at the plot bottom");
        scale.Transform(100).Should().BeApproximately(Plot.Top, 1e-9, "largest value sits at the plot top");
    }

    [Fact]
    public void Transform_and_InverseTransform_round_trip()
    {
        var scale = AxisScale.CreateValueAxis(0, 100, Plot, AxisSide.Left, explicitMin: 0, explicitMax: 100);
        foreach (var value in new[] { 0.0, 12.5, 50, 87.3, 100 })
            scale.InverseTransform(scale.Transform(value)).Should().BeApproximately(value, 1e-9);
    }

    [Fact]
    public void GetMajorTickValues_spans_min_to_max_on_the_step_grid()
    {
        var scale = AxisScale.CreateValueAxis(0, 100, Plot, AxisSide.Left, explicitMin: 0, explicitMax: 100, explicitStep: 20);
        scale.GetMajorTickValues().Should().Equal(0, 20, 40, 60, 80, 100);
    }

    [Fact]
    public void GetMajorTickValues_includes_negative_ticks()
    {
        var scale = AxisScale.CreateValueAxis(-40, 40, Plot, AxisSide.Left, explicitMin: -40, explicitMax: 40, explicitStep: 20);
        scale.GetMajorTickValues().Should().Equal(-40, -20, 0, 20, 40);
    }

    [Fact]
    public void IndexAxis_maps_index_range_onto_plot_extent()
    {
        var scale = AxisScale.CreateIndexAxis(-0.5, 3.5, Plot, AxisSide.Bottom);
        scale.Transform(-0.5).Should().BeApproximately(Plot.Left, 1e-9);
        scale.Transform(3.5).Should().BeApproximately(Plot.Right, 1e-9);
        // Category 0 sits one eighth in (0.5 of 4 units from -0.5).
        scale.Transform(0).Should().BeApproximately(Plot.Left + (0.5 / 4.0) * Plot.Width, 1e-9);
    }

    // ---- Logarithmic axis (F5) --------------------------------------------------------------

    [Fact]
    public void LogValueAxis_is_flagged_logarithmic_with_decade_bounds()
    {
        var scale = AxisScale.CreateLogValueAxis(5, 500, Plot, AxisSide.Left);
        scale.IsLogarithmic.Should().BeTrue();
        scale.LogBase.Should().Be(10);
        // Bounds snap out to whole decades: 5 -> 1, 500 -> 1000.
        scale.Minimum.Should().BeApproximately(1, 1e-9);
        scale.Maximum.Should().BeApproximately(1000, 1e-9);
    }

    [Fact]
    public void LogValueAxis_GetMajorTickValues_returns_one_tick_per_decade()
    {
        var scale = AxisScale.CreateLogValueAxis(1, 1000, Plot, AxisSide.Left);
        scale.GetMajorTickValues().Should().Equal(1, 10, 100, 1000);
    }

    [Fact]
    public void LogValueAxis_Transform_is_log_spaced_not_linear()
    {
        var scale = AxisScale.CreateLogValueAxis(1, 1000, Plot, AxisSide.Bottom, explicitMin: 1, explicitMax: 1000);
        // Equal ratios (each decade) must produce equal pixel spacing on a log axis.
        var x1 = scale.Transform(1);
        var x10 = scale.Transform(10);
        var x100 = scale.Transform(100);
        var x1000 = scale.Transform(1000);

        (x10 - x1).Should().BeApproximately(x100 - x10, 1e-6, "each decade spans the same pixel distance on a log axis");
        (x100 - x10).Should().BeApproximately(x1000 - x100, 1e-6);

        // A linear axis would NOT space these equally (100 is only 10% of the way from 1 to 1000
        // in the middle, not two-thirds of the way as it is on a log axis) — sanity-check the
        // midpoint (100) sits at 2/3 across a 3-decade span, not near the start.
        var fraction = (x100 - x1) / (x1000 - x1);
        fraction.Should().BeApproximately(2.0 / 3.0, 1e-6);
    }

    [Fact]
    public void LogValueAxis_InverseTransform_round_trips_through_log_space()
    {
        var scale = AxisScale.CreateLogValueAxis(1, 1000, Plot, AxisSide.Bottom, explicitMin: 1, explicitMax: 1000);
        foreach (var value in new[] { 1.0, 10.0, 50.0, 100.0, 1000.0 })
            scale.InverseTransform(scale.Transform(value)).Should().BeApproximately(value, 1e-6);
    }

    [Fact]
    public void LogValueAxis_guards_nonpositive_data_by_falling_back_to_a_positive_range()
    {
        // All-non-positive data has no valid log range; the axis must still produce a usable,
        // finite scale instead of NaN/-Infinity bounds.
        var scale = AxisScale.CreateLogValueAxis(-10, 0, Plot, AxisSide.Left);
        scale.Minimum.Should().BeGreaterThan(0);
        scale.Maximum.Should().BeGreaterThan(scale.Minimum);
        double.IsNaN(scale.Transform(5)).Should().BeFalse();
    }
}
