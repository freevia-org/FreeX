using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R72-render-chart-4-1: <see cref="ChartLegendPosition.TopRight"/> must map to OxyPlot's
/// dedicated <see cref="OxyPlot.Legends.LegendPosition.TopRight"/> corner placement instead of
/// collapsing into the plain Right fallback. R72-render-chart-4-2: a pie/doughnut with several
/// per-slice explosion overrides must explode EVERY one of them, not just the first.
/// </summary>
public sealed partial class ChartRendererTests
{
    [Fact]
    public void ColumnRenderer_LegendPositionTopRight_MapsToOxyPlotTopRightCorner()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 3)),
            LegendPosition = ChartLegendPosition.TopRight
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "North"),
                Cell(1, 3, "South"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "20")
            ],
            [],
            []));

        var legend = model.Legends.Should().ContainSingle().Subject;
        legend.LegendPosition.Should().Be(OxyPlot.Legends.LegendPosition.TopRight);
        legend.LegendPosition.Should().NotBe(OxyPlot.Legends.LegendPosition.RightMiddle);
        legend.LegendPosition.Should().NotBe(OxyPlot.Legends.LegendPosition.RightTop);
    }

    [Theory]
    [InlineData(ChartLegendPosition.Left, OxyPlot.Legends.LegendPosition.LeftMiddle)]
    [InlineData(ChartLegendPosition.Top, OxyPlot.Legends.LegendPosition.TopCenter)]
    [InlineData(ChartLegendPosition.Bottom, OxyPlot.Legends.LegendPosition.BottomCenter)]
    [InlineData(ChartLegendPosition.Right, OxyPlot.Legends.LegendPosition.RightMiddle)]
    public void ColumnRenderer_OtherLegendPositions_UnaffectedByTopRightMapping(
        ChartLegendPosition position, OxyPlot.Legends.LegendPosition expected)
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 3)),
            LegendPosition = position
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "North"),
                Cell(1, 3, "South"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "20")
            ],
            [],
            []));

        model.Legends.Should().ContainSingle().Which.LegendPosition.Should().Be(expected);
    }

    [Fact]
    public void PieRenderer_MultipleExplodedSlices_AllRenderExploded()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 2)),
            ExplodedSlices =
            [
                new ChartPointExplosion(0, 1, 0.1),
                new ChartPointExplosion(0, 3, 0.1)
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Cat"),
                Cell(1, 2, "Val"),
                Cell(2, 1, "A"),
                Cell(2, 2, "10"),
                Cell(3, 1, "B"),
                Cell(3, 2, "20"),
                Cell(4, 1, "C"),
                Cell(4, 2, "30"),
                Cell(5, 1, "D"),
                Cell(5, 2, "40")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>().Subject;
        series.Slices.Should().HaveCount(4);
        series.Slices[0].IsExploded.Should().BeFalse();
        series.Slices[1].IsExploded.Should().BeTrue();
        series.Slices[2].IsExploded.Should().BeFalse();
        series.Slices[3].IsExploded.Should().BeTrue();
    }

    [Fact]
    public void PieRenderer_NoExplosion_RendersFlat()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Cat"),
                Cell(1, 2, "Val"),
                Cell(2, 1, "A"),
                Cell(2, 2, "10"),
                Cell(3, 1, "B"),
                Cell(3, 2, "20")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>().Subject;
        series.Slices.Should().OnlyContain(slice => !slice.IsExploded);
    }

    [Fact]
    public void PieRenderer_SingleExplodedSlice_StillWorks()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2)),
            ExplodedSliceIndex = 2,
            ExplodedSliceDistance = 0.15
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Cat"),
                Cell(1, 2, "Val"),
                Cell(2, 1, "A"),
                Cell(2, 2, "10"),
                Cell(3, 1, "B"),
                Cell(3, 2, "20"),
                Cell(4, 1, "C"),
                Cell(4, 2, "30")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>().Subject;
        series.Slices.Should().HaveCount(3);
        series.Slices[0].IsExploded.Should().BeFalse();
        series.Slices[1].IsExploded.Should().BeFalse();
        series.Slices[2].IsExploded.Should().BeTrue();
    }
}
