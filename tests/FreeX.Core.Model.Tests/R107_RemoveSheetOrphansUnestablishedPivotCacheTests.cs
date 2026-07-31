using System.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R107: r106's <see cref="ConvertStructuredTableToRangeCommand"/> fix pins an unestablished
/// table-backed <see cref="PivotCacheModel"/>'s <see cref="PivotCacheModel.SourceTableId"/> the moment
/// its source table's name is freed, so a later, unrelated table renamed onto that freed name can never
/// silently hijack the pivot via <see cref="PivotTableRefreshService.Refresh"/>'s null-id name fallback.
/// But <see cref="RemoveSheetCommand"/> ("Delete Sheet") is a SECOND, ordinary way to free a structured
/// table's name -- deleting the sheet that hosts the table takes the whole <see cref="Sheet"/> object
/// (and its StructuredTables) out of the workbook, freeing the table's name workbook-wide, exactly like
/// Convert-to-Range does -- and until this fix, RemoveSheetCommand's pivot-cache handling only cleared
/// <see cref="PivotCacheModel.SourceSheetName"/> / snapshotted <see cref="PivotCacheModel.RawRecordsXml"/>,
/// never touching <see cref="PivotCacheModel.SourceTableId"/>. Since every table-backed cache freshly
/// loaded from a real .xlsx (or FreeX's native JSON) starts with SourceTableId == null (OOXML's
/// pivotCacheDefinition carries only the source table's name, and WorkbookOpenService never calls
/// PivotTableRefreshService.Refresh at open time), that is the NORMAL starting state, not an edge case --
/// so deleting the source sheet reopened the exact r104/r106 silent-rebind hole through this second path.
///
/// The fix: RemoveSheetCommand now pins any table-backed cache's SourceTableId to the removed table's own
/// (now-orphaned) id, at the moment the delete frees its name, mirroring ConvertStructuredTableToRangeCommand.
///
/// All tests drive the real product entry points (RemoveSheetCommand, RenameStructuredTableCommand,
/// RefreshPivotTableCommand) end to end, never asserting on a hand-built model bypassing the commands.
/// </summary>
public sealed class R107_RemoveSheetOrphansUnestablishedPivotCacheTests
{
    private static (Workbook Workbook, Sheet DataSheet, Sheet ReportSheet, PivotTableModel Pivot, PivotCacheModel Cache) CreateFreshlyLoadedTableBackedPivotAcrossSheetsWithDecoyTable(string workbookName)
    {
        var workbook = new Workbook(workbookName);
        var dataSheet = workbook.AddSheet("Data");

        // The pivot's real backing table: "SalesTable" at A1:D5 on the Data sheet.
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 1), new TextValue("Region"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 2), new TextValue("Quarter"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 3), new TextValue("Amount"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 4), new TextValue("Units"));

        dataSheet.SetCell(new CellAddress(dataSheet.Id, 2, 1), new TextValue("East"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 2, 2), new TextValue("Q1"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 2, 3), new NumberValue(10));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 2, 4), new NumberValue(2));

        dataSheet.SetCell(new CellAddress(dataSheet.Id, 3, 1), new TextValue("East"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 3, 2), new TextValue("Q2"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 3, 3), new NumberValue(15));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 3, 4), new NumberValue(3));

        dataSheet.SetCell(new CellAddress(dataSheet.Id, 4, 1), new TextValue("West"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 4, 2), new TextValue("Q1"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 4, 3), new NumberValue(20));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 4, 4), new NumberValue(4));

        dataSheet.SetCell(new CellAddress(dataSheet.Id, 5, 1), new TextValue("West"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 5, 2), new TextValue("Q2"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 5, 3), new NumberValue(25));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 5, 4), new NumberValue(5));

        dataSheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "SalesTable",
            DisplayName = "SalesTable",
            Range = new GridRange(new CellAddress(dataSheet.Id, 1, 1), new CellAddress(dataSheet.Id, 5, 4)),
            HasAutoFilter = true,
            HeaderRowCount = 1,
        });

        // The pivot itself, and a completely unrelated second table ("DecoyTable"), both live on a
        // SEPARATE, surviving sheet -- this is what makes RemoveSheetCommand (deleting only the Data
        // sheet) the reachable path here, distinct from R106's same-sheet Convert-to-Range scenario.
        var reportSheet = workbook.AddSheet("Report");

        reportSheet.SetCell(new CellAddress(reportSheet.Id, 1, 11), new TextValue("Widget"));
        reportSheet.SetCell(new CellAddress(reportSheet.Id, 1, 12), new TextValue("Color"));
        reportSheet.SetCell(new CellAddress(reportSheet.Id, 2, 11), new TextValue("Gadget"));
        reportSheet.SetCell(new CellAddress(reportSheet.Id, 2, 12), new TextValue("Blue"));

        reportSheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 2,
            Name = "DecoyTable",
            DisplayName = "DecoyTable",
            Range = new GridRange(new CellAddress(reportSheet.Id, 1, 11), new CellAddress(reportSheet.Id, 2, 12)),
            HasAutoFilter = true,
            HeaderRowCount = 1,
        });

        // Exactly the state a freshly-opened .xlsx/.fxl is in: the cache carries only the source
        // table's NAME and its source SHEET name -- SourceTableId is null because nothing has
        // refreshed this pivot since the workbook was loaded (WorkbookOpenService never calls
        // PivotTableRefreshService.Refresh at open time). Deliberately NOT calling
        // RefreshPivotTableCommand here, unlike R104's fixture -- that call would establish the id and
        // sidestep the exact vulnerable state this test targets.
        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.Table,
            SourceSheetName = dataSheet.Name,
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
            SourceRange = new GridRange(new CellAddress(dataSheet.Id, 1, 1), new CellAddress(dataSheet.Id, 5, 4)),
            TargetRange = new GridRange(new CellAddress(reportSheet.Id, 1, 1), new CellAddress(reportSheet.Id, 5, 3)),
            ReportLayout = PivotReportLayout.Tabular,
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        reportSheet.PivotTables.Add(pivot);

        return (workbook, dataSheet, reportSheet, pivot, cache);
    }

    // --- bug case: deleting the pivot's source sheet is a second, ordinary way to free a table's name,
    // and the load-then-never-refreshed state (SourceTableId still null) is the NORMAL starting
    // condition for every real workbook, so it must be just as protected as R106's same-sheet
    // Convert-to-Range path ---

    [Fact]
    public void Refresh_AfterDeleteSourceSheetThenUnrelatedTableRenamedOntoFreedName_FromFreshlyLoadedCache_DoesNotRebindToUnrelatedTable()
    {
        var (workbook, dataSheet, reportSheet, pivot, cache) = CreateFreshlyLoadedTableBackedPivotAcrossSheetsWithDecoyTable("PivotIdentityLoadThenDeleteSheetRenameCollisionTest");

        cache.SourceTableId.Should().BeNull("a cache just loaded from a file has never been refreshed, so no id has been established yet");

        // Step 1: the user deletes the Data sheet (right-click tab > Delete) -- a completely ordinary
        // action -- without ever having refreshed the pivot since opening the file. This frees
        // "SalesTable"'s name workbook-wide, since the whole Sheet object (and its StructuredTables
        // collection) goes with the delete.
        var remove = new RemoveSheetCommand(dataSheet.Id);
        remove.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        workbook.Sheets.Should().NotContain(s => s.Id == dataSheet.Id);
        cache.SourceTableName.Should().Be("SalesTable", "deleting the sheet must not touch the cache's source name");

        // Step 2: the user renames the completely unrelated "DecoyTable" (on the surviving Report
        // sheet) so it now reuses the just-freed "SalesTable" name.
        var rename = new RenameStructuredTableCommand(reportSheet.Id, tableId: 2, newName: "SalesTable");
        rename.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        reportSheet.StructuredTables.Single(t => t.Id == 2).Name.Should().Be("SalesTable");

        // Step 3: the FIRST ordinary refresh trigger since the file was opened (Refresh button, filter
        // change, layout change, adding a slicer) reaches PivotTableRefreshService.Refresh via
        // RefreshPivotTableCommand.
        var refresh = new RefreshPivotTableCommand(reportSheet.Id, pivot.Name);
        var outcome = refresh.Apply(new TestCommandContext(workbook));
        outcome.Success.Should().BeTrue();

        // Bug (before fix): the cache's SourceTableId was still null going into this refresh, so
        // PivotTableRefreshService.Refresh fell back to FindStructuredTableByName("SalesTable"), which
        // matched the RENAMED "DecoyTable" (id=2) purely by its new name -- the refresh unconditionally
        // repointed the pivot at it: pivot.SourceRange became DecoyTable's tiny 2x2-shaped range
        // instead of the real SalesTable's A1:D5, and cache.SourceReference/SourceTableId were
        // overwritten to match the decoy.
        cache.SourceTableId.Should().NotBe(2, "the cache must never be pinned to the decoy table's id");
        pivot.SourceRange.Should().NotBe(
            new GridRange(new CellAddress(reportSheet.Id, 1, 11), new CellAddress(reportSheet.Id, 2, 12)),
            "the pivot must not silently graft onto DecoyTable's unrelated range");
    }

    // --- no-regression sibling: a cache that already has an established SourceTableId (R104's
    // post-first-refresh state) must be completely untouched by RemoveSheetCommand's new orphaning
    // logic ---

    [Fact]
    public void RemoveSheet_OnSheetHostingTableWithAlreadyEstablishedCacheId_DoesNotAlterThatId()
    {
        var (workbook, dataSheet, reportSheet, pivot, cache) = CreateFreshlyLoadedTableBackedPivotAcrossSheetsWithDecoyTable("PivotIdentityAlreadyEstablishedIdDeleteSheetTest");

        // Establish the id via a real refresh first, exactly like R104's fixture -- this is the
        // post-first-refresh state the original r104 fix already protects.
        var initialRefresh = new RefreshPivotTableCommand(reportSheet.Id, pivot.Name);
        initialRefresh.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        cache.SourceTableId.Should().Be(1);

        var remove = new RemoveSheetCommand(dataSheet.Id);
        remove.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        // The new orphaning logic only pins caches whose id is still null; a cache that already has an
        // established id (even though it now names a table that no longer exists anywhere) must be left
        // exactly as-is, matching ConvertStructuredTableToRangeCommand's existing, deliberate behavior.
        cache.SourceTableId.Should().Be(1, "an already-established id must not be altered by deleting the sheet");
        cache.SourceTableName.Should().Be("SalesTable");
    }

    // --- no-regression sibling: reverting the Delete Sheet command must restore the orphaned cache's
    // SourceTableId back to null, exactly as it was before the command ran ---

    [Fact]
    public void RemoveSheet_Revert_RestoresOrphanedCacheIdToNull()
    {
        var (workbook, dataSheet, reportSheet, pivot, cache) = CreateFreshlyLoadedTableBackedPivotAcrossSheetsWithDecoyTable("PivotIdentityDeleteSheetRevertTest");

        cache.SourceTableId.Should().BeNull();

        var remove = new RemoveSheetCommand(dataSheet.Id);
        remove.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        cache.SourceTableId.Should().Be(1, "deleting the sheet must pin the freshly-orphaned cache's id so a later decoy rename cannot hijack it");

        remove.Revert(new TestCommandContext(workbook));
        workbook.Sheets.Should().Contain(s => s.Id == dataSheet.Id);
        cache.SourceTableId.Should().BeNull("undoing the delete must restore the cache to its pre-command, unestablished state");
    }
}
