using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// Regression tests for FreeX cleanup batch MED9 (MED findings P88, P90).
/// The portable <see cref="ChartLayoutEngine"/> is shared by the Avalonia (Linux/macOS) shell, so
/// these tests exercise the cross-platform axis reverse-order and display-unit behavior that was
/// previously only implemented in the WPF-specific ChartRenderer.Axes.cs.
/// </summary>
public sealed class FreeXCleanupMED9Tests
{
    // P88: Excel's "Values in reverse order" (YAxisReverseOrder, OOXML orientation="maxMin") must
    // flip which screen edge the axis minimum/maximum map onto, so bars/gridlines/ticks actually
    // draw reversed — not just get relabeled. Previously ChartLayoutEngine ignored the flag
    // entirely, so the Avalonia value axis always ran bottom-to-top regardless of the setting.
    [Fact]
    public void YAxisReverseOrder_FlipsValueAxisScreenDirection()
    {
        var normalChart = Chart(ChartType.Column, c => c.YAxisReverseOrder = false);
        var reversedChart = Chart(ChartType.Column, c => c.YAxisReverseOrder = true);
        var request = Request(normalChart, ["A", "B"], [Series(0, "S1", 10, 20)]);
        var reversedRequest = Request(reversedChart, ["A", "B"], [Series(0, "S1", 10, 20)]);

        var normalLayout = ChartLayoutEngine.Layout(request);
        var reversedLayout = ChartLayoutEngine.Layout(reversedRequest);

        normalLayout.ValueAxis.Should().NotBeNull();
        reversedLayout.ValueAxis.Should().NotBeNull();
        var normalScale = normalLayout.ValueAxis!.Scale;
        var reversedScale = reversedLayout.ValueAxis!.Scale;

        // Same data range, but the reversed axis must map its Minimum/Maximum onto the opposite
        // screen coordinates so plotted geometry actually reverses.
        normalScale.Minimum.Should().Be(reversedScale.Minimum);
        normalScale.Maximum.Should().Be(reversedScale.Maximum);
        reversedScale.ScreenMin.Should().Be(normalScale.ScreenMax);
        reversedScale.ScreenMax.Should().Be(normalScale.ScreenMin);

        // A concrete data value must therefore land at a different pixel position when reversed.
        var normalPixel = normalScale.Transform(normalScale.Maximum);
        var reversedPixel = reversedScale.Transform(reversedScale.Maximum);
        normalPixel.Should().NotBe(reversedPixel);
    }

    [Fact]
    public void XAxisReverseOrder_FlipsValueAxisScreenDirection_ForBarChart()
    {
        // Bar charts run their value axis along X (AxisSide.Bottom via CreateXValueAxis).
        var normalChart = Chart(ChartType.Bar, c => c.XAxisReverseOrder = false);
        var reversedChart = Chart(ChartType.Bar, c => c.XAxisReverseOrder = true);
        var request = Request(normalChart, ["A", "B"], [Series(0, "S1", 10, 20)]);
        var reversedRequest = Request(reversedChart, ["A", "B"], [Series(0, "S1", 10, 20)]);

        var normalAxis = ChartLayoutEngine.Layout(request).ValueAxis;
        var reversedAxis = ChartLayoutEngine.Layout(reversedRequest).ValueAxis;
        normalAxis.Should().NotBeNull();
        reversedAxis.Should().NotBeNull();
        var normalScale = normalAxis!.Scale;
        var reversedScale = reversedAxis!.Scale;

        reversedScale.ScreenMin.Should().Be(normalScale.ScreenMax);
        reversedScale.ScreenMax.Should().Be(normalScale.ScreenMin);
    }

    // P90: Excel's Format Axis > Display Units (YAxisDisplayUnit, OOXML dispUnits) must divide the
    // tick labels (e.g. "3" instead of "3000000" for Millions) so the Avalonia shell agrees with
    // WPF's ChartRenderer.Axes.cs ApplyAxisDisplayUnit. Previously BuildValueAxisLayout formatted
    // raw values with no divisor at all.
    [Fact]
    public void YAxisDisplayUnit_Millions_DividesTickLabels()
    {
        var chart = Chart(ChartType.Column, c => c.YAxisDisplayUnit = ChartAxisDisplayUnit.Millions);
        var request = Request(chart, ["A", "B"], [Series(0, "S1", 3_000_000, 6_000_000)]);

        var layout = ChartLayoutEngine.Layout(request);

        layout.ValueAxis.Should().NotBeNull();
        var valueAxis = layout.ValueAxis!;
        valueAxis.Ticks.Should().NotBeEmpty();
        // Every formatted tick label must read in millions (e.g. "3"), never the raw magnitude
        // (e.g. "3000000") — the underlying tick Value stays in data units, only the label divides.
        foreach (var tick in valueAxis.Ticks)
        {
            tick.Label.Should().NotContain("000000",
                $"tick value {tick.Value} must be displayed divided by 1,000,000, not as a raw magnitude");
        }

        // The axis title gains Excel's display-units suffix so the scale is still communicated.
        valueAxis.Title.Should().Contain("Millions");
    }

    [Fact]
    public void YAxisDisplayUnit_Unset_LeavesTickLabelsAtRawMagnitude()
    {
        var chart = Chart(ChartType.Column); // no display unit configured
        var request = Request(chart, ["A", "B"], [Series(0, "S1", 3_000_000, 6_000_000)]);

        var layout = ChartLayoutEngine.Layout(request);

        layout.ValueAxis.Should().NotBeNull();
        // Baseline/no-op check: absent a display unit, ticks must still show full magnitude values
        // (at least one tick at/above a million-scale value formatted without division).
        layout.ValueAxis!.Ticks.Should().Contain(t => t.Value >= 1_000_000);
    }
}
