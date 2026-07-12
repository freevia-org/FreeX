using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R31-undo-redo-newer-commands-deep-2/3: a family of pivot commands (RefreshPivotTableCommand,
/// ConfigurePivotTableViewCommand, ConfigurePivotTableFieldFiltersCommand,
/// ConfigurePivotTableCalculatedItemsCommand) update a bound PivotChart's DataRange/PivotCacheId
/// in Apply but used to never restore it in Revert -- so after Undo the chart kept showing (or
/// under-displaying) the post-Apply pivot shape instead of the pre-Apply one. All four now call
/// the shared UpdateBoundPivotChartRanges helper (already used by ConfigurePivotTableLayoutCommand
/// and MovePivotTableCommand) after restoring the pivot on Revert.
/// </summary>
public sealed class R31_pivot_chart_revert_bound_range_Tests
{
    // --- bug case: ConfigurePivotTableFieldFiltersCommand shrinks the pivot, Undo must restore the chart ---

    [Fact]
    public void ConfigurePivotTableFieldFiltersCommand_Undo_RestoresBoundPivotChartDataRangeAfterShrink()
    {
        var (workbook, sheet, pivot) = CreateTwoCategoryPivot("PivotFieldFilterChartRevertTest");
        var chart = AddBoundPivotChart(sheet, pivot);
        var ctx = new TestCommandContext(workbook);

        var fullRange = chart.DataRange;

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

        // Filtering down to just "A" shrinks the materialized pivot, and the bound chart's DataRange
        // must track the new (smaller) output range.
        chart.DataRange.RowCount.Should().BeLessThan(fullRange.RowCount, "the label filter drops the B row, shrinking the materialized pivot");
        chart.DataRange.Should().NotBe(fullRange);

        command.Revert(ctx);

        // Bug: before the fix, Revert restored the pivot's fields/filters but never touched the
        // chart, so chart.DataRange kept pointing at the shrunk 2-row range instead of the restored
        // 3-row (A, B, Grand Total) range.
        chart.DataRange.Should().Be(fullRange, "undo must restore the chart's DataRange to match the restored (unfiltered) pivot shape");
        chart.PivotCacheId.Should().Be(pivot.CacheId);
    }

    // --- sibling case: RefreshPivotTableCommand's own Revert must restore the bound chart too ---

    [Fact]
    public void RefreshPivotTableCommand_Undo_RestoresBoundPivotChartDataRangeAfterSourceGrowth()
    {
        var (workbook, sheet, pivot) = CreateTwoCategoryPivot("PivotRefreshChartRevertTest");
        var chart = AddBoundPivotChart(sheet, pivot);
        var ctx = new TestCommandContext(workbook);

        var originalRange = chart.DataRange;

        // Grow the source data (a new "C" category row) so a refresh materializes more pivot rows.
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("C"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(50));
        pivot.SourceRange = new GridRange(pivot.SourceRange.Start, new CellAddress(sheet.Id, 4, 2));

        var command = new RefreshPivotTableCommand(sheet.Id, "PivotTable1");

        command.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.RowCount.Should().BeGreaterThan(originalRange.RowCount, "the refresh picks up the new C category, growing the materialized pivot");
        chart.DataRange.Should().NotBe(originalRange);

        command.Revert(ctx);

        chart.DataRange.Should().Be(originalRange, "undo must restore the chart's DataRange to match the pre-refresh pivot shape");
        chart.PivotCacheId.Should().Be(pivot.CacheId);
    }

    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Pivot) CreateTwoCategoryPivot(string workbookName)
    {
        var workbook = new Workbook(workbookName);
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 10, 6)),
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
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
}
