using System.Globalization;
using System.Reflection;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FreeX.App.UI.Tests;

public sealed partial class ChartRendererTests
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
    public void ColumnRenderer_UsesRawChartDataValuesInsteadOfDisplayText()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [],
            [],
            [],
            ChartDataCells:
            [
                ChartCell(sheetId, 1, 1, "Category", new TextValue("Category")),
                ChartCell(sheetId, 1, 2, "Sales", new TextValue("Sales")),
                ChartCell(sheetId, 2, 1, "Currency", new TextValue("Currency")),
                ChartCell(sheetId, 2, 2, "1.234,50 EUR", new NumberValue(1234.5)),
                ChartCell(sheetId, 3, 1, "Percent", new TextValue("Percent")),
                ChartCell(sheetId, 3, 2, "25%", new NumberValue(0.25)),
                ChartCell(sheetId, 4, 1, "Date", new TextValue("Date")),
                ChartCell(sheetId, 4, 2, "01.01.2024", new DateTimeValue(45292))
            ]));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<RectangleBarSeries>().Subject;

        series.Items.Should().HaveCount(3);
        Math.Max(series.Items[0].Y0, series.Items[0].Y1).Should().BeApproximately(1234.5, 0.001);
        Math.Max(series.Items[1].Y0, series.Items[1].Y1).Should().BeApproximately(0.25, 0.001);
        Math.Max(series.Items[2].Y0, series.Items[2].Y1).Should().BeApproximately(45292, 0.001);
    }

    [Fact]
    public void ColumnRenderer_SwitchRowColumnReadsRowsAsSeries()
    {
        var sheetId = SheetId.New();
        // 3 rows x 4 cols:  (blank) Q1 Q2 Q3 / Sales 10 20 30 / Costs 5 8 13.
        // Switched: series names come from the first COLUMN (Sales, Costs) and the
        // categories from the first ROW (Q1..Q3) — Excel's "Switch Row/Column".
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            SeriesInRows = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 4))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [],
            [],
            [],
            ChartDataCells:
            [
                ChartCell(sheetId, 1, 2, "Q1"),
                ChartCell(sheetId, 1, 3, "Q2"),
                ChartCell(sheetId, 1, 4, "Q3"),
                ChartCell(sheetId, 2, 1, "Sales"),
                ChartCell(sheetId, 2, 2, "10"),
                ChartCell(sheetId, 2, 3, "20"),
                ChartCell(sheetId, 2, 4, "30"),
                ChartCell(sheetId, 3, 1, "Costs"),
                ChartCell(sheetId, 3, 2, "5"),
                ChartCell(sheetId, 3, 3, "8"),
                ChartCell(sheetId, 3, 4, "13")
            ]));

        model.Series.Should().HaveCount(2);
        var sales = model.Series[0].Should().BeOfType<RectangleBarSeries>().Subject;
        var costs = model.Series[1].Should().BeOfType<RectangleBarSeries>().Subject;
        sales.Title.Should().Be("Sales");
        costs.Title.Should().Be("Costs");
        sales.Items.Should().HaveCount(3);
        Math.Max(sales.Items[0].Y0, sales.Items[0].Y1).Should().BeApproximately(10, 0.001);
        Math.Max(sales.Items[2].Y0, sales.Items[2].Y1).Should().BeApproximately(30, 0.001);
        costs.Items.Should().HaveCount(3);
        Math.Max(costs.Items[1].Y0, costs.Items[1].Y1).Should().BeApproximately(8, 0.001);
        var bottomAxis = model.Axes.Single(axis => axis.Position == AxisPosition.Bottom);
        bottomAxis.FormatValue(0).Should().Be("Q1");
        bottomAxis.FormatValue(2).Should().Be("Q3");
    }

    [Fact]
    public void Render_ExportsAtRequestedRenderScale()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("ChartRenderer.cs");
        var render = source[
            source.IndexOf("public static ImageSource? Render(ChartModel chart, ViewportModel viewport, WorkbookTheme? theme, double renderScale)", StringComparison.Ordinal)..
            source.IndexOf("internal static PlotModel? BuildPlotModel", StringComparison.Ordinal)];

        render.Should().Contain("renderScale = NormalizeRenderScale(renderScale);");
        render.Should().Contain("chart.Width * renderScale");
        render.Should().Contain("chart.Height * renderScale");
        render.Should().Contain("IsVisiblyBlank(bitmap)");
        render.Should().Contain("RenderDirectFallback(chart, viewport, resolvedTheme, renderScale)");
        source.Should().Contain("private static double NormalizeRenderScale(double renderScale)");
        source.Should().Contain("Math.Ceiling(renderScale)");
        source.Should().Contain("Math.Max(2.0");

        var fallbackSource = AppUiSourceTestSupport.ReadAppUiSources("ChartRenderer.DirectFallback.cs");
        fallbackSource.Should().Contain("internal static ImageSource? RenderDirectFallback");
        fallbackSource.Should().Contain("BuildDirectChartData(chart, viewport, theme)");
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Render_ColumnChartWithVisibleDataProducesNonBlankBitmap(double renderScale)
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var chart = new ChartModel
            {
                Type = ChartType.Column,
                DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 2)),
                Width = 400,
                Height = 300
            };

            var image = ChartRenderer.Render(chart, new ViewportModel(
                [
                    new DisplayCell(1, 1, new TextValue("Quarter"), "Quarter", null, StyleId.Default, null),
                    new DisplayCell(1, 2, new TextValue("Revenue"), "Revenue", null, StyleId.Default, null),
                    new DisplayCell(2, 1, new TextValue("Q1"), "Q1", null, StyleId.Default, null),
                    new DisplayCell(2, 2, new NumberValue(10), "10", null, StyleId.Default, null),
                    new DisplayCell(3, 1, new TextValue("Q2"), "Q2", null, StyleId.Default, null),
                    new DisplayCell(3, 2, new NumberValue(18), "18", null, StyleId.Default, null),
                    new DisplayCell(4, 1, new TextValue("Q3"), "Q3", null, StyleId.Default, null),
                    new DisplayCell(4, 2, new NumberValue(14), "14", null, StyleId.Default, null),
                    new DisplayCell(5, 1, new TextValue("Q4"), "Q4", null, StyleId.Default, null),
                    new DisplayCell(5, 2, new NumberValue(26), "26", null, StyleId.Default, null)
                ],
                [],
                []),
                WorkbookTheme.Office,
                renderScale);

            var bitmap = image.Should().BeAssignableTo<BitmapSource>().Subject;
            CountVisiblePixels(bitmap).Should().BeGreaterThan(750);
        });
    }

    [Fact]
    public void RenderDirectFallback_ColumnChartWithVisibleDataProducesNonBlankBitmap()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var chart = new ChartModel
            {
                Type = ChartType.Column,
                DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 2)),
                Title = "Chart",
                Width = 400,
                Height = 300
            };

            var image = ChartRenderer.RenderDirectFallback(chart, new ViewportModel(
                [],
                [],
                [],
                ChartDataCells:
                [
                    ChartCell(sheetId, 1, 1, "Quarter", new TextValue("Quarter")),
                    ChartCell(sheetId, 1, 2, "Revenue", new TextValue("Revenue")),
                    ChartCell(sheetId, 2, 1, "Q1", new TextValue("Q1")),
                    ChartCell(sheetId, 2, 2, "10", new NumberValue(10)),
                    ChartCell(sheetId, 3, 1, "Q2", new TextValue("Q2")),
                    ChartCell(sheetId, 3, 2, "18", new NumberValue(18)),
                    ChartCell(sheetId, 4, 1, "Q3", new TextValue("Q3")),
                    ChartCell(sheetId, 4, 2, "14", new NumberValue(14)),
                    ChartCell(sheetId, 5, 1, "Q4", new TextValue("Q4")),
                    ChartCell(sheetId, 5, 2, "26", new NumberValue(26))
                ]),
                WorkbookTheme.Office,
                renderScale: 2.0);

            var bitmap = image.Should().BeAssignableTo<BitmapSource>().Subject;
            bitmap.PixelWidth.Should().Be(800);
            bitmap.PixelHeight.Should().Be(600);
            CountVisiblePixels(bitmap).Should().BeGreaterThan(4_000);
        });
    }

    [Fact]
    public void ClusteredColumnChart_PlacesSeriesSideBySideWithinCategorySlot()
    {
        // A clustered (grouped) Column chart with two series must draw the two bars for each
        // category SIDE BY SIDE, not stacked on top of each other. Each series' RectangleBarItem
        // x-window must be a disjoint sub-slot within the category's full bar width.
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [],
            [],
            [],
            ChartDataCells:
            [
                ChartCell(sheetId, 1, 1, "Cat", new TextValue("Cat")),
                ChartCell(sheetId, 1, 2, "Budget", new TextValue("Budget")),
                ChartCell(sheetId, 1, 3, "Actual", new TextValue("Actual")),
                ChartCell(sheetId, 2, 1, "A", new TextValue("A")),
                ChartCell(sheetId, 2, 2, "10", new NumberValue(10)),
                ChartCell(sheetId, 2, 3, "20", new NumberValue(20)),
                ChartCell(sheetId, 3, 1, "B", new TextValue("B")),
                ChartCell(sheetId, 3, 2, "30", new NumberValue(30)),
                ChartCell(sheetId, 3, 3, "40", new NumberValue(40))
            ]));

        var barSeries = model.Series.OfType<RectangleBarSeries>().ToList();
        barSeries.Should().HaveCount(2);

        // For category index 0, the two series' x-windows must not overlap.
        var s0 = barSeries[0].Items[0];
        var s1 = barSeries[1].Items[0];
        var s0Left = Math.Min(s0.X0, s0.X1);
        var s0Right = Math.Max(s0.X0, s0.X1);
        var s1Left = Math.Min(s1.X0, s1.X1);
        var s1Right = Math.Max(s1.X0, s1.X1);

        // Disjoint: one series entirely left of the other within the slot.
        (s0Right <= s1Left + 1e-9 || s1Right <= s0Left + 1e-9)
            .Should().BeTrue("clustered columns must sit side-by-side, not overlap");
        // Both windows stay within the category's full slot [-0.5, +0.5] around index 0.
        s0Left.Should().BeGreaterThanOrEqualTo(-0.5);
        s1Right.Should().BeLessThanOrEqualTo(0.5);
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

    internal static int CountVisiblePixels(BitmapSource bitmap)
    {
        var source = bitmap.Format == PixelFormats.Bgra32
            ? bitmap
            : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);

        var visible = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var blue = pixels[i];
            var green = pixels[i + 1];
            var red = pixels[i + 2];
            var alpha = pixels[i + 3];
            if (alpha > 10 && (red < 245 || green < 245 || blue < 245))
                visible++;
        }

        return visible;
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
}
