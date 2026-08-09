using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartOptionsDialogTestPlanTests
{
    [Fact]
    public void SeriesSettingsBuildCompletePortableValuesAndCommitPlan()
    {
        var session = new ChartSeriesOptionsDialogSession(CreateEditor(CreateChart()));
        var settings = new ChartSeriesOptionsDialogTestSettings(
            1, true, true, 2.25, ChartMarkerSymbol.Diamond, 8,
            "#4472C4", "#1F4E79", OutlineDash.DashDot, true,
            UseSeriesDataLabels: true,
            ShowValueLabels: true,
            ShowCategoryLabels: true,
            LabelPosition: DataLabelPosition.InsideEnd,
            ShowLeaderLines: true,
            Trendline: true,
            TrendlineType: ChartTrendlineType.Polynomial,
            TrendlineOrder: 3,
            OverrideChartType: ChartType.LineMarkers,
            InvertIfNegative: true);

        var values = session.BuildTestValues(settings);
        var options = session.BuildCommitPlanForTests(values);

        values.Fields.Keys.Should().BeEquivalentTo(session.BuildDialogPlan().Fields.Keys);
        options.SeriesIndex.Should().Be(1);
        options.OnSecondaryAxis.Should().BeTrue();
        options.LineDash.Should().Be(OutlineDash.DashDot);
        options.MarkerSymbol.Should().Be(ChartMarkerSymbol.Diamond);
        options.DataLabels!.ShowLeaderLines.Should().BeTrue();
        options.Trendline!.PolynomialOrder.Should().Be(3);
        options.OverrideChartType.Should().Be(ChartType.LineMarkers);
        options.InvertIfNegative.Should().BeTrue();
    }

    [Fact]
    public void PointSettingsBuildCompletePortableValuesAndCommitPlan()
    {
        var session = new ChartPointOptionsDialogSession(CreateEditor(CreateChart()));
        var settings = new ChartPointOptionsDialogTestSettings(
            1, 2, "#C00000", "#1F4E79", 1.5,
            ChartMarkerSymbol.Diamond, 7,
            UsePointDataLabels: true,
            ShowValueLabels: true,
            ShowCategoryLabels: true,
            LabelPosition: DataLabelPosition.InsideEnd,
            ExplosionPercent: 35,
            ShowLeaderLines: true);

        var values = session.BuildTestValues(settings);
        var options = session.BuildCommitPlanForTests(values);

        values.Fields.Keys.Should().BeEquivalentTo(session.BuildDialogPlan().Fields.Keys);
        options.SeriesIndex.Should().Be(1);
        options.PointIndex.Should().Be(2);
        options.StrokeWidthPt.Should().Be(1.5);
        options.MarkerSymbol.Should().Be(ChartMarkerSymbol.Diamond);
        options.ExplosionPercent.Should().Be(35);
        options.DataLabels!.ShowLeaderLines.Should().BeTrue();
    }

    [Fact]
    public void LayoutAreaAndPieSettingsOwnCultureAwareValueConstruction()
    {
        var culture = CultureInfo.GetCultureInfo("fr-FR");

        var layoutSession = new ChartLayoutOptionsDialogSession(CreateEditor(CreateChart()));
        var layout = layoutSession.BuildCommitPlanForTests(layoutSession.BuildTestValues(
            new ChartLayoutOptionsDialogTestSettings(
                ChartLayoutTarget.PlotArea, "inner",
                ChartManualLayoutMode.Edge, ChartManualLayoutMode.Factor,
                ChartManualLayoutMode.Factor, ChartManualLayoutMode.Edge,
                12.5, 0.1, 0.8, 20.5),
            culture), culture);

        var areaSession = new ChartAreaOptionsDialogSession(CreateEditor(CreateChart()));
        var area = areaSession.BuildCommitPlanForTests(areaSession.BuildTestValues(
            new ChartAreaOptionsDialogTestSettings(
                ChartAreaFormattingTarget.PlotArea, "#4472C4", null, null,
                FillTransparency: 40.5),
            culture), culture);

        var pieChart = CreateChart();
        pieChart.ChartType = ChartType.Doughnut;
        var pieSession = new ChartPieOptionsDialogSession(CreateEditor(pieChart));
        var pie = pieSession.BuildCommitPlanForTests(pieSession.BuildTestValues(
            new ChartPieOptionsDialogTestSettings(225, 68), culture), culture);

        layout.Target.Should().Be(ChartLayoutTarget.PlotArea);
        layout.X.Should().Be(12.5);
        layout.Height.Should().Be(20.5);
        area.Target.Should().Be(ChartAreaFormattingTarget.PlotArea);
        area.Fill.Should().BeOfType<ShapeFill.Solid>();
        pie.Should().Be(new ChartPieOptions(225, 68));
    }

    [Fact]
    public void DisplaySettingsOverlayOnlyRequestedSemanticsOnPortableDefaults()
    {
        var chart = CreateChart();
        chart.ChartType = ChartType.Waterfall;
        chart.Title = "Revenue";
        chart.ShowWaterfallConnectorLines = true;
        var session = new ChartDisplayOptionsDialogSession(CreateEditor(chart));

        var values = session.BuildTestValues(new ChartDisplayOptionsDialogTestSettings
        {
            WaterfallConnectorLines = false,
        });
        var options = session.BuildCommitPlanForTests(values);

        values.Fields.Keys.Should().BeEquivalentTo(session.BuildDialogPlan().Fields.Keys);
        options.Title.Should().Be("Revenue");
        options.ShowWaterfallConnectorLines.Should().BeFalse();
    }

    private static ChartShape CreateChart()
    {
        var chart = new ChartShape { ChartType = ChartType.LineMarkers };
        chart.Categories.AddRange(["Q1", "Q2", "Q3"]);
        var revenue = new ChartSeries { Name = "Revenue" };
        revenue.Values.AddRange([10.0, 20.0, 30.0]);
        var margin = new ChartSeries { Name = "Margin" };
        margin.Values.AddRange([1.0, 2.0, 3.0]);
        chart.Series.Add(revenue);
        chart.Series.Add(margin);
        return chart;
    }

    private static EditingSession CreateEditor(ChartShape chart)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 42,
            Name = "Chart",
            Kind = SlideShapeKind.Chart,
            Chart = chart,
        });
        presentation.Slides.Add(slide);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        editor.Select(42);
        return editor;
    }
}
