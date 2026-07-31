using System.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R104: PivotTableRefreshService.Refresh's table-tracking block (N32) used to resolve a table-backed
/// pivot cache's live source purely by matching <see cref="PivotCacheModel.SourceTableName"/> against
/// any <see cref="StructuredTableModel"/> currently in the workbook with that Name/DisplayName — never
/// checking <see cref="StructuredTableModel.Id"/>. That means: (1) "Convert to Range" on the pivot's
/// real backing table (<see cref="ConvertStructuredTableToRangeCommand"/>) removes the table but never
/// touches the pivot cache, leaving cache.SourceTableName dangling on the now-free name; (2) renaming a
/// completely unrelated table onto that freed name (<see cref="RenameStructuredTableCommand"/>) then
/// makes the NEXT ordinary refresh (<see cref="RefreshPivotTableCommand"/>) silently re-bind the pivot's
/// SourceRange/cache to that unrelated table's range and data — with no warning, no error, and the user
/// having no way to tell the pivot has been reattached to the wrong source.
///
/// The fix gives PivotCacheModel a stable SourceTableId (mirroring SlicerModel.SourceTableId's existing
/// stable-identity pattern for table slicers), established the first time a refresh resolves the source
/// by name, and required thereafter — a same-named-but-different table must NOT be treated as the same
/// source once an id has been pinned down.
///
/// Both tests drive the real product entry points (ConvertStructuredTableToRangeCommand,
/// RenameStructuredTableCommand, RefreshPivotTableCommand) end to end, never asserting on a hand-built
/// model bypassing the commands.
/// </summary>
public sealed class R104_PivotRefreshTableIdentityTests
{
    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Pivot, PivotCacheModel Cache) CreateTableBackedPivotWithDecoyTable(string workbookName)
    {
        var workbook = new Workbook(workbookName);
        var sheet = workbook.AddSheet("Data");

        // The pivot's real backing table: "SalesTable" at A1:D5.
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

        // A completely unrelated second table, "DecoyTable", parked well away from the pivot's own
        // rendered output range (F1:H10 below) so it can never collide with the pivot's target cells.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 11), new TextValue("Widget"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 12), new TextValue("Color"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 11), new TextValue("Gadget"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 12), new TextValue("Blue"));

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 2,
            Name = "DecoyTable",
            DisplayName = "DecoyTable",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 11), new CellAddress(sheet.Id, 2, 12)),
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
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
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
        sheet.PivotTables.Add(pivot);

        // Establish the cache's baseline via a real refresh, exactly like the file would have been
        // refreshed at least once since it was loaded/created — this is what pins down
        // cache.SourceTableId to the REAL "SalesTable" (Id=1), never to "DecoyTable".
        var initialRefresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        initialRefresh.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        return (workbook, sheet, pivot, cache);
    }

    // --- bug case: Convert-to-Range frees the pivot's table name, an unrelated table is renamed onto
    // it, and a subsequent ordinary refresh must NOT silently rebind the pivot to that unrelated table ---

    [Fact]
    public void Refresh_AfterConvertToRangeThenUnrelatedTableRenamedOntoFreedName_DoesNotRebindToUnrelatedTable()
    {
        var (workbook, sheet, pivot, cache) = CreateTableBackedPivotWithDecoyTable("PivotIdentityConvertRenameCollisionTest");

        cache.SourceTableId.Should().Be(1, "the very first refresh must have pinned the cache to the real SalesTable's stable id");

        // Step 1: the user runs Table Design > Convert to Range on the pivot's actual backing table.
        // This removes the StructuredTableModel but must not touch the pivot cache at all (matching
        // existing, deliberate ConvertStructuredTableToRangeCommand behavior).
        var convert = new ConvertStructuredTableToRangeCommand(sheet.Id, tableId: 1);
        convert.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.StructuredTables.Should().NotContain(t => t.Id == 1);
        cache.SourceTableName.Should().Be("SalesTable", "convert-to-range must not touch a dangling pivot cache's source name");

        // Step 2: the user renames the completely unrelated "DecoyTable" so it now reuses the just-freed
        // "SalesTable" name.
        var rename = new RenameStructuredTableCommand(sheet.Id, tableId: 2, newName: "SalesTable");
        rename.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.StructuredTables.Single(t => t.Id == 2).Name.Should().Be("SalesTable");

        // Step 3: any ordinary refresh trigger (Refresh button, filter change, layout change, adding a
        // slicer) reaches PivotTableRefreshService.Refresh via RefreshPivotTableCommand.
        var refresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        var outcome = refresh.Apply(new TestCommandContext(workbook));
        outcome.Success.Should().BeTrue();

        // Bug (before fix): FindStructuredTableByName("SalesTable") matched the RENAMED "DecoyTable"
        // (id=2) purely by its new name, and the refresh unconditionally repointed the pivot at it --
        // pivot.SourceRange became DecoyTable's A1:B2 range instead of the real SalesTable's A1:D5, and
        // cache.SourceReference/SourceSheetName were overwritten to match.
        pivot.SourceRange.Should().Be(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 4)),
            "the pivot must keep its last-known SalesTable extent, not silently graft onto DecoyTable's unrelated range");
        cache.SourceReference.Should().Be("A1:D5", "the cache must not be repointed at DecoyTable's reference");
        cache.SourceTableId.Should().Be(1, "the cache's stable id must still name the (now gone) real SalesTable, never DecoyTable's id");
    }

    // --- no-regression sibling: renaming the SAME backing table must still keep the pivot tracking it ---

    [Fact]
    public void Refresh_AfterRenamingSameBackingTable_StillTracksRenamedTableByStableId()
    {
        var (workbook, sheet, pivot, cache) = CreateTableBackedPivotWithDecoyTable("PivotIdentityLegitimateRenameTest");

        cache.SourceTableId.Should().Be(1);

        // Grow the REAL backing table so the fix must still be re-deriving SourceRange from the live
        // table by id (not merely "leaving it untouched"), same as the pre-existing N32 behavior.
        var resize = new ResizeStructuredTableCommand(
            sheet.Id,
            tableId: 1,
            newRange: new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 4)));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 2), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 3), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 4), new NumberValue(6));
        resize.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        // The user renames the pivot's REAL backing table (same id=1), e.g. via Table Design > Table
        // Name. RenameStructuredTableCommand's own pivot-repoint loop must still recognize this cache
        // as belonging to table id=1 and update cache.SourceTableName to match.
        var rename = new RenameStructuredTableCommand(sheet.Id, tableId: 1, newName: "SalesTableRenamed");
        rename.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        cache.SourceTableName.Should().Be("SalesTableRenamed");
        cache.SourceTableId.Should().Be(1, "the id does not change across a rename of the same table");

        var refresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        refresh.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        pivot.SourceRange.Should().Be(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 4)),
            "the renamed-but-same table's grown extent must still be tracked, matching the pre-existing N32 behavior");
        cache.SourceReference.Should().Be("A1:D6");
    }
}
