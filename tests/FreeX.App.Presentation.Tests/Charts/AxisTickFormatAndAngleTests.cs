using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// Tests that value-axis tick labels apply the axis number format (enum + custom format code)
/// and that axis label rotation angles thread through AxisLayout correctly.
/// </summary>
public sealed class AxisTickFormatAndAngleTests
{
    // ── Value-axis number format (enum path) ────────────────────────────────

    [Fact]
    public void ValueAxis_General_format_renders_raw_number()
    {
        var chart = Chart(ChartType.Column, c => c.YAxisNumberFormat = ChartDataLabelNumberFormat.General);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"], [Series(0, "S1", 100, 200)]));

        layout.ValueAxis.Should().NotBeNull();
        layout.ValueAxis!.Ticks.Should().NotBeEmpty();
        // General format: no currency symbol, no percent sign.
        layout.ValueAxis.Ticks.Any(t => t.Label.Contains('$')).Should().BeFalse("General format has no currency symbol");
        layout.ValueAxis.Ticks.Any(t => t.Label.Contains('%')).Should().BeFalse("General format has no percent sign");
    }

    [Fact]
    public void ValueAxis_Currency_enum_format_produces_dollar_labels()
    {
        var chart = Chart(ChartType.Column, c => c.YAxisNumberFormat = ChartDataLabelNumberFormat.Currency);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"], [Series(0, "S1", 1000, 2000)]));

        layout.ValueAxis.Should().NotBeNull();
        layout.ValueAxis!.Ticks.Should().NotBeEmpty();
        layout.ValueAxis.Ticks.Any(t => t.Label.Contains('$')).Should().BeTrue("Currency enum format must produce a dollar sign");
    }

    [Fact]
    public void ValueAxis_Percent_enum_format_produces_percent_labels()
    {
        var chart = Chart(ChartType.Column, c => c.YAxisNumberFormat = ChartDataLabelNumberFormat.Percent);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"], [Series(0, "S1", 0.5, 1.0)]));

        layout.ValueAxis.Should().NotBeNull();
        layout.ValueAxis!.Ticks.Should().NotBeEmpty();
        layout.ValueAxis.Ticks.Any(t => t.Label.Contains('%')).Should().BeTrue("Percent enum format must produce a percent sign");
    }

    // ── Value-axis custom number format code ────────────────────────────────

    [Fact]
    public void ValueAxis_custom_format_code_overrides_enum_and_applies_NumberFormatter()
    {
        // "$#,##0" applied to 1000 should produce "$1,000".
        var chart = Chart(ChartType.Column, c =>
        {
            c.YAxisNumberFormat = ChartDataLabelNumberFormat.General; // enum is overridden by the code
            c.YAxisNumberFormatCode = "$#,##0";
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A"], [Series(0, "S1", 1000)]));

        layout.ValueAxis.Should().NotBeNull();
        // Find the tick closest to 1000.
        var tick1000 = layout.ValueAxis!.Ticks
            .OrderBy(t => Math.Abs(t.Value - 1000))
            .First();
        tick1000.Label.Should().Be("$1,000",
            "custom format code '$#,##0' applied to 1000 must yield '$1,000'");
    }

    [Fact]
    public void ValueAxis_custom_format_percent_code_applies_correctly()
    {
        // "0%" applied to 0.5 should produce "50%".
        var chart = Chart(ChartType.Column, c =>
        {
            c.YAxisNumberFormat = ChartDataLabelNumberFormat.General;
            c.YAxisNumberFormatCode = "0%";
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"], [Series(0, "S1", 0.25, 0.75)]));

        layout.ValueAxis.Should().NotBeNull();
        // Some tick in the range should show a "%" sign.
        layout.ValueAxis!.Ticks.Any(t => t.Label.Contains('%')).Should().BeTrue(
            "custom '0%' format code must produce percent-sign labels");
    }

    [Fact]
    public void ValueAxis_empty_format_code_falls_back_to_enum()
    {
        var chart = Chart(ChartType.Column, c =>
        {
            c.YAxisNumberFormat = ChartDataLabelNumberFormat.Currency;
            c.YAxisNumberFormatCode = ""; // empty — falls back to enum
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A"], [Series(0, "S1", 1000)]));

        layout.ValueAxis.Should().NotBeNull();
        layout.ValueAxis!.Ticks.Any(t => t.Label.Contains('$')).Should().BeTrue(
            "empty format code falls back to the Currency enum which must produce a dollar sign");
    }

    // ── Label angle threading ───────────────────────────────────────────────

    [Fact]
    public void CategoryAxis_default_angle_is_zero()
    {
        var layout = ChartLayoutEngine.Layout(Request(Chart(ChartType.Column), ["A", "B"], [Series(0, "S1", 1, 2)]));

        layout.CategoryAxis.Should().NotBeNull();
        layout.CategoryAxis!.LabelAngle.Should().Be(0, "default X-axis label angle is 0 (horizontal)");
    }

    [Fact]
    public void CategoryAxis_label_angle_threads_from_model_XAxisLabelAngle()
    {
        var chart = Chart(ChartType.Column, c => c.XAxisLabelAngle = -45);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"], [Series(0, "S1", 1, 2, 3)]));

        layout.CategoryAxis.Should().NotBeNull();
        layout.CategoryAxis!.LabelAngle.Should().Be(-45,
            "XAxisLabelAngle = -45 must be carried through to AxisLayout.LabelAngle");
    }

    [Fact]
    public void ValueAxis_label_angle_threads_from_model_YAxisLabelAngle()
    {
        var chart = Chart(ChartType.Column, c => c.YAxisLabelAngle = -30);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"], [Series(0, "S1", 100, 200)]));

        layout.ValueAxis.Should().NotBeNull();
        layout.ValueAxis!.LabelAngle.Should().Be(-30,
            "YAxisLabelAngle = -30 must be carried through to the value AxisLayout.LabelAngle");
    }

    [Fact]
    public void ValueAxis_default_angle_is_zero()
    {
        var layout = ChartLayoutEngine.Layout(Request(Chart(ChartType.Column), ["A", "B"], [Series(0, "S1", 100, 200)]));

        layout.ValueAxis.Should().NotBeNull();
        layout.ValueAxis!.LabelAngle.Should().Be(0, "default Y-axis label angle is 0 (horizontal)");
    }

    [Fact]
    public void Bar_chart_category_angle_threads_as_YAxisLabelAngle()
    {
        // In bar charts the category axis is on the left (a vertical axis); its angle comes from YAxisLabelAngle.
        var chart = Chart(ChartType.Bar, c => c.YAxisLabelAngle = -45);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"], [Series(0, "S1", 1, 2)]));

        layout.CategoryAxis.Should().NotBeNull();
        layout.CategoryAxis!.LabelAngle.Should().Be(-45,
            "bar chart category axis angle is sourced from YAxisLabelAngle");
    }

    [Fact]
    public void Bar_chart_value_axis_angle_threads_as_XAxisLabelAngle()
    {
        var chart = Chart(ChartType.Bar, c => c.XAxisLabelAngle = -30);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"], [Series(0, "S1", 100, 200)]));

        layout.ValueAxis.Should().NotBeNull();
        layout.ValueAxis!.LabelAngle.Should().Be(-30,
            "bar chart value axis (bottom) angle is sourced from XAxisLabelAngle");
    }
}
