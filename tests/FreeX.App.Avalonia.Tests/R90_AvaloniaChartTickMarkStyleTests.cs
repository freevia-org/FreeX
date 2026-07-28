using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Immutable;

using FreeX.App.Avalonia.Charts;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R90-render-chart-axis-titles-5-1: the Avalonia shell renderer's RenderAxis unconditionally drew an
/// "outside" tick mark at every tick position regardless of <see cref="ChartModel.XAxisMajorTickStyle"/>
/// / <see cref="ChartModel.YAxisMajorTickStyle"/> -- so a chart configured for None/Inside/Cross tick
/// marks still rendered ordinary outside ticks on the Linux/macOS shell, diverging from both real Excel
/// and FreeX's own WPF/OxyPlot rendering of the same chart (ChartRenderer.Axes.cs' ApplyTickAndLabelStyle
/// does honor it). These tests drive the real rendering entry point (<see cref="AvaloniaChartRenderer.Render"/>)
/// used by the actual Linux/macOS shell, not a hand-built AxisLayout, per the round-90 real-entry-point
/// requirement.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R90_AvaloniaChartTickMarkStyleTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static ChartLayoutRequest BuildRequest(ChartModel chart) =>
        new()
        {
            Chart = chart,
            Categories = ["A", "B", "C"],
            Series = [new ChartSeriesData { SeriesIndex = 0, Values = [10, 20, 30] }],
            PlotArea = new PlotRect(0, 0, 300, 200),
            TextMeasurer = new AvaloniaTextMeasurer(),
        };

    // ── Major tick style: None suppresses tick marks entirely (fails before the R90 fix) ──────

    [Fact]
    public async Task R90_MajorTickStyle_None_on_both_axes_draws_no_tick_marks_only_the_two_axis_baselines()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel
            {
                Type = ChartType.Column,
                ShowLegend = false,
                XAxisMajorTickStyle = ChartAxisTickStyle.None,
                YAxisMajorTickStyle = ChartAxisTickStyle.None,
            };
            var request = BuildRequest(chart);
            var layout = ChartLayoutEngine.Layout(request);

            var renderer = new AvaloniaChartRenderer(chart, WorkbookTheme.Office);
            var canvas = renderer.Render(layout, 300, 200);

            // Before the fix every tick position got an unconditional outside AddLine regardless of
            // XAxisMajorTickStyle/YAxisMajorTickStyle, so this would be 2 baselines + N category ticks
            // + M value ticks. After the fix, None means zero tick-mark lines are drawn at all --
            // exactly the two axis baselines (category + value) remain.
            canvas.Children.OfType<Line>().Should().HaveCount(2,
                "R90: XAxisMajorTickStyle=None and YAxisMajorTickStyle=None must suppress every tick mark, leaving only the category and value axis baselines");
        }, CancellationToken.None);
    }

    // ── No-regression sibling: the default (Outside) major style still draws every tick ────────

    [Fact]
    public async Task R90_MajorTickStyle_default_Outside_still_draws_a_tick_per_position_on_both_axes()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel
            {
                Type = ChartType.Column,
                ShowLegend = false,
                // XAxisMajorTickStyle / YAxisMajorTickStyle left at their model default (Outside).
            };
            var request = BuildRequest(chart);
            var layout = ChartLayoutEngine.Layout(request);

            layout.CategoryAxis.Should().NotBeNull();
            layout.ValueAxis.Should().NotBeNull();
            var expectedTickLines = layout.CategoryAxis!.Ticks.Count + layout.ValueAxis!.Ticks.Count;

            var renderer = new AvaloniaChartRenderer(chart, WorkbookTheme.Office);
            var canvas = renderer.Render(layout, 300, 200);

            canvas.Children.OfType<Line>().Should().HaveCount(2 + expectedTickLines,
                "the default Outside tick style must keep drawing a tick mark at every category and value axis position (no regression from the R90 fix)");
        }, CancellationToken.None);
    }

    // ── Cross style: tick marks straddle the axis line on both sides ───────────────────────────

    [Fact]
    public async Task R90_MajorTickStyle_Cross_on_value_axis_draws_ticks_spanning_both_sides_of_the_axis_line()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel
            {
                Type = ChartType.Column,
                ShowLegend = false,
                XAxisMajorTickStyle = ChartAxisTickStyle.None, // isolate the value (Y, vertical/Left) axis
                YAxisMajorTickStyle = ChartAxisTickStyle.Cross,
            };
            var request = BuildRequest(chart);
            var layout = ChartLayoutEngine.Layout(request);
            var linePosition = layout.ValueAxis!.LinePosition;

            var renderer = new AvaloniaChartRenderer(chart, WorkbookTheme.Office);
            var canvas = renderer.Render(layout, 300, 200);

            // Value-axis tick marks are horizontal segments (Y constant, X varies); the vertical
            // baseline itself has X constant instead, so this isolates the tick marks.
            var valueAxisTicks = canvas.Children.OfType<Line>()
                .Where(l => l.StartPoint.Y == l.EndPoint.Y && l.StartPoint.X != l.EndPoint.X)
                .ToList();

            valueAxisTicks.Should().NotBeEmpty("Cross must still draw tick marks, just on both sides of the axis line");
            valueAxisTicks.Should().Contain(l =>
                    Math.Min(l.StartPoint.X, l.EndPoint.X) < linePosition &&
                    Math.Max(l.StartPoint.X, l.EndPoint.X) > linePosition,
                "R90: Cross tick marks must extend to both sides of the axis line, unlike Outside (which only ever extended away from the plot area)");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ChartAxisStyle_UsesPersistedLabelAndLineAppearance()
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel
            {
                Type = ChartType.Column,
                ShowLegend = false,
                XAxisLabelFontSize = 17,
                XAxisLabelAngle = -45,
                XAxisLabelTextColor = new CellColor(1, 2, 3),
                XAxisLineColor = new CellColor(4, 5, 6),
                XAxisLineThickness = 3,
            };
            var layout = ChartLayoutEngine.Layout(BuildRequest(chart));
            var canvas = new AvaloniaChartRenderer(chart, WorkbookTheme.Office).Render(layout, 300, 200);

            var label = canvas.Children.OfType<TextBlock>().First(text => text.Text == "A");
            label.FontSize.Should().Be(17);
            label.RenderTransform.Should().BeOfType<RotateTransform>();
            var labelBrush = label.Foreground.Should().BeOfType<ImmutableSolidColorBrush>().Subject;
            labelBrush.Color.Should().Be(Color.FromRgb(1, 2, 3));

            var axisLine = canvas.Children.OfType<Line>().First(line =>
                line.StrokeThickness == 3 &&
                line.Stroke is ImmutableSolidColorBrush brush && brush.Color == Color.FromRgb(4, 5, 6));
            axisLine.StrokeThickness.Should().Be(3);

            chart.ShowXAxisLabels = false;
            var withoutLabels = new AvaloniaChartRenderer(chart, WorkbookTheme.Office)
                .Render(ChartLayoutEngine.Layout(BuildRequest(chart)), 300, 200);
            withoutLabels.Children.OfType<TextBlock>().Should().NotContain(text => text.Text == "A");
        }, CancellationToken.None);
    }
}
