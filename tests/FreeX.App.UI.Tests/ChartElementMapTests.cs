using FluentAssertions;
using Free.Shared.Drawing;
using FreeX.App.Presentation.Charts.Selection;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// The WPF host reads title/legend/plot geometry back from OxyPlot's own layout pass, so the element
/// map must agree with where OxyPlot actually put things (not a guess).
/// </summary>
public sealed class ChartElementMapTests
{
    private static (ChartModel Chart, ViewportModel Viewport) ColumnChart()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            Title = "Sales",
            ShowLegend = true,
            LegendPosition = ChartLegendPosition.Right,
            ShowYAxisMajorGridlines = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 3)),
            Width = 480,
            Height = 300,
        };
        var viewport = new ViewportModel(
            [],
            [],
            [],
            ChartDataCells:
            [
                new ChartDataCell(sheetId, 1, 1, "Q"), new ChartDataCell(sheetId, 1, 2, "North"), new ChartDataCell(sheetId, 1, 3, "South"),
                new ChartDataCell(sheetId, 2, 1, "A"), new ChartDataCell(sheetId, 2, 2, "10"), new ChartDataCell(sheetId, 2, 3, "5"),
                new ChartDataCell(sheetId, 3, 1, "B"), new ChartDataCell(sheetId, 3, 2, "20"), new ChartDataCell(sheetId, 3, 3, "15"),
                new ChartDataCell(sheetId, 4, 1, "C"), new ChartDataCell(sheetId, 4, 2, "30"), new ChartDataCell(sheetId, 4, 3, "25"),
            ]);
        return (chart, viewport);
    }

    [Fact]
    public void Element_map_places_title_above_plot_and_selects_chart_area_in_the_corner()
    {
        WpfTestThread.Run(() =>
        {
            var (chart, viewport) = ColumnChart();

            var map = ChartRenderer.BuildElementMap(chart, viewport, WorkbookTheme.Office, 1.0);

            map.Width.Should().Be(480);
            map.Height.Should().Be(300);
            map.HitTest(new LayoutPoint(240, 12)).Should().Be(new ChartElementRef(ChartElementKind.ChartTitle));
            map.HitTest(new LayoutPoint(2, 298)).Should().Be(ChartElementRef.ChartArea);

            var plot = map.Regions.Single(r => r.Element.Kind == ChartElementKind.PlotArea).Shapes.Single().Bounds;
            plot.Left.Should().BeGreaterThan(0);
            plot.Top.Should().BeGreaterThan(12);
            plot.Right.Should().BeLessThan(480);
        });
    }

    [Fact]
    public void Element_map_selects_a_series_by_clicking_a_column_and_the_legend_by_clicking_it()
    {
        WpfTestThread.Run(() =>
        {
            var (chart, viewport) = ColumnChart();

            var map = ChartRenderer.BuildElementMap(chart, viewport, WorkbookTheme.Office, 1.0);

            var series = map.Regions.Where(r => r.Element.Kind == ChartElementKind.Series).ToList();
            series.Should().NotBeEmpty();
            var bar = series[0].Shapes.OfType<RectHitShape>().First().Rect;
            map.HitTest(bar.Center).Should().Be(series[0].Element);

            var legend = map.Regions.Single(r => r.Element.Kind == ChartElementKind.Legend).Shapes.Single().Bounds;
            map.HitTest(legend.Center).Should().Be(new ChartElementRef(ChartElementKind.Legend));
        });
    }

    [Fact]
    public void Manual_layouts_move_the_plot_legend_and_title_in_the_oxyplot_render()
    {
        WpfTestThread.Run(() =>
        {
            var (chart, viewport) = ColumnChart();
            chart.XAxisTitle = "Quarter";
            var baseline = ChartRenderer.BuildElementMap(chart, viewport, WorkbookTheme.Office, 1.0);
            var plotBefore = baseline.GetHighlightBounds(new ChartElementRef(ChartElementKind.PlotArea))[0];

            chart.PlotAreaLayout = new ChartManualLayoutModel
            {
                LayoutTarget = "inner", XMode = "edge", YMode = "edge", X = 0.2, Y = 0.3, Width = 0.5, Height = 0.4,
            };
            chart.LegendLayout = new ChartManualLayoutModel { XMode = "edge", YMode = "edge", X = 0.05, Y = 0.75 };
            chart.TitleLayout = new ChartManualLayoutModel { XMode = "edge", YMode = "edge", X = 0.6, Y = 0.02 };

            var map = ChartRenderer.BuildElementMap(chart, viewport, WorkbookTheme.Office, 1.0);

            var plot = map.GetHighlightBounds(new ChartElementRef(ChartElementKind.PlotArea))[0];
            plotBefore.Left.Should().NotBeApproximately(plot.Left, 1);
            plot.Left.Should().BeApproximately(0.2 * 480, 2);
            plot.Top.Should().BeApproximately(0.3 * 300, 2);
            plot.Width.Should().BeApproximately(0.5 * 480, 2);
            plot.Height.Should().BeApproximately(0.4 * 300, 2);

            var legend = map.GetHighlightBounds(new ChartElementRef(ChartElementKind.Legend))[0];
            legend.Left.Should().BeApproximately(0.05 * 480, 2);
            legend.Top.Should().BeApproximately(0.75 * 300, 2);

            var title = map.GetHighlightBounds(new ChartElementRef(ChartElementKind.ChartTitle))[0];
            title.Left.Should().BeApproximately(0.6 * 480, 2);

            // The bitmap itself is drawn with the same layout (no exception, non-empty image).
            ChartRenderer.Render(chart, viewport, WorkbookTheme.Office, 1.0).Should().NotBeNull();
        });
    }
}
