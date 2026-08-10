using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class ChartSeriesColumnPolicyTests
{
    [Fact]
    public void AuthoritativeMappings_ControlColumnsAndPreserveAuthoredSeriesIndexes()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            SeriesColumnMappings =
            [
                new ChartSeriesColumnMapping(9, 4),
                new ChartSeriesColumnMapping(5, 2),
            ],
        };

        ChartSeriesColumnPolicy.HasAuthoritativeMappings(chart, 2, 4).Should().BeTrue();
        ChartSeriesColumnPolicy.ShouldUseSourceColumn(chart, 2, 2, 4).Should().BeTrue();
        ChartSeriesColumnPolicy.ShouldUseSourceColumn(chart, 3, 2, 4).Should().BeFalse();
        ChartSeriesColumnPolicy.ResolveSeriesIndex(chart, 4, 2, 4).Should().Be(9);
        ChartSeriesColumnPolicy.GetCurrentSeriesColumns(chart, 2, 4).Should().Equal(
            new ChartSeriesColumn(5, 2),
            new ChartSeriesColumn(9, 4));
    }

    [Fact]
    public void OutOfSpanMappings_FallBackToPositionalColumnSeries()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Line,
            SeriesColumnMappings = [new ChartSeriesColumnMapping(7, 99)],
        };

        ChartSeriesColumnPolicy.HasAuthoritativeMappings(chart, 2, 4).Should().BeFalse();
        ChartSeriesColumnPolicy.GetCurrentSeriesColumns(chart, 2, 4).Should().Equal(
            new ChartSeriesColumn(0, 2),
            new ChartSeriesColumn(1, 3),
            new ChartSeriesColumn(2, 4));
    }

    [Fact]
    public void RowMajorCharts_IgnoreColumnMappingsAndKeepPositionalFallback()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            SeriesInRows = true,
            SeriesColumnMappings = [new ChartSeriesColumnMapping(7, 3)],
        };

        ChartSeriesColumnPolicy.HasAuthoritativeMappings(chart, 2, 4).Should().BeFalse();
        ChartSeriesColumnPolicy.ResolveSeriesIndex(chart, 4, 2, 4).Should().Be(2);
    }

    [Fact]
    public void ScatterCharts_SkipSharedXColumnAndIndexRemainingColumns()
    {
        var chart = new ChartModel { Type = ChartType.Scatter, FirstColIsCategories = false };

        ChartSeriesColumnPolicy.ShouldUseSourceColumn(chart, 2, 2, 4).Should().BeFalse();
        ChartSeriesColumnPolicy.ResolveSeriesIndex(chart, 3, 2, 4).Should().Be(0);
        ChartSeriesColumnPolicy.ResolveSeriesIndex(chart, 4, 2, 4).Should().Be(1);
    }

    [Fact]
    public void BubbleCharts_MapEachValueSizePairToOneSeriesIndex()
    {
        var chart = new ChartModel { Type = ChartType.Bubble };

        ChartSeriesColumnPolicy.ResolveSeriesIndex(chart, 3, 3, 6).Should().Be(0);
        ChartSeriesColumnPolicy.ResolveSeriesIndex(chart, 4, 3, 6).Should().Be(0);
        ChartSeriesColumnPolicy.ResolveSeriesIndex(chart, 5, 3, 6).Should().Be(1);
        ChartSeriesColumnPolicy.ResolveSeriesIndex(chart, 6, 3, 6).Should().Be(1);
    }
}
