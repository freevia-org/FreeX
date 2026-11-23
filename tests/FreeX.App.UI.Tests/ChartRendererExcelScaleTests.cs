using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Selection;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// The OxyPlot export is rendered at 2x or more pixels for crisp display. The layout (fonts, line
/// widths, gutters) must stay the one a 1x chart has - measured against Excel, whose 14 pt title and 9 pt
/// tick labels occupy the same share of the chart at any export size.
/// </summary>
public sealed class ChartRendererExcelScaleTests
{
    private static (ChartModel Chart, ViewportModel Viewport) ColumnChart(bool axisTitles = false)
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            Title = "Sales",
            ShowLegend = true,
            LegendPosition = ChartLegendPosition.Bottom,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 3)),
            Width = 640,
            Height = 400,
            XAxisTitle = axisTitles ? "Quarter" : null,
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

    [Theory]
    [InlineData(2.0)]
    [InlineData(3.0)]
    [InlineData(4.0)]
    public void Exporter_renders_the_one_x_layout_at_higher_pixel_density(double scale)
    {
        var (chart, _) = ColumnChart();

        var exporter = ChartRenderer.CreateExporter(chart, scale);

        exporter.Width.Should().Be((int)(640 * scale));
        exporter.Height.Should().Be((int)(400 * scale));
        exporter.Resolution.Should().Be(96.0 * scale, "OxyPlot lays out in device-independent pixels = bitmap pixels / (Resolution / 96)");
    }

    [Fact]
    public void Rendered_bitmap_is_the_requested_density_and_not_blank()
    {
        WpfTestThread.Run(() =>
        {
            var (chart, viewport) = ColumnChart();

            var image = ChartRenderer.Render(chart, viewport, WorkbookTheme.Office, 2.0);

            var bitmap = image.Should().BeAssignableTo<BitmapSource>().Subject;
            bitmap.PixelWidth.Should().Be(1280);
            bitmap.PixelHeight.Should().Be(800);
        });
    }

    [Fact]
    public void Element_map_plot_bounds_do_not_depend_on_the_render_scale()
    {
        WpfTestThread.Run(() =>
        {
            var (chart, viewport) = ColumnChart(axisTitles: true);

            var at2 = PlotBounds(ChartRenderer.BuildElementMap(chart, viewport, WorkbookTheme.Office, 2.0));
            var at4 = PlotBounds(ChartRenderer.BuildElementMap(chart, viewport, WorkbookTheme.Office, 4.0));

            at4.Left.Should().BeApproximately(at2.Left, 1.5);
            at4.Top.Should().BeApproximately(at2.Top, 1.5);
            at4.Right.Should().BeApproximately(at2.Right, 1.5);
            at4.Bottom.Should().BeApproximately(at2.Bottom, 1.5);
            // Excel's plot area for a titled chart with a bottom legend starts ~33 pt (44 px) from the top.
            at2.Top.Should().BeInRange(30, 70);
        });
    }

    private static LayoutRect PlotBounds(ChartElementMap map) =>
        map.Regions.Single(r => r.Element.Kind == ChartElementKind.PlotArea).Shapes.Single().Bounds;
}
