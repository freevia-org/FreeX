using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Text;
using FreeX.Core.Model;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for the UI-free <see cref="ChartLayoutRequestBuilder"/>: series/category extraction from a
/// chart's data range through a fake cell accessor, scatter X-value handling, the PlotRect pass-through, and
/// the unsupported-type guard. No Avalonia or running shell required.
/// </summary>
public sealed class ChartLayoutRequestBuilderTests
{
    /// <summary>A measurer that needs no Avalonia backend: every run is sized proportional to its length.</summary>
    private sealed class FakeTextMeasurer : ITextMeasurer
    {
        public TextSize Measure(string? text, string? fontFamily, double fontSize, bool bold, bool italic) =>
            string.IsNullOrEmpty(text) ? TextSize.Empty : new TextSize(text.Length * fontSize * 0.5, fontSize);
    }

    // Grid data used by most tests:
    //   col 1 (categories): "Region", "North", "South", "East"
    //   col 2 (Sales):      header "Sales", 10, 20, 30
    //   col 3 (Costs):      header "Costs",  5, 15, 25
    private static ChartLayoutRequestBuilder.ChartCellAccessor BuildAccessor()
    {
        var cells = new Dictionary<(uint, uint), (double? Value, string Text)>
        {
            [(1, 1)] = (null, "Region"),
            [(2, 1)] = (null, "North"),
            [(3, 1)] = (null, "South"),
            [(4, 1)] = (null, "East"),
            [(1, 2)] = (null, "Sales"),
            [(2, 2)] = (10, "10"),
            [(3, 2)] = (20, "20"),
            [(4, 2)] = (30, "30"),
            [(1, 3)] = (null, "Costs"),
            [(2, 3)] = (5, "5"),
            [(3, 3)] = (15, "15"),
            [(4, 3)] = (25, "25"),
        };

        return (uint row, uint col, out double value, out string text) =>
        {
            if (cells.TryGetValue((row, col), out var entry))
            {
                text = entry.Text;
                if (entry.Value is { } v)
                {
                    value = v;
                    return true;
                }
            }
            else
            {
                text = "";
            }

            value = 0;
            return false;
        };
    }

    private static ChartModel ColumnChart(uint endCol = 3) => new()
    {
        Type = ChartType.Column,
        FirstRowIsHeader = true,
        FirstColIsCategories = true,
        DataRange = new GridRange(
            new CellAddress(default, 1, 1),
            new CellAddress(default, 4, endCol)),
    };

    private static readonly PlotRect Plot = new(8, 12, 360, 240);

    [Fact]
    public void TryBuild_ExtractsCategoriesAndSeriesFromDataRange()
    {
        var request = ChartLayoutRequestBuilder.TryBuild(ColumnChart(), Plot, BuildAccessor(), new FakeTextMeasurer());

        request.Should().NotBeNull();
        request!.Categories.Should().Equal("North", "South", "East");
        request.Series.Should().HaveCount(2);

        request.Series[0].Name.Should().Be("Sales");
        request.Series[0].SeriesIndex.Should().Be(0);
        request.Series[0].Values.Should().Equal(10d, 20d, 30d);

        request.Series[1].Name.Should().Be("Costs");
        request.Series[1].SeriesIndex.Should().Be(1);
        request.Series[1].Values.Should().Equal(5d, 15d, 25d);
    }

    [Fact]
    public void TryBuild_SwitchRowColumnTransposesSeriesExtraction()
    {
        // Excel's "Switch Row/Column": each ROW becomes one series (named from the first
        // column) and the first row's cells become the category labels.
        var chart = ColumnChart();
        chart.SeriesInRows = true;

        var request = ChartLayoutRequestBuilder.TryBuild(chart, Plot, BuildAccessor(), new FakeTextMeasurer());

        request.Should().NotBeNull();
        request!.Categories.Should().Equal("Sales", "Costs");
        request.Series.Should().HaveCount(3);
        request.Series[0].Name.Should().Be("North");
        request.Series[0].Values.Should().Equal(10d, 5d);
        request.Series[1].Name.Should().Be("South");
        request.Series[1].Values.Should().Equal(20d, 15d);
        request.Series[2].Name.Should().Be("East");
        request.Series[2].Values.Should().Equal(30d, 25d);
    }

    [Fact]
    public void TryBuild_PassesPlotRectThrough()
    {
        var request = ChartLayoutRequestBuilder.TryBuild(ColumnChart(), Plot, BuildAccessor(), new FakeTextMeasurer());

        request.Should().NotBeNull();
        request!.PlotArea.Should().Be(Plot);
    }

    [Fact]
    public void TryBuild_WithoutHeaderRow_SynthesizesSeriesNames()
    {
        var chart = ColumnChart();
        chart.FirstRowIsHeader = false;

        var request = ChartLayoutRequestBuilder.TryBuild(chart, Plot, BuildAccessor(), new FakeTextMeasurer());

        request.Should().NotBeNull();
        // With no header row the first row is data, so each series has 4 points and a synthesized name.
        request!.Series[0].Name.Should().Be("Series 1");
        request.Series[0].Values.Should().HaveCount(4);
    }

    [Fact]
    public void TryBuild_Scatter_UsesFirstColumnAsSharedXValues()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstRowIsHeader = true,
            FirstColIsCategories = false,
            DataRange = new GridRange(
                new CellAddress(default, 1, 1),
                new CellAddress(default, 4, 3)),
        };

        var request = ChartLayoutRequestBuilder.TryBuild(chart, Plot, BuildAccessor(), new FakeTextMeasurer());

        request.Should().NotBeNull();
        // FirstColIsCategories is false, so the X column is the first data column (col 1).
        request!.Series.Should().NotBeEmpty();
        request.Series[0].XValues.Should().NotBeNull();
        request.Series[0].XValues!.Should().HaveCount(3);
    }

    // R115-presentation-chart-bubble-1: Bubble's real Excel data layout is a shared X column
    // followed by one (Y, Size) column pair per series -- column 1 = X, columns 2/3 = series 1's
    // (Y, Size), columns 4/5 = series 2's (Y, Size), etc. -- regardless of FirstColIsCategories.
    // Grid used by the Bubble tests:
    //   col 1 (shared X): 1, 2, 3
    //   col 2 (Series 1 Y):    header "Y1", 10, 20, 30
    //   col 3 (Series 1 Size): header "S1", 100, 200, 300
    //   col 4 (Series 2 Y):    header "Y2", 40, 50, 60
    //   col 5 (Series 2 Size): header "S2", 400, 500, 600
    private static ChartLayoutRequestBuilder.ChartCellAccessor BuildBubbleAccessor()
    {
        var cells = new Dictionary<(uint, uint), (double? Value, string Text)>
        {
            [(1, 1)] = (null, "X"),
            [(2, 1)] = (1, "1"),
            [(3, 1)] = (2, "2"),
            [(4, 1)] = (3, "3"),
            [(1, 2)] = (null, "Y1"),
            [(2, 2)] = (10, "10"),
            [(3, 2)] = (20, "20"),
            [(4, 2)] = (30, "30"),
            [(1, 3)] = (null, "S1"),
            [(2, 3)] = (100, "100"),
            [(3, 3)] = (200, "200"),
            [(4, 3)] = (300, "300"),
            [(1, 4)] = (null, "Y2"),
            [(2, 4)] = (40, "40"),
            [(3, 4)] = (50, "50"),
            [(4, 4)] = (60, "60"),
            [(1, 5)] = (null, "S2"),
            [(2, 5)] = (400, "400"),
            [(3, 5)] = (500, "500"),
            [(4, 5)] = (600, "600"),
        };

        return (uint row, uint col, out double value, out string text) =>
        {
            if (cells.TryGetValue((row, col), out var entry))
            {
                text = entry.Text;
                if (entry.Value is { } v)
                {
                    value = v;
                    return true;
                }
            }
            else
            {
                text = "";
            }

            value = 0;
            return false;
        };
    }

    private static ChartModel BubbleChart(bool firstColIsCategories = false) => new()
    {
        Type = ChartType.Bubble,
        FirstRowIsHeader = true,
        FirstColIsCategories = firstColIsCategories,
        DataRange = new GridRange(
            new CellAddress(default, 1, 1),
            new CellAddress(default, 4, 5)),
    };

    [Fact]
    public void R115_TryBuild_Bubble_PairsSharedXWithEachYSizeColumn()
    {
        var request = ChartLayoutRequestBuilder.TryBuild(BubbleChart(), Plot, BuildBubbleAccessor(), new FakeTextMeasurer());

        request.Should().NotBeNull();
        // Two (Y, Size) column pairs after the shared X column => exactly two series, not four
        // (or five) flat single-value series.
        request!.Series.Should().HaveCount(2);

        var series1 = request.Series[0];
        series1.Name.Should().Be("Y1");
        series1.Values.Should().Equal(10d, 20d, 30d);
        series1.XValues.Should().NotBeNull();
        series1.XValues!.Should().Equal(1d, 2d, 3d);
        series1.SizeValues.Should().NotBeNull();
        series1.SizeValues!.Should().Equal(100d, 200d, 300d);

        var series2 = request.Series[1];
        series2.Name.Should().Be("Y2");
        series2.Values.Should().Equal(40d, 50d, 60d);
        series2.XValues.Should().NotBeNull();
        series2.XValues!.Should().Equal(1d, 2d, 3d);
        series2.SizeValues.Should().NotBeNull();
        series2.SizeValues!.Should().Equal(400d, 500d, 600d);
    }

    [Fact]
    public void R115_TryBuild_Bubble_IgnoresFirstColIsCategoriesForSharedXColumn()
    {
        // Bubble always treats DataRange's first column as the shared X column, even when
        // FirstColIsCategories is set (unlike every other chart type extracted via ExtractSeries).
        var request = ChartLayoutRequestBuilder.TryBuild(
            BubbleChart(firstColIsCategories: true), Plot, BuildBubbleAccessor(), new FakeTextMeasurer());

        request.Should().NotBeNull();
        request!.Series.Should().HaveCount(2);
        request.Series[0].XValues!.Should().Equal(1d, 2d, 3d);
        request.Series[1].XValues!.Should().Equal(1d, 2d, 3d);
    }

    [Fact]
    public void R115_TryBuild_Bubble_NoRegression_ColumnChartStillUsesFlatSeriesExtraction()
    {
        // Sibling-behavior guard: the ordinary (non-Scatter, non-Bubble) family must still go
        // through the plain flat-column ExtractSeries path with no XValues/SizeValues -- adding the
        // Bubble branch must not perturb Column/Line/Area/etc.
        var request = ChartLayoutRequestBuilder.TryBuild(ColumnChart(), Plot, BuildAccessor(), new FakeTextMeasurer());

        request.Should().NotBeNull();
        request!.Series.Should().HaveCount(2);
        request.Series[0].XValues.Should().BeNull();
        request.Series[0].SizeValues.Should().BeNull();
        request.Series[1].XValues.Should().BeNull();
        request.Series[1].SizeValues.Should().BeNull();
    }

    [Fact]
    public void TryBuild_ReturnsNullForUnsupportedChartType()
    {
        var chart = ColumnChart();
        chart.Type = ChartType.Map; // Map is an unsupported type with no portable layout.

        var request = ChartLayoutRequestBuilder.TryBuild(chart, Plot, BuildAccessor(), new FakeTextMeasurer());

        request.Should().BeNull();
    }

    [Fact]
    public void TryBuild_ReturnsNullWhenNoDataRowsAfterHeader()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            // Only a single row/col: after stripping the header row and category col, nothing remains.
            DataRange = new GridRange(
                new CellAddress(default, 1, 1),
                new CellAddress(default, 1, 1)),
        };

        var request = ChartLayoutRequestBuilder.TryBuild(chart, Plot, BuildAccessor(), new FakeTextMeasurer());

        request.Should().BeNull();
    }
}
