using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.App.Presentation.Charts.Selection;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// Per-series trendlines / error bars: layout attaches them to the series that carries them (with that
/// series' own style), and the selection / format-pane / dialog planners address one series.
/// </summary>
public sealed class ChartPerSeriesAdornmentTests
{
    private static readonly PlotRect Inset = new(50, 40, 300, 200);

    private static ChartLayout Layout(ChartType type, Action<ChartModel> configure)
    {
        var chart = Chart(type, configure);
        var request = Request(
            chart,
            ["Q1", "Q2", "Q3", "Q4"],
            [Series(0, "North", 10, 20, 30, 45), Series(1, "South", 5, 15, 25, 20), Series(2, "East", 8, 9, 30, 12)],
            Inset);
        return ChartLayoutEngine.Layout(request);
    }

    [Theory]
    [InlineData(ChartType.Line)]
    [InlineData(ChartType.Column)]
    [InlineData(ChartType.Bar)]
    public void Trendline_isAttachedOnlyToTheSeriesThatCarriesIt_withItsOwnStyle(ChartType type)
    {
        var layout = Layout(type, c =>
        {
            c.ShowLinearTrendline = true;
            c.SeriesTrendlines =
            [
                new ChartSeriesTrendline { SeriesIndex = 1, Type = ChartTrendlineType.Polynomial, Order = 3, Thickness = 4 },
            ];
        });

        layout.Series[0].Trendline.Should().BeNull();
        layout.Series[2].Trendline.Should().BeNull();
        var trendline = layout.Series[1].Trendline.Should().NotBeNull().And.Subject.As<TrendlineLayout>();
        trendline.Fit.Should().Be(TrendlineFitKind.Polynomial);
        trendline.Style!.TrendlineThickness.Should().Be(4);
    }

    [Fact]
    public void Trendline_chartWideFlag_stillMeansEverySeries()
    {
        var layout = Layout(ChartType.Line, c => c.ShowLinearTrendline = true);

        layout.Series.Should().OnlyContain(s => s.Trendline != null);
    }

    [Fact]
    public void Trendline_onScatter_usesEachSeriesOwnPoints()
    {
        var chart = Chart(ChartType.Scatter, c =>
        {
            c.ShowLinearTrendline = true;
            c.SeriesTrendlines = [new ChartSeriesTrendline { SeriesIndex = 1 }];
        });
        var request = Request(
            chart,
            ["a", "b", "c"],
            [ScatterSeries(0, "S0", [1, 2, 3], 1, 2, 3), ScatterSeries(1, "S1", [1, 2, 3], 3, 2, 1)],
            Inset);

        var layout = ChartLayoutEngine.Layout(request);

        layout.Series[0].Trendline.Should().BeNull();
        layout.Series[1].Trendline.Should().NotBeNull();
    }

    [Fact]
    public void ErrorBars_areAttachedOnlyToTheirSeries_withTheirOwnSettings()
    {
        var layout = Layout(ChartType.Column, c =>
        {
            c.ShowErrorBars = true;
            c.SeriesErrorBars =
            [
                new ChartSeriesErrorBars { SeriesIndex = 2, Kind = ChartErrorBarKind.FixedValue, Value = 3, EndCaps = false, Thickness = 2 },
            ];
        });

        layout.Series[0].ErrorBars.Should().BeNull();
        layout.Series[1].ErrorBars.Should().BeNull();
        var bars = layout.Series[2].ErrorBars.Should().NotBeNull().And.Subject.As<ErrorBarLayout>();
        bars.EndCaps.Should().BeFalse();
        bars.Style!.ErrorBarThickness.Should().Be(2);
    }

    [Fact]
    public void ErrorBars_chartWideFlag_stillMeansEverySeries()
    {
        var layout = Layout(ChartType.Column, c => c.ShowErrorBars = true);

        layout.Series.Should().OnlyContain(s => s.ErrorBars != null);
    }

    [Fact]
    public void ErrorBars_onBar_andScatter_areAlsoPerSeries()
    {
        var bar = Layout(ChartType.Bar, c =>
        {
            c.ShowErrorBars = true;
            c.SeriesErrorBars = [new ChartSeriesErrorBars { SeriesIndex = 0 }];
        });
        bar.Series[0].ErrorBars.Should().NotBeNull();
        bar.Series[1].ErrorBars.Should().BeNull();

        var scatterChart = Chart(ChartType.Scatter, c =>
        {
            c.ShowErrorBars = true;
            c.SeriesErrorBars = [new ChartSeriesErrorBars { SeriesIndex = 1, AxisDirection = ChartErrorBarAxisDirection.X }];
        });
        var scatter = ChartLayoutEngine.Layout(Request(
            scatterChart,
            ["a", "b", "c"],
            [ScatterSeries(0, "S0", [1, 2, 3], 1, 2, 3), ScatterSeries(1, "S1", [1, 2, 3], 3, 2, 1)],
            Inset));
        scatter.Series[0].ErrorBars.Should().BeNull();
        scatter.Series[1].ErrorBars.Should().NotBeNull();
    }

    // ---- selection / delete -----------------------------------------------------------------

    [Fact]
    public void DeleteTrendline_targetsOnlyTheSelectedSeries()
    {
        var chart = Chart(ChartType.Line, c =>
        {
            c.ShowLinearTrendline = true;
            c.SeriesTrendlines = [new ChartSeriesTrendline { SeriesIndex = 0 }, new ChartSeriesTrendline { SeriesIndex = 1 }];
        });

        var plan = ChartElementCommandPlanner.PlanDelete(chart, ChartElementRef.ForTrendline(1));

        plan.Action.Should().Be(ChartElementDeleteAction.ApplyLayout);
        plan.Options!.ShowLinearTrendline.Should().BeFalse();
        plan.Options.TrendlineTargetSeriesIndex.Should().Be(1);
        // A series without one has nothing to delete.
        ChartElementCommandPlanner.PlanDelete(chart, ChartElementRef.ForTrendline(2)).Action
            .Should().Be(ChartElementDeleteAction.None);
    }

    [Fact]
    public void DeleteErrorBars_targetsOnlyTheSelectedSeries()
    {
        var chart = Chart(ChartType.Column, c =>
        {
            c.ShowErrorBars = true;
            c.SeriesErrorBars = [new ChartSeriesErrorBars { SeriesIndex = 1 }];
        });

        var plan = ChartElementCommandPlanner.PlanDelete(chart, ChartElementRef.ForErrorBars(1));

        plan.Options!.ErrorBarTargetSeriesIndex.Should().Be(1);
        ChartElementCommandPlanner.PlanDelete(chart, ChartElementRef.ForErrorBars(0)).Action
            .Should().Be(ChartElementDeleteAction.None);
    }

    [Fact]
    public void DeleteOnADataPoint_removesItsWholeSeries_likeExcel()
    {
        // Verified against Excel via COM: Selection.Delete on a selected point (Point.Delete) removes the
        // point's entire series from the chart (the worksheet cells stay); only the point's DATA LABEL can
        // be deleted on its own.
        var chart = Chart(ChartType.Column);

        var plan = ChartElementCommandPlanner.PlanDelete(chart, ChartElementRef.ForDataPoint(1, 2));

        plan.Action.Should().Be(ChartElementDeleteAction.RemoveSeries);
        plan.SeriesOrdinal.Should().Be(1);
    }

    // ---- format pane / dialogs --------------------------------------------------------------

    [Fact]
    public void FormatPane_showsAndEditsTheSelectedSeriesTrendline()
    {
        var chart = Chart(ChartType.Line, c =>
        {
            c.ShowLinearTrendline = true;
            c.SeriesTrendlines =
            [
                new ChartSeriesTrendline { SeriesIndex = 0 },
                new ChartSeriesTrendline { SeriesIndex = 1, ShowEquation = true },
            ];
        });

        var pane = ChartFormatPanePlanner.Build(chart, ChartElementRef.ForTrendline(1), "South");
        pane.Rows.Single(r => r.Id == ChartFormatPanePlanner.RowTrendlineEquation).Value.Should().Be("true");
        ChartFormatPanePlanner.Build(chart, ChartElementRef.ForTrendline(0), "North")
            .Rows.Single(r => r.Id == ChartFormatPanePlanner.RowTrendlineEquation).Value.Should().Be("false");

        var edit = ChartFormatPanePlanner.Plan(chart, ChartElementRef.ForTrendline(0), ChartFormatPanePlanner.RowTrendlineRSquared, "true");
        edit!.ShowTrendlineRSquared.Should().BeTrue();
        edit.TrendlineTargetSeriesIndex.Should().Be(0);
    }

    [Fact]
    public void TrendlineDialogPlanner_readsAndPlansPerSeries()
    {
        var chart = Chart(ChartType.Line, c =>
        {
            c.ShowLinearTrendline = true;
            c.SeriesTrendlines = [new ChartSeriesTrendline { SeriesIndex = 1, Type = ChartTrendlineType.Logarithmic }];
        });

        var withTrend = ChartTrendlinePlanner.Read(chart, 1);
        withTrend.ShowTrendline.Should().BeTrue();
        withTrend.Type.Should().Be(ChartTrendlineType.Logarithmic);
        ChartTrendlinePlanner.Read(chart, 0).ShowTrendline.Should().BeFalse();
        ChartTrendlinePlanner.Read(chart, null).ShowTrendline.Should().BeTrue();

        ChartTrendlinePlanner.Plan(withTrend, 1).TrendlineTargetSeriesIndex.Should().Be(1);
        ChartTrendlinePlanner.Plan(withTrend, null).TrendlineTargetSeriesIndex.Should().BeNull();
    }

    [Fact]
    public void ErrorBarsDialogPlanner_readsAndPlansPerSeries()
    {
        var chart = Chart(ChartType.Column, c =>
        {
            c.ShowErrorBars = true;
            c.SeriesErrorBars = [new ChartSeriesErrorBars { SeriesIndex = 2, Kind = ChartErrorBarKind.Percentage, Value = 7 }];
        });

        var withBars = ChartErrorBarsPlanner.Read(chart, 2);
        withBars.ShowErrorBars.Should().BeTrue();
        withBars.Kind.Should().Be(ChartErrorBarKind.Percentage);
        ChartErrorBarsPlanner.Read(chart, 0).ShowErrorBars.Should().BeFalse();

        ChartErrorBarsPlanner.Plan(withBars, 2).ErrorBarTargetSeriesIndex.Should().Be(2);
    }
}
