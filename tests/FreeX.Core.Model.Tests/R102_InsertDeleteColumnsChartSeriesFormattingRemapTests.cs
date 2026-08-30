using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R102: inserting or deleting a whole column STRICTLY INSIDE a chart's plotted <see
/// cref="ChartModel.DataRange"/> mis-attributed every SeriesIndex-keyed per-series/per-point
/// override (<see cref="ChartModel.SeriesFormats"/>, <see cref="ChartModel.PointFillColors"/>,
/// <see cref="ChartModel.TrendlineSeriesIndex"/>, etc.) to the wrong series. Before this fix,
/// <c>InsertColumnsCommand</c>/<c>DeleteColumnsCommand</c> only shifted <see
/// cref="ChartModel.DataRange"/> and (when populated) <see cref="ChartModel.SeriesColumnMappings"/>
/// — exactly like <see cref="RemoveChartSeriesCommand"/> was hardened against for a single removed
/// series (see R96_ChartSeriesFormatReindexTests), but a plain whole-column Insert/Delete never
/// took that path. In the common case (no explicit <see cref="ChartModel.SeriesColumnMappings"/> —
/// every freshly-created FreeX chart, and any Excel-authored chart with no column gaps), the
/// renderer/writer derive a series' index purely from its ORDINAL POSITION among the columns
/// inside DataRange, so inserting/deleting a column in the middle re-numbers every series after it.
/// </summary>
public sealed class R102_InsertDeleteColumnsChartSeriesFormattingRemapTests
{
    // A1:D10 with the default FirstColIsCategories=true: column A is categories, columns B/C/D
    // (2/3/4) are the three plotted series at SeriesIndex 0/1/2 respectively.
    private static GridRange ThreeSeriesRange(Sheet sheet) =>
        new(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 4));

    private static (Sheet Sheet, TestCommandContext Ctx, ChartModel Chart) CreateThreeSeriesChart()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var chart = new ChartModel
        {
            DataRange = ThreeSeriesRange(sheet),
            Type = ChartType.Column
        };
        sheet.Charts.Add(chart);
        return (sheet, ctx, chart);
    }

    [Fact]
    public void InsertColumn_StrictlyInsideChartRange_RemapsSeriesFormatsAndTrendlineIndex()
    {
        var (sheet, ctx, chart) = CreateThreeSeriesChart();
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0))); // B — red
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 255, 0))); // C — green
        chart.SeriesFormats.Add(new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 0, 255))); // D — blue
        chart.PointFillColors.Add(new ChartPointFillFormat(2, 0, CellColor.FromArgb(9, 9, 9)));
        chart.ShowLinearTrendline = true;
        chart.TrendlineSeriesIndex = 2; // attached to D (blue)

        // Insert one column at C (before the old column 3) — strictly between the first series
        // column (B=2) and the last (D=4), so it creates a new blank series in the middle instead
        // of merely sliding the whole plotted block.
        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 5)),
            because: "DataRange grows from A1:D10 to A1:E10 (the insert lands inside it)");

        // Old B (SeriesIndex 0) is untouched; old C (SeriesIndex 1, green) physically moved to D
        // and old D (SeriesIndex 2, blue) physically moved to E — so their formatting must move
        // WITH them to SeriesIndex 2 and 3 respectively, leaving the brand-new blank column (now C,
        // SeriesIndex 1) with no format at all, exactly like real Excel attaches nothing to a newly
        // inserted blank series.
        chart.SeriesFormats.Should().BeEquivalentTo(
        [
            new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0)),
            new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 255, 0)), // was SeriesIndex 1
            new ChartSeriesFormat(3, FillColor: CellColor.FromArgb(0, 0, 255))  // was SeriesIndex 2
        ]);
        chart.PointFillColors.Should().ContainSingle().Which.SeriesIndex.Should().Be(3); // was 2
        chart.TrendlineSeriesIndex.Should().Be(3, because: "the trendline must stay attached to the (blue) series it was on, which slid from D to E");
        chart.ShowLinearTrendline.Should().BeTrue();
    }

    [Fact]
    public void InsertColumn_StrictlyInsideChartRange_IsUndoable()
    {
        var (sheet, ctx, chart) = CreateThreeSeriesChart();
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 255, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 0, 255)));
        chart.TrendlineSeriesIndex = 2;
        chart.ShowLinearTrendline = true;
        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 1);

        cmd.Apply(ctx).Success.Should().BeTrue();
        chart.TrendlineSeriesIndex.Should().Be(3);

        cmd.Revert(ctx);

        chart.DataRange.Should().Be(ThreeSeriesRange(sheet));
        chart.SeriesFormats.Select(f => (f.SeriesIndex, f.FillColor)).Should().BeEquivalentTo(
        [
            (0, CellColor.FromArgb(255, 0, 0)),
            (1, CellColor.FromArgb(0, 255, 0)),
            (2, CellColor.FromArgb(0, 0, 255))
        ]);
        chart.TrendlineSeriesIndex.Should().Be(2, because: "undo must restore the pre-insert trendline attachment");
        chart.ShowLinearTrendline.Should().BeTrue();
    }

    [Fact]
    public void DeleteColumn_StrictlyInsideChartRange_DropsRemovedSeriesFormatAndShiftsSurvivors()
    {
        var (sheet, ctx, chart) = CreateThreeSeriesChart();
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0))); // B — red
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 255, 0))); // C — green
        chart.SeriesFormats.Add(new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 0, 255))); // D — blue

        // Delete column C (SeriesIndex 1, the middle series) — its own worksheet column is gone.
        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 3)),
            because: "DataRange shrinks from A1:D10 to A1:C10");

        chart.SeriesFormats.Should().BeEquivalentTo(
        [
            new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0)),
            new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 0, 255)) // old D, was SeriesIndex 2
        ], because: "the removed column's own (green) format must be dropped, and the surviving " +
                    "(blue) series that slid left from D to C must keep ITS OWN format at its new position");
    }

    [Fact]
    public void DeleteColumn_StrictlyInsideChartRange_IsUndoable()
    {
        var (sheet, ctx, chart) = CreateThreeSeriesChart();
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 255, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 0, 255)));
        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 1);

        cmd.Apply(ctx).Success.Should().BeTrue();
        chart.SeriesFormats.Should().HaveCount(2);

        cmd.Revert(ctx);

        chart.DataRange.Should().Be(ThreeSeriesRange(sheet));
        chart.SeriesFormats.Select(f => (f.SeriesIndex, f.FillColor)).Should().BeEquivalentTo(
        [
            (0, CellColor.FromArgb(255, 0, 0)),
            (1, CellColor.FromArgb(0, 255, 0)),
            (2, CellColor.FromArgb(0, 0, 255))
        ], because: "undo must restore the deleted-away series' own format alongside the DataRange");
    }

    [Fact]
    public void InsertColumn_BeforeTheWholeChartRange_LeavesSeriesFormatsUntouched()
    {
        // Sibling/no-regression case: an insert that lands AT OR BEFORE the whole DataRange shifts
        // the ENTIRE plotted block uniformly (no new series slot is created inside it), so every
        // series keeps its own already-correct SeriesIndex and nothing here should be remapped.
        var (sheet, ctx, chart) = CreateThreeSeriesChart();
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 255, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 0, 255)));

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 1, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 10, 5)),
            because: "the whole chart range slides right by one column");
        chart.SeriesFormats.Select(f => (f.SeriesIndex, f.FillColor)).Should().BeEquivalentTo(
        [
            (0, CellColor.FromArgb(255, 0, 0)),
            (1, CellColor.FromArgb(0, 255, 0)),
            (2, CellColor.FromArgb(0, 0, 255))
        ], because: "every series' relative position inside the (uniformly shifted) range is unchanged");
    }

    [Fact]
    public void InsertColumn_StrictlyInsideChartRange_WithAuthoritativeSeriesColumnMappings_LeavesSeriesFormatsUntouched()
    {
        // Sibling/no-regression case: when SeriesColumnMappings is populated and authoritative, a
        // series' SeriesIndex is the column-INDEPENDENT chart-XML idx (ChartSeriesColumnMapping
        // .SeriesXmlIndex) -- already correctly preserved by ShiftChartSeriesColumnMappingsUp, which
        // only moves each mapping's absolute ValueColumn, never its SeriesXmlIndex. The new R102 fix
        // must recognise this and do nothing, or it would double up and corrupt an already-correct case.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var originalDataRange = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 10, 5));
        var chart = new ChartModel
        {
            DataRange = originalDataRange,
            Type = ChartType.Column,
            SeriesColumnMappings =
            [
                new ChartSeriesColumnMapping(SeriesXmlIndex: 0, ValueColumn: 2), // B
                new ChartSeriesColumnMapping(SeriesXmlIndex: 1, ValueColumn: 4), // D
                new ChartSeriesColumnMapping(SeriesXmlIndex: 2, ValueColumn: 5)  // E
            ]
        };
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(255, 0, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(0, 255, 0)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(0, 0, 255)));
        sheet.Charts.Add(chart);

        // Insert strictly inside the mapped range (before D, absolute column 4).
        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 4, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.SeriesFormats.Select(f => (f.SeriesIndex, f.FillColor)).Should().BeEquivalentTo(
        [
            (0, CellColor.FromArgb(255, 0, 0)),
            (1, CellColor.FromArgb(0, 255, 0)),
            (2, CellColor.FromArgb(0, 0, 255))
        ], because: "SeriesIndex here is the column-independent chart-XML idx, which the insert never changes");
    }

    // ── R102 follow-up: 6 SeriesIndex-keyed collections RemoveChartSeriesCommand treats as
    // SeriesIndex-keyed (MultiLevelCategoryXml, ExplodedSlices, RangeDataLabels,
    // SeriesRangeDataLabels, AdditionalSeriesErrorBarsXml, AdditionalSeriesTrendlinesXml) that the
    // column-axis remap above did NOT yet cover -- mirrors the row-axis sibling's coverage of the
    // same set (R102_InsertDeleteRowsChartSeriesFormattingRemapTests
    // .InsertRow_StrictlyInsideChartRange_RemapsExtendedSeriesIndexKeyedCollections). All entries
    // below are seeded on SeriesIndex 2 (the LAST series, column D) whose position after any of
    // these edits is easy to miscompute by accident (0 -> shifts to 3 on insert, or would
    // ambiguously equal the deleted index on a naive off-by-one) -- so an unremapped/mis-remapped
    // entry is guaranteed detectable, not masked by a coincidental identity mapping.

    [Fact]
    public void InsertColumn_StrictlyInsideChartRange_RemapsExtendedSeriesIndexKeyedCollections()
    {
        var (sheet, ctx, chart) = CreateThreeSeriesChart();
        chart.MultiLevelCategoryXml.Add(new ChartSeriesRawXmlEntry(2, "<c:cat>blue</c:cat>"));
        chart.ExplodedSlices.Add(new ChartPointExplosion(2, 0, 0.25));
        chart.RangeDataLabels.Add(new ChartRangeDataLabel(2, 0, "blue-label"));
        chart.SeriesRangeDataLabels.Add(new ChartSeriesRangeDataLabels(2, "Sheet1!$F$1:$F$1", 1, []));
        chart.AdditionalSeriesErrorBarsXml.Add(new ChartSeriesRawXmlEntry(2, "<c:errBars/>"));
        chart.AdditionalSeriesTrendlinesXml.Add(new ChartSeriesRawXmlEntry(2, "<c:trendline/>"));

        // Insert one column at C (before the old column 3) -- strictly inside the plotted range, so
        // old D (SeriesIndex 2, blue) physically moves to E (SeriesIndex 3).
        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.MultiLevelCategoryXml.Should().ContainSingle().Which.SeriesIndex.Should().Be(3);
        chart.ExplodedSlices.Should().ContainSingle().Which.SeriesIndex.Should().Be(3);
        chart.RangeDataLabels.Should().ContainSingle().Which.SeriesIndex.Should().Be(3);
        chart.SeriesRangeDataLabels.Should().ContainSingle().Which.SeriesIndex.Should().Be(3);
        chart.AdditionalSeriesErrorBarsXml.Should().ContainSingle().Which.SeriesIndex.Should().Be(3);
        chart.AdditionalSeriesTrendlinesXml.Should().ContainSingle().Which.SeriesIndex.Should().Be(3);
    }

    [Fact]
    public void InsertColumn_StrictlyInsideChartRange_ExtendedCollections_IsUndoable()
    {
        var (sheet, ctx, chart) = CreateThreeSeriesChart();
        chart.MultiLevelCategoryXml.Add(new ChartSeriesRawXmlEntry(2, "<c:cat>blue</c:cat>"));
        chart.ExplodedSlices.Add(new ChartPointExplosion(2, 0, 0.25));
        chart.RangeDataLabels.Add(new ChartRangeDataLabel(2, 0, "blue-label"));
        chart.SeriesRangeDataLabels.Add(new ChartSeriesRangeDataLabels(2, "Sheet1!$F$1:$F$1", 1, []));
        chart.AdditionalSeriesErrorBarsXml.Add(new ChartSeriesRawXmlEntry(2, "<c:errBars/>"));
        chart.AdditionalSeriesTrendlinesXml.Add(new ChartSeriesRawXmlEntry(2, "<c:trendline/>"));
        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 1);

        cmd.Apply(ctx).Success.Should().BeTrue();
        chart.MultiLevelCategoryXml.Should().ContainSingle().Which.SeriesIndex.Should().Be(3);

        cmd.Revert(ctx);

        chart.DataRange.Should().Be(ThreeSeriesRange(sheet));
        chart.MultiLevelCategoryXml.Should().ContainSingle().Which.SeriesIndex.Should().Be(2,
            because: "undo must restore the pre-insert MultiLevelCategoryXml SeriesIndex too");
        chart.ExplodedSlices.Should().ContainSingle().Which.SeriesIndex.Should().Be(2);
        chart.RangeDataLabels.Should().ContainSingle().Which.SeriesIndex.Should().Be(2);
        chart.SeriesRangeDataLabels.Should().ContainSingle().Which.SeriesIndex.Should().Be(2);
        chart.AdditionalSeriesErrorBarsXml.Should().ContainSingle().Which.SeriesIndex.Should().Be(2);
        chart.AdditionalSeriesTrendlinesXml.Should().ContainSingle().Which.SeriesIndex.Should().Be(2);
    }

    [Fact]
    public void InsertColumn_BeforeFirstPlottedSeries_ExtendedCollectionsStayAttachedToTheirSeries()
    {
        // Rule-4 enumeration case: insert BEFORE the first plotted series column (B, col 2) but
        // still strictly inside DataRange (A1:D10 starts at col 1 for categories) -- every existing
        // series slides right by one, so a SeriesIndex-keyed entry must move WITH its series (not
        // stay at the same index, which would silently reattach it to a different series).
        var (sheet, ctx, chart) = CreateThreeSeriesChart();
        chart.RangeDataLabels.Add(new ChartRangeDataLabel(0, 0, "red-label")); // attached to B (first series)
        chart.AdditionalSeriesTrendlinesXml.Add(new ChartSeriesRawXmlEntry(0, "<c:trendline/>"));

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 2, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.RangeDataLabels.Should().ContainSingle().Which.SeriesIndex.Should().Be(1,
            because: "the first series' own column physically moved from B to C, so its label must move with it");
        chart.AdditionalSeriesTrendlinesXml.Should().ContainSingle().Which.SeriesIndex.Should().Be(1);
    }

    [Fact]
    public void DeleteColumn_StrictlyInsideChartRange_ExtendedCollections_DropsRemovedAndShiftsSurvivor()
    {
        var (sheet, ctx, chart) = CreateThreeSeriesChart();
        // Entry on the series being deleted (SeriesIndex 1, column C) must be dropped; entry on the
        // series that slides left afterwards (SeriesIndex 2, column D -> C) must shift down to 1 --
        // seeding BOTH in the same test makes a fixture that only "happens to" leave index 2 alone
        // (a masking bug) immediately visible as a leftover/duplicate entry.
        chart.MultiLevelCategoryXml.Add(new ChartSeriesRawXmlEntry(1, "<c:cat>green-deleted</c:cat>"));
        chart.ExplodedSlices.Add(new ChartPointExplosion(1, 0, 0.10));
        chart.RangeDataLabels.Add(new ChartRangeDataLabel(2, 0, "blue-label"));
        chart.SeriesRangeDataLabels.Add(new ChartSeriesRangeDataLabels(2, "Sheet1!$F$1:$F$1", 1, []));
        chart.AdditionalSeriesErrorBarsXml.Add(new ChartSeriesRawXmlEntry(2, "<c:errBars/>"));
        chart.AdditionalSeriesTrendlinesXml.Add(new ChartSeriesRawXmlEntry(2, "<c:trendline/>"));

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.MultiLevelCategoryXml.Should().BeEmpty(because: "its own (deleted, green) column is gone");
        chart.ExplodedSlices.Should().BeEmpty(because: "its own (deleted, green) column is gone");
        chart.RangeDataLabels.Should().ContainSingle().Which.SeriesIndex.Should().Be(1, because: "old D (blue) slid left to C");
        chart.SeriesRangeDataLabels.Should().ContainSingle().Which.SeriesIndex.Should().Be(1);
        chart.AdditionalSeriesErrorBarsXml.Should().ContainSingle().Which.SeriesIndex.Should().Be(1);
        chart.AdditionalSeriesTrendlinesXml.Should().ContainSingle().Which.SeriesIndex.Should().Be(1);
    }

    [Fact]
    public void DeleteColumn_StrictlyInsideChartRange_ExtendedCollections_IsUndoable()
    {
        var (sheet, ctx, chart) = CreateThreeSeriesChart();
        chart.MultiLevelCategoryXml.Add(new ChartSeriesRawXmlEntry(1, "<c:cat>green-deleted</c:cat>"));
        chart.RangeDataLabels.Add(new ChartRangeDataLabel(2, 0, "blue-label"));
        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 1);

        cmd.Apply(ctx).Success.Should().BeTrue();
        chart.MultiLevelCategoryXml.Should().BeEmpty();
        chart.RangeDataLabels.Should().ContainSingle().Which.SeriesIndex.Should().Be(1);

        cmd.Revert(ctx);

        chart.DataRange.Should().Be(ThreeSeriesRange(sheet));
        chart.MultiLevelCategoryXml.Should().ContainSingle().Which.SeriesIndex.Should().Be(1,
            because: "undo must restore the deleted-away series' own MultiLevelCategoryXml entry");
        chart.RangeDataLabels.Should().ContainSingle().Which.SeriesIndex.Should().Be(2,
            because: "undo must restore the surviving series' pre-delete SeriesIndex");
    }

    [Fact]
    public void InsertColumn_StrictlyInsideChartRange_WhenSeriesInRowsIsTrue_LeavesExtendedCollectionsUntouched()
    {
        // Rule-4 enumeration case: a Switch-Row/Column chart's series axis is ROWS, not columns --
        // the column-insert/delete path must leave every SeriesIndex-keyed collection untouched
        // (only the row-axis remap may touch it for this chart). Guards the
        // `if (chart.SeriesInRows ...) return;` early-exit in RemapChartSeriesFormattingForColumnInsert.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var chart = new ChartModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4)),
            Type = ChartType.Column,
            SeriesInRows = true
        };
        chart.MultiLevelCategoryXml.Add(new ChartSeriesRawXmlEntry(1, "<c:cat>x</c:cat>"));
        chart.RangeDataLabels.Add(new ChartRangeDataLabel(2, 0, "x-label"));
        sheet.Charts.Add(chart);

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.MultiLevelCategoryXml.Should().ContainSingle().Which.SeriesIndex.Should().Be(1,
            because: "columns are the category axis here, not the series axis -- a column insert must never remap SeriesIndex for this chart");
        chart.RangeDataLabels.Should().ContainSingle().Which.SeriesIndex.Should().Be(2);
    }

    [Fact]
    public void SeriesInsert_UnaffectedCollectionsRetainTheirExactReferences()
    {
        var (sheet, _, chart) = CreateThreeSeriesChart();
        var first = new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(1, 2, 3));
        var second = new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(4, 5, 6));
        chart.SeriesFormats.AddRange([first, second]);
        var formats = chart.SeriesFormats;
        var emptyIndexes = chart.ComboLineSeriesIndexes;

        RowColumnShiftHelpers.RemapChartSeriesFormattingForColumnInsert(chart, sheet.Id, start: 4, count: 1);

        chart.SeriesFormats.Should().BeSameAs(formats);
        chart.SeriesFormats[0].Should().BeSameAs(first);
        chart.SeriesFormats[1].Should().BeSameAs(second);
        chart.ComboLineSeriesIndexes.Should().BeSameAs(emptyIndexes);
    }

    [Fact]
    public void SeriesDelete_ChangedCollectionReplacesWithoutMutatingRetainedAlias()
    {
        var (sheet, _, chart) = CreateThreeSeriesChart();
        var first = new ChartSeriesFormat(0, FillColor: CellColor.FromArgb(1, 2, 3));
        var removed = new ChartSeriesFormat(1, FillColor: CellColor.FromArgb(4, 5, 6));
        var shifted = new ChartSeriesFormat(2, FillColor: CellColor.FromArgb(7, 8, 9));
        chart.SeriesFormats.AddRange([first, removed, shifted]);
        var original = chart.SeriesFormats;

        RowColumnShiftHelpers.RemapChartSeriesFormattingForColumnDelete(chart, sheet.Id, start: 3, count: 1);

        chart.SeriesFormats.Should().NotBeSameAs(original);
        chart.SeriesFormats.Select(format => format.SeriesIndex).Should().Equal(0, 1);
        chart.SeriesFormats[0].Should().BeSameAs(first);
        original.Select(format => format.SeriesIndex).Should().Equal(0, 1, 2);
        original[2].Should().BeSameAs(shifted);
    }

    [Fact]
    public void SeriesRemap_SharedCollectionDoesNotLeakIntoAnotherChart()
    {
        var (sheet, _, chart) = CreateThreeSeriesChart();
        var shared = new List<ChartSeriesFormat>
        {
            new(0, FillColor: CellColor.FromArgb(1, 2, 3)),
            new(2, FillColor: CellColor.FromArgb(4, 5, 6))
        };
        chart.SeriesFormats = shared;
        var otherChart = new ChartModel { SeriesFormats = shared };

        RowColumnShiftHelpers.RemapChartSeriesFormattingForColumnInsert(chart, sheet.Id, start: 3, count: 1);

        chart.SeriesFormats.Should().NotBeSameAs(shared);
        chart.SeriesFormats.Select(format => format.SeriesIndex).Should().Equal(0, 3);
        otherChart.SeriesFormats.Should().BeSameAs(shared);
        shared.Select(format => format.SeriesIndex).Should().Equal(0, 2);
    }
}
