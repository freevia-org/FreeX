using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R109-structured-table-id-watermark-real-persistence: r107 stopped structured-table id reuse with
/// <see cref="Workbook.NextStructuredTableIdWatermark"/>, a session-long in-memory counter. r108 found
/// (correctly) that the counter itself is never persisted, and shipped a fix in
/// <c>CreateStructuredTableCommand.NextTableId</c> that floors the next id against every LIVE
/// <see cref="SlicerModel.SourceTableId"/> and <see cref="PivotCacheModel.SourceTableId"/> instead --
/// re-deriving the watermark from whatever the file actually persisted.
///
/// This round re-verified that claim against the REAL adapters (not the hand-built
/// "as if reloaded" state <c>R108_StructuredTableIdWatermarkSurvivesReloadTests</c> uses) and found it
/// was only half true:
/// <list type="bullet">
/// <item><see cref="SlicerModel.SourceTableId"/> genuinely round-trips through both XLSX
/// (x15:tableSlicerCache/@tableId) and native .fxl (the slicer DTO already had the field) -- r108's
/// claim holds here, proven below by a REAL Save/Load round trip.</item>
/// <item><see cref="PivotCacheModel.SourceTableId"/> did NOT round-trip through either format: the
/// native-JSON <c>PivotCacheDto</c> had no field for it at all (XLSX never had a schema-valid slot for
/// it either -- OOXML's pivotCacheDefinition worksheetSource only carries a name). r108's own comment
/// claiming it "round-trips through the native JSON pivot-cache DTO" was checked against the DTO and
/// was false. This round added the missing field to the native-JSON DTO (our own format, not OOXML --
/// no schema invention involved) so it now genuinely round-trips there. XLSX still has no home for it;
/// see the class doc note on <see cref="XlsxPivotCache_SourceTableId_NeverRoundTrips_ButThatIsSafeByDesign"/>
/// for why that is not a gap.</item>
/// </list>
/// </summary>
public sealed class R109_StructuredTableIdWatermarkPersistenceTests
{
    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Ctx) BuildWorkbookWithOneTable()
    {
        var workbook = new Workbook("R109WatermarkPersistence");
        var sheet = workbook.AddSheet("Data");
        var ctx = new TestCommandContext(workbook);
        for (var row = 1u; row <= 3; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"H{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue($"V{row}"));
        }

        var create = new CreateStructuredTableCommand(
            sheet.Id, new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)));
        create.Apply(ctx).Success.Should().BeTrue();
        create.CreatedTableId!.Value.Should().Be(1);

        return (workbook, sheet, ctx);
    }

    private static void AddMoreRows(Sheet sheet)
    {
        for (var row = 10u; row <= 12; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 5), new TextValue($"H{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 6), new TextValue($"V{row}"));
        }
    }

    // ── Slicer vector: real Save/Load through BOTH adapters, not a hand-built "as if reloaded" state ──

    [Fact]
    public void NativeJson_DanglingSlicerBinding_RealSaveAndLoad_BlocksIdReuseOnNewTable()
    {
        var (workbook, sheet, ctx) = BuildWorkbookWithOneTable();
        var firstId = sheet.StructuredTables[0].Id;

        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Slicer1",
            CacheName = "Slicer_Slicer1",
            SourceTableId = firstId,
            SourceTableColumnId = 0,
        });

        var convert = new ConvertStructuredTableToRangeCommand(sheet.Id, firstId);
        convert.Apply(ctx).Success.Should().BeTrue();
        sheet.StructuredTables.Should().BeEmpty();

        // Real Save, real fresh Load -- a brand-new Workbook object, not the same in-memory instance.
        using var saved = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, saved);
        saved.Position = 0;
        var reloaded = new NativeJsonAdapter().Load(saved);

        var reloadedSlicer = reloaded.Slicers.Should().ContainSingle().Subject;
        reloadedSlicer.SourceTableId.Should().Be(firstId,
            "SlicerModel.SourceTableId must genuinely round-trip through native .fxl");
        reloaded.NextStructuredTableIdWatermark.Should().Be(0,
            "the in-memory watermark itself is never persisted -- this is the exact condition the floor-fold must compensate for");

        var reloadedSheet = reloaded.GetSheetAt(0);
        AddMoreRows(reloadedSheet);
        var reloadedCtx = new TestCommandContext(reloaded);
        var createSecond = new CreateStructuredTableCommand(
            reloadedSheet.Id, new GridRange(new CellAddress(reloadedSheet.Id, 10, 5), new CellAddress(reloadedSheet.Id, 12, 6)));
        createSecond.Apply(reloadedCtx).Success.Should().BeTrue();

        createSecond.CreatedTableId!.Value.Should().NotBe(firstId,
            "a real native .fxl save+load with a dangling slicer must still block reissuing the freed id");
    }

    [Fact]
    public void Xlsx_DanglingSlicerBinding_RealSaveAndLoad_BlocksIdReuseOnNewTable()
    {
        var (workbook, sheet, ctx) = BuildWorkbookWithOneTable();
        var firstId = sheet.StructuredTables[0].Id;

        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Slicer1",
            CacheName = "Slicer_Slicer1",
            SourceTableId = firstId,
            SourceTableColumnId = 0,
        });

        var convert = new ConvertStructuredTableToRangeCommand(sheet.Id, firstId);
        convert.Apply(ctx).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);

        var reloadedSlicer = reloaded.Slicers.Should().ContainSingle().Subject;
        reloadedSlicer.SourceTableId.Should().Be(firstId,
            "SlicerModel.SourceTableId must genuinely round-trip through real XLSX (x15:tableSlicerCache/@tableId)");

        var reloadedSheet = reloaded.GetSheetAt(0);
        AddMoreRows(reloadedSheet);
        var reloadedCtx = new TestCommandContext(reloaded);
        var createSecond = new CreateStructuredTableCommand(
            reloadedSheet.Id, new GridRange(new CellAddress(reloadedSheet.Id, 10, 5), new CellAddress(reloadedSheet.Id, 12, 6)));
        createSecond.Apply(reloadedCtx).Success.Should().BeTrue();

        createSecond.CreatedTableId!.Value.Should().NotBe(firstId,
            "a real XLSX save+load with a dangling slicer must still block reissuing the freed id");
    }

    // ── Pivot-cache vector: R109's actual behaviour change (native .fxl only) ──

    [Fact]
    public void NativeJson_DanglingPivotCacheBinding_RealSaveAndLoad_PersistsSourceTableIdAndBlocksIdReuse()
    {
        var (workbook, sheet, ctx) = BuildWorkbookWithOneTable();
        var firstId = sheet.StructuredTables[0].Id;
        var tableName = sheet.StructuredTables[0].Name;

        // SourceTableId starts null and SourceTableName matches the table -- exactly the "never
        // refreshed since load" starting state CommandGuards.PinOrphanedPivotCacheSourceTableIds
        // targets, so the pin below happens through the REAL command path, not a hand-set field.
        var cache109 = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.Table,
            SourceTableName = tableName,
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
        };
        // Real pivot caches always carry a Fields entry per source column; XlsxPivotTableWriter's
        // record-generation step needs at least one to produce a schema-valid cache part.
        cache109.Fields.Add(new PivotCacheFieldModel("H1"));
        cache109.Fields.Add(new PivotCacheFieldModel("V1"));
        workbook.PivotCaches.Add(cache109);

        var convert = new ConvertStructuredTableToRangeCommand(sheet.Id, firstId);
        convert.Apply(ctx).Success.Should().BeTrue();

        var cache = workbook.PivotCaches.Should().ContainSingle().Subject;
        cache.SourceTableId.Should().Be(firstId,
            "CommandGuards.PinOrphanedPivotCacheSourceTableIds must have pinned it in memory before save");

        using var saved = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, saved);
        saved.Position = 0;
        var reloaded = new NativeJsonAdapter().Load(saved);

        var reloadedCache = reloaded.PivotCaches.Should().ContainSingle().Subject;
        reloadedCache.SourceTableId.Should().Be(firstId,
            "R109: PivotCacheModel.SourceTableId must now round-trip through the native .fxl pivot-cache DTO " +
            "(it did not before this round -- the DTO had no field for it)");

        var reloadedSheet = reloaded.GetSheetAt(0);
        AddMoreRows(reloadedSheet);
        var reloadedCtx = new TestCommandContext(reloaded);
        var createSecond = new CreateStructuredTableCommand(
            reloadedSheet.Id, new GridRange(new CellAddress(reloadedSheet.Id, 10, 5), new CellAddress(reloadedSheet.Id, 12, 6)));
        createSecond.Apply(reloadedCtx).Success.Should().BeTrue();

        createSecond.CreatedTableId!.Value.Should().NotBe(firstId,
            "a real native .fxl save+load with a dangling pivot-cache binding must now also block reissuing the freed id");
    }

    /// <summary>
    /// Documents (does not "fix", because nothing is broken) that XLSX has no schema-valid home for
    /// PivotCacheModel.SourceTableId -- a pivot cache loaded from a real XLSX file always comes back
    /// with SourceTableId null, even when it was pinned to a freed id in memory before save. This is
    /// safe rather than dangerous: PivotTableRefreshService.Refresh only ever fills a null
    /// SourceTableId in FROM A CURRENTLY-LIVE TABLE (resolved by name), never from a stale id, so a
    /// null SourceTableId can never itself resolve back to a freed id after reload. There is nothing
    /// durable dangling on this path for the watermark/floor scheme to need to protect -- the only
    /// residual exposure (a brand-new table coincidentally reusing the freed table's NAME) is a
    /// pre-existing, already-documented name-collision case orthogonal to id reuse (see
    /// PivotTableRefreshService.Refresh's own doc comment), not something this round's scope covers.
    /// </summary>
    [Fact]
    public void XlsxPivotCache_SourceTableId_NeverRoundTrips_ButThatIsSafeByDesign()
    {
        var (workbook, sheet, ctx) = BuildWorkbookWithOneTable();
        var firstId = sheet.StructuredTables[0].Id;
        var tableName = sheet.StructuredTables[0].Name;

        var cache109 = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.Table,
            SourceTableName = tableName,
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
        };
        // Real pivot caches always carry a Fields entry per source column; XlsxPivotTableWriter's
        // record-generation step needs at least one to produce a schema-valid cache part.
        cache109.Fields.Add(new PivotCacheFieldModel("H1"));
        cache109.Fields.Add(new PivotCacheFieldModel("V1"));
        workbook.PivotCaches.Add(cache109);

        // XlsxPivotTableWriter.Save only runs (and therefore only writes the pivotCache part at all)
        // when the workbook actually has a live PivotTable referencing a cache -- a cache with no
        // PivotTable is never persisted at all in XLSX, independent of anything this round changed. A
        // minimal live PivotTable is required here purely to get the cache itself onto disk so this
        // test can observe what happens to SourceTableId specifically.
        sheet.PivotTables.Add(new PivotTableModel
        {
            Name = "Pivot1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 20, 1), new CellAddress(sheet.Id, 22, 2)),
        });

        var convert = new ConvertStructuredTableToRangeCommand(sheet.Id, firstId);
        convert.Apply(ctx).Success.Should().BeTrue();
        workbook.PivotCaches.Single().SourceTableId.Should().Be(firstId, "pinned in memory before save");

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);

        var reloadedCache = reloaded.PivotCaches.Should().ContainSingle().Subject;
        reloadedCache.SourceTableId.Should().BeNull(
            "OOXML's pivotCacheDefinition worksheetSource has no id attribute -- this field genuinely " +
            "cannot round-trip through XLSX without inventing a non-native schema extension");
        reloadedCache.SourceTableName.Should().Be(tableName,
            "the name-based fallback is what actually survives XLSX -- and it is the safe one, since it " +
            "only ever resolves to a currently-live table, never a freed id");
    }

    // ── Cell-patch fast save: proven categorically unreachable whenever a slicer is present, so it
    // can never be the vector that silently drops the persisted id-anchor for THIS bug class. ──

    /// <summary>
    /// Discovered while building this round's real-adapter coverage: <c>WorkbookHasPatchUnsafePivotFeatures</c>
    /// (XlsxFileAdapter.SourcePackageSnapshot.cs) blocks the fast cell-patch path for the WHOLE
    /// workbook unconditionally whenever ANY slicer or timeline exists (<c>workbook.Slicers.Count > 0
    /// || workbook.Timelines.Count > 0</c>) -- not just while a structural table edit is pending, but
    /// for every subsequent plain cell edit too, for as long as the slicer exists. So the scenario
    /// "does the id-reuse guarantee survive a cell-patch save" is moot for the slicer vector: cell-patch
    /// save is never reachable at all while the dangling slicer that pins the freed id is still present
    /// -- every save that could disturb it is forced through the full ClosedXML rebuild instead, the
    /// same categorical-unreachability pattern <see cref="R101_DrawingChartHyperlinkPatchSafetyGuardTests"/>'s
    /// R106/R108 audits already established for other structural drawing/table changes. This test proves
    /// that directly against the real adapter rather than asserting it from reading the guard's source.
    /// </summary>
    [Fact]
    public void Xlsx_WithDanglingSlicerBinding_CellPatchFastSaveIsCategoricallyUnreachable()
    {
        var (workbook, sheet, ctx) = BuildWorkbookWithOneTable();
        var firstId = sheet.StructuredTables[0].Id;

        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Slicer1",
            CacheName = "Slicer_Slicer1",
            SourceTableId = firstId,
            SourceTableColumnId = 0,
        });

        var convert = new ConvertStructuredTableToRangeCommand(sheet.Id, firstId);
        convert.Apply(ctx).Success.Should().BeTrue();

        // First save is a full rebuild (fresh workbook, no source package yet).
        var adapter = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        // Load it back as a real loaded-from-file package and attempt to make it patch-eligible,
        // mirroring the "open the file, edit a cell, hit Ctrl+S" flow the fast cell-patch path exists
        // for. This must FAIL, and for the specific reason "workbook_postprocessing_pivots" -- proving
        // the dangling slicer itself is what keeps this workbook permanently on the full-rebuild path.
        var reloaded = adapter.Load(saved);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(reloaded, out var blockReason)
            .Should().BeFalse("a workbook with any live slicer is unconditionally cell-patch-ineligible");
        blockReason.Should().Be("workbook_postprocessing_pivots");

        // A plain literal cell edit therefore still goes through the full rebuild, not the patch path --
        // confirmed via LastSaveDiagnostics rather than assumed.
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.SetCell(new CellAddress(reloadedSheet.Id, 1, 1), new TextValue("Edited"));
        using var resaved = new MemoryStream();
        adapter.Save(reloaded, resaved);
        adapter.LastSaveDiagnostics.PathLabel.Should().Be("full_save",
            "with a live slicer present, even an ordinary cell edit must take the full rebuild -- the " +
            "cell-patch path this test class is otherwise probing is unreachable for as long as the " +
            "dangling slicer that pins the freed table id is present");

        resaved.Position = 0;
        var reloadedAgain = new XlsxFileAdapter().Load(resaved);
        reloadedAgain.Slicers.Should().ContainSingle().Subject.SourceTableId.Should().Be(firstId);

        var finalSheet = reloadedAgain.GetSheetAt(0);
        AddMoreRows(finalSheet);
        var finalCtx = new TestCommandContext(reloadedAgain);
        var createSecond = new CreateStructuredTableCommand(
            finalSheet.Id, new GridRange(new CellAddress(finalSheet.Id, 10, 5), new CellAddress(finalSheet.Id, 12, 6)));
        createSecond.Apply(finalCtx).Success.Should().BeTrue();

        createSecond.CreatedTableId!.Value.Should().NotBe(firstId,
            "even routed exclusively through full rebuilds, the id-reuse guarantee must still hold");
    }

    // ── Excel-authored file: no watermark, no dangling reference, just plain live-table max+1 ──

    [Fact]
    public void XlsxLoadedWorkbook_WithNonContiguousTableIds_AllocatesAboveTheHighestExistingId()
    {
        // Simulates a file produced by real Excel (or by this app after earlier table
        // creates/deletes): NextStructuredTableIdWatermark is always 0 fresh out of any Load, and the
        // live tables' ids are not contiguous from 1 (Excel does not renumber surviving tables when an
        // earlier one is deleted).
        var workbook = new Workbook("ExcelStyleGaps");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 5,
            Name = "Table5",
            DisplayName = "Table5",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            Columns = { new StructuredTableColumnModel(1, "A") },
        });

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);

        reloaded.NextStructuredTableIdWatermark.Should().Be(0, "the watermark is always 0 fresh out of Load");
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.StructuredTables.Should().ContainSingle().Which.Id.Should().Be(5);

        reloadedSheet.SetCell(new CellAddress(reloadedSheet.Id, 3, 1), new TextValue("H"));
        reloadedSheet.SetCell(new CellAddress(reloadedSheet.Id, 4, 1), new TextValue("V"));
        var createSecond = new CreateStructuredTableCommand(
            reloadedSheet.Id, new GridRange(new CellAddress(reloadedSheet.Id, 3, 1), new CellAddress(reloadedSheet.Id, 4, 1)));
        createSecond.Apply(new TestCommandContext(reloaded)).Success.Should().BeTrue();

        createSecond.CreatedTableId!.Value.Should().Be(6,
            "the allocator must floor against the live table's actual id (5), not the number of tables (1)");
    }
}
