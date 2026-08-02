using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R117-io-chart-embedded-bubble-size-1 / R117-presentation-chart-stock-ohlc-1.
/// <para>
/// Bubble: <see cref="ChartModel.EmbeddedSeriesData"/> gained a <c>SizeValues</c> field this round
/// (previously <see cref="ChartEmbeddedSeriesData"/> only carried Categories/Values, so a
/// fallback-loaded Bubble chart rendered at the correct X/Y positions but every bubble at the
/// uniform default/minimum radius -- see the R115 note this round's model change references). The
/// WPF renderer fix is in <c>ChartRenderer.cs</c>'s <c>BuildEmbeddedCellLookup</c>, which now
/// populates the reserved trailing size column from <c>SizeValues</c> instead of always leaving it
/// empty.
/// </para>
/// <para>
/// Stock: the reader already captured each of Open/High/Low/Close correctly as separate
/// <see cref="ChartEmbeddedSeriesData"/> list entries (one per classic &lt;c:ser&gt;, in that fixed
/// OOXML document order), and <c>BuildEmbeddedCellLookup</c> already lays out one synthesized column
/// per list entry in that same order -- exactly the column layout <c>ChartRenderer.Stock.cs</c>'s
/// <c>BuildStockModel</c> already expects (see its <c>openCol</c>/<c>highCol</c>/<c>lowCol</c>/
/// <c>closeCol</c> offset math). So the WPF renderer needed NO change for Stock: this test is a
/// CONFIRMATION that the existing embedded-fallback substitution already renders Stock correctly (it
/// passes both before and after this round's changes) -- the real Stock bug this round fixes lives
/// entirely in the portable <c>ChartLayoutRequestBuilder</c>/<c>ChartLayoutEngine</c> path (see
/// FreeX.App.Presentation.Tests's ChartLayoutRequestBuilderTests/StockLayoutTests R117 additions for
/// the fail-before/pass-after evidence).
/// </para>
/// </summary>
public sealed partial class ChartRendererTests
{
    [Fact]
    public void BubbleRenderer_EmbeddedFallback_RendersRealPointSizes_WhenSizeValuesCached()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bubble,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1)),
            EmbeddedSeriesData =
            [
                new ChartEmbeddedSeriesData(
                    0, "Deals",
                    Categories: ["1", "2"],
                    Values: [5.0, 8.0],
                    SizeValues: [10.0, 40.0])
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel([], [], []));

        model.Should().NotBeNull();
        var series = model!.Series.Should().ContainSingle().Which.Should().BeOfType<ScatterSeries>().Subject;
        series.Points.Should().HaveCount(2);

        // THE FIX: pre-fix, the reserved size column was always empty (SizeValues didn't exist on
        // ChartEmbeddedSeriesData), so BuildBubbleModel's maxSize scan found nothing, every rawSize
        // defaulted to 1, and BubbleRadius(1, maxSize<=0, ...) returned the uniform MinBubbleRadius
        // (1.0) for every point -- both points below would have had IDENTICAL sizes. With the cached
        // sizes [10, 40] now recovered, the smaller cached size must produce a strictly smaller
        // rendered radius than the larger one.
        series.Points[0].Size.Should().BeLessThan(series.Points[1].Size,
            "THE BUG: without SizeValues both points render at the same uniform default radius (1.0)");
        series.Points[0].Size.Should().NotBe(1.0,
            "a real cached size must not fall back to the uniform default/minimum radius");
    }

    [Fact]
    public void BubbleRenderer_EmbeddedFallback_WithoutSizeValues_StillRendersUniformDefaultSize()
    {
        // Sibling no-regression: a Bubble chart whose source XML genuinely had no bubbleSize numCache
        // (SizeValues stays null) must keep the pre-existing uniform-default-radius behavior, not
        // throw or otherwise misbehave.
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bubble,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1)),
            EmbeddedSeriesData =
            [
                new ChartEmbeddedSeriesData(0, "Deals", Categories: ["1", "2"], Values: [5.0, 8.0])
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel([], [], []));

        model.Should().NotBeNull();
        var series = model!.Series.Should().ContainSingle().Which.Should().BeOfType<ScatterSeries>().Subject;
        series.Points.Should().HaveCount(2);
        series.Points[0].Size.Should().Be(series.Points[1].Size, "no cached size data means every bubble keeps the uniform default radius");
    }

    [Fact]
    public void StockRenderer_EmbeddedFallback_RendersRealOhlc_WhenLiveCellsEmpty()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Stock,
            StockSubtype = StockChartSubtype.OpenHighLowClose,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1)),
            EmbeddedSeriesData =
            [
                new ChartEmbeddedSeriesData(0, "Open", Categories: ["D1", "D2"], Values: [101.0, 121.0]),
                new ChartEmbeddedSeriesData(1, "High", Categories: ["D1", "D2"], Values: [108.0, 128.0]),
                new ChartEmbeddedSeriesData(2, "Low", Categories: ["D1", "D2"], Values: [98.0, 118.0]),
                new ChartEmbeddedSeriesData(3, "Close", Categories: ["D1", "D2"], Values: [106.0, 126.0])
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel([], [], []));

        model.Should().NotBeNull();
        var series = model!.Series.Should().ContainSingle().Which.Should().BeOfType<HighLowSeries>().Subject;
        series.Items.Should().HaveCount(2, "2 cached OHLC rows must produce 2 stock bars, not a blank chart");
        series.Items[0].High.Should().Be(108.0);
        series.Items[0].Low.Should().Be(98.0);
        series.Items[0].Open.Should().Be(101.0);
        series.Items[0].Close.Should().Be(106.0);
        series.Items[1].High.Should().Be(128.0);
        series.Items[1].Low.Should().Be(118.0);
        series.Items[1].Open.Should().Be(121.0);
        series.Items[1].Close.Should().Be(126.0);
    }

    [Fact]
    public void StockRenderer_EmbeddedFallback_HighLowCloseWithoutOpen_UsesCloseAsOpen()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Stock,
            StockSubtype = StockChartSubtype.HighLowClose,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1)),
            EmbeddedSeriesData =
            [
                new ChartEmbeddedSeriesData(0, "High", Categories: ["D1"], Values: [20.0]),
                new ChartEmbeddedSeriesData(1, "Low", Categories: ["D1"], Values: [10.0]),
                new ChartEmbeddedSeriesData(2, "Close", Categories: ["D1"], Values: [15.0])
            ]
        };

        var model = BuildPlotModel(chart, new ViewportModel([], [], []));

        model.Should().NotBeNull();
        var series = model!.Series.Should().ContainSingle().Which.Should().BeOfType<HighLowSeries>().Subject;
        series.Items.Should().ContainSingle().Which.Open.Should().Be(15.0, "HighLowClose (3-column) has no Open series, so Open falls back to Close");
    }

    // ---------------------------------------------------------------------------------------------
    // Sibling no-regression: an ordinary cell-range Stock chart still renders identically.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void StockRenderer_OrdinaryCellRangeChart_UnaffectedByEmbeddedFallbackFix()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Stock,
            StockSubtype = StockChartSubtype.OpenHighLowClose,
            FirstRowIsHeader = false,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 5))
        };

        // GetStockXValues (ChartRenderer.Stock.cs) needs at least one category label to plot any
        // point at all (an empty category list produces zero x-values, so the row loop breaks
        // immediately) -- column 1 is the category (date) column, columns 2-5 are Open/High/Low/Close.
        var viewport = new ViewportModel(
        [
            Cell(1, 1, "2026-01-02"),
            Cell(1, 2, "101"),
            Cell(1, 3, "108"),
            Cell(1, 4, "98"),
            Cell(1, 5, "106")
        ], [], []);

        var model = BuildPlotModel(chart, viewport);

        model.Should().NotBeNull();
        var series = model!.Series.Should().ContainSingle().Which.Should().BeOfType<HighLowSeries>().Subject;
        var item = series.Items.Should().ContainSingle().Subject;
        item.Open.Should().Be(101.0);
        item.High.Should().Be(108.0);
        item.Low.Should().Be(98.0);
        item.Close.Should().Be(106.0);
    }
}
