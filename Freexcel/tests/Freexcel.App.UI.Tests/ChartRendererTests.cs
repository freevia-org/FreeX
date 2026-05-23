using System.Reflection;
using FluentAssertions;
using Freexcel.App.UI;
using Freexcel.Core.Model;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;

namespace Freexcel.App.UI.Tests;

public sealed class ChartRendererTests
{
    [Theory]
    [InlineData(ChartType.Surface)]
    [InlineData(ChartType.Treemap)]
    [InlineData(ChartType.Sunburst)]
    [InlineData(ChartType.Histogram)]
    [InlineData(ChartType.Pareto)]
    [InlineData(ChartType.BoxAndWhisker)]
    [InlineData(ChartType.Waterfall)]
    [InlineData(ChartType.Funnel)]
    [InlineData(ChartType.Map)]
    public void ChartRenderer_DoesNotRenderDeferredAdvancedChartFamiliesAsLineCharts(ChartType type)
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = type,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
        };

        var model = BuildNullablePlotModel(chart, new ViewportModel(
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

        model.Should().BeNull();
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
    public void PieRenderer_AnglesInsideDataLabelsWhenRotationIsRequested()
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

        model.Series.Should().ContainSingle().Which.Should().BeOfType<PieSeries>().Subject
            .AreInsideLabelsAngled.Should().BeTrue();
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
}
