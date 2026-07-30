using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R99: <see cref="RemoveChartSeriesCommand"/> and <see cref="ChangeChartSourceCommand"/> remap every
/// OTHER SeriesIndex-keyed collection when a series is removed / the source changes, but used to leave
/// <see cref="ChartModel.SeriesPlotOrder"/> (declaration-order idx list) and
/// <see cref="ChartModel.LegendEntries"/> (legend-POSITION-keyed hide/format overrides, resolved
/// through <see cref="ChartModel.SeriesPlotOrder"/> by
/// <c>ChartRenderer.SeriesFormatting.cs</c>'s <c>IsLegendEntryDeleted</c>) stale. That silently
/// un-hid or mis-hid legend keys after a series edit. See
/// tests/FreeX.App.UI.Tests/ChartRendererTests.DeferredFollowup.cs's
/// ComboLegendEntryDelete_HidesSeriesByLegendPosition_NotChartXmlIndex for the renderer-side
/// consumption this producer-side fix must feed correctly.
/// </summary>
public sealed class R99_ChartSeriesPlotOrderAndLegendEntriesRemapTests
{
    // 4 columns (1-4), 4 rows (1-4); FirstColIsCategories defaults true for a Column chart, so
    // col 1 is categories and cols 2/3/4 are 3 series at SeriesIndex 0/1/2 respectively.
    private static GridRange ThreeSeriesRange(Sheet sheet) =>
        new(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4));

    private static (Sheet Sheet, TestCommandContext Ctx, ChartModel Chart) CreateThreeSeriesChart()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = ThreeSeriesRange(sheet);
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        return (sheet, ctx, chart);
    }

    [Fact]
    public void RemoveChartSeriesCommand_RemapsPlotOrderAndKeepsCorrectLegendKeyHidden()
    {
        // Repro from the finding: identity plot order (as XlsxChartPartReader.Bar.cs populates
        // for essentially every bar/column/line chart), legend key for series idx 2 (the LAST
        // series) is hidden via its declaration-order position (2, since plot order is identity).
        var (sheet, ctx, chart) = CreateThreeSeriesChart();
        chart.SeriesPlotOrder = [0, 1, 2];
        chart.LegendEntries = [new ChartLegendEntryModel(2, IsDeleted: true)];

        // Remove the FIRST series (SeriesIndex 0) via Select Data > Remove -- an earlier series
        // than the one whose legend key is hidden.
        var outcome = new RemoveChartSeriesCommand(sheet.Id, chart.Id, 0).Apply(ctx);

        outcome.Success.Should().BeTrue();

        // Old series idx 2 is now idx 1 (idx 0 removed, idx 1 -> 0, idx 2 -> 1).
        chart.SeriesPlotOrder.Should().Equal(0, 1);

        // The legend entry must now resolve (via the NEW plot order) to the RENUMBERED series
        // idx 1 -- i.e. the same physical series that was hidden before the edit -- not to the
        // stale idx 2 (which no longer exists) and not silently disappear.
        var entry = chart.LegendEntries.Should().ContainSingle().Subject;
        entry.IsDeleted.Should().BeTrue();
        var resolvedSeriesIndex = chart.SeriesPlotOrder[entry.Index];
        resolvedSeriesIndex.Should().Be(1, "the previously-hidden series (old idx 2) is now idx 1 after removing idx 0");
    }

    [Fact]
    public void RemoveChartSeriesCommand_DropsLegendEntryThatPointedAtTheRemovedSeries()
    {
        var (sheet, ctx, chart) = CreateThreeSeriesChart();
        // Combo-style non-identity plot order: declared order is series idx 1, 2, 0.
        chart.SeriesPlotOrder = [1, 2, 0];
        // Hide legend position 0 -> declared-first series -> idx 1 (the one about to be removed).
        chart.LegendEntries = [new ChartLegendEntryModel(0, IsDeleted: true)];

        var outcome = new RemoveChartSeriesCommand(sheet.Id, chart.Id, 1).Apply(ctx);

        outcome.Success.Should().BeTrue();
        // idx 1 removed: remaining idx 2 -> 1; plot order [1,2,0] minus the "1" entry, decremented.
        chart.SeriesPlotOrder.Should().Equal(1, 0);
        // The legend entry that pointed at the now-removed series must be dropped entirely,
        // not left stale (pointing at some unrelated survivor).
        chart.LegendEntries.Should().BeEmpty();
    }

    [Fact]
    public void RemoveChartSeriesCommand_LegacyEmptyPlotOrder_RemapsLegendEntriesLikeOtherIndexKeyedLists()
    {
        // Legacy positional case (no SeriesPlotOrder): LegendEntries.Index IS the series idx
        // directly (IsLegendEntryDeleted's fallback), so it must remap exactly like every other
        // SeriesIndex-keyed override.
        var (sheet, ctx, chart) = CreateThreeSeriesChart();
        chart.LegendEntries = [new ChartLegendEntryModel(2, IsDeleted: true)];

        var outcome = new RemoveChartSeriesCommand(sheet.Id, chart.Id, 0).Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.SeriesPlotOrder.Should().BeEmpty();
        var entry = chart.LegendEntries.Should().ContainSingle().Subject;
        entry.Index.Should().Be(1); // was 2, shifted down by the removal of idx 0
        entry.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void RemoveChartSeriesCommand_IsUndoable_RestoresPlotOrderAndLegendEntries()
    {
        var (sheet, ctx, chart) = CreateThreeSeriesChart();
        chart.SeriesPlotOrder = [0, 1, 2];
        chart.LegendEntries = [new ChartLegendEntryModel(2, IsDeleted: true)];
        var command = new RemoveChartSeriesCommand(sheet.Id, chart.Id, 0);

        command.Apply(ctx).Success.Should().BeTrue();
        chart.SeriesPlotOrder.Should().Equal(0, 1);

        command.Revert(ctx);

        chart.SeriesPlotOrder.Should().Equal(0, 1, 2);
        chart.LegendEntries.Should().ContainSingle()
            .Which.Index.Should().Be(2);
    }

    [Fact]
    public void ChangeChartSourceCommand_ClearsPlotOrderAndLegendEntriesOnDataRangeChange()
    {
        // No-regression sibling: ChangeChartSourceCommand's clearing block explicitly names
        // "plot order" in its own comment as something that must be cleared alongside the other
        // SeriesIndex-keyed overrides -- it must actually do so.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var originalRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4));
        new AddChartCommand(sheet.Id, originalRange, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        chart.SeriesPlotOrder = [1, 2, 0];
        chart.LegendEntries = [new ChartLegendEntryModel(0, IsDeleted: true)];
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 3));

        var outcome = new ChangeChartSourceCommand(sheet.Id, chart.Id, newRange).Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.SeriesPlotOrder.Should().BeEmpty();
        chart.LegendEntries.Should().BeEmpty();
    }

    [Fact]
    public void ChangeChartSourceCommand_IsUndoable_RestoresPlotOrderAndLegendEntries()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var originalRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4));
        new AddChartCommand(sheet.Id, originalRange, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        chart.SeriesPlotOrder = [1, 2, 0];
        chart.LegendEntries = [new ChartLegendEntryModel(0, IsDeleted: true)];
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 3));
        var command = new ChangeChartSourceCommand(sheet.Id, chart.Id, newRange);

        command.Apply(ctx).Success.Should().BeTrue();
        chart.SeriesPlotOrder.Should().BeEmpty();

        command.Revert(ctx);

        chart.SeriesPlotOrder.Should().Equal(1, 2, 0);
        chart.LegendEntries.Should().ContainSingle()
            .Which.Index.Should().Be(0);
    }
}
