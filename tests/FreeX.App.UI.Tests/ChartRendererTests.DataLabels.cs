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
    public void BarRenderer_AppliesLastDensePointSpecificDataLabelFormatting()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            ShowDataLabels = true,
            DataLabelFillColor = new CellColor(255, 255, 255)
        };
        chart.PointDataLabelFormats.Add(new ChartPointDataLabelFormat(
            0,
            1,
            TextColor: new CellColor(192, 0, 0),
            FontSize: 9));
        for (var index = 0; index < 20; index++)
        {
            chart.PointDataLabelFormats.Add(new ChartPointDataLabelFormat(
                1,
                index,
                TextColor: new CellColor(89, 89, 89),
                FontSize: 8));
        }
        chart.PointDataLabelFormats.Add(new ChartPointDataLabelFormat(
            0,
            1,
            TextColor: new CellColor(0, 97, 0),
            FontSize: 15));

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
        annotations[1].TextColor.Should().Be(OxyColor.FromRgb(0, 97, 0));
        annotations[1].FontSize.Should().Be(15);
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
        series.OutsideLabelFormat.Should().Be(
            "{1}" + Environment.NewLine + "{0}" + Environment.NewLine + "{2:0%}");
    }

    [Fact]
    public void PieRenderer_DataLabelAnnotationsAggregatePositiveTotalsWithoutLinq()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("ChartRenderer.SeriesFormatting.cs");
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
    public void DataLabelFormatLookups_UseSparseReverseScansAndDenseIndexWithoutLinqPredicates()
    {
        var source = AppUiSourceTestSupport.ReadAppUiSources("ChartRenderer.SeriesFormatting.cs");
        var pointLookup = source[
            source.IndexOf("private readonly struct ChartPointDataLabelFormatLookup", StringComparison.Ordinal)..
            source.IndexOf("private static double ColumnBarHalfWidth", StringComparison.Ordinal)];
        var seriesLookup = source[
            source.IndexOf("private static ChartSeriesFormat? GetSeriesFormat", StringComparison.Ordinal)..
            source.IndexOf("private static void ApplyLineFormat", StringComparison.Ordinal)];
        var sharedStylePlanner = WorkspaceFileLocator.ReadAllTextWithFailureMessage(
            "Unable to locate workspace file",
            "src",
            "FreeX.App.Presentation",
            "Charts",
            "ChartStylePlanner.cs");

        source.Should().Contain("private const int PointDataLabelFormatLookupThreshold = 16");
        pointLookup.Should().Contain("new Dictionary<(int SeriesIndex, int PointIndex), ChartPointDataLabelFormat>(formats.Count)");
        pointLookup.Should().Contain("_indexedFormats[(format.SeriesIndex, format.PointIndex)] = format;");
        pointLookup.Should().Contain("_indexedFormats.TryGetValue((seriesIndex, pointIndex)");
        pointLookup.Should().Contain("for (var i = _formats.Count - 1; i >= 0; i--)");
        seriesLookup.Should().Contain("ChartStylePlanner.FindSeriesFormat(chart, seriesIndex)");
        sharedStylePlanner.Should().Contain("for (var i = formats.Count - 1; i >= 0; i--)");
        sharedStylePlanner.Should().Contain("format.SeriesIndex == seriesIndex");
        pointLookup.Should().NotContain("LastOrDefault");
        pointLookup.Should().NotContain(".Where(");
        sharedStylePlanner.Should().NotContain("LastOrDefault");
        sharedStylePlanner.Should().NotContain(".Where(");
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
            DataLabelAngle = 45,
            ShowLegend = false
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
            ShowDataLabelPercentage = true,
            ShowLegend = false
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
}
