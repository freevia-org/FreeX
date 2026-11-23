using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// R87-render-chart-plot-5-1: the portable chart layout (Avalonia + PDF, both driven by
/// ChartLayoutEngine/AxisScale) used to force the value axis to include zero for every chart type
/// whenever the data was all-positive, even for Line/Scatter/Bubble -- which have no zero-anchored
/// geometry and should auto-fit tight to the data extents instead, matching both Excel and FreeX's
/// own WPF renderer (ChartRenderer.cs "else // Line / 3D Line" creates a plain LinearAxis with no
/// Minimum/Maximum override, so OxyPlot's own tight auto-range is used). Column/Bar/Area keep the
/// zero-forced baseline (their geometry is drawn from a zero baseline, e.g. Column's
/// RectangleBarItem(Math.Min(0, v), Math.Max(0, v)) and Area's documented "fills each band down to
/// the flat zero baseline" -- confirmed empirically: OxyPlot's own AreaSeries auto-ranges to include
/// its ConstantY2 = 0 baseline, exactly like Column, unlike LineSeries/ScatterSeries).
/// </summary>
public sealed class R87_LineValueAxisTightRangeTests
{
    // A "zoomed-in trend" data set clustered far from zero (e.g. temperature/stock-like values).
    private static readonly double?[] TightClusterValues = [1000, 1005, 998, 1010];

    [Fact]
    public void Line_chart_value_axis_auto_fits_tight_to_data_instead_of_forcing_zero()
    {
        var request = Request(Chart(ChartType.Line), ["A", "B", "C", "D"], [Series(0, "S1", TightClusterValues)]);
        var layout = ChartLayoutEngine.Layout(request);

        var scale = layout.ValueAxis!.Scale;
        // Before the fix this was forced to 0; the correct auto-fit range stays well above zero.
        scale.Minimum.Should().BeGreaterThan(500, "a Line chart's value axis should not be pulled down to zero for tightly-clustered positive data");
        scale.Maximum.Should().BeLessThan(2000);
    }

    [Fact]
    public void Scatter_chart_both_axes_auto_fit_tight_to_data_instead_of_forcing_zero()
    {
        var request = Request(
            Chart(ChartType.Scatter),
            [],
            [ScatterSeries(0, "S1", [1000, 1002, 1004, 1006], TightClusterValues)]);
        var layout = ChartLayoutEngine.Layout(request);

        layout.CategoryAxis!.Scale.Minimum.Should().BeGreaterThan(500, "Scatter's X axis should auto-fit tight, not force zero");
        layout.ValueAxis!.Scale.Minimum.Should().BeGreaterThan(500, "Scatter's Y axis should auto-fit tight, not force zero");
    }

    // ---- No-regression siblings: bar-geometry chart types keep the zero-forced baseline ----

    [Fact]
    public void Column_chart_value_axis_baselines_at_zero_for_wide_spread_positive_data()
    {
        var request = Request(Chart(ChartType.Column), ["A", "B", "C", "D"], [Series(0, "S1", 200, 500, 800, 1000)]);
        var layout = ChartLayoutEngine.Layout(request);

        layout.ValueAxis!.Scale.Minimum.Should().Be(0, "Column bars grow from a zero baseline when the data spread is wide");
    }

    [Fact]
    public void Column_chart_with_a_tight_cluster_truncates_the_axis_like_excel()
    {
        // Excel (measured): the 998..1010 cluster is 1.2% of its magnitude, below the 1/6 threshold, so even a
        // column chart gets a non-zero baseline (992..1012 by 2).
        var request = Request(Chart(ChartType.Column), ["A", "B", "C", "D"], [Series(0, "S1", TightClusterValues)]);
        var scale = ChartLayoutEngine.Layout(request).ValueAxis!.Scale;

        scale.Minimum.Should().Be(992);
        scale.Maximum.Should().Be(1012);
        scale.MajorStep.Should().Be(2);
    }

    [Fact]
    public void Area_chart_value_axis_baselines_at_zero_for_wide_spread_positive_data()
    {
        var request = Request(Chart(ChartType.Area), ["A", "B", "C", "D"], [Series(0, "S1", 200, 500, 800, 1000)]);
        var layout = ChartLayoutEngine.Layout(request);

        layout.ValueAxis!.Scale.Minimum.Should().Be(0, "Area bands fill down to a zero baseline when the data spread is wide");
    }
}
