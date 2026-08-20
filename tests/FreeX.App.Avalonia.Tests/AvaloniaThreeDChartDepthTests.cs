using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls.Shapes;
using Avalonia.Headless;

using FreeX.App.Avalonia.Charts;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>Regression coverage for the Office-style projected facets on native 3-D bars/columns.</summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaThreeDChartDepthTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(ChartType.ThreeDColumn)]
    [InlineData(ChartType.ThreeDBar)]
    public async Task NativeThreeDRectangularCharts_render_top_and_side_facets(ChartType type)
    {
        await Session.Dispatch(() =>
        {
            var chart = new ChartModel { Type = type, ShowLegend = false };
            var layout = ChartLayoutEngine.Layout(new ChartLayoutRequest
            {
                Chart = chart,
                Categories = ["Q1", "Q2"],
                Series = [new ChartSeriesData { SeriesIndex = 0, Name = "Revenue", Values = [120, 200] }],
                PlotArea = new PlotRect(0, 0, 300, 200),
                TextMeasurer = new AvaloniaTextMeasurer(),
            });

            var canvas = new AvaloniaChartRenderer(chart, WorkbookTheme.Office).Render(layout, 300, 200);

            canvas.Children.OfType<Polygon>().Should().HaveCount(4,
                "each 3-D bar/column front face must receive one top and one side depth facet");
        }, CancellationToken.None);
    }
}
