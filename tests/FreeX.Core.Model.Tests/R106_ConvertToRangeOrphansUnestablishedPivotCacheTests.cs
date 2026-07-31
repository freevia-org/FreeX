using System.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R106: r104 gave <see cref="PivotCacheModel"/> a stable <see cref="PivotCacheModel.SourceTableId"/>
/// so <see cref="PivotTableRefreshService.Refresh"/> can tell "the same table, renamed" apart from "an
/// unrelated table that now happens to share the name" -- but only once that id has been established by
/// a prior refresh. Every table-backed pivot cache freshly loaded from a real .xlsx (or FreeX's native
/// JSON) starts with SourceTableId == null, since OOXML's pivotCacheDefinition carries only the source
/// table's name, and nothing in the load path (WorkbookOpenService only calls
/// PivotTableRefreshService.ApplyLoadedPivotStyles, never Refresh) ever establishes the id at open time.
/// That is the NORMAL starting state for every real workbook, not an edge case.
///
/// In that unestablished-id state, "Convert to Range" on the pivot's real backing table frees the
/// table's name without touching the pivot cache (by design), and if a completely unrelated table is
/// then renamed onto that freed name, <see cref="RenameStructuredTableCommand"/>'s own repoint loop
/// only matches by the renamed table's OLD name -- so it correctly does not fire -- and the next
/// ordinary refresh's null-id fallback resolves the dangling name against the decoy and silently
/// rebinds the pivot to its data. This is the identical bug r104 fixed, reachable from the state every
/// loaded file actually starts in.
///
/// The fix: <see cref="ConvertStructuredTableToRangeCommand"/> now pins an unestablished cache's
/// SourceTableId to the table being removed's own (now-orphaned) id at the moment its name is freed, so
/// the id-based lookup in Refresh leaves the pivot's last-known extent untouched instead of ever falling
/// back to a name match against a decoy that later reuses the freed name.
///
/// All tests drive the real product entry points (ConvertStructuredTableToRangeCommand,
/// RenameStructuredTableCommand, RefreshPivotTableCommand) end to end, never asserting on a hand-built
/// model bypassing the commands.
/// </summary>
public sealed class R106_ConvertToRangeOrphansUnestablishedPivotCacheTests
{
    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Pivot, PivotCacheModel Cache) CreateFreshlyLoadedTableBackedPivotWithDecoyTable(string workbookName)
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

        // Exactly the state a freshly-opened .xlsx/.fxl is in: the cache carries only the source
        // table's NAME (as read from OOXML's pivotCacheDefinition) -- SourceTableId is null because
        // nothing has refreshed this pivot since the workbook was loaded (WorkbookOpenService never
        // calls PivotTableRefreshService.Refresh at open time). Deliberately NOT calling
        // RefreshPivotTableCommand here, unlike R104's fixture -- that call would establish the id and
        // sidestep the exact vulnerable state this test targets.
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

        return (workbook, sheet, pivot, cache);
    }

    // --- bug case: the load-then-never-refreshed state (SourceTableId still null) is the NORMAL
    // starting condition for every real workbook, and must be just as protected as the post-refresh
    // state R104 already covers ---

    [Fact]
    public void Refresh_AfterConvertToRangeThenUnrelatedTableRenamedOntoFreedName_FromFreshlyLoadedCache_DoesNotRebindToUnrelatedTable()
    {
        var (workbook, sheet, pivot, cache) = CreateFreshlyLoadedTableBackedPivotWithDecoyTable("PivotIdentityLoadThenConvertRenameCollisionTest");

        cache.SourceTableId.Should().BeNull("a cache just loaded from a file has never been refreshed, so no id has been established yet");

        // Step 1: the user runs Table Design > Convert to Range on the pivot's actual backing table,
        // without ever having refreshed the pivot since opening the file.
        var convert = new ConvertStructuredTableToRangeCommand(sheet.Id, tableId: 1);
        convert.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.StructuredTables.Should().NotContain(t => t.Id == 1);
        cache.SourceTableName.Should().Be("SalesTable", "convert-to-range must not touch the cache's source name");

        // Step 2: the user renames the completely unrelated "DecoyTable" so it now reuses the just-freed
        // "SalesTable" name.
        var rename = new RenameStructuredTableCommand(sheet.Id, tableId: 2, newName: "SalesTable");
        rename.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.StructuredTables.Single(t => t.Id == 2).Name.Should().Be("SalesTable");

        // Step 3: the FIRST ordinary refresh trigger since the file was opened (Refresh button, filter
        // change, layout change, adding a slicer) reaches PivotTableRefreshService.Refresh via
        // RefreshPivotTableCommand.
        var refresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        var outcome = refresh.Apply(new TestCommandContext(workbook));
        outcome.Success.Should().BeTrue();

        // Bug (before fix): the cache's SourceTableId was still null going into this refresh, so
        // PivotTableRefreshService.Refresh fell back to FindStructuredTableByName("SalesTable"), which
        // matched the RENAMED "DecoyTable" (id=2) purely by its new name -- the refresh unconditionally
        // repointed the pivot at it: pivot.SourceRange became DecoyTable's tiny A1:B2-shaped range
        // instead of the real SalesTable's A1:D5, and cache.SourceReference/SourceTableId were
        // overwritten to match the decoy.
        pivot.SourceRange.Should().Be(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 4)),
            "the pivot must keep its last-known SalesTable extent, not silently graft onto DecoyTable's unrelated range");
        cache.SourceReference.Should().Be("A1:D5", "the cache must not be repointed at DecoyTable's reference");
        cache.SourceTableId.Should().NotBe(2, "the cache must never be pinned to the decoy table's id");
    }

    // --- no-regression sibling: a cache that already has an established SourceTableId (R104's
    // post-first-refresh state) must be completely untouched by Convert-to-Range's new orphaning logic ---

    [Fact]
    public void ConvertToRange_OnTableWithAlreadyEstablishedCacheId_DoesNotAlterThatId()
    {
        var (workbook, sheet, pivot, cache) = CreateFreshlyLoadedTableBackedPivotWithDecoyTable("PivotIdentityAlreadyEstablishedIdTest");

        // Establish the id via a real refresh first, exactly like R104's fixture -- this is the
        // post-first-refresh state the original r104 fix already protects.
        var initialRefresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        initialRefresh.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        cache.SourceTableId.Should().Be(1);

        var convert = new ConvertStructuredTableToRangeCommand(sheet.Id, tableId: 1);
        convert.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        // The new orphaning logic only pins caches whose id is still null; a cache that already has an
        // established id (even though it now names the just-removed table) must be left exactly as-is,
        // matching existing, deliberate behavior that Convert-to-Range never otherwise touches pivot
        // caches.
        cache.SourceTableId.Should().Be(1, "an already-established id must not be altered by Convert-to-Range");
        cache.SourceTableName.Should().Be("SalesTable");
    }

    // --- no-regression sibling: reverting the Convert-to-Range command must restore the orphaned
    // cache's SourceTableId back to null, exactly as it was before the command ran ---

    [Fact]
    public void ConvertToRange_Revert_RestoresOrphanedCacheIdToNull()
    {
        var (workbook, sheet, pivot, cache) = CreateFreshlyLoadedTableBackedPivotWithDecoyTable("PivotIdentityConvertRevertTest");

        cache.SourceTableId.Should().BeNull();

        var convert = new ConvertStructuredTableToRangeCommand(sheet.Id, tableId: 1);
        convert.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        cache.SourceTableId.Should().Be(1, "convert-to-range must pin the freshly-orphaned cache's id so a later decoy rename cannot hijack it");

        convert.Revert(new TestCommandContext(workbook));
        sheet.StructuredTables.Should().Contain(t => t.Id == 1 && t.Name == "SalesTable");
        cache.SourceTableId.Should().BeNull("undoing convert-to-range must restore the cache to its pre-command, unestablished state");
    }

    // --- no-regression sibling: a cache sourced from an entirely different table must be unaffected
    // by Convert-to-Range on an unrelated table, even while both caches are in the unestablished state ---

    [Fact]
    public void ConvertToRange_DoesNotOrphanCacheBelongingToADifferentTable()
    {
        var (workbook, sheet, pivot, cache) = CreateFreshlyLoadedTableBackedPivotWithDecoyTable("PivotIdentityUnrelatedCacheUnaffectedTest");

        // Convert the UNRELATED "DecoyTable" (id=2), not the pivot's real backing table.
        var convert = new ConvertStructuredTableToRangeCommand(sheet.Id, tableId: 2);
        convert.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        // The pivot cache names "SalesTable", not "DecoyTable" -- it must not be touched at all.
        cache.SourceTableId.Should().BeNull("a cache naming an untouched table must not be pinned by an unrelated table's Convert-to-Range");
        cache.SourceTableName.Should().Be("SalesTable");

        var refresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        refresh.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        pivot.SourceRange.Should().Be(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 4)),
            "the still-live SalesTable must keep tracking normally, unaffected by DecoyTable's conversion");
        cache.SourceTableId.Should().Be(1, "the ordinary refresh must still resolve and pin the real SalesTable's id");
    }
}
