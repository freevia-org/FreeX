using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Excel stores trendlines and error bars per series. These tests pin the per-series model semantics:
/// the chart-wide flags still mean "every series" until per-series entries exist, a targeted edit or
/// delete touches one series only, and undo restores the previous per-series state.
/// </summary>
public sealed class ChartPerSeriesAdornmentCommandTests
{
    private static (Workbook Workbook, Sheet Sheet, ChartModel Chart, TestCommandContext Ctx) CreateThreeSeriesChart(
        Action<ChartModel>? configure = null)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 4)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
        };
        configure?.Invoke(chart);
        sheet.Charts.Add(chart);
        return (wb, sheet, chart, ctx);
    }

    [Fact]
    public void ChartWideTrendline_StillMeansEverySeries_UntilPerSeriesEntriesExist()
    {
        var (_, _, chart, _) = CreateThreeSeriesChart(c => c.ShowLinearTrendline = true);

        chart.HasTrendline(0).Should().BeTrue();
        chart.HasTrendline(1).Should().BeTrue();
        chart.HasTrendline(2).Should().BeTrue();
        chart.HasAnyTrendline().Should().BeTrue();
    }

    [Fact]
    public void PerSeriesEntries_AreAuthoritative_AndProjectTheirOwnSettings()
    {
        var (_, _, chart, _) = CreateThreeSeriesChart(c =>
        {
            c.ShowLinearTrendline = true;
            c.SeriesTrendlines =
            [
                new ChartSeriesTrendline { SeriesIndex = 1, Type = ChartTrendlineType.Exponential, ShowEquation = true },
            ];
        });

        chart.HasTrendline(0).Should().BeFalse();
        chart.HasTrendline(2).Should().BeFalse();
        var projection = chart.ResolveTrendline(1)!;
        projection.TrendlineType.Should().Be(ChartTrendlineType.Exponential);
        projection.ShowTrendlineEquation.Should().BeTrue();
        projection.TrendlineSeriesIndex.Should().Be(1);
        // The projection never leaks into the chart itself.
        chart.TrendlineType.Should().Be(ChartTrendlineType.Linear);
    }

    [Fact]
    public void TargetedDelete_OnChartWideTrendline_RemovesOnlyThatSeries()
    {
        var (_, sheet, chart, ctx) = CreateThreeSeriesChart(c => c.ShowLinearTrendline = true);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            chart.Id,
            new ChartLayoutOptions(ShowLinearTrendline: false) { TrendlineTargetSeriesIndex = 1 });

        command.Apply(ctx).Success.Should().BeTrue();

        chart.SeriesTrendlines.Select(t => t.SeriesIndex).Should().Equal(0, 2);
        chart.ShowLinearTrendline.Should().BeTrue();
        chart.HasTrendline(0).Should().BeTrue();
        chart.HasTrendline(1).Should().BeFalse();
        chart.HasTrendline(2).Should().BeTrue();

        command.Revert(ctx);
        chart.SeriesTrendlines.Should().BeEmpty();
        chart.ShowLinearTrendline.Should().BeTrue();
        chart.HasTrendline(1).Should().BeTrue();
    }

    [Fact]
    public void TargetedDelete_OfTheLastTrendline_TurnsTheChartFlagOff()
    {
        var (_, sheet, chart, ctx) = CreateThreeSeriesChart(c =>
        {
            c.ShowLinearTrendline = true;
            c.SeriesTrendlines = [new ChartSeriesTrendline { SeriesIndex = 2 }];
        });

        new SetChartLayoutCommand(
                sheet.Id,
                chart.Id,
                new ChartLayoutOptions(ShowLinearTrendline: false) { TrendlineTargetSeriesIndex = 2 })
            .Apply(ctx).Success.Should().BeTrue();

        chart.SeriesTrendlines.Should().BeEmpty();
        chart.ShowLinearTrendline.Should().BeFalse();
        chart.HasAnyTrendline().Should().BeFalse();
    }

    [Fact]
    public void TargetedEdit_AddsAndFormatsOneSeriesTrendline_WithoutTouchingTheOthers()
    {
        var (_, sheet, chart, ctx) = CreateThreeSeriesChart();

        new SetChartLayoutCommand(
                sheet.Id,
                chart.Id,
                new ChartLayoutOptions(
                    ShowLinearTrendline: true,
                    TrendlineType: ChartTrendlineType.Polynomial,
                    TrendlineOrder: 3,
                    ShowTrendlineRSquared: true) { TrendlineTargetSeriesIndex = 1 })
            .Apply(ctx).Success.Should().BeTrue();

        chart.ShowLinearTrendline.Should().BeTrue();
        var only = chart.SeriesTrendlines.Should().ContainSingle().Subject;
        only.SeriesIndex.Should().Be(1);
        only.Type.Should().Be(ChartTrendlineType.Polynomial);
        only.Order.Should().Be(3);
        only.ShowRSquared.Should().BeTrue();
        chart.HasTrendline(0).Should().BeFalse();

        // A second targeted edit on the same series changes just that series.
        new SetChartLayoutCommand(
                sheet.Id,
                chart.Id,
                new ChartLayoutOptions(ShowTrendlineEquation: true) { TrendlineTargetSeriesIndex = 1 })
            .Apply(ctx).Success.Should().BeTrue();
        chart.SeriesTrendlines.Should().ContainSingle().Which.ShowEquation.Should().BeTrue();
    }

    [Fact]
    public void ChartWideEdit_WithPerSeriesEntries_AppliesToEveryEntry_AndHideClearsThemAll()
    {
        var (_, sheet, chart, ctx) = CreateThreeSeriesChart(c =>
        {
            c.ShowLinearTrendline = true;
            c.SeriesTrendlines =
            [
                new ChartSeriesTrendline { SeriesIndex = 0 },
                new ChartSeriesTrendline { SeriesIndex = 2, Type = ChartTrendlineType.Logarithmic },
            ];
        });

        new SetChartLayoutCommand(sheet.Id, chart.Id, new ChartLayoutOptions(TrendlineThickness: 4))
            .Apply(ctx).Success.Should().BeTrue();
        chart.SeriesTrendlines.Should().OnlyContain(t => t.Thickness == 4);
        chart.SeriesTrendlines.Single(t => t.SeriesIndex == 2).Type.Should().Be(ChartTrendlineType.Logarithmic);

        new SetChartLayoutCommand(sheet.Id, chart.Id, new ChartLayoutOptions(ShowLinearTrendline: false))
            .Apply(ctx).Success.Should().BeTrue();
        chart.SeriesTrendlines.Should().BeEmpty();
        chart.ShowLinearTrendline.Should().BeFalse();
    }

    [Fact]
    public void TargetedDelete_OnErrorBars_RemovesOnlyThatSeries_AndUndoRestores()
    {
        var (_, sheet, chart, ctx) = CreateThreeSeriesChart(c => c.ShowErrorBars = true);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            chart.Id,
            new ChartLayoutOptions(ShowErrorBars: false) { ErrorBarTargetSeriesIndex = 0 });

        command.Apply(ctx).Success.Should().BeTrue();
        chart.SeriesErrorBars.Select(e => e.SeriesIndex).Should().Equal(1, 2);
        chart.HasErrorBars(0).Should().BeFalse();
        chart.HasErrorBars(1).Should().BeTrue();

        command.Revert(ctx);
        chart.SeriesErrorBars.Should().BeEmpty();
        chart.HasErrorBars(0).Should().BeTrue();
    }

    [Fact]
    public void RemovingASeries_RemapsPerSeriesEntries_AndUndoRestores()
    {
        var (_, sheet, chart, ctx) = CreateThreeSeriesChart(c =>
        {
            c.ShowLinearTrendline = true;
            c.ShowErrorBars = true;
            c.SeriesTrendlines =
            [
                new ChartSeriesTrendline { SeriesIndex = 0 },
                new ChartSeriesTrendline { SeriesIndex = 2, Type = ChartTrendlineType.Power },
            ];
            c.SeriesErrorBars = [new ChartSeriesErrorBars { SeriesIndex = 1 }];
        });

        var command = new RemoveChartSeriesCommand(sheet.Id, chart.Id, 1);
        command.Apply(ctx).Success.Should().BeTrue();

        chart.SeriesTrendlines.Select(t => t.SeriesIndex).Should().Equal(0, 1);
        chart.SeriesTrendlines.Single(t => t.SeriesIndex == 1).Type.Should().Be(ChartTrendlineType.Power);
        chart.SeriesErrorBars.Should().BeEmpty();
        chart.ShowErrorBars.Should().BeFalse();
        chart.ShowLinearTrendline.Should().BeTrue();

        command.Revert(ctx);
        chart.SeriesTrendlines.Select(t => t.SeriesIndex).Should().Equal(0, 2);
        chart.SeriesErrorBars.Should().ContainSingle().Which.SeriesIndex.Should().Be(1);
        chart.ShowErrorBars.Should().BeTrue();
    }

    [Fact]
    public void RemovingTheOnlySeriesWithATrendline_TurnsTheFlagOff()
    {
        var (_, sheet, chart, ctx) = CreateThreeSeriesChart(c =>
        {
            c.ShowLinearTrendline = true;
            c.SeriesTrendlines = [new ChartSeriesTrendline { SeriesIndex = 1 }];
        });

        new RemoveChartSeriesCommand(sheet.Id, chart.Id, 1).Apply(ctx).Success.Should().BeTrue();

        chart.SeriesTrendlines.Should().BeEmpty();
        chart.ShowLinearTrendline.Should().BeFalse();
    }
}
