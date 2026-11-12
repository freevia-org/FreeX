namespace FreeW.Core.Model.Tests;

/// <summary>
/// Chart grouping (clustered / stacked / 100% stacked) and the radar, bubble, stock and combination
/// kinds: defaults that keep existing charts unchanged, kind-shape normalisation, and exact undo.
/// </summary>
public class ChartFamilyModelTests
{
    private sealed class TestCtx(TextDocument doc) : IDocumentCommandContext
    {
        public TextDocument Document => doc;
    }

    private static Chart ThreeSeries(ChartKind kind = ChartKind.Column)
    {
        var chart = new Chart { Kind = kind };
        chart.Categories.AddRange(["Q1", "Q2", "Q3"]);
        chart.Series.Add(new ChartSeries("A", [1, 2, 3]));
        chart.Series.Add(new ChartSeries("B", [4, 5, 6]));
        chart.Series.Add(new ChartSeries("C", [7, 8, 9]));
        return chart;
    }

    private static string Describe(Chart chart) =>
        $"{chart.Kind}/{chart.Grouping}/{chart.RadarFilled}|" + string.Join(";", chart.Series.Select(series =>
            $"{series.Name}:{string.Join(",", series.Values)}:{string.Join(",", series.BubbleSizes)}:{series.PlotAsLine}:{series.UseSecondaryAxis}"));

    [Fact]
    public void NewChart_DefaultsToStandardGrouping_WithNoPerSeriesSettings()
    {
        var chart = Chart.Create(ChartKind.Column, ["A"], [1d]);

        chart.Grouping.Should().Be(ChartGrouping.Standard);
        chart.RadarFilled.Should().BeFalse();
        chart.Series[0].PlotAsLine.Should().BeFalse();
        chart.Series[0].UseSecondaryAxis.Should().BeFalse();
        chart.Series[0].BubbleSizes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ChartKind.Column, true)]
    [InlineData(ChartKind.Bar, true)]
    [InlineData(ChartKind.Line, true)]
    [InlineData(ChartKind.Area, true)]
    [InlineData(ChartKind.Pie, false)]
    [InlineData(ChartKind.Scatter, false)]
    [InlineData(ChartKind.Doughnut, false)]
    [InlineData(ChartKind.Radar, false)]
    [InlineData(ChartKind.Bubble, false)]
    [InlineData(ChartKind.Stock, false)]
    [InlineData(ChartKind.Combo, false)]
    public void Grouping_AppliesOnlyToColumnBarLineAndArea(ChartKind kind, bool supported)
    {
        var chart = new Chart { Kind = kind, Grouping = ChartGrouping.Stacked };

        chart.SupportsGrouping.Should().Be(supported);
        chart.EffectiveGrouping.Should().Be(supported ? ChartGrouping.Stacked : ChartGrouping.Standard);
    }

    [Fact]
    public void Clone_CopiesGroupingAndEveryPerSeriesSetting_Independently()
    {
        var chart = ThreeSeries(ChartKind.Combo);
        chart.Grouping = ChartGrouping.PercentStacked;
        chart.RadarFilled = true;
        chart.Series[2].PlotAsLine = true;
        chart.Series[2].UseSecondaryAxis = true;
        chart.Series[0].BubbleSizes.AddRange([3, 2, 1]);

        var clone = chart.Clone();
        Describe(clone).Should().Be(Describe(chart));

        clone.Series[0].BubbleSizes[0] = 99;
        clone.Series[2].PlotAsLine = false;
        chart.Series[0].BubbleSizes[0].Should().Be(3);
        chart.Series[2].PlotAsLine.Should().BeTrue();
    }

    [Fact]
    public void Stock_WithThreeSeries_IsLeftAlone()
    {
        var chart = ThreeSeries(ChartKind.Stock);
        var before = Describe(chart);

        ChartKindNormalizer.Normalize(chart);

        Describe(chart).Should().Be(before);
        ChartKindNormalizer.IsStockShaped(chart).Should().BeTrue();
    }

    [Fact]
    public void Stock_FromASingleSeries_GetsAHighLowCloseBand()
    {
        var chart = Chart.Create(ChartKind.Stock, ["Mon", "Tue"], [10d, 20d], "Price");

        ChartKindNormalizer.Normalize(chart);

        chart.Series.Select(series => series.Name).Should().Equal("High", "Low", "Close");
        chart.Series[0].Values.Should().Equal(11, 22);
        chart.Series[1].Values.Should().Equal(9, 18);
        chart.Series[2].Values.Should().Equal(10, 20);
        for (var index = 0; index < 2; index++)
        {
            chart.Series[0].Values[index].Should().BeGreaterThanOrEqualTo(chart.Series[2].Values[index]);
            chart.Series[1].Values[index].Should().BeLessThanOrEqualTo(chart.Series[2].Values[index]);
        }
    }

    [Fact]
    public void Stock_FromMoreThanThreeSeries_KeepsTheFirstThree()
    {
        var chart = ThreeSeries(ChartKind.Stock);
        chart.Series.Add(new ChartSeries("D", [0, 0, 0]));

        ChartKindNormalizer.Normalize(chart);

        chart.Series.Select(series => series.Name).Should().Equal("A", "B", "C");
    }

    [Fact]
    public void Combo_AlwaysEndsUpWithAColumnSeriesAndALineSeries()
    {
        var plain = ThreeSeries(ChartKind.Combo);
        ChartKindNormalizer.Normalize(plain);
        plain.Series.Select(series => series.PlotAsLine).Should().Equal(false, false, true);

        var allLines = ThreeSeries(ChartKind.Combo);
        foreach (var series in allLines.Series)
            series.PlotAsLine = true;
        ChartKindNormalizer.Normalize(allLines);
        allLines.Series.Select(series => series.PlotAsLine).Should().Equal(false, true, true);

        var single = Chart.Create(ChartKind.Combo, ["A", "B"], [1d, 2d], "Sales");
        ChartKindNormalizer.Normalize(single);
        single.Series.Should().HaveCount(2);
        single.Series[1].PlotAsLine.Should().BeTrue();
        single.Series[1].Values.Should().Equal(1, 2);
        ChartKindNormalizer.IsComboShaped(single).Should().BeTrue();
    }

    [Fact]
    public void Combo_SecondaryAxisOnlyAppliesToLineSeries()
    {
        var chart = ThreeSeries(ChartKind.Combo);
        chart.Series[0].UseSecondaryAxis = true;
        chart.Series[2].PlotAsLine = true;
        chart.Series[2].UseSecondaryAxis = true;

        ChartKindNormalizer.Normalize(chart);

        chart.Series.Select(series => series.UseSecondaryAxis).Should().Equal(false, false, true);
    }

    [Fact]
    public void Bubble_GetsOneSizePerPoint_DefaultingToTheValueMagnitude()
    {
        var chart = Chart.Create(ChartKind.Bubble, ["1", "2", "3"], [5d, -8d, 0d]);
        chart.Series[0].BubbleSizes.Add(42);

        ChartKindNormalizer.Normalize(chart);

        chart.Series[0].BubbleSizes.Should().Equal(42, 8, 1);
        ChartKindNormalizer.BubbleSizeAt(chart.Series[0], 0).Should().Be(42);
        ChartKindNormalizer.BubbleSizeAt(chart.Series[0], 99).Should().Be(0);
        ChartKindNormalizer.BubbleSizeAt(new ChartSeries("x", [double.NaN]), 0).Should().Be(0);
    }

    [Fact]
    public void LeavingAKind_DropsThatKindsSettings()
    {
        var chart = ThreeSeries(ChartKind.Pie);
        chart.Grouping = ChartGrouping.Stacked;
        chart.RadarFilled = true;
        chart.Series[1].PlotAsLine = true;
        chart.Series[1].UseSecondaryAxis = true;
        chart.Series[0].BubbleSizes.AddRange([1, 2, 3]);

        ChartKindNormalizer.Normalize(chart);

        Describe(chart).Should().Be("Pie/Standard/False|A:1,2,3::False:False;B:4,5,6::False:False;C:7,8,9::False:False");
    }

    [Theory]
    [InlineData(ChartKind.Stock)]
    [InlineData(ChartKind.Combo)]
    [InlineData(ChartKind.Bubble)]
    [InlineData(ChartKind.Radar)]
    [InlineData(ChartKind.Pie)]
    public void SetChartKind_ReshapesTheData_AndUndoRestoresTheChartExactly(ChartKind target)
    {
        var chart = Chart.Create(ChartKind.Column, ["A", "B"], [3d, 4d], "Sales");
        chart.Grouping = ChartGrouping.Stacked;
        var originalSeries = chart.Series[0];
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph { Runs = { Run.FromChart(chart) } });
        var context = new TestCtx(document);
        var before = Describe(chart);
        var command = new SetChartKindCommand(0, 0, target);

        command.HasEffect(context).Should().BeTrue();
        command.Apply(context);

        chart.Kind.Should().Be(target);
        chart.Grouping.Should().Be(ChartGrouping.Standard, "the target kinds cannot stack");
        if (target == ChartKind.Stock)
            ChartKindNormalizer.IsStockShaped(chart).Should().BeTrue();
        if (target == ChartKind.Combo)
            ChartKindNormalizer.IsComboShaped(chart).Should().BeTrue();

        command.Revert(context);

        Describe(chart).Should().Be(before);
        chart.Series.Should().ContainSingle().Which.Should().BeSameAs(originalSeries);
    }

    [Fact]
    public void SetChartKind_BetweenStackableKinds_KeepsTheGrouping()
    {
        var chart = ThreeSeries();
        chart.Grouping = ChartGrouping.PercentStacked;
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph { Runs = { Run.FromChart(chart) } });

        new SetChartKindCommand(0, 0, ChartKind.Area).Apply(new TestCtx(document));

        chart.Kind.Should().Be(ChartKind.Area);
        chart.Grouping.Should().Be(ChartGrouping.PercentStacked);
    }

    [Fact]
    public void ReplaceChartData_CarriesGroupingAndSeriesSettings_AndUndoRestoresThem()
    {
        var chart = ThreeSeries();
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph { Runs = { Run.FromChart(chart) } });
        var context = new TestCtx(document);
        var before = Describe(chart);
        var replacement = ThreeSeries(ChartKind.Combo);
        replacement.Series[2].PlotAsLine = true;
        replacement.Series[2].UseSecondaryAxis = true;
        var command = new ReplaceChartDataCommand(0, 0, replacement);

        command.Apply(context);
        Describe(chart).Should().Be(Describe(replacement));
        chart.Series[2].Should().NotBeSameAs(replacement.Series[2]);

        command.Revert(context);
        Describe(chart).Should().Be(before);
    }
}
