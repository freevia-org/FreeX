using System.Reflection;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

/// <summary>
/// Tests the embedded-numCache fallback for column/bar charts whose series reference
/// a cross-sheet range (e.g. a chart on "10 Charts" whose val formula is
/// '4. Dynamic Histogram'!$B$31:$B$32).  When the live ChartDataCells do not include the
/// referenced sheet's cells (the normal case for cross-sheet refs), the renderer must fall
/// back to the numCache values stored in <see cref="ChartModel.EmbeddedSeriesData"/>.
/// </summary>
public sealed partial class ChartRendererTests
{
    /// <summary>
    /// A column chart with EmbeddedSeriesData (cross-sheet numCache) and NO matching live cells
    /// should render N bars from the embedded cache values rather than producing a blank chart.
    /// Regression guard for chart5/chart6 in 10-Advanced-Excel-Charts.xlsx.
    /// </summary>
    [Fact]
    public void ColumnRenderer_CrossSheetRef_WithEmbeddedNumCache_RendersFromCache_WhenLiveCellsEmpty()
    {
        // Simulate: chart hosted on "10 Charts" (chartSheetId), series val formula references
        // "4. Dynamic Histogram" (dataSheetId) rows 31-32.  The viewport provides NO cells for
        // dataSheetId, so live-cell resolution yields nothing.  The chart XML carries numCache
        // values 6 and 4 in EmbeddedSeriesData — these must be used to produce 2 bars.
        var chartSheetId = SheetId.New();
        var dataSheetId = SheetId.New();

        // DataRange points at data sheet (correctly resolved by reader's sheetNameResolver)
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(
                new CellAddress(dataSheetId, 31, 2),
                new CellAddress(dataSheetId, 32, 2)),
            // EmbeddedSeriesData populated from numCache (new reader behavior for cross-sheet refs)
            EmbeddedSeriesData =
            [
                new ChartEmbeddedSeriesData(
                    SeriesIndex: 0,
                    SeriesName: null,
                    Categories: [],
                    Values: [6.0, 4.0])
            ]
        };

        // Viewport has NO cells from dataSheetId — only chartSheetId cells visible
        var viewport = new ViewportModel(
            [Cell(1, 1, "Visible on chart sheet")],
            [],
            []);

        var model = BuildPlotModel(chart, viewport);

        model.Should().NotBeNull("EmbeddedSeriesData must be used when live cells are absent");
        var series = model!.Series.Should().ContainSingle().Which.Should().BeOfType<RectangleBarSeries>().Subject;
        series.Items.Should().HaveCount(2, "2 cached values → 2 bars");
        Math.Max(series.Items[0].Y0, series.Items[0].Y1)
            .Should().BeApproximately(6.0, 0.001, "first cached value is 6");
        Math.Max(series.Items[1].Y0, series.Items[1].Y1)
            .Should().BeApproximately(4.0, 0.001, "second cached value is 4");
    }

    /// <summary>
    /// A column chart with EmbeddedSeriesData but WITH matching live cells should prefer
    /// the live cells (not the cached values).  This ensures edits remain reactive.
    /// </summary>
    [Fact]
    public void ColumnRenderer_CrossSheetRef_WithEmbeddedNumCache_PrefersLiveCellsWhenPresent()
    {
        var dataSheetId = SheetId.New();

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(
                new CellAddress(dataSheetId, 1, 1),
                new CellAddress(dataSheetId, 2, 1)),
            FirstRowIsHeader = false,
            FirstColIsCategories = false,
            // EmbeddedSeriesData has stale cache values 6 and 4
            EmbeddedSeriesData =
            [
                new ChartEmbeddedSeriesData(
                    SeriesIndex: 0,
                    SeriesName: null,
                    Categories: [],
                    Values: [6.0, 4.0])
            ]
        };

        // Viewport provides LIVE cells from dataSheetId with DIFFERENT values (99 and 77)
        var viewport = new ViewportModel(
            [],
            [],
            [],
            ChartDataCells:
            [
                ChartCell(dataSheetId, 1, 1, "99", new NumberValue(99)),
                ChartCell(dataSheetId, 2, 1, "77", new NumberValue(77))
            ]);

        var model = BuildPlotModel(chart, viewport);

        model.Should().NotBeNull();
        var series = model!.Series.Should().ContainSingle().Which.Should().BeOfType<RectangleBarSeries>().Subject;
        series.Items.Should().HaveCount(2);
        Math.Max(series.Items[0].Y0, series.Items[0].Y1)
            .Should().BeApproximately(99.0, 0.001, "live cell value 99 should take priority over cached 6");
        Math.Max(series.Items[1].Y0, series.Items[1].Y1)
            .Should().BeApproximately(77.0, 0.001, "live cell value 77 should take priority over cached 4");
    }

    /// <summary>
    /// A column chart with N=5 cached values (chart6/NN=06 shape) renders 5 bars.
    /// </summary>
    [Fact]
    public void ColumnRenderer_CrossSheetRef_FiveValueNumCache_RendersFiveBars()
    {
        var chartSheetId = SheetId.New();
        var dataSheetId = SheetId.New();

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(
                new CellAddress(dataSheetId, 31, 3),
                new CellAddress(dataSheetId, 35, 3)),
            EmbeddedSeriesData =
            [
                new ChartEmbeddedSeriesData(
                    SeriesIndex: 0,
                    SeriesName: null,
                    Categories: [],
                    Values: [4.0, 6.0, 3.0, 8.0, 5.0])
            ]
        };

        var viewport = new ViewportModel([], [], []);

        var model = BuildPlotModel(chart, viewport);

        model.Should().NotBeNull();
        var series = model!.Series.Should().ContainSingle().Which.Should().BeOfType<RectangleBarSeries>().Subject;
        series.Items.Should().HaveCount(5, "5 cached values → 5 bars");
        Math.Max(series.Items[0].Y0, series.Items[0].Y1).Should().BeApproximately(4.0, 0.001);
        Math.Max(series.Items[2].Y0, series.Items[2].Y1).Should().BeApproximately(3.0, 0.001);
        Math.Max(series.Items[4].Y0, series.Items[4].Y1).Should().BeApproximately(5.0, 0.001);
    }
}
