using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r262: finishing the question r261 left open. A guard over RefreshPivotTableCommand's four
/// snapshots was written and reverted because, with a real <see cref="PivotCacheModel"/> present, a
/// second refresh over untouched data still reported a change and the responsible clause was not
/// identified.
///
/// <para>These tests ask the question directly, from the model's own public state, one candidate at a
/// time: does a settled refresh leave the rendered cells, the last-rendered range and the merged
/// regions alone? Whatever answers "no" is either the clause to fix or a product bug in the refresh
/// -- and either way the answer belongs in the record rather than in a guard nobody can demonstrate.</para>
/// </summary>
public sealed class R262_RefreshChurnDiagnosisTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    /// <summary>A pivot with a real cache, refreshed twice so every derived structure has settled.</summary>
    private static (Sheet Sheet, TestCommandContext Ctx, PivotTableModel Pivot) SetUpSettledPivot()
    {
        var workbook = new Workbook("R262");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));

        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = Range(sheet, "A1", "B3").ToString(),
        });
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Category"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F8"),
            ReportLayout = PivotReportLayout.Tabular,
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var ctx = new TestCommandContext(workbook);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        new RefreshPivotTableCommand(sheet.Id, "PivotTable1").Apply(ctx);

        return (sheet, ctx, pivot);
    }

    private static string RenderOf(Sheet sheet, GridRange range)
    {
        var rendered = new System.Text.StringBuilder();
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            for (var col = range.Start.Col; col <= range.End.Col; col++)
            {
                rendered.Append(row).Append(',').Append(col).Append('=')
                    .Append(sheet.GetValue(row, col)).Append(';');
            }
        }
        return rendered.ToString();
    }

    [Fact]
    public void ASettledRefreshLeavesTheRenderedCellsAlone()
    {
        var (sheet, ctx, pivot) = SetUpSettledPivot();
        var before = RenderOf(sheet, pivot.TargetRange);

        new RefreshPivotTableCommand(sheet.Id, "PivotTable1").Apply(ctx);

        RenderOf(sheet, pivot.TargetRange).Should().Be(before,
            "a refresh over untouched source data must re-render identical values");
    }

    [Fact]
    public void ASettledRefreshLeavesTheLastRenderedRangeAlone()
    {
        var (sheet, ctx, pivot) = SetUpSettledPivot();
        var before = pivot.LastRenderedRange;

        new RefreshPivotTableCommand(sheet.Id, "PivotTable1").Apply(ctx);

        pivot.LastRenderedRange.Should().Be(before);
    }

    [Fact]
    public void ASettledRefreshLeavesTheMergedRegionsAlone()
    {
        var (sheet, ctx, pivot) = SetUpSettledPivot();
        var footprint = pivot.LastRenderedRange ?? pivot.TargetRange;
        var before = sheet.MergedRegions.Where(region => region.Overlaps(footprint)).ToList();

        new RefreshPivotTableCommand(sheet.Id, "PivotTable1").Apply(ctx);

        sheet.MergedRegions.Where(region => region.Overlaps(footprint)).Should().BeEquivalentTo(before);
    }

    [Fact]
    public void ASettledRefreshLeavesThePivotsOwnFieldListsAlone()
    {
        var (sheet, ctx, pivot) = SetUpSettledPivot();
        var before = (
            Rows: pivot.RowFields.Count,
            Columns: pivot.ColumnFields.Count,
            Pages: pivot.PageFields.Count,
            Data: pivot.DataFields.Count);

        new RefreshPivotTableCommand(sheet.Id, "PivotTable1").Apply(ctx);

        (pivot.RowFields.Count, pivot.ColumnFields.Count, pivot.PageFields.Count, pivot.DataFields.Count)
            .Should().Be(before);
    }

    /// <summary>
    /// The test r261 could not make pass. With <c>SharedItemKinds</c> also stripped in
    /// <c>SameCacheFields</c>, a refresh over untouched data reports the no-op it always was.
    /// </summary>
    [Fact]
    public void ASettledRefreshReportsANoOp()
    {
        var (sheet, ctx, _) = SetUpSettledPivot();

        new RefreshPivotTableCommand(sheet.Id, "PivotTable1").Apply(ctx)
            .IsNoOp.Should().BeTrue("nothing has changed since the last refresh");
    }

    [Fact]
    public void ARefreshAfterASourceValueChangesIsNotANoOp()
    {
        var (sheet, ctx, _) = SetUpSettledPivot();
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(999));

        new RefreshPivotTableCommand(sheet.Id, "PivotTable1").Apply(ctx)
            .IsNoOp.Should().BeFalse("the rendered total changes");
    }

    /// <summary>
    /// The cache half in isolation: the row field is restricted to A and B, so a new category C
    /// renders nothing new and leaves the pivot's own field lists alone -- only the cache's shared
    /// items grow. This is the case r261's broken comparison could not distinguish from any other.
    /// </summary>
    [Fact]
    public void ARefreshWhereOnlyTheCacheSharedItemsGrowIsNotANoOp()
    {
        var (sheet, ctx, pivot) = SetUpSettledPivot();
        pivot.RowFields.Clear();
        pivot.RowFields.Add(new PivotFieldModel(0, SelectedItems: ["A", "B"]));
        new RefreshPivotTableCommand(sheet.Id, "PivotTable1").Apply(ctx);

        sheet.SetCell(Addr(sheet, "A4"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));
        pivot.SourceRange = Range(sheet, "A1", "B4");

        new RefreshPivotTableCommand(sheet.Id, "PivotTable1").Apply(ctx)
            .IsNoOp.Should().BeFalse("the cache gains a shared item even though C is filtered out");
    }
}
