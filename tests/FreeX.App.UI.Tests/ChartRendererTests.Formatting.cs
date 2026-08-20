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
    [Theory]
    [InlineData(0, -90)]
    [InlineData(90, 0)]
    public void PieRenderer_AdaptsDrawingMlFirstSliceAngleForOxyPlot(
        double drawingMlAngle,
        double expectedOxyPlotAngle)
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            FirstSliceAngle = drawingMlAngle,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Category"), Cell(1, 2, "Share"),
                Cell(2, 1, "North"), Cell(2, 2, "60"),
                Cell(3, 1, "South"), Cell(3, 2, "40"),
            ],
            [],
            []));

        model.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>()
            .Subject.StartAngle.Should().Be(expectedOxyPlotAngle);
    }

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

    // R66-meta-2: X/Star/Plus/Dot/Dash/Auto (added in R65) must not all collapse to Circle in the
    // OxyPlot renderer the way they did before this fix.
    [Theory]
    [InlineData(ChartMarkerStyle.X, MarkerType.Cross)]
    [InlineData(ChartMarkerStyle.Star, MarkerType.Star)]
    [InlineData(ChartMarkerStyle.Plus, MarkerType.Plus)]
    [InlineData(ChartMarkerStyle.Dot, MarkerType.Circle)]
    [InlineData(ChartMarkerStyle.Dash, MarkerType.Square)]
    [InlineData(ChartMarkerStyle.Auto, MarkerType.Circle)]
    public void LineRenderer_MapsNewMarkerStylesToDistinctOxyMarkerTypes(
        ChartMarkerStyle markerStyle, MarkerType expectedOxyMarkerType)
    {
        // Before the fix: every one of these new ChartMarkerStyle members fell through the switch's
        // default arm and rendered as MarkerType.Circle, regardless of the requested shape.
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Line,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            SeriesFormats = [new ChartSeriesFormat(0, MarkerStyle: markerStyle)]
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
        series.MarkerType.Should().Be(expectedOxyMarkerType);
    }

    // R68-meta-2: Dot shares OxyPlot's MarkerType.Circle with Auto/Circle (OxyPlot has no smaller
    // dedicated dot marker type), so it must be distinguished by a reduced marker SIZE instead --
    // before the fix, Dot rendered pixel-identical to a full Circle marker at the same requested
    // size. This mirrors the Avalonia chart renderer's own Dot glyph (dotR = r * 0.45).
    [Fact]
    public void LineRenderer_DotMarkerRendersSmallerThanFullCircleAtSameRequestedSize()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Line,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            SeriesFormats = [new ChartSeriesFormat(0, MarkerStyle: ChartMarkerStyle.Dot, MarkerSize: 10)]
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
        series.MarkerType.Should().Be(MarkerType.Circle);
        series.MarkerSize.Should().BeLessThan(10);
        series.MarkerSize.Should().Be(4.5); // 10 * 0.45
    }

    // Sibling no-regression test: Auto and Circle keep rendering at the full requested marker
    // size (only Dot is scaled down) after the R68 Dot-size fix.
    [Theory]
    [InlineData(ChartMarkerStyle.Auto)]
    [InlineData(ChartMarkerStyle.Circle)]
    public void LineRenderer_NonDotCircleMarkersKeepFullRequestedSize(ChartMarkerStyle markerStyle)
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Line,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            SeriesFormats = [new ChartSeriesFormat(0, MarkerStyle: markerStyle, MarkerSize: 10)]
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
        series.MarkerType.Should().Be(MarkerType.Circle);
        series.MarkerSize.Should().Be(10);
    }

    // Sibling no-regression test: the five pre-existing marker styles must keep mapping to their
    // original OxyPlot marker types after the new members were added to the switch.
    [Theory]
    [InlineData(ChartMarkerStyle.None, MarkerType.None)]
    [InlineData(ChartMarkerStyle.Circle, MarkerType.Circle)]
    [InlineData(ChartMarkerStyle.Square, MarkerType.Square)]
    [InlineData(ChartMarkerStyle.Diamond, MarkerType.Diamond)]
    [InlineData(ChartMarkerStyle.Triangle, MarkerType.Triangle)]
    public void LineRenderer_PreExistingMarkerStylesStillMapUnchanged(
        ChartMarkerStyle markerStyle, MarkerType expectedOxyMarkerType)
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Line,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            SeriesFormats = [new ChartSeriesFormat(0, MarkerStyle: markerStyle)]
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
        series.MarkerType.Should().Be(expectedOxyMarkerType);
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

    [Fact]
    public void DoughnutRenderer_AppliesPerPointFillColorsOverridingSeriesColor()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Doughnut,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2)),
            SeriesFormats =
            [
                new ChartSeriesFormat(0, FillColor: new CellColor(70, 130, 180)) // series-level (steel blue)
            ],
            PointFillColors =
            [
                new ChartPointFillFormat(0, 0, FillColor: new CellColor(0x92, 0xD0, 0x50)), // slice 0 -> green
                new ChartPointFillFormat(0, 2, FillColor: new CellColor(0xFF, 0xC0, 0x00))  // slice 2 -> gold
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
                Cell(4, 2, "30")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>().Subject;
        series.Slices.Should().HaveCount(3);

        // Slice 0 -> per-point green
        series.Slices[0].Fill.Should().Be(OxyColor.FromRgb(0x92, 0xD0, 0x50));

        // Slice 1 -> no per-point override, falls back to series-level color
        series.Slices[1].Fill.Should().Be(OxyColor.FromRgb(70, 130, 180));

        // Slice 2 -> per-point gold
        series.Slices[2].Fill.Should().Be(OxyColor.FromRgb(0xFF, 0xC0, 0x00));
    }

    [Fact]
    public void DoughnutRenderer_PerPointFillWithThemeColorResolvesAgainstTheme()
    {
        var sheetId = SheetId.New();
        var theme = WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(255, 0, 0));
        var chart = new ChartModel
        {
            Type = ChartType.Doughnut,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            PointFillColors =
            [
                new ChartPointFillFormat(0, 0, FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2))
            ]
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
            []),
            theme);

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>().Subject;
        series.Slices[0].Fill.Should().Be(OxyColor.FromRgb(255, 0, 0));
    }

    [Fact]
    public void ColumnRenderer_AppliesChartAreaFillColorToBackground()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            // Simulate a dark gradient-fill background approximated as a solid color
            ChartAreaFillColor = new CellColor(30, 30, 40)
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

        model.Background.Should().Be(OxyColor.FromRgb(30, 30, 40));
    }

    [Fact]
    public void ColumnRenderer_AppliesPlotAreaFillColorToPlotAreaBackground()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            PlotAreaFillColor = new CellColor(200, 220, 255)
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

        model.PlotAreaBackground.Should().Be(OxyColor.FromRgb(200, 220, 255));
    }

    [Fact]
    public void ColumnRenderer_NullFillColors_LeavesOxyPlotDefaults()
    {
        // Charts without explicit fill (most charts) must not regress — background stays at OxyPlot default
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
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

        // OxyPlot default background is Undefined when not set explicitly
        model.Background.Should().Be(OxyColors.Undefined);
    }

    [Fact]
    public void ColumnRenderer_ChartAreaFillThemeColorResolvesViaTheme()
    {
        var sheetId = SheetId.New();
        var theme = WorkbookTheme.Office
            .WithColor(WorkbookThemeColorSlot.Dark1, new CellColor(20, 25, 30));
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            ChartAreaFillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1, 0.25)
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
            []),
            theme);

        // With Dark1=(20,25,30) and tint=0.25, the resolved color should not be null
        model.Background.Should().NotBe(OxyColors.Undefined);
    }

    [Fact]
    public void ColumnRenderer_ChartDefaultTextColorAppliedToModelTextColor()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            ChartDefaultTextColor = new CellColor(255, 255, 255)  // white text for dark theme
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

        model.TextColor.Should().Be(OxyColor.FromRgb(255, 255, 255));
    }

    [Fact]
    public void DoughnutRenderer_AllShowFlagsZero_ProducesNoDataLabels()
    {
        var sheetId = SheetId.New();
        // ShowDataLabels = false (all show flags were 0 in XML, reader didn't set it)
        var chart = new ChartModel
        {
            Type = ChartType.Doughnut,
            ShowDataLabels = false,
            ShowLegend = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Cat"),
                Cell(1, 2, "Val"),
                Cell(2, 1, "A"),
                Cell(2, 2, "1"),
                Cell(3, 1, "B"),
                Cell(3, 2, "0")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>().Subject;
        // Native OxyPlot pie labels should be empty strings when ShowDataLabels=false
        series.InsideLabelFormat.Should().BeEmpty();
        series.OutsideLabelFormat.Should().BeEmpty();
        // No annotation labels either
        model.Annotations.Should().BeEmpty();
    }
}
