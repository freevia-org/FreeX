using System.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R116: RefreshPivotTableCommand.Apply funnels into PivotTableRefreshService.Refresh, which mutates the
/// SAME live PivotTableModel/PivotCacheModel objects in place -- pruning pivotTable.RowFields/
/// ColumnFields/PageFields/DataFields (RemoveAll) whenever a field's SourceFieldIndex no longer fits the
/// live header count, and rebuilding cache.Fields (Clear+AddRange via ReconcileCacheFields) to match the
/// live source's current header set -- exactly like ChangePivotTableSourceCommand's own call into the
/// same Refresh method. ChangePivotTableSourceCommand captures a PivotSourceSnapshot that restores
/// cache.Fields (and pre-validates that its own field lists can never need pruning), but
/// RefreshPivotTableCommand -- the actual F5 / "Refresh PivotTable" entry point that runs after ordinary
/// edits shrink the live source -- captured only a raw cell snapshot of the rendered range, never the
/// field lists or cache.Fields that Refresh prunes/rebuilds. Undo therefore looked like it worked (the
/// old rendered cells came back) while the pivot's underlying field configuration and the cache a bound
/// slicer reads from stayed permanently pruned.
///
/// Both tests drive the real product entry points (ResizeStructuredTableCommand, RefreshPivotTableCommand)
/// end to end and assert on the live PivotTableModel/PivotCacheModel objects Undo is supposed to restore,
/// never a hand-built snapshot bypassing the commands.
/// </summary>
public sealed class R116_RefreshPivotTableUndoRestoresFieldsTests
{
    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Pivot, PivotCacheModel Cache) CreateTableBackedPivotWithFilterFieldOnThirdColumn(string workbookName)
    {
        var workbook = new Workbook(workbookName);
        var sheet = workbook.AddSheet("Data");

        // A 3-column backing table: Category / Amount / Extra.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Extra"));

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Alpha"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("X"));

        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Beta"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new TextValue("Y"));

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "SalesTable",
            DisplayName = "SalesTable",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            HasAutoFilter = true,
            HeaderRowCount = 1,
        });

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.Table,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:C3",
            SourceTableName = "SalesTable",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
        };
        cache.Fields.Add(new PivotCacheFieldModel("Category", ContainsString: true, SharedItems: ["Alpha", "Beta"], SharedItemKinds: ['s', 's']));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        cache.Fields.Add(new PivotCacheFieldModel("Extra", ContainsString: true, SharedItems: ["X", "Y"], SharedItemKinds: ['s', 's']));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 1, 6), new CellAddress(sheet.Id, 10, 8)),
            ReportLayout = PivotReportLayout.Tabular,
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.PageFields.Add(new PivotFieldModel(2)); // filters on "Extra" -- the column that will disappear
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        // Establish a baseline via a real refresh, exactly like the file would have been refreshed at
        // least once since it was loaded/created -- this is what pins cache.SourceTableId to the real
        // "SalesTable" and materializes the initial render.
        var initialRefresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        initialRefresh.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        return (workbook, sheet, pivot, cache);
    }

    [Fact]
    public void Refresh_UndoAfterSourceTableShrinkDropsAFieldsColumn_RestoresPrunedFieldsAndCacheFields()
    {
        var (workbook, sheet, pivot, cache) = CreateTableBackedPivotWithFilterFieldOnThirdColumn("PivotRefreshUndoFieldsTest");

        pivot.PageFields.Should().ContainSingle(field => field.SourceFieldIndex == 2);
        cache.Fields.Select(f => f.Name).Should().BeEquivalentTo(["Category", "Amount", "Extra"]);

        // The user drops the "Extra" column from the backing table (e.g. Table Design > Resize Table),
        // narrowing it to just Category/Amount. This alone does not touch the pivot -- only the next
        // Refresh (F5 / ribbon Refresh / Refresh All) does.
        var resize = new ResizeStructuredTableCommand(
            sheet.Id,
            tableId: 1,
            newRange: new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)));
        resize.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        var refresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        var ctx = new TestCommandContext(workbook);
        var outcome = refresh.Apply(ctx);
        outcome.Success.Should().BeTrue();

        // Sanity: the refresh really did prune the now-invalid filter field and rebuild cache.Fields down
        // to the two surviving columns -- otherwise this test would not be exercising the bug at all.
        pivot.PageFields.Should().BeEmpty("the Extra column that field filtered on is gone from the live source");
        pivot.RowFields.Should().ContainSingle(field => field.SourceFieldIndex == 0);
        pivot.DataFields.Should().ContainSingle(field => field.SourceFieldIndex == 1);
        cache.Fields.Select(f => f.Name).Should().BeEquivalentTo(["Category", "Amount"]);

        refresh.Revert(ctx);

        // Bug (before fix): Revert only restored the rendered cell snapshot; PageFields stayed empty and
        // cache.Fields stayed at 2 entries forever, even though Undo is supposed to put the pivot back
        // exactly as it was before the Refresh that pruned it.
        pivot.PageFields.Should().ContainSingle(field => field.SourceFieldIndex == 2,
            "Undo must restore the filter field that Refresh pruned when the Extra column disappeared");
        pivot.RowFields.Should().ContainSingle(field => field.SourceFieldIndex == 0);
        pivot.DataFields.Should().ContainSingle(field => field.SourceFieldIndex == 1);
        cache.Fields.Select(f => f.Name).Should().BeEquivalentTo(["Category", "Amount", "Extra"],
            "Undo must restore the cache field a bound slicer reads from, not just the rendered cells");
    }

    // --- no-regression sibling: an ordinary refresh that changes cache SharedItems but prunes NOTHING
    // must still be fully undoable, and must not disturb field lists that were never touched. ---

    [Fact]
    public void Refresh_UndoAfterOrdinaryDataEditWithNoPruning_RestoresPriorCacheSharedItemsAndFieldLists()
    {
        var workbook = new Workbook("PivotRefreshUndoNoPruningTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Alpha"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Beta"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:B3",
        };
        cache.Fields.Add(new PivotCacheFieldModel("Category", ContainsString: true, SharedItems: ["Alpha", "Beta"], SharedItemKinds: ['s', 's']));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 5, 6)),
            ReportLayout = PivotReportLayout.Tabular,
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var originalRowFields = pivot.RowFields.ToList();
        var originalDataFields = pivot.DataFields.ToList();

        // The user edits an existing row's Category value (still inside the unchanged SourceRange, no
        // column disappears) and hits Refresh -- an everyday "the numbers changed, refresh the pivot"
        // action, not a source-shrink scenario.
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Gamma"));

        var refresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        var ctx = new TestCommandContext(workbook);
        refresh.Apply(ctx).Success.Should().BeTrue();

        // R115: the surviving Category field picks up the new distinct value while retaining the old one
        // (Excel's stale-item retention), so this refresh legitimately changes cache.Fields without
        // pruning anything.
        cache.Fields.Single(f => f.Name == "Category").SharedItems.Should().BeEquivalentTo(["Alpha", "Beta", "Gamma"]);
        pivot.RowFields.Should().BeEquivalentTo(originalRowFields);
        pivot.DataFields.Should().BeEquivalentTo(originalDataFields);

        refresh.Revert(ctx);

        cache.Fields.Single(f => f.Name == "Category").SharedItems.Should().BeEquivalentTo(["Alpha", "Beta"],
            "Undo must restore the cache field's SharedItems to what they were before this refresh, not just leave the grown list in place");
        pivot.RowFields.Should().BeEquivalentTo(originalRowFields, "fields that were never pruned must come back unchanged too");
        pivot.DataFields.Should().BeEquivalentTo(originalDataFields);
        sheet.GetCell(new CellAddress(sheet.Id, 3, 1))!.Value.Should().Be(new TextValue("Gamma"),
            "Revert only undoes the pivot's own refresh side effects, not the unrelated cell edit that preceded it");
    }
}
