using System.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R107-round2: r104/r106/r107 each fixed ONE way a structured table's name gets freed workbook-wide
/// (an explicit rename-onto, Convert to Range, Delete Sheet) without pinning a never-refreshed
/// table-backed <see cref="PivotCacheModel"/>'s <see cref="PivotCacheModel.SourceTableId"/> -- but a
/// row or column DELETE that fully consumes a structured table's range is a FOURTH, completely ordinary
/// way this happens, reachable through <see cref="DeleteRowsCommand"/>/<see cref="DeleteColumnsCommand"/>,
/// and until this fix it was entirely unguarded: RowColumnShiftHelpers.ShiftStructuredTables silently
/// drops a table from <c>sheet.StructuredTables</c> whenever the shifted range collapses to nothing
/// (<c>AddressShift.ShiftRange</c> returns null), with no pivot-cache handling at all.
///
/// Since every table-backed cache freshly loaded from a real .xlsx (or FreeX's native JSON) starts with
/// SourceTableId == null (WorkbookOpenService never calls PivotTableRefreshService.Refresh at open
/// time), that is the NORMAL starting state -- so deleting the rows/columns that make up the pivot's
/// source table reopens the exact r104/r106/r107 silent-rebind hole through this fourth path.
///
/// The fix: RowColumnShiftHelpers.ShiftStructuredTables now calls the same shared
/// CommandGuards.PinOrphanedPivotCacheSourceTableIds helper the other three sites use, at the moment a
/// table disappears from the shift. Undo is handled by RestoreAddressBearingState restoring every
/// cache's SourceTableId from the pre-shift snapshot (PivotCacheSourceSnapshot now also carries
/// SourceTableId), matching how SourceReference is already restored there.
///
/// All tests drive the real product entry points (DeleteRowsCommand, RenameStructuredTableCommand,
/// RefreshPivotTableCommand) end to end, never asserting on a hand-built model bypassing the commands.
/// </summary>
public sealed class R107_RowDeleteFullyConsumingTableOrphansUnestablishedPivotCacheTests
{
    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Pivot, PivotCacheModel Cache) CreateFreshlyLoadedTableBackedPivotWithDecoyTable(string workbookName)
    {
        var workbook = new Workbook(workbookName);
        var sheet = workbook.AddSheet("Data");

        // The pivot's real backing table: "SalesTable" at A1:D5 (rows 1-5).
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

        // A completely unrelated second table ("DecoyTable"), placed on rows 10-11 -- well clear of
        // the rows 1-5 that will be deleted below -- so the row delete removes ONLY SalesTable.
        sheet.SetCell(new CellAddress(sheet.Id, 10, 6), new TextValue("Widget"));
        sheet.SetCell(new CellAddress(sheet.Id, 10, 7), new TextValue("Color"));
        sheet.SetCell(new CellAddress(sheet.Id, 11, 6), new TextValue("Gadget"));
        sheet.SetCell(new CellAddress(sheet.Id, 11, 7), new TextValue("Blue"));

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 2,
            Name = "DecoyTable",
            DisplayName = "DecoyTable",
            Range = new GridRange(new CellAddress(sheet.Id, 10, 6), new CellAddress(sheet.Id, 11, 7)),
            HasAutoFilter = true,
            HeaderRowCount = 1,
        });

        // Exactly the state a freshly-opened .xlsx/.fxl is in: the cache carries only the source
        // table's NAME -- SourceTableId is null because nothing has refreshed this pivot since the
        // workbook was loaded. Deliberately NOT calling RefreshPivotTableCommand here -- that call
        // would establish the id and sidestep the exact vulnerable state this test targets.
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
            // A real table-backed pivot's stored SourceRange is a completely separate field from the
            // cache's SourceTableName -- PivotTableRefreshService.Refresh is what re-derives the real
            // SourceRange from the live table on every refresh (see its own doc comment), so the value
            // stored here before that first refresh is a stale/placeholder one, not necessarily equal
            // to the table's own range. Deliberately placed at rows 50-54 (well clear of rows 1-5,
            // where SalesTable and the row delete below both live) so that this test exercises ONLY the
            // table-identity hazard under test -- a pivot whose stored SourceRange happens to coincide
            // exactly with the deleted band would also be dropped as a pivot table object by
            // RowColumnShiftHelpers.ShiftPivotTables's own (separate, out-of-scope-here) collapse
            // check, which would make it impossible to observe this pivot's post-refresh behavior at
            // all -- that is a distinct concern from the SourceTableId orphaning this test targets.
            SourceRange = new GridRange(new CellAddress(sheet.Id, 50, 1), new CellAddress(sheet.Id, 54, 4)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 20, 1), new CellAddress(sheet.Id, 24, 3)),
            ReportLayout = PivotReportLayout.Tabular,
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        return (workbook, sheet, pivot, cache);
    }

    // --- bug case: deleting the rows that make up the pivot's source table is a fourth, ordinary way
    // to free a table's name, and the load-then-never-refreshed state (SourceTableId still null) is the
    // NORMAL starting condition for every real workbook, so it must be just as protected as the other
    // three known paths ---

    [Fact]
    public void Refresh_AfterDeletingTableRowsThenUnrelatedTableRenamedOntoFreedName_FromFreshlyLoadedCache_DoesNotRebindToUnrelatedTable()
    {
        var (workbook, sheet, pivot, cache) = CreateFreshlyLoadedTableBackedPivotWithDecoyTable(
            "PivotIdentityLoadThenRowDeleteRenameCollisionTest");

        cache.SourceTableId.Should().BeNull("a cache just loaded from a file has never been refreshed, so no id has been established yet");

        // Step 1: the user selects rows 1-5 (SalesTable's entire range) and deletes them -- a
        // completely ordinary action -- without ever having refreshed the pivot since opening the
        // file. This fully consumes SalesTable's range, so it disappears from sheet.StructuredTables,
        // freeing its name workbook-wide.
        var deleteRows = new DeleteRowsCommand(sheet.Id, startRow: 1, count: 5);
        deleteRows.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.StructuredTables.Should().NotContain(t => t.Name == "SalesTable");
        cache.SourceTableName.Should().Be("SalesTable", "deleting the rows must not touch the cache's source name");

        // DecoyTable survived (shifted up from rows 10-11 to rows 5-6) and kept its own id/name.
        var decoy = sheet.StructuredTables.Single(t => t.Id == 2);
        decoy.Name.Should().Be("DecoyTable");

        // Step 2: the user renames the completely unrelated "DecoyTable" so it now reuses the
        // just-freed "SalesTable" name.
        var rename = new RenameStructuredTableCommand(sheet.Id, tableId: 2, newName: "SalesTable");
        rename.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.StructuredTables.Single(t => t.Id == 2).Name.Should().Be("SalesTable");

        // Step 3: the FIRST ordinary refresh trigger since the file was opened reaches
        // PivotTableRefreshService.Refresh via RefreshPivotTableCommand.
        var refresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        var outcome = refresh.Apply(new TestCommandContext(workbook));
        outcome.Success.Should().BeTrue();

        // Bug (before fix): the cache's SourceTableId was still null going into this refresh, so
        // PivotTableRefreshService.Refresh fell back to FindStructuredTableByName("SalesTable"), which
        // matched the RENAMED "DecoyTable" (id=2) purely by its new name -- the refresh unconditionally
        // repointed the pivot at it.
        cache.SourceTableId.Should().NotBe(2, "the cache must never be pinned to the decoy table's id");
        pivot.SourceRange.Should().NotBe(decoy.Range, "the pivot must not silently graft onto DecoyTable's unrelated range");
    }

    // --- no-regression sibling: a cache that already has an established SourceTableId (R104's
    // post-first-refresh state) must be completely untouched by the row-delete orphaning logic ---

    [Fact]
    public void DeleteRows_OnTableWithAlreadyEstablishedCacheId_DoesNotAlterThatId()
    {
        var (workbook, sheet, pivot, cache) = CreateFreshlyLoadedTableBackedPivotWithDecoyTable(
            "PivotIdentityAlreadyEstablishedIdRowDeleteTest");

        var initialRefresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        initialRefresh.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        cache.SourceTableId.Should().Be(1);

        var deleteRows = new DeleteRowsCommand(sheet.Id, startRow: 1, count: 5);
        deleteRows.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        cache.SourceTableId.Should().Be(1, "an already-established id must not be altered by deleting the table's rows");
        cache.SourceTableName.Should().Be("SalesTable");
    }

    // --- no-regression sibling: reverting the row delete must restore the orphaned cache's
    // SourceTableId back to exactly null, its pre-command state ---

    [Fact]
    public void DeleteRows_Revert_RestoresOrphanedCacheIdToNull()
    {
        var (workbook, sheet, pivot, cache) = CreateFreshlyLoadedTableBackedPivotWithDecoyTable(
            "PivotIdentityRowDeleteRevertTest");

        cache.SourceTableId.Should().BeNull();

        var deleteRows = new DeleteRowsCommand(sheet.Id, startRow: 1, count: 5);
        deleteRows.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        cache.SourceTableId.Should().Be(1, "deleting the rows must pin the freshly-orphaned cache's id so a later decoy rename cannot hijack it");

        deleteRows.Revert(new TestCommandContext(workbook));
        sheet.StructuredTables.Should().Contain(t => t.Name == "SalesTable");
        cache.SourceTableId.Should().BeNull("undoing the delete must restore the cache to its pre-command, unestablished state");
    }
}
