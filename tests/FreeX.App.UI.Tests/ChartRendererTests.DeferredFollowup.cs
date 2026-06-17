using System;
using System.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

/// <summary>
/// Tests for the three deferred chart-fidelity follow-ups (see
/// docs/fidelity/2026-06-17-chart-deferred-followup.md):
///   1. Combo legend-entry delete by legend POSITION (not series chart-XML idx).
///   2. Combo band gap width honored so the band reads continuous like Excel.
///   3. Stacked bar built from N single-cell series in one column with no categories
///      (progress-bar idiom) synthesizes N series of one point so it is not blank.
/// </summary>
public sealed partial class ChartRendererTests
{
    // ── Item 1: legend-entry delete uses legend POSITION (declaration order) ────────────────

    [Fact]
    public void ComboLegendEntryDelete_HidesSeriesByLegendPosition_NotChartXmlIndex()
    {
        // Contextures file 04 shape: declaration order T_Low(idx1,colD), Target(idx2,colE),
        // Qty(idx0,colB,line). <c:legendEntry><c:idx val="0"/><c:delete/> means "hide the entry
        // at LEGEND POSITION 0" = the first declared series = T_Low (the transparent spacer),
        // NOT the series whose chart-XML idx is 0 (Qty, the line).
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.StackedColumn,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 5)),
            UseComboLineForSecondarySeries = true,
            ComboLineSeriesIndexes = [0],
            SeriesColumnMappings =
            [
                new ChartSeriesColumnMapping(0, 2), // Qty   -> col B (line)
                new ChartSeriesColumnMapping(1, 4), // T_Low -> col D (spacer)
                new ChartSeriesColumnMapping(2, 5)  // Target-> col E (band)
            ],
            // Declaration order in the chart XML: T_Low(1), Target(2), Qty(0).
            SeriesPlotOrder = [1, 2, 0],
            // Hide legend POSITION 0 -> first declared series -> T_Low (idx 1).
            LegendEntries = [new ChartLegendEntryModel(0, IsDeleted: true)]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Month"),
                Cell(1, 2, "Qty"),
                Cell(1, 3, "Ignored"),
                Cell(1, 4, "T_Low"),
                Cell(1, 5, "Target"),
                Cell(2, 1, "Jan"),
                Cell(2, 2, "150"),
                Cell(2, 3, "999"),
                Cell(2, 4, "250"),
                Cell(2, 5, "100")
            ],
            [],
            []));

        var titles = model.Series.Select(s => s.Title).ToList();
        // T_Low must be hidden (blank title); Qty and Target keep their titles.
        titles.Should().NotContain("T_Low");
        titles.Should().Contain("Qty");
        titles.Should().Contain("Target");
    }

    [Fact]
    public void LegendEntryDelete_WithoutPlotOrder_FallsBackToChartXmlIndex()
    {
        // Regression guard for the legacy bullet-chart helper pattern: when there is no
        // SeriesPlotOrder (plain positional charts), the legendEntry idx is matched directly
        // against the series chart-XML index (= positional column index).
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 4)),
            LegendEntries = [new ChartLegendEntryModel(2, IsDeleted: true)]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Cat"), Cell(1, 2, "Actual"), Cell(1, 3, "Budget"), Cell(1, 4, "Helper"),
                Cell(2, 1, "A"),   Cell(2, 2, "100"),    Cell(2, 3, "200"),    Cell(2, 4, "450"),
                Cell(3, 1, "B"),   Cell(3, 2, "150"),    Cell(3, 3, "250"),    Cell(3, 4, "450")
            ],
            [],
            []));

        var allBar = model.Series.OfType<RectangleBarSeries>().ToList();
        allBar.Should().HaveCount(3);
        allBar[0].Title.Should().Be("Actual");
        allBar[1].Title.Should().Be("Budget");
        allBar[2].Title.Should().BeEmpty("series index 2 legend entry is deleted (positional fallback)");
    }

    // ── Item 2: combo band gap width honored (continuous band like Excel) ──────────────────

    [Fact]
    public void StackedColumnBand_HonorsGapWidthZero_DrawsFullWidthBars()
    {
        // Contextures file 04 band: barChart gapWidth=0 means the band columns should fill the
        // whole category slot (touching neighbors) so the band reads continuous. With the old
        // hardcoded i +/- 0.35 half-width the band drew narrow columns with wide gaps.
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.StackedColumn,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2)),
            BarGapWidth = 0
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Month"), Cell(1, 2, "Band"),
                Cell(2, 1, "Jan"),   Cell(2, 2, "100"),
                Cell(3, 1, "Feb"),   Cell(3, 2, "100"),
                Cell(4, 1, "Mar"),   Cell(4, 2, "100")
            ],
            [],
            []));

        var bar = model.Series.OfType<RectangleBarSeries>().Single();
        bar.Items.Should().NotBeEmpty();
        var first = bar.Items[0];
        // Half-width should be ~0.49 (near full slot), not 0.35.
        var halfWidth = (first.X1 - first.X0) / 2.0;
        halfWidth.Should().BeApproximately(0.49, 1e-6);
    }

    [Fact]
    public void StackedColumn_DefaultGapWidth_KeepsModerateBarWidth()
    {
        // Regression guard: with no explicit gapWidth the stacked columns keep the moderate
        // default half-width (0.35), so ordinary stacked charts are unaffected.
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.StackedColumn,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Month"), Cell(1, 2, "A"),
                Cell(2, 1, "Jan"),   Cell(2, 2, "100"),
                Cell(3, 1, "Feb"),   Cell(3, 2, "100")
            ],
            [],
            []));

        var bar = model.Series.OfType<RectangleBarSeries>().Single();
        var first = bar.Items[0];
        var halfWidth = (first.X1 - first.X0) / 2.0;
        halfWidth.Should().BeApproximately(0.35, 1e-6);
    }

    // ── Item 3: stacked bar of N single-cell series in one column, no categories ────────────

    [Fact]
    public void StackedBar_SingleCellSeriesNoCategories_SynthesizesProgressBar()
    {
        // Contextures/ExcelExamples1 "todo" chart20 shape: 12 stacked-bar series, each a single
        // cell todo!$J$4..$J$15, ptCount=1, NO <c:cat>. The union DataRange is one column J x N
        // rows with 0 categories, which the normal stacked-bar builder skips (blank). The renderer
        // must detect this shape and synthesize N series of one point in ONE category so the bar
        // stacks to the sum (here 0.30 + 0.15 = 0.45 ~ 45%).
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.StackedBar,
            FirstRowIsHeader = false,
            FirstColIsCategories = false,
            // One column (col 10 / J), rows 4..6 (3 single-cell series).
            DataRange = new GridRange(new CellAddress(sheetId, 4, 10), new CellAddress(sheetId, 6, 10)),
            SeriesColumnMappings =
            [
                new ChartSeriesColumnMapping(0, 10),
                new ChartSeriesColumnMapping(1, 10),
                new ChartSeriesColumnMapping(2, 10)
            ],
            SeriesPlotOrder = [0, 1, 2]
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                NumericCell(4, 10, 0.30, "0.30"),
                NumericCell(5, 10, 0.15, "0.15"),
                NumericCell(6, 10, 0.00, "0.00")
            ],
            [],
            []));

        var bars = model.Series.OfType<RectangleBarSeries>().ToList();
        // One stacked series per single-cell source (3), not collapsed to a single series.
        bars.Should().HaveCount(3);

        // Each contributes exactly one rectangle in the single category (index 0).
        bars.Where(b => b.Items.Count > 0).Should().HaveCountGreaterThanOrEqualTo(2);

        // The stacked extent must reach ~0.45 (the sum of the non-zero series).
        var maxExtent = bars.SelectMany(b => b.Items).Select(it => Math.Max(it.X0, it.X1)).DefaultIfEmpty(0).Max();
        maxExtent.Should().BeApproximately(0.45, 1e-9);
    }

    [Fact]
    public void StackedBar_NormalMultiColumn_NotTreatedAsSingleCellSynthesis()
    {
        // Regression guard: a normal multi-series stacked bar (2 columns, categories present) must
        // keep its standard per-column rendering and not trigger the single-cell synthesis path.
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.StackedBar,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Cat"), Cell(1, 2, "A"), Cell(1, 3, "B"),
                Cell(2, 1, "X"),   Cell(2, 2, "10"), Cell(2, 3, "20"),
                Cell(3, 1, "Y"),   Cell(3, 2, "30"), Cell(3, 3, "40")
            ],
            [],
            []));

        var bars = model.Series.OfType<RectangleBarSeries>().ToList();
        bars.Should().HaveCount(2, "two source columns -> two stacked series");
        // Each series has two categories (X, Y).
        bars.Should().OnlyContain(b => b.Items.Count == 2);
    }
}
