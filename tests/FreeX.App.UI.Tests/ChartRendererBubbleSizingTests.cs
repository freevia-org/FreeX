using System.Reflection;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

// R22-chart-model-render-2: the WPF/OxyPlot bubble renderer (ChartRenderer.Bubble.cs) previously
// passed the raw size-column value straight through as the OxyPlot marker size, ignoring
// chart.BubbleScale, chart.BubbleSizeRepresents, and chart.ShowNegativeBubbles entirely. These
// tests pin the ported behavior, mirroring the Avalonia ChartLayoutEngine.LayoutBubble reference
// implementation (BubbleRadius/MaxBubbleSize).
public sealed class ChartRendererBubbleSizingTests
{
    private static PlotModel BuildPlotModel(ChartModel chart, ViewportModel viewport)
    {
        return ChartRenderer.BuildPlotModel(chart, viewport).Should().BeOfType<PlotModel>().Subject;
    }

    private static DisplayCell Cell(uint row, uint col, string text) =>
        new(row, col, null, text, null, StyleId.Default, null);

    private static double ExpectedRadius(double size, double maxSize, ChartBubbleSizeRepresents represents) =>
        Math.Max(1.0, 20.0 * (represents == ChartBubbleSizeRepresents.Width
            ? Math.Clamp(Math.Abs(size) / maxSize, 0, 1)
            : Math.Sqrt(Math.Clamp(Math.Abs(size) / maxSize, 0, 1))));

    [Fact]
    public void BubbleRenderer_AppliesBubbleScalePercentageToRadius()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bubble,
            FirstColIsCategories = false,
            BubbleScale = 50,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Market"),
                Cell(2, 1, "1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "4"),
                Cell(3, 1, "2"),
                Cell(3, 2, "20"),
                Cell(3, 3, "8")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<ScatterSeries>().Subject;
        series.Points.Select(p => p.Size).Should().Equal(
            ExpectedRadius(4, 8, ChartBubbleSizeRepresents.Area) * 0.5,
            ExpectedRadius(8, 8, ChartBubbleSizeRepresents.Area) * 0.5);
    }

    [Fact]
    public void BubbleRenderer_WidthRepresentationScalesRadiusLinearlyInsteadOfBySquareRoot()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bubble,
            FirstColIsCategories = false,
            BubbleSizeRepresents = ChartBubbleSizeRepresents.Width,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Market"),
                Cell(2, 1, "1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "4"),
                Cell(3, 1, "2"),
                Cell(3, 2, "20"),
                Cell(3, 3, "8")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<ScatterSeries>().Subject;
        series.Points.Select(p => p.Size).Should().Equal(
            ExpectedRadius(4, 8, ChartBubbleSizeRepresents.Width),
            ExpectedRadius(8, 8, ChartBubbleSizeRepresents.Width));
        // Sanity check: linear (Width) sizing must differ from the default square-root (Area) sizing.
        series.Points[0].Size.Should().NotBe(ExpectedRadius(4, 8, ChartBubbleSizeRepresents.Area));
    }

    [Fact]
    public void BubbleRenderer_SkipsNegativeSizePointWhenShowNegativeBubblesIsFalse()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bubble,
            FirstColIsCategories = false,
            ShowNegativeBubbles = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Market"),
                Cell(2, 1, "1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "4"),
                Cell(3, 1, "2"),
                Cell(3, 2, "20"),
                Cell(3, 3, "-8")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<ScatterSeries>().Subject;
        series.Points.Select(p => (p.X, p.Y)).Should().Equal((1, 10));
    }

    [Fact]
    public void BubbleRenderer_IncludesNegativeSizePointWhenShowNegativeBubblesIsTrue()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bubble,
            FirstColIsCategories = false,
            ShowNegativeBubbles = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Market"),
                Cell(2, 1, "1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "4"),
                Cell(3, 1, "2"),
                Cell(3, 2, "20"),
                Cell(3, 3, "-8")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<ScatterSeries>().Subject;
        series.Points.Select(p => (p.X, p.Y, p.Size)).Should().Equal(
            (1, 10, ExpectedRadius(4, 8, ChartBubbleSizeRepresents.Area)),
            (2, 20, ExpectedRadius(-8, 8, ChartBubbleSizeRepresents.Area)));
    }
}
