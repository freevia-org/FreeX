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

namespace FreeX.App.UI.Tests;

public sealed partial class ChartRendererTests
{
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
        series.Items.Should().Contain(item => item.X0 == 0.8432601880877743 && item.X1 == 1.1567398119122257 && item.Y0 == 0 && item.Y1 == 0,
            "an implicit clustered column uses Excel's native gapWidth=219 geometry");
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
        var source = AppUiSourceTestSupport.ReadAppUiSources("ChartRenderer.Annotations.cs");
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
}
