using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R94-app-pivot-cache-5-1: PivotTableRefreshService.Refresh's table-tracking block (N32) re-resolves a
/// table-backed pivot's SourceRange/cache.SourceReference/cache.SourceSheetName from the live
/// StructuredTableModel on every refresh, but used to never touch cache.Fields. A structured-table
/// column resize (ResizeStructuredTableCommand -- which has zero pivot awareness and freely allows
/// narrowing, since ValidateResizeRange only requires ColCount >= 1) can shrink the live table's column
/// count out from under the cache, leaving cache.Fields stuck at its old, wider count forever after a
/// refresh. On save, XlsxPivotTableWriter emits &lt;cacheFields count="N"&gt; from the stale,
/// wide cache.Fields.Count while pivotCacheRecords re-resolves the CURRENT (narrower) source range and
/// writes fewer value children per &lt;r&gt; record than cacheFields declares -- a structural mismatch
/// Excel repairs or misreads on open.
///
/// Both tests drive the real product entry points: ResizeStructuredTableCommand (the command a table
/// column-drag resize or Table Design > Resize Table dialog constructs) followed by
/// RefreshPivotTableCommand (the command the ribbon's Data > Refresh action constructs) -- not the
/// internal service method or a hand-built cache directly.
/// </summary>
public sealed class R94_PivotCacheFieldsReconciledOnTableResizeRefreshTests
{
    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Pivot, PivotCacheModel Cache) CreateTableBackedPivot(string workbookName)
    {
        var workbook = new Workbook(workbookName);
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Quarter"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Units"));

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new NumberValue(2));

        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Q2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new NumberValue(3));

        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new NumberValue(4));

        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new TextValue("Q2"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new NumberValue(25));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 4), new NumberValue(5));

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "SalesTable",
            DisplayName = "SalesTable",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 4)),
            HasAutoFilter = true,
            HeaderRowCount = 1,
        });

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.Table,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:D5",
            SourceTableName = "SalesTable",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 4,
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true, SharedItems: ["East", "West"], SharedItemKinds: ['s', 's']));
        cache.Fields.Add(new PivotCacheFieldModel("Quarter", ContainsString: true, SharedItems: ["Q1", "Q2"], SharedItemKinds: ['s', 's']));
        // Amount carries a distinctive NumberFormatId so the test can prove the surviving field's
        // metadata is *preserved* (matched by name), not thrown away and rebuilt from scratch.
        cache.Fields.Add(new PivotCacheFieldModel("Amount", NumberFormatId: 44, ContainsNumber: true));
        cache.Fields.Add(new PivotCacheFieldModel("Units", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 4)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 1, 6), new CellAddress(sheet.Id, 10, 8)),
            ReportLayout = PivotReportLayout.Tabular,
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Units", "sum"));
        sheet.PivotTables.Add(pivot);

        var initialRefresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        initialRefresh.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        return (workbook, sheet, pivot, cache);
    }

    // --- bug case: a structured-table column resize narrows the live table under a table-backed pivot ---

    [Fact]
    public void Refresh_AfterTableColumnResizeNarrowsSource_ReconcilesCacheFieldsToLiveHeaderCount()
    {
        var (workbook, sheet, pivot, cache) = CreateTableBackedPivot("PivotCacheFieldsResizeNarrowTest");

        // Sanity: the cache starts with all four fields, matching the pre-resize table.
        cache.Fields.Should().HaveCount(4);

        // Resize the backing table from A1:D5 down to A1:C5 -- drops the "Units" column entirely.
        // This is the real command a Table Design > Resize Table action (or dragging the resize
        // handle) constructs; it has zero pivot awareness and freely allows narrowing.
        var resize = new ResizeStructuredTableCommand(
            sheet.Id,
            tableId: 1,
            newRange: new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)));
        resize.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        var refresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        var outcome = refresh.Apply(new TestCommandContext(workbook));
        outcome.Success.Should().BeTrue();

        // Bug (before fix): cache.Fields stayed at its stale count of 4 forever, even though the live
        // table (and therefore pivotCacheRecords on save) now only has 3 columns -- producing a
        // cacheFields/pivotCacheRecords width mismatch that corrupts the saved pivot cache.
        cache.Fields.Should().HaveCount(3, "cache.Fields must track the live (narrowed) table header count exactly the way ChangePivotTableSourceCommand reconciles it on an explicit Change Data Source");
        cache.Fields.Select(f => f.Name).Should().Equal("Region", "Quarter", "Amount");

        // The surviving "Amount" field's metadata (matched by name) must be preserved, not rebuilt
        // from scratch -- losing NumberFormatId/sharedItems/grouping on every refresh would itself be
        // a regression even once the field-count mismatch is fixed.
        cache.Fields.Single(f => f.Name == "Amount").NumberFormatId.Should().Be(44);
        cache.Fields.Single(f => f.Name == "Region").SharedItems.Should().Equal("East", "West");

        // The now out-of-range "Sum of Units" data field must have been dropped from the layout by the
        // existing R92 field-validity pruning (unaffected by this fix, but consistent with it: the
        // cache and the live pivot fields must agree on the same narrower header count).
        pivot.DataFields.Should().ContainSingle(field => field.Name == "Sum of Amount");
    }

    // --- no-regression sibling: an ordinary refresh with no table resize must not churn cache.Fields ---

    [Fact]
    public void Refresh_WithoutTableResize_LeavesCacheFieldsUntouched()
    {
        var (workbook, sheet, pivot, cache) = CreateTableBackedPivot("PivotCacheFieldsNoResizeTest");
        var fieldsBeforeRefresh = cache.Fields.ToList();

        // A second, ordinary refresh with no structural change to the backing table at all.
        var refresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        var outcome = refresh.Apply(new TestCommandContext(workbook));
        outcome.Success.Should().BeTrue();

        cache.Fields.Should().HaveCount(4);
        cache.Fields.Select(f => f.Name).Should().Equal("Region", "Quarter", "Amount", "Units");
        // R115-commands-pivot-sharedItems-refresh: the fields must still be VALUE-equal (same names,
        // NumberFormatId, SharedItems content) when nothing in the underlying data changed -- but they
        // are no longer guaranteed to be the SAME record instances any more. Re-deriving SharedItems
        // from the live column on every refresh (not only when the header itself changed) is exactly
        // the fix for R115's staleness defect, and doing that unconditionally means a brand-new
        // (immutable) PivotCacheFieldModel record is built even when the recomputed content happens to
        // be identical to what was already there -- see PivotCacheFieldFactory.MergeFromSourceData.
        // BeEquivalentTo (deep/structural, including collection CONTENT) rather than Equal (which uses
        // PivotCacheFieldModel's synthesized record Equals -- and List<T> has no value-equality
        // override, so two content-identical-but-different-instance SharedItems lists would otherwise
        // register as a false mismatch).
        cache.Fields.Should().BeEquivalentTo(fieldsBeforeRefresh, options => options.WithStrictOrdering());

        pivot.DataFields.Should().HaveCount(2, "no field was invalidated -- both data fields must still be present");
    }
}
