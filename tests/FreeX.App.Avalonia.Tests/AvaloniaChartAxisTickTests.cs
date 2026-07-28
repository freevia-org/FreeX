using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests verifying that axis tick-label angle and number-format metadata thread correctly
/// through AxisLayout and are exposed for the shell renderer to apply.
/// No Avalonia headless session is required — all assertions are on the portable data model.
/// </summary>
public sealed class AvaloniaChartAxisTickTests
{
    // ── AxisLayout.LabelAngle is surfaced ────────────────────────────────────

    [Fact]
    public void AxisLayout_LabelAngle_DefaultsToZero()
    {
        var axis = new AxisLayout
        {
            Side = AxisSide.Bottom,
            LinePosition = 200,
            Ticks = [],
            Scale = AxisScale.CreateValueAxis(0, 100, new PlotRect(0, 0, 300, 200), AxisSide.Bottom),
        };
        axis.LabelAngle.Should().Be(0, "default label angle must be 0 (horizontal)");
    }

    [Fact]
    public void AxisLayout_LabelAngle_CanBeSetToNegative45()
    {
        var axis = new AxisLayout
        {
            Side = AxisSide.Bottom,
            LinePosition = 200,
            Ticks = [],
            Scale = AxisScale.CreateValueAxis(0, 100, new PlotRect(0, 0, 300, 200), AxisSide.Bottom),
            LabelAngle = -45,
        };
        axis.LabelAngle.Should().Be(-45, "a -45 degree label angle must round-trip through AxisLayout");
    }

    [Fact]
    public void AxisLayout_LabelAngle_CanBeSetToPositive90()
    {
        var axis = new AxisLayout
        {
            Side = AxisSide.Left,
            LinePosition = 50,
            Ticks = [],
            Scale = AxisScale.CreateValueAxis(0, 100, new PlotRect(0, 0, 300, 200), AxisSide.Left),
            LabelAngle = 90,
        };
        axis.LabelAngle.Should().Be(90);
    }

    // ── AxisLayout.LabelAngle threading from ChartLayoutEngine ──────────────

    [Fact]
    public void ChartLayout_CategoryAxis_CarriesXAxisLabelAngle_FromColumnChart()
    {
        // Category axis of a column chart is horizontal (bottom). Angle from XAxisLabelAngle.
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            ShowLegend = false,
            XAxisLabelAngle = -45,
        };
        var request = new ChartLayoutRequest
        {
            Chart = chart,
            Categories = ["Jan", "Feb", "Mar"],
            Series = [new ChartSeriesData { SeriesIndex = 0, Values = [100, 200, 150] }],
            PlotArea = new PlotRect(0, 0, 400, 300),
            TextMeasurer = new FakeAxisTestMeasurer(),
        };

        var layout = ChartLayoutEngine.Layout(request);

        layout.CategoryAxis.Should().NotBeNull();
        layout.CategoryAxis!.LabelAngle.Should().Be(-45,
            "XAxisLabelAngle = -45 on a column chart must appear on the category (X) axis");
    }

    [Fact]
    public void ChartLayout_ValueAxis_CarriesYAxisLabelAngle_FromColumnChart()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            ShowLegend = false,
            YAxisLabelAngle = -30,
        };
        var request = new ChartLayoutRequest
        {
            Chart = chart,
            Categories = ["A", "B"],
            Series = [new ChartSeriesData { SeriesIndex = 0, Values = [500, 1000] }],
            PlotArea = new PlotRect(0, 0, 400, 300),
            TextMeasurer = new FakeAxisTestMeasurer(),
        };

        var layout = ChartLayoutEngine.Layout(request);

        layout.ValueAxis.Should().NotBeNull();
        layout.ValueAxis!.LabelAngle.Should().Be(-30,
            "YAxisLabelAngle = -30 on a column chart must appear on the value (Y) axis");
    }

    // ── Custom number format code round-trip (format applied to tick labels) ──

    [Fact]
    public void ChartLayout_ValueAxis_CustomFormatCode_ProducesCurrencyTickLabel()
    {
        // "$#,##0" on value axis with 1000 should produce a "$1,000" tick label.
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            ShowLegend = false,
            YAxisNumberFormat = ChartDataLabelNumberFormat.General,
            YAxisNumberFormatCode = "$#,##0",
        };
        var request = new ChartLayoutRequest
        {
            Chart = chart,
            Categories = ["A"],
            Series = [new ChartSeriesData { SeriesIndex = 0, Values = [1000.0] }],
            PlotArea = new PlotRect(0, 0, 400, 300),
            TextMeasurer = new FakeAxisTestMeasurer(),
        };

        var layout = ChartLayoutEngine.Layout(request);

        layout.ValueAxis.Should().NotBeNull();
        var tick1000 = layout.ValueAxis!.Ticks
            .OrderBy(t => Math.Abs(t.Value - 1000))
            .First();
        tick1000.Label.Should().Be("$1,000",
            "custom format '$#,##0' applied to 1000 must produce '$1,000'");
    }

    [Fact]
    public void ChartLayout_ValueAxis_CustomFormatCode_TakesPriorityOverEnum()
    {
        // When a custom format code is set it should override the enum (Number → not showing "0.00").
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            ShowLegend = false,
            YAxisNumberFormat = ChartDataLabelNumberFormat.Number, // enum says "0.00"
            YAxisNumberFormatCode = "$#,##0",                     // code overrides
        };
        var request = new ChartLayoutRequest
        {
            Chart = chart,
            Categories = ["A"],
            Series = [new ChartSeriesData { SeriesIndex = 0, Values = [2000.0] }],
            PlotArea = new PlotRect(0, 0, 400, 300),
            TextMeasurer = new FakeAxisTestMeasurer(),
        };

        var layout = ChartLayoutEngine.Layout(request);

        layout.ValueAxis.Should().NotBeNull();
        var tick2000 = layout.ValueAxis!.Ticks
            .OrderBy(t => Math.Abs(t.Value - 2000))
            .First();
        tick2000.Label.Should().Contain("$",
            "custom format code must override the Number enum — label must contain '$'");
        tick2000.Label.Should().NotContain(".00",
            "custom '$#,##0' code has no decimal places, so '.00' must not appear");
    }

    // ── Axis tick label angle metadata available to renderer ──────────────────

    [Fact]
    public void AxisLayout_LabelAngle_IsNonZeroWhenModelHasAngle()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            ShowLegend = false,
            XAxisLabelAngle = -45,
        };
        var request = new ChartLayoutRequest
        {
            Chart = chart,
            Categories = ["Q1", "Q2", "Q3", "Q4"],
            Series = [new ChartSeriesData { SeriesIndex = 0, Values = [10.0, 20.0, 30.0, 40.0] }],
            PlotArea = new PlotRect(0, 0, 400, 300),
            TextMeasurer = new FakeAxisTestMeasurer(),
        };

        var layout = ChartLayoutEngine.Layout(request);

        // The shell renderer reads LabelAngle from the AxisLayout and applies a rotation.
        // This test verifies the contract that the angle flows through without being clamped or zeroed.
        layout.CategoryAxis!.LabelAngle.Should().NotBe(0,
            "a non-zero XAxisLabelAngle must produce a non-zero LabelAngle on the category axis layout");
        layout.CategoryAxis.LabelAngle.Should().Be(-45);
    }

    [Fact]
    public void ChartLayout_ValueAxis_MinorTickStyleProducesTicksWithoutMinorGridlines()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            ShowLegend = false,
            YAxisMinimum = 0,
            YAxisMaximum = 20,
            YAxisMajorUnit = 10,
            YAxisMinorUnit = 5,
            YAxisMinorTickStyle = ChartAxisTickStyle.Inside,
            ShowYAxisMinorGridlines = false,
        };
        var layout = ChartLayoutEngine.Layout(new ChartLayoutRequest
        {
            Chart = chart,
            Categories = ["A", "B"],
            Series = [new ChartSeriesData { SeriesIndex = 0, Values = [5, 15] }],
            PlotArea = new PlotRect(0, 0, 400, 300),
            TextMeasurer = new FakeAxisTestMeasurer(),
        });

        layout.ValueAxis!.MinorTicks.Should().NotBeNullOrEmpty(
            "minor tick marks are independent of minor gridline visibility");
    }
}

/// <summary>Minimal text measurer stub for axis layout tests (no font measurement needed).</summary>
internal sealed class FakeAxisTestMeasurer : FreeX.App.Presentation.Text.ITextMeasurer
{
    public FreeX.App.Presentation.Text.TextSize Measure(string? text, string? fontName, double fontSize, bool bold, bool italic)
        => new((text?.Length ?? 1) * fontSize * 0.6, fontSize + 4);
}
