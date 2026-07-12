using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Text;
using FreeX.Core.Model;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

public sealed partial class ChartRendererTests
{
    // WPF↔Avalonia parity harness for stacked/100%-stacked area. The Windows host renders via OxyPlot
    // (BuildStackedAreaModel → AreaSeries.Points/Points2); the Linux/macOS host renders via the shared
    // ChartLayoutEngine (LayoutStackedAreas → SeriesLayout.Points/BaselinePoints). Both are driven from
    // the SAME two-series/two-category fixture (North = 10, 20; South = 5, 15) and must produce
    // band-identical geometry — the top polyline and the running-stack bottom of every band must agree
    // value-for-value — so opening a stacked-area workbook renders the same cumulative stack on both
    // platforms. Follow-up to R27-chart-types-deep-1.

    private sealed class NullTextMeasurer : ITextMeasurer
    {
        public TextSize Measure(string? text, string? fontFamily, double fontSize, bool bold, bool italic) => TextSize.Empty;
    }

    // The shared engine's portable equivalent of TwoSeriesTwoCategoryViewport (North = 10, 20; South = 5, 15).
    private static ChartLayout SharedStackedAreaLayout(ChartType type) =>
        ChartLayoutEngine.Layout(new ChartLayoutRequest
        {
            Chart = new ChartModel { Type = type, ShowLegend = false },
            Categories = ["Q1", "Q2"],
            Series =
            [
                new ChartSeriesData { SeriesIndex = 0, Name = "North", Values = [10, 20] },
                new ChartSeriesData { SeriesIndex = 1, Name = "South", Values = [5, 15] },
            ],
            PlotArea = new PlotRect(0, 0, 400, 300),
            TextMeasurer = new NullTextMeasurer(),
        });

    [Theory]
    [InlineData(ChartType.StackedArea)]
    [InlineData(ChartType.PercentStackedArea)]
    public void StackedArea_WpfOxyPlot_and_shared_engine_produce_band_identical_geometry(ChartType type)
    {
        var sheetId = SheetId.New();
        var wpf = BuildPlotModel(StackedAreaChart(type, sheetId), TwoSeriesTwoCategoryViewport());
        var shared = SharedStackedAreaLayout(type);

        var wpfBands = wpf.Series.OfType<AreaSeries>().ToList();
        wpfBands.Should().HaveCount(2);
        var sharedBands = shared.Series.Where(s => s.Kind == SeriesGeometryKind.Area).ToList();
        sharedBands.Should().HaveCount(2);

        for (var s = 0; s < 2; s++)
        {
            var wpfBand = wpfBands[s];
            var sharedBand = sharedBands[s];

            // Same number of points per band on both platforms.
            wpfBand.Points.Should().HaveCount(2);
            sharedBand.Points.Should().HaveCount(2);
            sharedBand.BaselinePoints.Should().HaveCount(2,
                "the shared stacked-area band must carry a per-category bottom polyline (the variable baseline)");

            for (var i = 0; i < 2; i++)
            {
                // OxyPlot's AreaSeries.Points is the band top; Points2 is its (variable) bottom. The
                // shared engine's SeriesLayout.Points[i].DataY / BaselinePoints[i].DataY carry the same
                // cumulative value-space top/bottom. These are the platform-independent geometry both
                // renderers stroke/fill, so they must match value-for-value.
                sharedBand.Points[i].DataY.Should().BeApproximately(wpfBand.Points[i].Y, 1e-6,
                    $"band {s} top at point {i} must match WPF");
                sharedBand.BaselinePoints[i].DataY.Should().BeApproximately(wpfBand.Points2[i].Y, 1e-6,
                    $"band {s} bottom (running-stack baseline) at point {i} must match WPF");
            }
        }
    }
}
