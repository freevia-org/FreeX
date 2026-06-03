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
    public void Render_ExportsAtRequestedRenderScale()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "ChartRenderer.cs"));
        var render = source[
            source.IndexOf("public static ImageSource? Render(ChartModel chart, ViewportModel viewport, WorkbookTheme? theme, double renderScale)", StringComparison.Ordinal)..
            source.IndexOf("private static PlotModel? BuildPlotModel", StringComparison.Ordinal)];

        render.Should().Contain("Math.Clamp(renderScale, 0.25, 4.0)");
        render.Should().Contain("chart.Width * renderScale");
        render.Should().Contain("chart.Height * renderScale");
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
