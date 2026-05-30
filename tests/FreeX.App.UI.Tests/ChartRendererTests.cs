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

public sealed class ChartRendererTests
{
    [Fact]
    public void ColumnRenderer_UsesChartDataCellsWhenSourceRangeIsOutsideVisibleViewport()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 20, 5), new CellAddress(sheetId, 22, 6))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [Cell(1, 1, "Visible")],
            [],
            [],
            ChartDataCells:
            [
                ChartCell(sheetId, 20, 5, "Category"),
                ChartCell(sheetId, 20, 6, "Sales"),
                ChartCell(sheetId, 21, 5, "A"),
                ChartCell(sheetId, 21, 6, "10"),
                ChartCell(sheetId, 22, 5, "B"),
                ChartCell(sheetId, 22, 6, "20")
            ]));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<RectangleBarSeries>().Subject;
        series.Items.Should().HaveCount(2);
        model.Axes.Single(axis => axis.Position == AxisPosition.Bottom).FormatValue(1).Should().Be("B");
    }

    [Fact]
    public void ChartRenderer_DoesNotRenderMapChart()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Map,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
        };

        var model = BuildNullablePlotModel(chart, new ViewportModel(
            [Cell(2, 1, "US"), Cell(2, 2, "10"), Cell(3, 1, "UK"), Cell(3, 2, "20")],
            [],
            []));

        model.Should().BeNull();
    }

    [Fact]
    public void ParetoRenderer_SortsBarsDescendingWithCumulativeLine()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Pareto,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Item"), Cell(1, 2, "Count"),
                Cell(2, 1, "A"),   Cell(2, 2, "10"),
                Cell(3, 1, "B"),   Cell(3, 2, "50"),
                Cell(4, 1, "C"),   Cell(4, 2, "20")
            ],
            [],
            []));

        model.Series.Should().HaveCount(2);
        model.Series[0].Should().BeOfType<RectangleBarSeries>();
        model.Series[1].Should().BeOfType<LineSeries>();
        model.Axes.Should().Contain(a => a.Position == AxisPosition.Right);
        var catAxis = model.Axes.OfType<CategoryAxis>().Should().ContainSingle().Subject;
        catAxis.Labels[0].Should().Be("B");  // highest value first
    }

    [Fact]
    public void BoxAndWhiskerRenderer_ComputesStatsPerColumn()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.BoxAndWhisker,
            FirstRowIsHeader = true,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "S1"), Cell(1, 2, "S2"),
                Cell(2, 1, "1"), Cell(2, 2, "10"),
                Cell(3, 1, "2"), Cell(3, 2, "20"),
                Cell(4, 1, "3"), Cell(4, 2, "30"),
                Cell(5, 1, "4"), Cell(5, 2, "40")
            ],
            [],
            []));

        model.Series.Should().ContainSingle().Which.Should().BeOfType<BoxPlotSeries>();
        var bps = (BoxPlotSeries)model.Series[0];
        bps.Items.Should().HaveCount(2);
        bps.Items[0].Median.Should().BeApproximately(2.5, 0.001);
        bps.Items[1].Median.Should().BeApproximately(25.0, 0.001);
    }

    [Fact]
    public void AdvancedFamilyRenderers_AvoidLinqAggregateAndOutlierScaffolding()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "ChartRenderer.AdvancedFamilies.cs"));

        source.Should().NotContain(".Sum(");
        source.Should().NotContain(".Max(");
        source.Should().NotContain(".Where(");
        source.Should().NotContain(".ToList(");
        source.Should().NotContain(".FirstOrDefault(");
        source.Should().NotContain(".LastOrDefault(");
    }

    [Fact]
    public void StockRenderer_BuildsDateAxisXValuesWithoutLinqScaffolding()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "ChartRenderer.Stock.cs"));

        source.Should().NotContain("Enumerable.Range");
        source.Should().NotContain(".Select(DateTimeAxis.ToDouble)");
        source.Should().NotContain(".Min()");
        source.Should().NotContain(".Max()");
    }

    [Fact]
    public void TreemapRenderer_ProducesRectangleAnnotationsProportionalToValues()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Treemap,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Item"), Cell(1, 2, "Value"),
                Cell(2, 1, "A"),   Cell(2, 2, "75"),
                Cell(3, 1, "B"),   Cell(3, 2, "25")
            ],
            [],
            []));

        var rects = model.Annotations.OfType<RectangleAnnotation>().ToList();
        rects.Should().HaveCount(2);
        var widthA = rects[0].MaximumX - rects[0].MinimumX;
        var widthB = rects[1].MaximumX - rects[1].MinimumX;
        widthA.Should().BeApproximately(0.75, 0.001);
        widthB.Should().BeApproximately(0.25, 0.001);
    }

    [Fact]
    public void SunburstRenderer_UsesPieSeriesWithInnerDiameter()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Sunburst,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Region"), Cell(1, 2, "Sales"),
                Cell(2, 1, "North"),  Cell(2, 2, "60"),
                Cell(3, 1, "South"),  Cell(3, 2, "40")
            ],
            [],
            []));

        model.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>();
        var ps = (PieSeries)model.Series[0];
        ps.InnerDiameter.Should().BeGreaterThan(0);
        ps.Slices.Should().HaveCount(2);
    }

    [Fact]
    public void FunnelRenderer_ProducesCenteredDecreasingRectangles()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Funnel,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Stage"),  Cell(1, 2, "Count"),
                Cell(2, 1, "Lead"),   Cell(2, 2, "100"),
                Cell(3, 1, "Qual"),   Cell(3, 2, "60"),
                Cell(4, 1, "Close"),  Cell(4, 2, "20")
            ],
            [],
            []));

        var rects = model.Annotations.OfType<RectangleAnnotation>().ToList();
        rects.Should().HaveCount(3);
        var width0 = rects[0].MaximumX - rects[0].MinimumX;
        var width1 = rects[1].MaximumX - rects[1].MinimumX;
        var width2 = rects[2].MaximumX - rects[2].MinimumX;
        width0.Should().BeGreaterThan(width1);
        width1.Should().BeGreaterThan(width2);
    }

    [Fact]
    public void ThreeDColumnRenderer_UsesColumnSeries()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.ThreeDColumn,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Category"),
                Cell(1, 2, "Sales"),
                Cell(2, 1, "A"),
                Cell(2, 2, "10"),
                Cell(3, 1, "B"),
                Cell(3, 2, "20")
            ],
            [],
            []));

        model.Series.Should().ContainSingle().Which.Should().BeOfType<RectangleBarSeries>();
        model.Axes.Should().Contain(axis => axis.Position == AxisPosition.Bottom);
        model.Axes.Should().Contain(axis => axis.Position == AxisPosition.Left);
    }

    [Fact]
    public void ThreeDAreaRenderer_UsesAreaSeries()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.ThreeDArea,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Category"),
                Cell(1, 2, "Sales"),
                Cell(2, 1, "A"),
                Cell(2, 2, "10"),
                Cell(3, 1, "B"),
                Cell(3, 2, "20")
            ],
            [],
            []));

        model.Series.Should().ContainSingle().Which.Should().BeOfType<AreaSeries>();
        model.Axes.Should().Contain(axis => axis.Position == AxisPosition.Bottom);
        model.Axes.Should().Contain(axis => axis.Position == AxisPosition.Left);
    }

    [Fact]
    public void ThreeDLineRenderer_UsesLineSeries()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.ThreeDLine,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Category"),
                Cell(1, 2, "Sales"),
                Cell(2, 1, "A"),
                Cell(2, 2, "10"),
                Cell(3, 1, "B"),
                Cell(3, 2, "20")
            ],
            [],
            []));

        model.Series.Should().ContainSingle().Which.Should().BeOfType<LineSeries>();
        model.Axes.Should().Contain(axis => axis.Position == AxisPosition.Bottom);
        model.Axes.Should().Contain(axis => axis.Position == AxisPosition.Left);
    }

    [Fact]
    public void SurfaceRenderer_UsesMatrixRectangleSeries()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.ThreeDSurface,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "North"),
                Cell(1, 3, "South"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "20"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "30"),
                Cell(3, 3, "40")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<RectangleBarSeries>().Subject;
        series.Items.Should().HaveCount(4);
        series.Items.Select(item => item.Color).Should().OnlyHaveUniqueItems();
        model.Axes.Should().Contain(axis => axis.Position == AxisPosition.Bottom);
        model.Axes.Should().Contain(axis => axis.Position == AxisPosition.Left);
    }

    [Fact]
    public void SurfaceRenderer_ParsesInvariantDecimalValues()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            var sheetId = SheetId.New();
            var chart = new ChartModel
            {
                Type = ChartType.Surface,
                DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 3))
            };

            var model = BuildPlotModel(chart, new ViewportModel(
                [
                    Cell(1, 1, "Quarter"),
                    Cell(1, 2, "North"),
                    Cell(1, 3, "South"),
                    Cell(2, 1, "Q1"),
                    Cell(2, 2, "1.5"),
                    Cell(2, 3, "2.5")
                ],
                [],
                []));

            var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<RectangleBarSeries>().Subject;
            series.Items.Should().HaveCount(2);
            series.Items.Select(item => item.Color).Should().OnlyHaveUniqueItems();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void SurfaceRenderer_AvoidsMinMaxLinqScaffolding()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "ChartRenderer.Surface.cs"));

        source.Should().NotContain("surfaceValues.Min(");
        source.Should().NotContain("surfaceValues.Max(");
    }

    [Fact]
    public void ChartRenderer_ParsesInvariantDecimalValuesUnderNonInvariantCulture()
    {
        RunWithCulture("de-DE", () =>
        {
            var sheetId = SheetId.New();
            var columnModel = BuildPlotModel(new ChartModel
            {
                Type = ChartType.Column,
                DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
            }, new ViewportModel(
                [
                    Cell(1, 1, "Category"), Cell(1, 2, "Sales"),
                    Cell(2, 1, "A"), Cell(2, 2, "1.5"),
                    Cell(3, 1, "B"), Cell(3, 2, "2.5")
                ],
                [],
                []));
            columnModel.Series.OfType<RectangleBarSeries>().Single().Items
                .Select(item => item.Y1)
                .Should().Equal(1.5, 2.5);

            var scatterModel = BuildPlotModel(new ChartModel
            {
                Type = ChartType.Scatter,
                FirstColIsCategories = false,
                DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
            }, new ViewportModel(
                [
                    Cell(1, 1, "X"), Cell(1, 2, "Y"),
                    Cell(2, 1, "1.5"), Cell(2, 2, "10.5"),
                    Cell(3, 1, "2.5"), Cell(3, 2, "20.5")
                ],
                [],
                []));
            scatterModel.Series.OfType<ScatterSeries>().Single().Points
                .Select(point => (point.X, point.Y))
                .Should().Equal((1.5, 10.5), (2.5, 20.5));

            var radarModel = BuildPlotModel(new ChartModel
            {
                Type = ChartType.Radar,
                DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
            }, new ViewportModel(
                [
                    Cell(1, 1, "Metric"), Cell(1, 2, "Score"),
                    Cell(2, 1, "A"), Cell(2, 2, "1.5"),
                    Cell(3, 1, "B"), Cell(3, 2, "2.5")
                ],
                [],
                []));
            radarModel.Series.OfType<LineSeries>().Single().Points
                .Select(point => point.Y)
                .Should().Equal(1.5, 2.5, 1.5);

            var stackedModel = BuildPlotModel(new ChartModel
            {
                Type = ChartType.PercentStackedBar,
                DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 3)),
                ShowDataLabels = true
            }, new ViewportModel(
                [
                    Cell(1, 1, "Quarter"), Cell(1, 2, "North"), Cell(1, 3, "South"),
                    Cell(2, 1, "Q1"), Cell(2, 2, "1.5"), Cell(2, 3, "2.5")
                ],
                [],
                []));
            stackedModel.Annotations.OfType<TextAnnotation>().Select(annotation => annotation.Text)
                .Should().BeEquivalentTo("1.5", "2.5");
        });
    }

    [Theory]
    [InlineData(ChartBlankDisplayMode.Gap, 3, true, false)]
    [InlineData(ChartBlankDisplayMode.Span, 2, false, false)]
    [InlineData(ChartBlankDisplayMode.Zero, 3, false, true)]
    public void LineRenderer_HonorsBlankDisplayMode(
        ChartBlankDisplayMode blankDisplayMode,
        int expectedPointCount,
        bool expectedGapPoint,
        bool expectedZeroPoint)
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Line,
            BlankDisplayMode = blankDisplayMode,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Category"),
                Cell(1, 2, "Sales"),
                Cell(2, 1, "A"),
                Cell(2, 2, "10"),
                Cell(3, 1, "B"),
                Cell(3, 2, ""),
                Cell(4, 1, "C"),
                Cell(4, 2, "30")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<LineSeries>().Subject;
        series.Points.Should().HaveCount(expectedPointCount);
        series.Points.Any(point => double.IsNaN(point.Y)).Should().Be(expectedGapPoint);
        series.Points.Any(point => point.X == 1 && point.Y == 0).Should().Be(expectedZeroPoint);
    }

    [Theory]
    [InlineData(ChartBlankDisplayMode.Gap, 3, true, false)]
    [InlineData(ChartBlankDisplayMode.Span, 2, false, false)]
    [InlineData(ChartBlankDisplayMode.Zero, 3, false, true)]
    public void AreaRenderer_HonorsBlankDisplayMode(
        ChartBlankDisplayMode blankDisplayMode,
        int expectedPointCount,
        bool expectedGapPoint,
        bool expectedZeroPoint)
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Area,
            BlankDisplayMode = blankDisplayMode,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Category"),
                Cell(1, 2, "Sales"),
                Cell(2, 1, "A"),
                Cell(2, 2, "10"),
                Cell(3, 1, "B"),
                Cell(3, 2, ""),
                Cell(4, 1, "C"),
                Cell(4, 2, "30")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<AreaSeries>().Subject;
        series.Points.Should().HaveCount(expectedPointCount);
        series.Points.Any(point => double.IsNaN(point.Y)).Should().Be(expectedGapPoint);
        series.Points.Any(point => point.X == 1 && point.Y == 0).Should().Be(expectedZeroPoint);
    }

    [Fact]
    public void ColumnRenderer_HonorsBlankDisplayAsZero()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            BlankDisplayMode = ChartBlankDisplayMode.Zero,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Category"),
                Cell(1, 2, "Sales"),
                Cell(2, 1, "A"),
                Cell(2, 2, "10"),
                Cell(3, 1, "B"),
                Cell(3, 2, ""),
                Cell(4, 1, "C"),
                Cell(4, 2, "30")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<RectangleBarSeries>().Subject;
        series.Items.Should().HaveCount(3);
        series.Items.Should().Contain(item => item.X0 == 0.65 && item.X1 == 1.35 && item.Y0 == 0 && item.Y1 == 0);
    }

    [Fact]
    public void BarRenderer_HonorsBlankDisplayAsZero()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            BlankDisplayMode = ChartBlankDisplayMode.Zero,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Category"),
                Cell(1, 2, "Sales"),
                Cell(2, 1, "A"),
                Cell(2, 2, "10"),
                Cell(3, 1, "B"),
                Cell(3, 2, ""),
                Cell(4, 1, "C"),
                Cell(4, 2, "30")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<BarSeries>().Subject;
        series.Items.Should().HaveCount(3);
        series.Items.Should().Contain(item => item.Value == 0);
    }

    [Fact]
    public void ColumnRenderer_AddsChartDataTableAnnotations()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)),
            DataTable = new ChartDataTableModel
            {
                ShowHorizontalBorder = true,
                ShowVerticalBorder = true,
                ShowOutline = true
            }
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "North"),
                Cell(1, 3, "South"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "20"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "30"),
                Cell(3, 3, "40")
            ],
            [],
            []));

        model.Annotations
            .OfType<TextAnnotation>()
            .Select(annotation => annotation.Text)
            .Should()
            .Contain(["North | South", "Q1 | 10 | 20", "Q2 | 30 | 40"]);
    }

    [Fact]
    public void ChartDataTableAnnotations_BuildRowsWithoutListJoinPipelines()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "ChartRenderer.Annotations.cs"));
        var dataTableAnnotations = source[
            source.IndexOf("private static void AddChartDataTableAnnotations", StringComparison.Ordinal)..
            source.IndexOf("private static int AppendChartDataTablePart", StringComparison.Ordinal)];

        dataTableAnnotations.Should().Contain("var textBuilder = new StringBuilder();");
        dataTableAnnotations.Should().Contain("AppendChartDataTablePart(");
        dataTableAnnotations.Should().Contain("AddChartDataTableAnnotation(");
        dataTableAnnotations.Should().NotContain("new List<string>");
        dataTableAnnotations.Should().NotContain("string.Join(");
    }

    [Fact]
    public void ColumnRenderer_AppliesChartDataTableDirectStyle()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            DataTable = new ChartDataTableModel
            {
                ShowOutline = true,
                FillColor = new CellColor(255, 242, 204),
                BorderColor = new CellColor(191, 144, 0),
                BorderThickness = 2.5,
                TextColor = new CellColor(112, 48, 160),
                FontSize = 11.5
            }
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "North"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10")
            ],
            [],
            []));

        var dataTableAnnotations = model.Annotations
            .OfType<TextAnnotation>()
            .Where(annotation => annotation.Text?.Contains("North", StringComparison.Ordinal) == true ||
                                 annotation.Text?.Contains("Q1", StringComparison.Ordinal) == true)
            .ToList();
        dataTableAnnotations.Should().HaveCount(2);
        dataTableAnnotations.Should().OnlyContain(annotation => annotation.Background == OxyColor.FromRgb(255, 242, 204));
        dataTableAnnotations.Should().OnlyContain(annotation => annotation.Stroke == OxyColor.FromRgb(191, 144, 0));
        dataTableAnnotations.Should().OnlyContain(annotation => annotation.StrokeThickness == 2.5);
        dataTableAnnotations.Should().OnlyContain(annotation => annotation.TextColor == OxyColor.FromRgb(112, 48, 160));
        dataTableAnnotations.Should().OnlyContain(annotation => annotation.FontSize == 11.5);
    }

    [Fact]
    public void ColumnRenderer_AddsLegendKeysToChartDataTableWhenRequested()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 3)),
            DataTable = new ChartDataTableModel
            {
                ShowLegendKeys = true
            }
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

        model.Annotations
            .OfType<TextAnnotation>()
            .Select(annotation => annotation.Text)
            .Should()
            .Contain("* North | * South");
    }

    [Fact]
    public void PivotChartRenderer_AddsFieldButtonAnnotations()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Region"),
                Cell(1, 2, "Sum of Amount"),
                Cell(2, 1, "East"),
                Cell(2, 2, "10")
            ],
            [],
            []));

        model.Annotations
            .OfType<TextAnnotation>()
            .Select(annotation => annotation.Text)
            .Should()
            .Contain(["PivotTable1", "Axis Fields", "Values"]);
    }

    [Fact]
    public void PivotChartFieldButtons_AddAnnotationsWithoutCaptionList()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "ChartRenderer.Annotations.cs"));
        var fieldButtons = source[
            source.IndexOf("private static void AddPivotChartFieldButtons", StringComparison.Ordinal)..
            source.IndexOf("private static void AddPivotChartFieldButtonAnnotation", StringComparison.Ordinal)];

        fieldButtons.Should().Contain("var index = 0;");
        fieldButtons.Should().Contain("AddPivotChartFieldButtonAnnotation(");
        fieldButtons.Should().NotContain("new List<string>");
        fieldButtons.Should().NotContain("captions.Add(");
        fieldButtons.Should().NotContain("captions.Count");
    }

    [Fact]
    public void PivotChartRenderer_HidesFieldButtonAnnotationsWhenDisabled()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            ShowPivotChartFieldButtons = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Region"),
                Cell(1, 2, "Sum of Amount"),
                Cell(2, 1, "East"),
                Cell(2, 2, "10")
            ],
            [],
            []));

        model.Annotations
            .OfType<TextAnnotation>()
            .Select(annotation => annotation.Text)
            .Should()
            .NotContain(["PivotTable1", "Axis Fields", "Values"]);
    }

    [Fact]
    public void PivotChartRenderer_HidesIndividualFieldButtonAnnotations()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            ShowPivotChartValueFieldButtons = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Region"),
                Cell(1, 2, "Sum of Amount"),
                Cell(2, 1, "East"),
                Cell(2, 2, "10")
            ],
            [],
            []));

        model.Annotations
            .OfType<TextAnnotation>()
            .Select(annotation => annotation.Text)
            .Should()
            .Contain(["PivotTable1", "Axis Fields"])
            .And
            .NotContain("Values");
    }

    [Fact]
    public void GridView_HitTestsPivotChartFieldButtons()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            Left = 100,
            Top = 80,
            Width = 400,
            Height = 300
        };

        GridView.HitTestPivotChartFieldButton(
                [chart],
                new System.Windows.Point(148, 116),
                rowHeaderWidth: 40,
                columnHeaderHeight: 24)
            .Should()
            .Be((chart, "PivotTable1"));

        GridView.HitTestPivotChartFieldButton(
                [chart],
                new System.Windows.Point(148, 374),
                rowHeaderWidth: 40,
                columnHeaderHeight: 24)
            .Should()
            .Be((chart, "Axis Fields"));

        GridView.HitTestPivotChartFieldButton(
                [chart],
                new System.Windows.Point(428, 374),
                rowHeaderWidth: 40,
                columnHeaderHeight: 24)
            .Should()
            .Be((chart, "Values"));
    }

    [Fact]
    public void GridView_HitTestsPivotChartFieldButtonBoundaries()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            Left = 100,
            Top = 80,
            Width = 400,
            Height = 300
        };

        GridView.HitTestPivotChartFieldButton(
                [chart],
                new System.Windows.Point(296, 134),
                rowHeaderWidth: 40,
                columnHeaderHeight: 24)
            .Should()
            .Be((chart, "PivotTable1"));

        GridView.HitTestPivotChartFieldButton(
                [chart],
                new System.Windows.Point(264, 392),
                rowHeaderWidth: 40,
                columnHeaderHeight: 24)
            .Should()
            .Be((chart, "Axis Fields"));

        GridView.HitTestPivotChartFieldButton(
                [chart],
                new System.Windows.Point(524, 392),
                rowHeaderWidth: 40,
                columnHeaderHeight: 24)
            .Should()
            .Be((chart, "Values"));
    }

    [Fact]
    public void GridView_DoesNotHitTestHiddenPivotChartFieldButtons()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            ShowPivotChartFieldButtons = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            Left = 100,
            Top = 80,
            Width = 400,
            Height = 300
        };

        GridView.HitTestPivotChartFieldButton(
                [chart],
                new System.Windows.Point(148, 116),
                rowHeaderWidth: 40,
                columnHeaderHeight: 24)
            .Should()
            .BeNull();
    }

    [Fact]
    public void GridView_DoesNotHitTestPivotChartFieldButtonsOutsideChartBounds()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            Left = 100,
            Top = 80,
            Width = 40,
            Height = 120
        };

        GridView.HitTestPivotChartFieldButton(
                [chart],
                new System.Windows.Point(185, 116),
                rowHeaderWidth: 40,
                columnHeaderHeight: 24)
            .Should()
            .BeNull();
    }

    [Fact]
    public void GridView_DoesNotHitTestIndividuallyHiddenPivotChartFieldButtons()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            ShowPivotChartValueFieldButtons = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            Left = 100,
            Top = 80,
            Width = 400,
            Height = 300
        };

        GridView.HitTestPivotChartFieldButton(
                [chart],
                new System.Windows.Point(428, 374),
                rowHeaderWidth: 40,
                columnHeaderHeight: 24)
            .Should()
            .BeNull();

        GridView.HitTestPivotChartFieldButton(
                [chart],
                new System.Windows.Point(148, 374),
                rowHeaderWidth: 40,
                columnHeaderHeight: 24)
            .Should()
            .Be((chart, "Axis Fields"));
    }

    [Fact]
    public void PercentStackedBarRenderer_FormatsPercentageDataLabels()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.PercentStackedBar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 3)),
            ShowDataLabels = true,
            ShowDataLabelPercentage = true
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "North"),
                Cell(1, 3, "South"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "25"),
                Cell(2, 3, "75")
            ],
            [],
            []));

        model.Annotations.Should().HaveCount(2);
        model.Annotations.Should().AllBeOfType<TextAnnotation>();
        model.Annotations.Cast<TextAnnotation>().Select(annotation => annotation.Text)
            .Should().BeEquivalentTo("25%", "75%");
        model.Series.Should().AllSatisfy(series =>
            series.Should().BeOfType<RectangleBarSeries>().Subject.LabelFormatString.Should().BeNull());
    }

    [Fact]
    public void PercentStackedBarRenderer_FormatsValueDataLabelsFromSourceValues()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.PercentStackedBar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 3)),
            ShowDataLabels = true
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "North"),
                Cell(1, 3, "South"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "30")
            ],
            [],
            []));

        model.Annotations.Should().HaveCount(2);
        model.Annotations.Should().AllBeOfType<TextAnnotation>();
        model.Annotations.Cast<TextAnnotation>().Select(annotation => annotation.Text)
            .Should().BeEquivalentTo("10", "30");
        model.Series.Should().AllSatisfy(series =>
            series.Should().BeOfType<RectangleBarSeries>().Subject.LabelFormatString.Should().BeNull());
    }

    [Fact]
    public void BarRenderer_IgnoresPercentageToggleForNativeValueLabels()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            ShowDataLabels = true,
            ShowDataLabelPercentage = true
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<BarSeries>().Subject;
        series.LabelFormatString.Should().Be("{0}");
        model.Annotations.Should().BeEmpty();
    }

    [Fact]
    public void BarRenderer_IgnoresPercentageToggleForCategoryAnnotations()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            ShowDataLabels = true,
            ShowDataLabelCategoryName = true,
            ShowDataLabelPercentage = true
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10")
            ],
            [],
            []));

        var annotation = model.Annotations.Should().ContainSingle().Which.Should().BeOfType<TextAnnotation>().Subject;
        annotation.Text.Should().Be("Q1, 10");
        annotation.Background.Should().Be(OxyColors.Transparent);
        annotation.Stroke.Should().Be(OxyColors.Transparent);
        annotation.StrokeThickness.Should().Be(0);
    }

    [Fact]
    public void BarRenderer_AppliesYAxisStylingButIgnoresNumericBoundsOnCategoryAxis()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            ShowYAxisLabels = false,
            YAxisLabelTextColor = new CellColor(31, 78, 121),
            YAxisLabelFontSize = 14,
            YAxisLineColor = new CellColor(217, 83, 25),
            YAxisLineThickness = 2.5,
            YAxisMajorTickStyle = ChartAxisTickStyle.None,
            YAxisNumberFormat = ChartDataLabelNumberFormat.Currency,
            YAxisMinimum = 5,
            YAxisMaximum = 9
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "30")
            ],
            [],
            []));

        var axis = model.Axes.Single(axis => axis.Position == AxisPosition.Left)
            .Should().BeOfType<CategoryAxis>().Subject;
        axis.TextColor.Should().Be(OxyColors.Transparent);
        axis.FontSize.Should().Be(14);
        axis.AxislineColor.Should().Be(OxyColor.FromRgb(217, 83, 25));
        axis.AxislineThickness.Should().Be(2.5);
        axis.MajorTickSize.Should().Be(0);
        axis.Minimum.Should().NotBe(5);
        axis.Maximum.Should().NotBe(9);
        axis.FormatValue(0).Should().Be("Q1");
    }

    [Fact]
    public void ColumnRenderer_AppliesAxisTickPlacement()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            XAxisMajorTickStyle = ChartAxisTickStyle.Inside,
            XAxisMinorTickStyle = ChartAxisTickStyle.None,
            YAxisMajorTickStyle = ChartAxisTickStyle.Cross,
            YAxisMinorTickStyle = ChartAxisTickStyle.Inside
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10")
            ],
            [],
            []));

        var xAxis = model.Axes.Single(axis => axis.Position == AxisPosition.Bottom);
        xAxis.TickStyle.Should().Be(TickStyle.Inside);
        xAxis.MajorTickSize.Should().Be(4);
        xAxis.MinorTickSize.Should().Be(0);

        var yAxis = model.Axes.Single(axis => axis.Position == AxisPosition.Left);
        yAxis.TickStyle.Should().Be(TickStyle.Crossing);
        yAxis.MajorTickSize.Should().Be(8);
        yAxis.MinorTickSize.Should().Be(4);
    }

    [Fact]
    public void ColumnRenderer_AppliesAxisLabelAngles()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            XAxisLabelAngle = -45,
            YAxisLabelAngle = 90
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10")
            ],
            [],
            []));

        model.Axes.Single(axis => axis.Position == AxisPosition.Bottom).Angle.Should().Be(-45);
        model.Axes.Single(axis => axis.Position == AxisPosition.Left).Angle.Should().Be(90);
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
    public void BarRenderer_UsesAnnotationsForDataLabelFillAndBorder()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            ShowDataLabels = true,
            DataLabelFillColor = new CellColor(255, 242, 204),
            DataLabelBorderColor = new CellColor(191, 144, 0),
            DataLabelBorderThickness = 1.5,
            DataLabelAngle = -35
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<BarSeries>().Subject;
        series.LabelFormatString.Should().BeNull();
        var annotation = model.Annotations.Should().ContainSingle().Which.Should().BeOfType<TextAnnotation>().Subject;
        annotation.Text.Should().Be("10");
        annotation.Background.Should().Be(OxyColor.FromRgb(255, 242, 204));
        annotation.Stroke.Should().Be(OxyColor.FromRgb(191, 144, 0));
        annotation.StrokeThickness.Should().Be(1.5);
        annotation.TextRotation.Should().Be(-35);
    }

    [Fact]
    public void BarRenderer_UsesAnnotationsForRotatedValueLabels()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            ShowDataLabels = true,
            DataLabelAngle = 45
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10")
            ],
            [],
            []));

        model.Series.Should().ContainSingle().Which.Should().BeOfType<BarSeries>().Subject.LabelFormatString.Should().BeNull();
        var annotation = model.Annotations.Should().ContainSingle().Which.Should().BeOfType<TextAnnotation>().Subject;
        annotation.Text.Should().Be("10");
        annotation.TextRotation.Should().Be(45);
    }

    [Fact]
    public void BarRenderer_AppliesPointSpecificDataLabelFormatting()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            ShowDataLabels = true,
            DataLabelFillColor = new CellColor(255, 255, 255),
            DataLabelBorderColor = new CellColor(191, 191, 191),
            DataLabelBorderThickness = 0.5,
            PointDataLabelFormats =
            [
                new ChartPointDataLabelFormat(
                    0,
                    1,
                    FillColor: new CellColor(226, 239, 218),
                    BorderColor: new CellColor(112, 173, 71),
                    BorderThickness: 2,
                    TextColor: new CellColor(0, 97, 0),
                    FontSize: 14)
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

        var annotations = model.Annotations.OfType<TextAnnotation>().ToList();
        annotations.Should().HaveCount(2);
        annotations[0].Background.Should().Be(OxyColor.FromRgb(255, 255, 255));
        annotations[0].Stroke.Should().Be(OxyColor.FromRgb(191, 191, 191));
        annotations[0].FontSize.Should().Be(11);
        annotations[1].Background.Should().Be(OxyColor.FromRgb(226, 239, 218));
        annotations[1].Stroke.Should().Be(OxyColor.FromRgb(112, 173, 71));
        annotations[1].StrokeThickness.Should().Be(2);
        annotations[1].TextColor.Should().Be(OxyColor.FromRgb(0, 97, 0));
        annotations[1].FontSize.Should().Be(14);
    }

    [Fact]
    public void PieRenderer_UsesCorrectCategoryValueAndPercentagePlaceholders()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            ShowDataLabels = true,
            DataLabelPosition = ChartDataLabelPosition.OutsideEnd,
            ShowDataLabelCategoryName = true,
            ShowDataLabelPercentage = true,
            DataLabelSeparator = ChartDataLabelSeparator.NewLine
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>().Subject;
        series.OutsideLabelFormat.Should().Be("{1}" + Environment.NewLine + "{2:0%}");
    }

    [Fact]
    public void PieRenderer_DataLabelAnnotationsAggregatePositiveTotalsWithoutLinq()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "ChartRenderer.SeriesFormatting.cs"));
        var annotations = source[
            source.IndexOf("private static void AddPieDataLabelAnnotations", StringComparison.Ordinal)..
            source.IndexOf("private static void AddPieAnnotationAxes", StringComparison.Ordinal)];

        annotations.Should().Contain("for (var i = 0; i < points.Count; i++)");
        annotations.Should().Contain("total += Math.Max(0, points[i].Value);");
        annotations.Should().Contain("var positiveValue = Math.Max(0, point.Value);");
        annotations.Should().NotContain("points.Sum(");
        annotations.Should().NotContain(".Sum(");
    }

    [Fact]
    public void PieRenderer_RotatesInsideDataLabelsWhenRotationIsRequested()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            ShowDataLabels = true,
            DataLabelPosition = ChartDataLabelPosition.InsideEnd,
            DataLabelAngle = 45
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>().Subject;
        series.AreInsideLabelsAngled.Should().BeFalse();
        series.InsideLabelFormat.Should().BeEmpty();
        model.Annotations.OfType<TextAnnotation>().Should().ContainSingle().Which.TextRotation.Should().Be(45);
    }

    [Theory]
    [InlineData(ChartType.Pie)]
    [InlineData(ChartType.Doughnut)]
    public void PieRenderer_UsesTextAnnotationsForArbitraryDataLabelAngles(ChartType chartType)
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = chartType,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            ShowDataLabels = true,
            DataLabelPosition = ChartDataLabelPosition.OutsideEnd,
            DataLabelAngle = 37,
            ShowDataLabelCategoryName = true,
            ShowDataLabelPercentage = true
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "25"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "75")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>().Subject;
        series.InsideLabelFormat.Should().BeEmpty();
        series.OutsideLabelFormat.Should().BeEmpty();

        var annotations = model.Annotations.OfType<TextAnnotation>().ToList();
        annotations.Should().HaveCount(2);
        annotations.Should().OnlyContain(annotation => annotation.TextRotation == 37);
        annotations[0].Text.Should().Be("Q1, 25%");
        annotations[1].Text.Should().Be("Q2, 75%");
    }

    [Fact]
    public void PieRenderer_HidesLabelsWhenDataLabelsAreOff()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            ShowDataLabels = false,
            DataLabelPosition = ChartDataLabelPosition.OutsideEnd
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>().Subject;
        series.InsideLabelFormat.Should().BeEmpty();
        series.OutsideLabelFormat.Should().BeEmpty();
    }

    [Fact]
    public void BarRenderer_AppliesNativeDataLabelNumberFormat()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            ShowDataLabels = true,
            DataLabelNumberFormat = ChartDataLabelNumberFormat.Currency
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<BarSeries>().Subject;
        series.LabelFormatString.Should().Be("{0:$#,##0.00}");
    }

    [Fact]
    public void BarRenderer_AppliesNativeDataLabelTextColorAndFontSize()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            ShowDataLabels = true,
            DataLabelTextColor = new CellColor(192, 0, 0),
            DataLabelFontSize = 13
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
        series.LabelFormatString.Should().Be("{0}");
        series.TextColor.Should().Be(OxyColor.FromRgb(192, 0, 0));
        series.FontSize.Should().Be(13);
    }

    [Fact]
    public void PieRenderer_AppliesDataLabelTextColorAndFontSize()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            ShowDataLabels = true,
            DataLabelPosition = ChartDataLabelPosition.Center,
            DataLabelTextColor = new CellColor(112, 48, 160),
            DataLabelFontSize = 14
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
        series.InsideLabelColor.Should().Be(OxyColor.FromRgb(112, 48, 160));
        series.TextColor.Should().Be(OxyColor.FromRgb(112, 48, 160));
        series.FontSize.Should().Be(14);
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
    public void ScatterRenderer_UsesFirstNumericColumnAsXValues()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "2"),
                Cell(3, 2, "20")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<ScatterSeries>().Subject;
        series.Title.Should().Be("Revenue");
        series.Points.Select(point => (point.X, point.Y)).Should().Equal((1, 10), (2, 20));
    }

    [Fact]
    public void ScatterRenderer_IndexesSeriesFormatsAfterSharedXColumn()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)),
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    0,
                    FillColor: new CellColor(255, 192, 0),
                    StrokeColor: new CellColor(68, 114, 196),
                    StrokeThickness: 2,
                    MarkerStyle: ChartMarkerStyle.Diamond,
                    MarkerSize: 7),
                new ChartSeriesFormat(
                    1,
                    FillColor: new CellColor(112, 173, 71),
                    StrokeColor: new CellColor(55, 86, 35),
                    StrokeThickness: 3,
                    MarkerStyle: ChartMarkerStyle.Triangle,
                    MarkerSize: 9)
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Cost"),
                Cell(2, 1, "1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "6"),
                Cell(3, 1, "2"),
                Cell(3, 2, "20"),
                Cell(3, 3, "11")
            ],
            [],
            []));

        model.Series.Should().HaveCount(2);
        var first = model.Series[0].Should().BeOfType<ScatterSeries>().Subject;
        var second = model.Series[1].Should().BeOfType<ScatterSeries>().Subject;

        first.Title.Should().Be("Revenue");
        first.MarkerType.Should().Be(MarkerType.Diamond);
        first.MarkerFill.Should().Be(OxyColor.FromRgb(255, 192, 0));
        first.MarkerStroke.Should().Be(OxyColor.FromRgb(68, 114, 196));
        first.MarkerStrokeThickness.Should().Be(2);
        first.MarkerSize.Should().Be(7);
        first.Points.Select(point => (point.X, point.Y)).Should().Equal((1, 10), (2, 20));

        second.Title.Should().Be("Cost");
        second.MarkerType.Should().Be(MarkerType.Triangle);
        second.MarkerFill.Should().Be(OxyColor.FromRgb(112, 173, 71));
        second.MarkerStroke.Should().Be(OxyColor.FromRgb(55, 86, 35));
        second.MarkerStrokeThickness.Should().Be(3);
        second.MarkerSize.Should().Be(9);
        second.Points.Select(point => (point.X, point.Y)).Should().Equal((1, 6), (2, 11));
    }

    [Fact]
    public void ScatterRenderer_AssignsRequestedSeriesToSecondaryAxis()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)),
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [1]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Cost"),
                Cell(2, 1, "1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "6"),
                Cell(3, 1, "2"),
                Cell(3, 2, "20"),
                Cell(3, 3, "11")
            ],
            [],
            []));

        model.Axes.Should().Contain(axis => axis.Key == "SecondaryY");
        var first = model.Series[0].Should().BeOfType<ScatterSeries>().Subject;
        var second = model.Series[1].Should().BeOfType<ScatterSeries>().Subject;
        first.YAxisKey.Should().BeNull();
        second.YAxisKey.Should().Be("SecondaryY");
    }

    [Fact]
    public void ColumnRenderer_DoesNotAddSecondaryAxisWhenNoSeriesUsesIt()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            ShowSecondaryAxis = true
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

        model.Series.Should().ContainSingle();
        model.Series.OfType<RectangleBarSeries>().Single().YAxisKey.Should().BeNull();
        model.Axes.Should().NotContain(axis => axis.Key == "SecondaryY");
    }

    [Fact]
    public void ScatterRenderer_AddsLinearTrendlineFromActualXValues()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            ShowLinearTrendline = true
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "3"),
                Cell(3, 2, "30")
            ],
            [],
            []));

        model.Series.Should().HaveCount(2);
        var trendline = model.Series[1].Should().BeOfType<LineSeries>().Subject;
        trendline.Title.Should().Be("Linear Trendline");
        trendline.Points.Select(point => (point.X, point.Y)).Should().Equal((1, 10), (3, 30));
    }

    [Fact]
    public void BarRenderer_AddsLinearTrendlineFromCategoryValues()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            ShowLinearTrendline = true
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "30")
            ],
            [],
            []));

        model.Series.Should().HaveCount(2);
        var trendline = model.Series[1].Should().BeOfType<LineSeries>().Subject;
        trendline.Title.Should().Be("Linear Trendline");
        trendline.Points.Select(point => (point.X, point.Y)).Should().Equal((10, 0), (30, 1));
    }

    [Fact]
    public void BarRenderer_CalculatesTrendlineFromCategoryOrderBeforeRenderingHorizontally()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2)),
            ShowLinearTrendline = true
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "30"),
                Cell(4, 1, "Q3"),
                Cell(4, 2, "30")
            ],
            [],
            []));

        model.Series.Should().HaveCount(2);
        var trendline = model.Series[1].Should().BeOfType<LineSeries>().Subject;
        trendline.Points.Select(point => (Math.Round(point.X, 3), point.Y))
            .Should().Equal((13.333, 0), (33.333, 2));
    }

    [Fact]
    public void BarRenderer_PositionsTrendlineInfoInHorizontalAxisSpace()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2)),
            ShowLinearTrendline = true,
            ShowTrendlineEquation = true,
            ShowTrendlineRSquared = true
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "30"),
                Cell(4, 1, "Q3"),
                Cell(4, 2, "30")
            ],
            [],
            []));

        var annotation = model.Annotations.Should().ContainSingle().Which.Should().BeOfType<TextAnnotation>().Subject;
        annotation.Text.Should().Contain("y = 10x + 13.333");
        annotation.Text.Should().Contain("R² = ");
        annotation.TextPosition.Should().Be(new DataPoint(10, 2));
    }

    [Fact]
    public void AreaRenderer_AddsLinearTrendlineFromCategoryValues()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Area,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            ShowLinearTrendline = true
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "30")
            ],
            [],
            []));

        model.Series.Should().HaveCount(2);
        var trendline = model.Series[1].Should().BeOfType<LineSeries>().Subject;
        trendline.Title.Should().Be("Linear Trendline");
        trendline.Points.Select(point => (point.X, point.Y)).Should().Equal((0, 10), (1, 30));
    }

    [Fact]
    public void ColumnRenderer_RendersRequestedComboSeriesAsLine()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)),
            UseComboLineForSecondarySeries = true,
            ComboLineSeriesIndexes = [1]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Margin"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "2"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "30"),
                Cell(3, 3, "5")
            ],
            [],
            []));

        model.Series.Should().HaveCount(2);
        model.Series[0].Should().BeOfType<RectangleBarSeries>();
        var line = model.Series[1].Should().BeOfType<LineSeries>().Subject;
        line.Title.Should().Be("Margin");
        line.Points.Select(point => (point.X, point.Y)).Should().Equal((0, 2), (1, 5));
    }

    [Fact]
    public void ColumnRenderer_DoesNotTreatEmptyComboSeriesListAsAllSeries()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 4)),
            UseComboLineForSecondarySeries = true,
            ComboLineSeriesIndexes = []
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Cost"),
                Cell(1, 4, "Margin"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "4"),
                Cell(2, 4, "2"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "30"),
                Cell(3, 3, "12"),
                Cell(3, 4, "5")
            ],
            [],
            []));

        model.Series.Should().HaveCount(3);
        model.Series.Should().OnlyContain(series => series is RectangleBarSeries);
    }

    [Fact]
    public void StackedColumnRenderer_RendersRequestedComboSeriesAsLine()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.StackedColumn,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 4)),
            UseComboLineForSecondarySeries = true,
            ComboLineSeriesIndexes = [2]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Cost"),
                Cell(1, 4, "Margin"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "4"),
                Cell(2, 4, "2"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "30"),
                Cell(3, 3, "12"),
                Cell(3, 4, "5")
            ],
            [],
            []));

        model.Series.Should().HaveCount(3);
        model.Series[0].Should().BeOfType<RectangleBarSeries>();
        model.Series[1].Should().BeOfType<RectangleBarSeries>();
        var line = model.Series[2].Should().BeOfType<LineSeries>().Subject;
        line.Title.Should().Be("Margin");
        line.Points.Select(point => (point.X, point.Y)).Should().Equal((0, 2), (1, 5));
    }

    [Fact]
    public void PercentStackedColumnRenderer_RendersRequestedComboSeriesAsLine()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.PercentStackedColumn,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 4)),
            UseComboLineForSecondarySeries = true,
            ComboLineSeriesIndexes = [2]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Cost"),
                Cell(1, 4, "Margin"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "4"),
                Cell(2, 4, "20"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "30"),
                Cell(3, 3, "12"),
                Cell(3, 4, "50")
            ],
            [],
            []));

        model.Series.Should().HaveCount(3);
        model.Series[0].Should().BeOfType<RectangleBarSeries>();
        model.Series[1].Should().BeOfType<RectangleBarSeries>();
        var line = model.Series[2].Should().BeOfType<LineSeries>().Subject;
        line.Title.Should().Be("Margin");
        line.Points.Select(point => (point.X, point.Y)).Should().Equal((0, 20), (1, 50));
    }

    [Fact]
    public void ColumnRenderer_DoesNotApplyLogScaleToCategoryXAxis()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            XAxisLogScale = true
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "30")
            ],
            [],
            []));

        model.Axes.Single(axis => axis.Position == AxisPosition.Bottom)
            .Should().BeOfType<LinearAxis>();
    }

    [Fact]
    public void ScatterRenderer_AppliesLogScaleToNumericXAxis()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            XAxisLogScale = true
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "10"),
                Cell(3, 2, "30")
            ],
            [],
            []));

        model.Axes.Single(axis => axis.Position == AxisPosition.Bottom)
            .Should().BeOfType<LogarithmicAxis>();
    }

    [Fact]
    public void ColumnRenderer_DoesNotApplyNumberFormatToCategoryXAxis()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            XAxisNumberFormat = ChartDataLabelNumberFormat.Currency
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "Q2"),
                Cell(3, 2, "30")
            ],
            [],
            []));

        var axis = model.Axes.Single(axis => axis.Position == AxisPosition.Bottom);
        axis.FormatValue(0).Should().Be("Q1");
        axis.FormatValue(1).Should().Be("Q2");
    }

    [Fact]
    public void ScatterRenderer_AppliesNumberFormatToNumericXAxis()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            XAxisNumberFormat = ChartDataLabelNumberFormat.Currency
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "1"),
                Cell(2, 2, "10"),
                Cell(3, 1, "10"),
                Cell(3, 2, "30")
            ],
            [],
            []));

        var axis = model.Axes.Single(axis => axis.Position == AxisPosition.Bottom);
        axis.FormatValue(10).Should().Be("$10.00");
    }

    [Fact]
    public void BubbleRenderer_UsesXyAndSizeColumns()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bubble,
            FirstColIsCategories = false,
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
        series.Title.Should().Be("Revenue");
        series.Points.Select(point => (point.X, point.Y, point.Size)).Should().Equal((1, 10, 4), (2, 20, 8));
    }

    [Fact]
    public void BubbleRenderer_RendersMultipleYAndSizePairsAgainstSharedXValues()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bubble,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 5)),
            SeriesFormats =
            [
                new ChartSeriesFormat(0, FillColor: new CellColor(68, 114, 196)),
                new ChartSeriesFormat(1, FillColor: new CellColor(112, 173, 71))
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Margin A"),
                Cell(1, 3, "Size A"),
                Cell(1, 4, "Margin B"),
                Cell(1, 5, "Size B"),
                Cell(2, 1, "1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "4"),
                Cell(2, 4, "7"),
                Cell(2, 5, "3"),
                Cell(3, 1, "2"),
                Cell(3, 2, "20"),
                Cell(3, 3, "8"),
                Cell(3, 4, "11"),
                Cell(3, 5, "6")
            ],
            [],
            []));

        var series = model.Series.Should().HaveCount(2).And.AllBeOfType<ScatterSeries>().Subject.Cast<ScatterSeries>().ToList();
        series[0].Title.Should().Be("Margin A");
        series[0].Points.Select(point => (point.X, point.Y, point.Size)).Should().Equal((1, 10, 4), (2, 20, 8));
        series[0].MarkerFill.Should().Be(OxyColor.FromRgb(68, 114, 196));
        series[1].Title.Should().Be("Margin B");
        series[1].Points.Select(point => (point.X, point.Y, point.Size)).Should().Equal((1, 7, 3), (2, 11, 6));
        series[1].MarkerFill.Should().Be(OxyColor.FromRgb(112, 173, 71));
    }

    [Fact]
    public void BubbleRenderer_AddsLinearTrendlineFromActualXValues()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bubble,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)),
            ShowLinearTrendline = true
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "X"),
                Cell(1, 2, "Revenue"),
                Cell(1, 3, "Market"),
                Cell(2, 1, "1"),
                Cell(2, 2, "10"),
                Cell(2, 3, "4"),
                Cell(3, 1, "3"),
                Cell(3, 2, "30"),
                Cell(3, 3, "8")
            ],
            [],
            []));

        model.Series.Should().HaveCount(2);
        var trendline = model.Series[1].Should().BeOfType<LineSeries>().Subject;
        trendline.Title.Should().Be("Linear Trendline");
        trendline.Points.Select(point => (point.X, point.Y)).Should().Equal((1, 10), (3, 30));
    }

    [Fact]
    public void BubbleRenderer_IgnoresCategoryFlagAndUsesFirstRangeColumnAsXValues()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bubble,
            FirstColIsCategories = true,
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
        series.Title.Should().Be("Revenue");
        series.Points.Select(point => (point.X, point.Y, point.Size)).Should().Equal((1, 10, 4), (2, 20, 8));
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
    public void RadarRenderer_UsesPolarAxesAndClosesEachSeries()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Radar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 3))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Metric"),
                Cell(1, 2, "Product A"),
                Cell(1, 3, "Product B"),
                Cell(2, 1, "Speed"),
                Cell(2, 2, "4"),
                Cell(2, 3, "3"),
                Cell(3, 1, "Cost"),
                Cell(3, 2, "2"),
                Cell(3, 3, "5"),
                Cell(4, 1, "Quality"),
                Cell(4, 2, "5"),
                Cell(4, 3, "4")
            ],
            [],
            []));

        model.PlotType.Should().Be(PlotType.Polar);
        model.Axes.Should().ContainSingle(axis => axis is AngleAxis);
        model.Axes.Should().ContainSingle(axis => axis is MagnitudeAxis);

        var series = model.Series.Should().HaveCount(2).And.AllBeOfType<LineSeries>().Subject.Cast<LineSeries>().ToList();
        series[0].Title.Should().Be("Product A");
        series[0].Points.Should().HaveCount(4);
        series[0].Points.First().Should().Be(series[0].Points.Last());
        series[0].Points.Select(point => point.Y).Should().Equal(4, 2, 5, 4);
    }

    [Fact]
    public void StockRenderer_UsesHighLowSeriesWithOhlcColumns()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Stock,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 5))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Date"),
                Cell(1, 2, "Open"),
                Cell(1, 3, "High"),
                Cell(1, 4, "Low"),
                Cell(1, 5, "Close"),
                Cell(2, 1, "Mon"),
                Cell(2, 2, "10"),
                Cell(2, 3, "15"),
                Cell(2, 4, "9"),
                Cell(2, 5, "13"),
                Cell(3, 1, "Tue"),
                Cell(3, 2, "13"),
                Cell(3, 3, "18"),
                Cell(3, 4, "12"),
                Cell(3, 5, "16")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<HighLowSeries>().Subject;
        series.Title.Should().Be("Stock");
        series.Items.Should().HaveCount(2);
        series.Items[0].Open.Should().Be(10);
        series.Items[0].High.Should().Be(15);
        series.Items[0].Low.Should().Be(9);
        series.Items[0].Close.Should().Be(13);
    }

    [Fact]
    public void StockRenderer_UsesVolumeColumnAndOhlcColumnsForVolumeOpenHighLowClose()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Stock,
            StockSubtype = StockChartSubtype.VolumeOpenHighLowClose,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 6))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Date"),
                Cell(1, 2, "Volume"),
                Cell(1, 3, "Open"),
                Cell(1, 4, "High"),
                Cell(1, 5, "Low"),
                Cell(1, 6, "Close"),
                Cell(2, 1, "Mon"),
                Cell(2, 2, "1000"),
                Cell(2, 3, "10"),
                Cell(2, 4, "15"),
                Cell(2, 5, "9"),
                Cell(2, 6, "13"),
                Cell(3, 1, "Tue"),
                Cell(3, 2, "1200"),
                Cell(3, 3, "13"),
                Cell(3, 4, "18"),
                Cell(3, 5, "12"),
                Cell(3, 6, "16")
            ],
            [],
            []));

        model.Series.Should().HaveCount(2);
        model.Series[0].Should().BeOfType<RectangleBarSeries>();
        var stockSeries = model.Series[1].Should().BeOfType<HighLowSeries>().Subject;
        stockSeries.Items[0].Open.Should().Be(10);
        stockSeries.Items[0].High.Should().Be(15);
        stockSeries.Items[0].Low.Should().Be(9);
        stockSeries.Items[0].Close.Should().Be(13);
    }

    [Fact]
    public void ThreeDBarRenderer_UsesBarSeries()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.ThreeDBar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Category"),
                Cell(1, 2, "Sales"),
                Cell(2, 1, "A"),
                Cell(2, 2, "10"),
                Cell(3, 1, "B"),
                Cell(3, 2, "20")
            ],
            [],
            []));

        model.Series.Should().ContainSingle().Which.Should().BeOfType<BarSeries>();
        model.Axes.Should().Contain(axis => axis.Position == AxisPosition.Left);
        model.Axes.Should().Contain(axis => axis.Position == AxisPosition.Bottom);
    }

    [Fact]
    public void ThreeDPieRenderer_UsesPieSeries()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.ThreeDPie,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Category"),
                Cell(1, 2, "Sales"),
                Cell(2, 1, "A"),
                Cell(2, 2, "10"),
                Cell(3, 1, "B"),
                Cell(3, 2, "20")
            ],
            [],
            []));

        model.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>();
        model.Axes.Should().BeEmpty();
    }

    [Fact]
    public void StockRenderer_UsesDateTimeAxisForDateCategories()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Stock,
            StockSubtype = StockChartSubtype.VolumeOpenHighLowClose,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 6))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Date"),
                Cell(1, 2, "Volume"),
                Cell(1, 3, "Open"),
                Cell(1, 4, "High"),
                Cell(1, 5, "Low"),
                Cell(1, 6, "Close"),
                Cell(2, 1, "2026-01-02"),
                Cell(2, 2, "1000"),
                Cell(2, 3, "10"),
                Cell(2, 4, "15"),
                Cell(2, 5, "9"),
                Cell(2, 6, "13"),
                Cell(3, 1, "2026-01-05"),
                Cell(3, 2, "1200"),
                Cell(3, 3, "13"),
                Cell(3, 4, "18"),
                Cell(3, 5, "12"),
                Cell(3, 6, "16")
            ],
            [],
            []));

        var axis = model.Axes.Should().ContainSingle(a => a.Position == AxisPosition.Bottom).Which;
        axis.Should().BeOfType<DateTimeAxis>();

        var stockSeries = model.Series[1].Should().BeOfType<HighLowSeries>().Subject;
        stockSeries.Items[0].X.Should().BeApproximately(DateTimeAxis.ToDouble(new DateTime(2026, 1, 2)), 0.0001);
        stockSeries.Items[1].X.Should().BeApproximately(DateTimeAxis.ToDouble(new DateTime(2026, 1, 5)), 0.0001);

        var volumeSeries = model.Series[0].Should().BeOfType<RectangleBarSeries>().Subject;
        var firstVolume = volumeSeries.Items[0];
        ((firstVolume.X0 + firstVolume.X1) / 2).Should().BeApproximately(stockSeries.Items[0].X, 0.0001);
    }

    [Fact]
    public void StockRenderer_UsesCandlestickSeriesWhenUpDownBarsAreEnabled()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Stock,
            StockSubtype = StockChartSubtype.OpenHighLowClose,
            ShowUpDownBars = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 5))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Date"),
                Cell(1, 2, "Open"),
                Cell(1, 3, "High"),
                Cell(1, 4, "Low"),
                Cell(1, 5, "Close"),
                Cell(2, 1, "2026-01-02"),
                Cell(2, 2, "10"),
                Cell(2, 3, "15"),
                Cell(2, 4, "9"),
                Cell(2, 5, "13"),
                Cell(3, 1, "2026-01-05"),
                Cell(3, 2, "13"),
                Cell(3, 3, "18"),
                Cell(3, 4, "12"),
                Cell(3, 5, "11")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<CandleStickSeries>().Subject;
        series.IncreasingColor.Should().Be(OxyColors.White);
        series.DecreasingColor.Should().Be(OxyColors.Black);
        series.Items.Should().HaveCount(2);
        series.Items[0].Open.Should().Be(10);
        series.Items[0].Close.Should().Be(13);
        series.Items[1].Open.Should().Be(13);
        series.Items[1].Close.Should().Be(11);
    }

    [Fact]
    public void StockRenderer_AppliesUpDownBarFormattingToCandlesticks()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Stock,
            StockSubtype = StockChartSubtype.OpenHighLowClose,
            ShowUpDownBars = true,
            UpDownBarGapWidth = 150,
            UpBarFillColor = new CellColor(226, 239, 218),
            UpBarBorderColor = new CellColor(84, 130, 53),
            UpBarBorderThickness = 2.25,
            DownBarFillColor = new CellColor(248, 203, 173),
            DownBarBorderColor = new CellColor(192, 0, 0),
            DownBarBorderThickness = 1.25,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 5))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Date"),
                Cell(1, 2, "Open"),
                Cell(1, 3, "High"),
                Cell(1, 4, "Low"),
                Cell(1, 5, "Close"),
                Cell(2, 1, "2026-01-02"),
                Cell(2, 2, "10"),
                Cell(2, 3, "15"),
                Cell(2, 4, "9"),
                Cell(2, 5, "13"),
                Cell(3, 1, "2026-01-05"),
                Cell(3, 2, "13"),
                Cell(3, 3, "18"),
                Cell(3, 4, "12"),
                Cell(3, 5, "11")
            ],
            [],
            []));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<CandleStickSeries>().Subject;
        series.IncreasingColor.Should().Be(OxyColor.FromRgb(226, 239, 218));
        series.DecreasingColor.Should().Be(OxyColor.FromRgb(248, 203, 173));
        series.Color.Should().Be(OxyColor.FromRgb(84, 130, 53));
        series.StrokeThickness.Should().Be(2.25);
        series.CandleWidth.Should().BeApproximately(0.4, 0.0001);
    }

    private static PlotModel BuildPlotModel(ChartModel chart, ViewportModel viewport)
    {
        var method = typeof(ChartRenderer).GetMethod(
            "BuildPlotModel",
            BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(ChartModel), typeof(ViewportModel)]);
        method.Should().NotBeNull();
        return method!.Invoke(null, [chart, viewport]).Should().BeOfType<PlotModel>().Subject;
    }

    private static PlotModel? BuildNullablePlotModel(ChartModel chart, ViewportModel viewport)
    {
        var method = typeof(ChartRenderer).GetMethod(
            "BuildPlotModel",
            BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(ChartModel), typeof(ViewportModel)]);
        method.Should().NotBeNull();
        return method!.Invoke(null, [chart, viewport]) as PlotModel;
    }

    private static PlotModel BuildPlotModel(ChartModel chart, ViewportModel viewport, WorkbookTheme theme)
    {
        var method = typeof(ChartRenderer).GetMethod(
            "BuildPlotModel",
            BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(ChartModel), typeof(ViewportModel), typeof(WorkbookTheme)]);
        method.Should().NotBeNull();
        return method!.Invoke(null, [chart, viewport, theme]).Should().BeOfType<PlotModel>().Subject;
    }

    private static DisplayCell Cell(uint row, uint col, string text) =>
        new(row, col, null, text, null, StyleId.Default, null);

    private static ChartDataCell ChartCell(SheetId sheetId, uint row, uint col, string text) =>
        new(sheetId, row, col, text);

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }

    private static void RunWithCulture(string cultureName, Action action)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
