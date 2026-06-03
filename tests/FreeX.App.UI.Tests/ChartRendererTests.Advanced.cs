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
        var percentAxis = model.Axes.Should().Contain(a => a.Position == AxisPosition.Right).Which;
        percentAxis.MajorStep.Should().Be(20);
        percentAxis.FormatValue(80).Should().Be("80%");
        var catAxis = model.Axes.OfType<CategoryAxis>().Should().ContainSingle().Subject;
        catAxis.Labels[0].Should().Be("B");  // highest value first
    }

    [Fact]
    public void ParetoRenderer_AggregatesRepeatedCategoriesBeforeSorting()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Pareto,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Item"), Cell(1, 2, "Count"),
                Cell(2, 1, "A"),   Cell(2, 2, "10"),
                Cell(3, 1, "B"),   Cell(3, 2, "50"),
                Cell(4, 1, "A"),   Cell(4, 2, "25"),
                Cell(5, 1, "C"),   Cell(5, 2, "20")
            ],
            [],
            []));

        var bars = model.Series[0].Should().BeOfType<RectangleBarSeries>().Subject;
        bars.Items.Select(item => Math.Max(item.Y0, item.Y1)).Should().Equal(50, 35, 20);
        var cumulativeLine = model.Series[1].Should().BeOfType<LineSeries>().Subject;
        cumulativeLine.Points.Select(point => point.Y).Should().Equal(
            100.0 * 50 / 105,
            100.0 * 85 / 105,
            100.0);
        var catAxis = model.Axes.OfType<CategoryAxis>().Should().ContainSingle().Subject;
        catAxis.Labels.Should().Equal("B", "A", "C");
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
    public void AdvancedFamilyRenderers_AggregateTotalsWhileCollectingValues()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "ChartRenderer.AdvancedFamilies.cs"));
        var pareto = source[
            source.IndexOf("internal static PlotModel BuildParetoModel", StringComparison.Ordinal)..
            source.IndexOf("internal static PlotModel BuildBoxAndWhiskerModel", StringComparison.Ordinal)];
        var treemap = source[
            source.IndexOf("internal static PlotModel BuildTreemapModel", StringComparison.Ordinal)..
            source.IndexOf("internal static PlotModel BuildSunburstModel", StringComparison.Ordinal)];
        var funnel = source[
            source.IndexOf("internal static PlotModel BuildFunnelModel", StringComparison.Ordinal)..];

        pareto.Should().Contain("total += v;");
        pareto.Should().NotContain("total += values[index].Value");
        treemap.Should().Contain("total += v;");
        treemap.Should().NotContain("total += values[index].Value");
        funnel.Should().Contain("if (value > maxVal)");
        funnel.Should().NotContain("values[index].Value > maxVal");
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
}
