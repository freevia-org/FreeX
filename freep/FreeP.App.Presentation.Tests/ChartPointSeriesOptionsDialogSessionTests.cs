using System.Globalization;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartPointSeriesOptionsDialogSessionTests
{
    [Fact]
    public void PointSession_ClampsRequestedIdentityAndDispatchesCultureAwareOptions()
    {
        var chart = CreateChart();
        var editor = CreateEditor(chart);
        var session = new ChartPointOptionsDialogSession(
            editor,
            initialSeriesIndex: 99,
            initialPointIndex: 99,
            CultureInfo.GetCultureInfo("fr-FR"));
        var state = session.State;

        state.SeriesIndex.Should().Be(1);
        state.PointIndex.Should().Be(2);
        state.SeriesOptions[state.SeriesIndex].Label.Should().Be("Margin");
        state.PointOptions[state.PointIndex].Label.Should().Be("3: Q3");

        var result = session.TryCommit(PointInput(state) with
        {
            FillColorText = "#C00000",
            StrokeColorText = "#1F4E79",
            StrokeWidthText = "1,5",
            UsePointDataLabels = true,
            ShowValueLabels = true,
            ShowCategoryLabels = true,
            ShowLeaderLines = true,
            LabelPositionIndex = session.FindLabelPositionIndex(DataLabelPosition.InsideEnd),
            LabelFontFamily = "Aptos",
            LabelFontSizeText = "9,5",
            LabelBold = true,
            MarkerIndex = session.FindMarkerIndex(ChartMarkerSymbol.Diamond),
            MarkerSizeText = "7,5",
            ExplosionText = "35",
        });

        result.Succeeded.Should().BeTrue();
        result.CommitPlan!.SeriesIndex.Should().Be(1);
        result.CommitPlan.PointIndex.Should().Be(2);
        var style = chart.Series[1].PointStyles[2];
        style.FillColor!.Resolved.Should().Be(SrgbColor.FromRgb(0xC00000));
        style.StrokeColor!.Resolved.Should().Be(SrgbColor.FromRgb(0x1F4E79));
        style.StrokeWidthPt.Should().Be(1.5);
        style.Marker!.Symbol.Should().Be(ChartMarkerSymbol.Diamond);
        style.Marker.SizePt.Should().Be(7.5);
        style.ExplosionPercent.Should().Be(35);
        style.DataLabels!.ShowValue.Should().BeTrue();
        style.DataLabels.ShowCategoryName.Should().BeTrue();
        style.DataLabels.ShowLeaderLines.Should().BeTrue();
        style.DataLabels.TextStyle!.FontSizePt.Should().Be(9.5);
        editor.CanUndo.Should().BeTrue();

        editor.Undo();
        chart.Series[1].PointStyles.Should().NotContainKey(2);
    }

    [Fact]
    public void PointSession_InvalidValueDoesNotDispatch()
    {
        var chart = CreateChart();
        var editor = CreateEditor(chart);
        var session = new ChartPointOptionsDialogSession(editor, culture: CultureInfo.InvariantCulture);

        var result = session.TryCommit(PointInput(session.State) with
        {
            StrokeWidthText = "-1",
        });

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("Outline width must be a non-negative finite number or blank.");
        editor.CanUndo.Should().BeFalse();
        chart.Series[0].PointStyles.Should().BeEmpty();
    }

    [Fact]
    public void SeriesSession_PreservesImportedSubtypeAndRichFillWhileDispatchingOneUndoStep()
    {
        var chart = CreateChart();
        var importedFill = new ShapeFill.Gradient(
            new ThemeAwareColor(SrgbColor.FromRgb(0x112233)),
            new ThemeAwareColor(SrgbColor.FromRgb(0xAABBCC)),
            35);
        chart.Series[1].OverrideChartType = ChartType.ColumnClustered;
        chart.Series[1].Fill = importedFill;
        chart.Series[1].InvertIfNegative = null;
        var editor = CreateEditor(chart);
        var session = new ChartSeriesOptionsDialogSession(
            editor,
            initialSeriesIndex: 99,
            CultureInfo.GetCultureInfo("fr-FR"));
        var state = session.State;

        state.SeriesIndex.Should().Be(1);
        state.SeriesChartTypeOptions[state.SeriesChartTypeIndex].Value
            .Should().Be(ChartType.ColumnClustered);
        state.SeriesChartTypeOptions[state.SeriesChartTypeIndex].Label
            .Should().Contain("imported");
        state.InvertIfNegative.Should().BeNull();

        var result = session.TryCommit(SeriesInput(state) with
        {
            SmoothLine = true,
            LineWidthText = "2,5",
            ErrorBarsEnabled = true,
            ErrorValueText = "12,5",
            TrendlineEnabled = true,
            TrendlineTypeIndex = session.FindTrendlineTypeIndex(ChartTrendlineType.Polynomial),
            TrendlineOrderText = "3",
            TrendlineForwardText = "1,5",
            LabelFontSizeText = "9,5",
        });

        result.Succeeded.Should().BeTrue();
        var series = chart.Series[1];
        series.OverrideChartType.Should().Be(ChartType.ColumnClustered);
        var fill = series.Fill.Should().BeOfType<ShapeFill.Gradient>().Subject;
        fill.AngleDegrees.Should().Be(35);
        series.InvertIfNegative.Should().BeNull();
        series.SmoothLine.Should().BeTrue();
        series.LineStyle!.WidthPt.Should().Be(2.5);
        series.ErrorBars!.Value.Should().Be(12.5);
        series.Trendline!.PolynomialOrder.Should().Be(3);
        series.Trendline.Forward.Should().Be(1.5);
        editor.CanUndo.Should().BeTrue();

        editor.Undo();
        series.SmoothLine.Should().BeNull();
        series.LineStyle.Should().BeNull();
        series.OverrideChartType.Should().Be(ChartType.ColumnClustered);
        series.Fill.Should().BeSameAs(importedFill);
    }

    [Fact]
    public void SeriesSession_InvalidValueDoesNotDispatch()
    {
        var chart = CreateChart();
        var editor = CreateEditor(chart);
        var session = new ChartSeriesOptionsDialogSession(editor, culture: CultureInfo.InvariantCulture);

        var result = session.TryCommit(SeriesInput(session.State) with
        {
            LineWidthText = "NaN",
        });

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("Line width must be a non-negative finite number or blank.");
        editor.CanUndo.Should().BeFalse();
        chart.Series[0].LineStyle.Should().BeNull();
    }

    private static ChartPointOptionsDialogInput PointInput(ChartPointOptionsDialogState state) => new(
        state.SeriesIndex,
        state.PointIndex,
        state.FillColorText,
        state.StrokeColorText,
        state.StrokeWidthText,
        state.UsePointDataLabels,
        state.ShowValueLabels,
        state.ShowPercentLabels,
        state.ShowCategoryLabels,
        state.ShowSeriesLabels,
        state.ShowLegendKeys,
        state.ShowBubbleSize,
        state.ShowLeaderLines,
        state.LabelPositionIndex,
        state.LabelNumberFormat,
        state.LabelSeparator,
        state.LabelFontFamily,
        state.LabelFontSizeText,
        state.LabelBold,
        state.LabelItalic,
        state.LabelColorText,
        state.MarkerIndex,
        state.MarkerSizeText,
        state.ExplosionText);

    private static ChartSeriesOptionsDialogInput SeriesInput(ChartSeriesOptionsDialogState state) => new(
        state.SeriesIndex,
        state.SeriesChartTypeIndex,
        state.SmoothLine,
        state.OnSecondaryAxis,
        state.InvertIfNegative,
        state.LineWidthText,
        state.LineColorText,
        state.LineDashIndex,
        state.NoLine,
        state.FillColorText,
        state.UseSeriesDataLabels,
        state.ShowValueLabels,
        state.ShowPercentLabels,
        state.ShowCategoryLabels,
        state.ShowSeriesLabels,
        state.ShowLegendKeys,
        state.ShowBubbleSize,
        state.ShowLeaderLines,
        state.ErrorBarsEnabled,
        state.ErrorDirectionIndex,
        state.ErrorBarTypeIndex,
        state.ErrorValueTypeIndex,
        state.ErrorValueText,
        state.ErrorNoEndCap,
        state.TrendlineEnabled,
        state.TrendlineTypeIndex,
        state.TrendlineOrderText,
        state.TrendlinePeriodText,
        state.TrendlineForwardText,
        state.TrendlineBackwardText,
        state.TrendlineEquation,
        state.TrendlineRSquared,
        state.LabelPositionIndex,
        state.LabelNumberFormat,
        state.LabelSeparator,
        state.LabelFontFamily,
        state.LabelFontSizeText,
        state.LabelBold,
        state.LabelItalic,
        state.LabelColorText,
        state.MarkerIndex,
        state.MarkerSizeText);

    private static ChartShape CreateChart()
    {
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(["Q1", "Q2", "Q3"]);
        var revenue = new ChartSeries { Name = "Revenue" };
        revenue.Values.Add(10);
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
