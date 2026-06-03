using System.Globalization;
using System.IO;
using System.Reflection;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

public sealed partial class ChartRendererTests
{
    [Fact]
    public void ColumnRenderer_AppliesLegendOverlayPlacement()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 3)),
            LegendPosition = ChartLegendPosition.Right,
            LegendOverlay = true
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
        legend.LegendPlacement.Should().Be(LegendPlacement.Inside);
        legend.LegendPosition.Should().Be(OxyPlot.Legends.LegendPosition.RightTop);
    }

    [Fact]
    public void LineRenderer_AppliesSeriesFormatToMarkers()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Line,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    0,
                    FillColor: new CellColor(255, 192, 0),
                    StrokeColor: new CellColor(68, 114, 196),
                    StrokeThickness: 2,
                    MarkerStyle: ChartMarkerStyle.Diamond,
                    MarkerSize: 8)
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "20")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<LineSeries>().Subject;
        series.MarkerType.Should().Be(MarkerType.Diamond);
        series.MarkerSize.Should().Be(8);
        series.MarkerFill.Should().Be(OxyColor.FromRgb(255, 192, 0));
        series.MarkerStroke.Should().Be(OxyColor.FromRgb(68, 114, 196));
        series.MarkerStrokeThickness.Should().Be(2);
    }

    [Fact]
    public void BarRenderer_AppliesSeriesFormatToFillAndOutline()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    0,
                    FillColor: new CellColor(112, 173, 71),
                    StrokeColor: new CellColor(55, 86, 35),
                    StrokeThickness: 2.25)
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "20")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<BarSeries>().Subject;
        series.FillColor.Should().Be(OxyColor.FromRgb(112, 173, 71));
        series.StrokeColor.Should().Be(OxyColor.FromRgb(55, 86, 35));
        series.StrokeThickness.Should().Be(2.25);
    }

    [Fact]
    public void BarRenderer_AppliesWorkbookThemeSeriesAndLegendColors()
    {
        var sheetId = SheetId.New();
        var theme = WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent4, new CellColor(20, 90, 160))
            .WithColor(WorkbookThemeColorSlot.Accent5, new CellColor(40, 120, 80))
            .WithColor(WorkbookThemeColorSlot.Dark1, new CellColor(30, 30, 30));
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            LegendTextColor = new CellColor(200, 200, 200),
            LegendTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1),
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    0,
                    FillColor: new CellColor(112, 173, 71),
                    StrokeColor: new CellColor(55, 86, 35),
                    FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4),
                    StrokeThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent5))
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "20")
            ],
            [],
            []),
            theme);

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<BarSeries>().Subject;
        series.FillColor.Should().Be(OxyColor.FromRgb(20, 90, 160));
        series.StrokeColor.Should().Be(OxyColor.FromRgb(40, 120, 80));
        model.Legends.Should().ContainSingle().Which.LegendTextColor.Should().Be(OxyColor.FromRgb(30, 30, 30));
    }

    [Fact]
    public void AreaRenderer_AppliesSeriesFormatToFillOutlineAndDash()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Area,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    0,
                    FillColor: new CellColor(91, 155, 213),
                    StrokeColor: new CellColor(31, 78, 121),
                    StrokeThickness: 2.5,
                    DashStyle: ChartLineDashStyle.Dot)
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "20")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<AreaSeries>().Subject;
        series.Fill.Should().Be(OxyColor.FromRgb(91, 155, 213));
        series.Color.Should().Be(OxyColor.FromRgb(31, 78, 121));
        series.StrokeThickness.Should().Be(2.5);
        series.LineStyle.Should().Be(LineStyle.Dot);
    }

    [Fact]
    public void PieRenderer_UsesDistinctSliceColorsByDefault()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "20"),
                Cell(4, 1, "Q3"),
                Cell(4, 2, "30")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>().Subject;
        series.Slices.Select(slice => slice.Fill).Should().OnlyHaveUniqueItems();
        series.Slices.Should().OnlyContain(slice => !slice.Fill.IsInvisible());
    }

    [Fact]
    public void PieRenderer_AppliesSeriesFormatToSliceFillAndOutline()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    0,
                    FillColor: new CellColor(91, 155, 213),
                    StrokeColor: new CellColor(31, 78, 121),
                    StrokeThickness: 2.5)
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "20")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>().Subject;
        series.Stroke.Should().Be(OxyColor.FromRgb(31, 78, 121));
        series.StrokeThickness.Should().Be(2.5);
        series.Slices.Should().HaveCount(2);
        series.Slices.Should().OnlyContain(slice => slice.Fill == OxyColor.FromRgb(91, 155, 213));
    }
}
