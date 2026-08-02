using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Text;
using FreeX.Core.Model;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R117-io-chart-embedded-bubble-size-1 / R117-presentation-chart-stock-ohlc-1.
/// <para>
/// Bubble: <see cref="ChartLayoutRequestBuilder.BuildFromEmbeddedData"/> now forwards a Bubble
/// series' cached <see cref="ChartEmbeddedSeriesData.SizeValues"/> onto the resulting
/// <see cref="ChartSeriesData.SizeValues"/> (previously always null -- see the R115 note the r117
/// model change references -- so <c>ChartLayoutEngine.LayoutBubble</c> always fell back to its
/// default/minimum bubble radius for a fallback-loaded chart).
/// </para>
/// <para>
/// Stock: this is the layer where the ACTUAL bug lived. <c>ChartLayoutRequestBuilder.TryBuild</c>
/// routed Stock to the generic <c>ExtractSeries</c> (one <see cref="ChartSeriesData"/> PER COLUMN,
/// only <c>Values</c> set) for a live cell-range chart, and <c>BuildFromEmbeddedData</c> did the
/// same per-embedded-series-entry thing for the named-range fallback -- but
/// <c>ChartLayoutEngine.LayoutStock</c> reads a SINGLE series' <c>HighValues</c>/<c>LowValues</c>/
/// <c>OpenValues</c>. Neither path ever populated those fields, so EVERY Stock chart through the
/// portable engine -- live-range or fallback -- rendered zero <c>StockElements</c> (a completely
/// blank chart). <c>ExtractStockSeries</c> (live) and <c>BuildStockRequestFromEmbeddedData</c>
/// (fallback) now both merge the per-column/per-dimension data into that single shape.
/// </para>
/// </summary>
public sealed class R117_ChartLayoutRequestBuilderBubbleSizeAndStockOhlcTests
{
    private sealed class FakeTextMeasurer : ITextMeasurer
    {
        public TextSize Measure(string? text, string? fontFamily, double fontSize, bool bold, bool italic) =>
            string.IsNullOrEmpty(text) ? TextSize.Empty : new TextSize(text.Length * fontSize * 0.5, fontSize);
    }

    private static readonly PlotRect Plot = new(8, 12, 360, 240);

    private static bool NeverCalled(uint row, uint col, out double value, out string displayText)
    {
        value = 0;
        displayText = "";
        return false;
    }

    // -----------------------------------------------------------------------------------------
    // Bubble: embedded-fallback SizeValues must reach ChartSeriesData.SizeValues.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public void TryBuild_BubbleEmbeddedFallback_ForwardsSizeValuesFromEmbeddedCache()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Bubble,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(default, 1, 1), new CellAddress(default, 1, 1)),
            EmbeddedSeriesData =
            [
                new ChartEmbeddedSeriesData(
                    0, "Deals",
                    Categories: ["1", "2", "3"],
                    Values: [5.0, 8.0, 3.0],
                    SizeValues: [40.0, 65.0, 90.0])
            ]
        };

        var request = ChartLayoutRequestBuilder.TryBuild(chart, Plot, NeverCalled, new FakeTextMeasurer());

        request.Should().NotBeNull();
        var series = request!.Series.Should().ContainSingle().Subject;
        series.Values.Should().Equal(5.0, 8.0, 3.0);
        // THE FIX: pre-fix, BuildFromEmbeddedData never set SizeValues at all (see the R115 note this
        // round's ChartEmbeddedSeriesData change references), so ChartLayoutEngine.LayoutBubble always
        // used its uncached default/minimum radius for every point.
        series.SizeValues.Should().NotBeNull(
            "THE BUG: SizeValues was never forwarded from the embedded cache pre-fix");
        series.SizeValues.Should().Equal(40.0, 65.0, 90.0);
    }

    [Fact]
    public void TryBuild_BubbleEmbeddedFallback_WithoutSizeValues_LeavesSizeValuesNull()
    {
        // Sibling no-regression: a Bubble chart whose source XML had no bubbleSize cache at all.
        var chart = new ChartModel
        {
            Type = ChartType.Bubble,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(default, 1, 1), new CellAddress(default, 1, 1)),
            EmbeddedSeriesData =
            [
                new ChartEmbeddedSeriesData(0, "Deals", Categories: ["1", "2"], Values: [5.0, 8.0])
            ]
        };

        var request = ChartLayoutRequestBuilder.TryBuild(chart, Plot, NeverCalled, new FakeTextMeasurer());

        request.Should().NotBeNull();
        request!.Series.Should().ContainSingle().Which.SizeValues.Should().BeNull();
    }

    // -----------------------------------------------------------------------------------------
    // Stock: embedded-fallback per-dimension series list must merge into one HighValues/LowValues/
    // OpenValues/Values-shaped ChartSeriesData.
    // -----------------------------------------------------------------------------------------
    [Fact]
    public void TryBuild_StockEmbeddedFallback_MergesOpenHighLowCloseIntoSingleSeries()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Stock,
            StockSubtype = StockChartSubtype.OpenHighLowClose,
            DataRange = new GridRange(new CellAddress(default, 1, 1), new CellAddress(default, 1, 1)),
            EmbeddedSeriesData =
            [
                new ChartEmbeddedSeriesData(0, "Open", ["D1", "D2"], [101.0, 121.0]),
                new ChartEmbeddedSeriesData(1, "High", ["D1", "D2"], [108.0, 128.0]),
                new ChartEmbeddedSeriesData(2, "Low", ["D1", "D2"], [98.0, 118.0]),
                new ChartEmbeddedSeriesData(3, "Close", ["D1", "D2"], [106.0, 126.0]),
            ]
        };

        var request = ChartLayoutRequestBuilder.TryBuild(chart, Plot, NeverCalled, new FakeTextMeasurer());

        request.Should().NotBeNull();
        // THE BUG: pre-fix this fell through to the generic per-embedded-series-entry loop, so
        // request.Series would have had 4 entries (one per dimension), each with only Values set and
        // HighValues/LowValues/OpenValues all null -- ChartLayoutEngine.LayoutStock's
        // `if (highs is null || lows is null)` guard would hit for every one of them, producing zero
        // StockElements (a completely blank chart).
        var series = request!.Series.Should().ContainSingle(
            "the four per-dimension embedded series must merge into ONE stock series, not one per dimension")
            .Subject;
        series.Values.Should().Equal(new double?[] { 106.0, 126.0 }, "Values must carry Close");
        series.HighValues.Should().NotBeNull().And.Equal(108.0, 128.0);
        series.LowValues.Should().NotBeNull().And.Equal(98.0, 118.0);
        series.OpenValues.Should().NotBeNull().And.Equal(101.0, 121.0);
    }

    [Fact]
    public void TryBuild_StockEmbeddedFallback_HighLowCloseWithoutOpen_LeavesOpenValuesNull()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Stock,
            StockSubtype = StockChartSubtype.HighLowClose,
            DataRange = new GridRange(new CellAddress(default, 1, 1), new CellAddress(default, 1, 1)),
            EmbeddedSeriesData =
            [
                new ChartEmbeddedSeriesData(0, "High", ["D1"], [20.0]),
                new ChartEmbeddedSeriesData(1, "Low", ["D1"], [10.0]),
                new ChartEmbeddedSeriesData(2, "Close", ["D1"], [15.0]),
            ]
        };

        var request = ChartLayoutRequestBuilder.TryBuild(chart, Plot, NeverCalled, new FakeTextMeasurer());

        request.Should().NotBeNull();
        var series = request!.Series.Should().ContainSingle().Subject;
        series.Values.Should().Equal(15.0);
        series.HighValues.Should().Equal(20.0);
        series.LowValues.Should().Equal(10.0);
        series.OpenValues.Should().BeNull("HighLowClose has no Open dimension");
    }

    // -----------------------------------------------------------------------------------------
    // Stock: an ORDINARY (live cell-range) chart must ALSO produce the merged single series --
    // this was broken independently of the embedded-fallback feature (ExtractSeries, the generic
    // per-column extractor, was used for Stock too and never set HighValues/LowValues/OpenValues).
    // -----------------------------------------------------------------------------------------
    [Fact]
    public void TryBuild_StockOrdinaryCellRangeChart_MergesOpenHighLowCloseIntoSingleSeries()
    {
        // Columns: 1=Date (category), 2=Open, 3=High, 4=Low, 5=Close.
        var cells = new Dictionary<(uint, uint), (double? Value, string Text)>
        {
            [(1, 1)] = (null, "Date"),
            [(2, 1)] = (null, "D1"),
            [(3, 1)] = (null, "D2"),
            [(1, 2)] = (null, "Open"),
            [(2, 2)] = (101, "101"),
            [(3, 2)] = (121, "121"),
            [(1, 3)] = (null, "High"),
            [(2, 3)] = (108, "108"),
            [(3, 3)] = (128, "128"),
            [(1, 4)] = (null, "Low"),
            [(2, 4)] = (98, "98"),
            [(3, 4)] = (118, "118"),
            [(1, 5)] = (null, "Close"),
            [(2, 5)] = (106, "106"),
            [(3, 5)] = (126, "126"),
        };
        bool Accessor(uint row, uint col, out double value, out string text)
        {
            if (cells.TryGetValue((row, col), out var entry))
            {
                text = entry.Text;
                if (entry.Value is { } v) { value = v; return true; }
            }
            else
            {
                text = "";
            }

            value = 0;
            return false;
        }

        var chart = new ChartModel
        {
            Type = ChartType.Stock,
            StockSubtype = StockChartSubtype.OpenHighLowClose,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(default, 1, 1), new CellAddress(default, 3, 5)),
        };

        var request = ChartLayoutRequestBuilder.TryBuild(chart, Plot, Accessor, new FakeTextMeasurer());

        request.Should().NotBeNull();
        request!.Categories.Should().Equal("D1", "D2");
        var series = request.Series.Should().ContainSingle(
            "an ordinary cell-range Stock chart must ALSO merge its Open/High/Low/Close columns into one series")
            .Subject;
        series.Values.Should().Equal(106.0, 126.0);
        series.HighValues.Should().NotBeNull().And.Equal(108.0, 128.0);
        series.LowValues.Should().NotBeNull().And.Equal(98.0, 118.0);
        series.OpenValues.Should().NotBeNull().And.Equal(101.0, 121.0);
    }

    [Fact]
    public void TryBuild_StockOrdinaryCellRangeChart_HighLowCloseWithoutOpenColumn_LeavesOpenValuesNull()
    {
        // Columns: 1=Date (category), 2=High, 3=Low, 4=Close (no Open column: 3-column HLC subtype).
        var cells = new Dictionary<(uint, uint), (double? Value, string Text)>
        {
            [(1, 1)] = (null, "Date"),
            [(2, 1)] = (null, "D1"),
            [(1, 2)] = (null, "High"),
            [(2, 2)] = (20, "20"),
            [(1, 3)] = (null, "Low"),
            [(2, 3)] = (10, "10"),
            [(1, 4)] = (null, "Close"),
            [(2, 4)] = (15, "15"),
        };
        bool Accessor(uint row, uint col, out double value, out string text)
        {
            if (cells.TryGetValue((row, col), out var entry))
            {
                text = entry.Text;
                if (entry.Value is { } v) { value = v; return true; }
            }
            else
            {
                text = "";
            }

            value = 0;
            return false;
        }

        var chart = new ChartModel
        {
            Type = ChartType.Stock,
            StockSubtype = StockChartSubtype.HighLowClose,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(default, 1, 1), new CellAddress(default, 2, 4)),
        };

        var request = ChartLayoutRequestBuilder.TryBuild(chart, Plot, Accessor, new FakeTextMeasurer());

        request.Should().NotBeNull();
        var series = request!.Series.Should().ContainSingle().Subject;
        series.Values.Should().Equal(15.0);
        series.HighValues.Should().Equal(20.0);
        series.LowValues.Should().Equal(10.0);
        series.OpenValues.Should().BeNull();
    }
}
