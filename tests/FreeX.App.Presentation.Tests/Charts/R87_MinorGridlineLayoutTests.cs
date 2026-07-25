using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// R87-render-chart-plot-5-4: minor axis gridlines (Format Axis > Gridlines > Minor Gridlines) were
/// drawn by the WPF renderer (ChartRenderer.Axes.cs ApplyGridlineStyle sets
/// axis.MinorGridlineStyle = Dot using ChartModel.[X/Y]AxisMinorUnit) but the portable layout
/// (AxisLayout, consumed by Avalonia and the PDF builder) never computed any minor-tick data at all,
/// so the portable shells silently dropped them. AxisLayout now carries a MinorTicks list, populated
/// by BuildValueAxisLayout whenever the chart model requests minor gridlines for that axis.
/// </summary>
public sealed class R87_MinorGridlineLayoutTests
{
    [Fact]
    public void Value_axis_emits_minor_ticks_when_minor_gridlines_are_requested()
    {
        var request = Request(Chart(ChartType.Column, c =>
        {
            c.ShowYAxisMinorGridlines = true;
            c.YAxisMajorUnit = 100;
            c.YAxisMinorUnit = 20;
        }), ["A", "B"], [Series(0, "S1", 50, 180)]);
        var layout = ChartLayoutEngine.Layout(request);

        var minorTicks = layout.ValueAxis!.MinorTicks;
        minorTicks.Should().NotBeNull("minor gridlines were requested with an explicit minor unit");
        minorTicks!.Should().NotBeEmpty();
        // Minor ticks land on 20-unit boundaries between the (zero-forced, column) axis bounds.
        minorTicks.Select(t => t.Value).Should().Contain(20);
        minorTicks.Select(t => t.Value).Should().Contain(180);
    }

    // ---- No-regression sibling: minor gridlines off (the default) still emit no minor-tick data ----

    [Fact]
    public void Value_axis_emits_no_minor_ticks_when_minor_gridlines_are_not_requested()
    {
        var request = Request(Chart(ChartType.Column, c =>
        {
            c.YAxisMajorUnit = 100;
            c.YAxisMinorUnit = 20;
            // ShowYAxisMinorGridlines left at its default (false).
        }), ["A", "B"], [Series(0, "S1", 50, 180)]);
        var layout = ChartLayoutEngine.Layout(request);

        layout.ValueAxis!.MinorTicks.Should().BeNull("minor gridlines were not requested");
    }
}
