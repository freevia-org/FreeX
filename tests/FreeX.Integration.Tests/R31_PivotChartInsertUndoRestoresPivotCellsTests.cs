using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R31-undo-redo-newer-commands-deep-1: AddPivotChartCommand.Apply unconditionally calls
/// PivotTableRefreshService.Refresh (which reads live source data and rewrites the pivot's
/// rendered sheet cells) before building the chart. Because a PivotTable's rendered output is a
/// cache that Excel does NOT auto-recalculate on source edits, a user can edit a source cell,
/// then Insert &gt; PivotChart -- which incidentally refreshes (and thus mutates) the pivot's
/// sheet cells as a side effect of inserting the chart. Undo previously only removed the chart,
/// leaving the sheet's pivot output permanently at the refreshed values instead of restoring the
/// exact pre-command state. Fixed by snapshotting the pivot's rendered range before Refresh (like
/// every sibling pivot-editing command already does) and restoring it in Revert.
/// </summary>
public sealed class R31_PivotChartInsertUndoRestoresPivotCellsTests
{
    [Fact]
    public void AddPivotChartCommand_Undo_RestoresStalePivotCellsThatRefreshMutatedAsASideEffect()
    {
        // Category "A" starts with Amount 10 and the pivot is refreshed once so the sheet shows
        // the materialized Sum-of-Amount output (Excel-style: a rendered cache, not a live formula).
        var (sheet, ctx, pivot) = CreatePivotRefreshedOnce();

        var preEditRenderedRange = pivot.LastRenderedRange;
        preEditRenderedRange.Should().NotBeNull();
        var sumForCategoryACellBeforeEdit = FindSumCellForCategory(sheet, preEditRenderedRange!.Value, "A");
        sheet.GetValue(sumForCategoryACellBeforeEdit).Should().Be(new NumberValue(10d));

        // Edit the source amount for "A" WITHOUT refreshing -- exactly like Excel, where editing
        // source data does not auto-recalculate an existing pivot report.
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(999));

        // The sheet's rendered pivot output is now stale: it still shows the pre-edit sum.
        sheet.GetValue(sumForCategoryACellBeforeEdit).Should().Be(new NumberValue(10d),
            "Excel pivot output does not auto-refresh on a source-cell edit");

        var preApplySnapshot = CaptureRange(sheet, preEditRenderedRange.Value);

        var command = new AddPivotChartCommand(sheet.Id, "PivotTable1", ChartType.Column, "Amount by Category");
        command.Apply(ctx).Success.Should().BeTrue();

        // Inserting the PivotChart incidentally refreshed the pivot table, so the sheet now shows
        // the up-to-date (999) sum -- confirming the mutating side effect actually happened.
        sheet.GetValue(sumForCategoryACellBeforeEdit).Should().Be(new NumberValue(999d));
        sheet.Charts.Should().ContainSingle().Which.IsPivotChart.Should().BeTrue();

        command.Revert(ctx);

        // The chart is gone, and the pivot's rendered cells must be back to the STALE, pre-Apply
        // state -- not the refreshed one -- because Revert must undo everything Apply did,
        // including Apply's incidental refresh side effect.
        sheet.Charts.Should().BeEmpty();
        CaptureRange(sheet, preEditRenderedRange.Value).Should().BeEquivalentTo(preApplySnapshot);
        sheet.GetValue(sumForCategoryACellBeforeEdit).Should().Be(new NumberValue(10d));
        pivot.LastRenderedRange.Should().Be(preEditRenderedRange);
    }

    [Fact]
    public void AddPivotChartCommand_Undo_RemovesChartAndKeepsPivotTableWhenNoStaleEditExists()
    {
        // Representative already-working sibling case: no stale pre-Apply edit at all (the pivot
        // has never been refreshed before Insert PivotChart runs). Undo must still simply remove
        // the chart and leave the pivot table definition intact.
        var workbook = new Workbook("PivotChartInsertUndoTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 7,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "E8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var command = new AddPivotChartCommand(sheet.Id, "PivotTable1", ChartType.Column, "Amount by Category");

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.Charts.Should().ContainSingle().Which.IsPivotChart.Should().BeTrue();

        command.Revert(ctx);

        sheet.Charts.Should().BeEmpty();
        sheet.PivotTables.Should().ContainSingle().Which.Name.Should().Be("PivotTable1");
    }

    private static (Sheet Sheet, TestCommandContext Context, PivotTableModel Pivot) CreatePivotRefreshedOnce()
    {
        var workbook = new Workbook("PivotChartInsertUndoTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "E6")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        return (sheet, ctx, pivot);
    }

    private static void SeedData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
    }

    private static CellAddress FindSumCellForCategory(Sheet sheet, GridRange range, string category)
    {
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            var labelAddress = new CellAddress(sheet.Id, row, range.Start.Col);
            if (sheet.GetValue(labelAddress) is TextValue text && text.Value == category)
                return new CellAddress(sheet.Id, row, range.Start.Col + 1);
        }

        throw new InvalidOperationException($"Category '{category}' not found in rendered pivot range {range}.");
    }

    private static Dictionary<CellAddress, ScalarValue> CaptureRange(Sheet sheet, GridRange range)
    {
        var snapshot = new Dictionary<CellAddress, ScalarValue>();
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        for (var col = range.Start.Col; col <= range.End.Col; col++)
        {
            var address = new CellAddress(sheet.Id, row, col);
            snapshot[address] = sheet.GetValue(address);
        }

        return snapshot;
    }

    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));
}
