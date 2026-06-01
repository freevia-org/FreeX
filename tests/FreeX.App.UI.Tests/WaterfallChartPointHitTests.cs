using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using System.Windows;

namespace FreeX.App.UI.Tests;

public sealed class WaterfallChartPointHitTests
{
    [Fact]
    public void HitTestWaterfallChartPoint_MapsPointerXToDataPointIndex()
    {
        var chart = CreateChart();

        AssertHit(chart, new Point(70, 80), 0);
        AssertHit(chart, new Point(170, 80), 2);
        AssertHit(chart, new Point(270, 80), 3);
    }

    [Fact]
    public void HitTestWaterfallChartPoint_IgnoresNonWaterfallAndHiddenCharts()
    {
        var hidden = CreateChart();
        hidden.IsVisible = false;
        var column = CreateChart();
        column.Type = ChartType.Column;

        GridView.HitTestWaterfallChartPoint([hidden, column], new Point(170, 80), 50, 25)
            .Should().BeNull();
    }

    private static void AssertHit(ChartModel chart, Point point, int expectedPointIndex)
    {
        var hit = GridView.HitTestWaterfallChartPoint([chart], point, rowHeaderWidth: 50, columnHeaderHeight: 25);

        hit.Should().NotBeNull();
        hit!.Value.Chart.Should().BeSameAs(chart);
        hit.Value.PointIndex.Should().Be(expectedPointIndex);
    }

    private static ChartModel CreateChart()
    {
        var sheetId = SheetId.New();
        return new ChartModel
        {
            Type = ChartType.Waterfall,
            Left = 20,
            Top = 30,
            Width = 200,
            Height = 100,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 5, 2))
        };
    }
}
