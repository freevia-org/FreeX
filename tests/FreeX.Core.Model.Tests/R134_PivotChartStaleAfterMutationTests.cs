using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R134-commands-pivotchart-stale-datarange: a bound PivotChart's DataRange/PivotCacheId must track
/// the pivot's CURRENT materialized output range after every mutation that can move/resize/re-source
/// it. Before this fix, <see cref="SetSlicerSelectionCommand"/> and <see cref="SetTimelineRangeCommand"/>
/// (a slicer/timeline selection change -- the anchor HIGH finding) never synced the chart at all, so it
/// kept rendering the cells the pivot occupied BEFORE the selection change -- stale and silently
/// inconsistent with the pivot right next to it. <see cref="ChangePivotTableSourceCommand"/> ("Change
/// Data Source") and <see cref="ConfigurePivotTableOptionsCommand"/> (report-layout/grand-total options
/// that resize the pivot's geometry) had the identical gap. All four now call the new shared
/// <see cref="PivotTableRefreshService.UpdateBoundPivotCharts"/> helper on both Apply and Revert,
/// mirroring the sync every OTHER pivot-mutating command (ConfigurePivotTableLayoutCommand,
/// ConfigurePivotTableFieldFiltersCommand, ConfigurePivotTableCalculatedItemsCommand,
/// RefreshPivotTableCommand, RenamePivotTableCommand, MovePivotTableCommand,
/// ClearPivotTableViewCommand) already performed.
/// </summary>
public sealed class R134_PivotChartStaleAfterMutationTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Pivot) CreateThreeCategoryPivot(string workbookName)
    {
        var workbook = new Workbook(workbookName);
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B4"),
            TargetRange = Range(sheet, "D3", "F10"),
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var cache = new PivotCacheModel { CacheId = 1, SourceType = PivotCacheSourceType.WorksheetRange, SourceSheetName = sheet.Name, SourceReference = pivot.SourceRange.ToString() };
        workbook.PivotCaches.Add(cache);

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        return (workbook, sheet, pivot);
    }

    private static ChartModel AddBoundPivotChart(Sheet sheet, PivotTableModel pivot)
    {
        var chart = new ChartModel
        {
            Name = "PivotChart1",
            IsPivotChart = true,
            PivotTableName = pivot.Name,
            DataRange = PivotTableRefreshService.GetMaterializedOutputRange(sheet, pivot),
            PivotCacheId = pivot.CacheId,
        };
        sheet.Charts.Add(chart);
        return chart;
    }

    // ── anchor HIGH bug: slicer selection change never synced the bound chart ──────────────────────

    [Fact]
    public void SetSlicerSelectionCommand_Apply_UpdatesBoundPivotChartDataRange()
    {
        var (workbook, sheet, pivot) = CreateThreeCategoryPivot("R134SlicerChartTest");
        var chart = AddBoundPivotChart(sheet, pivot);
        var ctx = new TestCommandContext(workbook);
        var fullRange = chart.DataRange;

        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Category Slicer",
            CacheName = "Slicer_Category",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Category",
        });

        var command = new SetSlicerSelectionCommand("Category Slicer", ["A"]);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // Filtering down to just "A" shrinks the materialized pivot from 4 rows (A, B, C, Grand Total)
        // to 2 (A, Grand Total); the bound chart's DataRange must track the new, smaller output range.
        chart.DataRange.RowCount.Should().BeLessThan(fullRange.RowCount, "the slicer filters out the B and C rows, shrinking the materialized pivot");
        chart.DataRange.Should().NotBe(fullRange);
        chart.DataRange.Should().Be(PivotTableRefreshService.GetMaterializedOutputRange(sheet, pivot));
        chart.PivotCacheId.Should().Be(pivot.CacheId);
    }

    [Fact]
    public void SetSlicerSelectionCommand_Revert_RestoresBoundPivotChartDataRange()
    {
        var (workbook, sheet, pivot) = CreateThreeCategoryPivot("R134SlicerChartRevertTest");
        var chart = AddBoundPivotChart(sheet, pivot);
        var ctx = new TestCommandContext(workbook);
        var fullRange = chart.DataRange;

        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Category Slicer",
            CacheName = "Slicer_Category",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Category",
        });

        var command = new SetSlicerSelectionCommand("Category Slicer", ["A"]);
        command.Apply(ctx).Success.Should().BeTrue();
        chart.DataRange.Should().NotBe(fullRange);

        command.Revert(ctx);

        chart.DataRange.Should().Be(fullRange, "undo must restore the chart's DataRange to match the restored (unfiltered) pivot shape");
        chart.PivotCacheId.Should().Be(pivot.CacheId);
    }

    // ── sibling: timeline range change has the identical gap ───────────────────────────────────────

    [Fact]
    public void SetTimelineRangeCommand_Apply_UpdatesBoundPivotChartDataRange()
    {
        var workbook = new Workbook("R134TimelineChartTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Product"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Date"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Sales"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("Widget"));
        sheet.SetCell(Addr(sheet, "B2"), DateTimeValue.FromDateTime(new DateTime(2026, 1, 5)));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(100));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("Gadget"));
        sheet.SetCell(Addr(sheet, "B3"), DateTimeValue.FromDateTime(new DateTime(2026, 2, 2)));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(200));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C3"),
            TargetRange = Range(sheet, "E3", "G7"),
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Sales", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var chart = AddBoundPivotChart(sheet, pivot);
        var fullRange = chart.DataRange;
        var ctx = new TestCommandContext(workbook);

        workbook.Timelines.Add(new TimelineModel
        {
            Name = "Date Timeline",
            CacheName = "Timeline_Date",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Date",
        });

        // Narrow to January only: the Gadget/February row drops out, shrinking the pivot.
        var outcome = new SetTimelineRangeCommand("Date Timeline", "2026-01-01", "2026-01-31").Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        chart.DataRange.RowCount.Should().BeLessThan(fullRange.RowCount, "the timeline range drops the February row, shrinking the materialized pivot");
        chart.DataRange.Should().Be(PivotTableRefreshService.GetMaterializedOutputRange(sheet, pivot));
        chart.PivotCacheId.Should().Be(pivot.CacheId);
    }

    // ── sibling: Change Data Source has the identical gap ───────────────────────────────────────────

    [Fact]
    public void ChangePivotTableSourceCommand_Apply_UpdatesBoundPivotChartDataRange()
    {
        var (workbook, sheet, pivot) = CreateThreeCategoryPivot("R134ChangeSourceChartTest");
        // Start from a narrower 2-category source so the "Change Data Source" call below grows it.
        pivot.SourceRange = Range(sheet, "A1", "B3");
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        var chart = AddBoundPivotChart(sheet, pivot);
        var narrowRange = chart.DataRange;
        var ctx = new TestCommandContext(workbook);

        var command = new ChangePivotTableSourceCommand(sheet.Id, "PivotTable1", Range(sheet, "A1", "B4"));
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        chart.DataRange.RowCount.Should().BeGreaterThan(narrowRange.RowCount, "redirecting the source to include category C grows the materialized pivot");
        chart.DataRange.Should().Be(PivotTableRefreshService.GetMaterializedOutputRange(sheet, pivot));
        chart.PivotCacheId.Should().Be(pivot.CacheId);

        command.Revert(ctx);

        chart.DataRange.Should().Be(narrowRange, "undo must restore the chart's DataRange to match the pre-change-source pivot shape");
    }

    // ── sibling: pivot Options (report layout / grand totals) has the identical gap ────────────────

    [Fact]
    public void ConfigurePivotTableOptionsCommand_Apply_UpdatesBoundPivotChartDataRange()
    {
        var (workbook, sheet, pivot) = CreateThreeCategoryPivot("R134OptionsChartTest");
        var chart = AddBoundPivotChart(sheet, pivot);
        var fullRange = chart.DataRange;
        var ctx = new TestCommandContext(workbook);

        // For a row-only pivot (no column fields), the bottom "Grand Total" row that terminates the
        // materialized output is gated by ShowColumnGrandTotals, not ShowRowGrandTotals -- mirrors
        // WriteRowPivot's own `if (pivotTable.ShowColumnGrandTotals)` check for that row (Excel's
        // model: the grand total ACROSS the row items is a "column" total).
        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: pivot.ShowRowGrandTotals,
            showColumnGrandTotals: false,
            showSubtotals: pivot.ShowSubtotals,
            subtotalPlacement: pivot.SubtotalPlacement,
            repeatItemLabels: pivot.RepeatItemLabels,
            blankLineAfterItems: pivot.BlankLineAfterItems,
            styleName: pivot.StyleName);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        chart.DataRange.RowCount.Should().BeLessThan(fullRange.RowCount, "turning off the grand total drops the Grand Total row, shrinking the materialized pivot");
        chart.DataRange.Should().Be(PivotTableRefreshService.GetMaterializedOutputRange(sheet, pivot));
        chart.PivotCacheId.Should().Be(pivot.CacheId);

        command.Revert(ctx);

        chart.DataRange.Should().Be(fullRange, "undo must restore the chart's DataRange to match the pre-options-change pivot shape");
    }

    // ── no-regression: an already-correct sibling command must keep working through the shared helper ──

    [Fact]
    public void ConfigurePivotTableFieldFiltersCommand_StillUpdatesBoundPivotChartDataRange()
    {
        var (workbook, sheet, pivot) = CreateThreeCategoryPivot("R134FieldFiltersChartNoRegressionTest");
        var chart = AddBoundPivotChart(sheet, pivot);
        var fullRange = chart.DataRange;
        var ctx = new TestCommandContext(workbook);

        var command = new ConfigurePivotTableFieldFiltersCommand(
            sheet.Id,
            "PivotTable1",
            rowFields: pivot.RowFields.ToList(),
            columnFields: [],
            pageFields: [],
            labelFilters: [new PivotLabelFilterModel(0, PivotLabelFilterKind.Equals, "A")],
            valueFilters: [],
            sorts: []);

        command.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.RowCount.Should().BeLessThan(fullRange.RowCount);
        chart.DataRange.Should().Be(PivotTableRefreshService.GetMaterializedOutputRange(sheet, pivot));
    }
}
