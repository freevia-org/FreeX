using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R108: <see cref="Workbook.NextStructuredTableIdWatermark"/> (the R107 fix) is a plain in-memory
/// counter -- there is no field for it in NativeJsonAdapter's WorkbookDto and no equivalent slot in
/// XLSX, so it silently resets to 0 across every save/reload, in BOTH the native .fxl format and XLSX.
/// The durable references it exists to protect DO survive that round-trip, though:
/// <see cref="SlicerModel.SourceTableId"/> round-trips through the real
/// <c>x15:tableSlicerCache/@tableId</c> XLSX attribute and the native-JSON slicer DTO, and
/// <see cref="PivotCacheModel.SourceTableId"/> round-trips through the native-JSON pivot-cache DTO.
/// So a table id that was freed and pinned into one of those bindings before save
/// (<see cref="CommandGuards.PinOrphanedPivotCacheSourceTableIds"/>) would, after a reload with the
/// watermark back at 0, be handed straight back out to a brand-new table -- silently re-binding the
/// dangling slicer/pivot-cache to unrelated data the moment <see
/// cref="SlicerItemResolver.ResolveTableColumnItems"/> or a pivot refresh resolves it.
///
/// The fix: <c>CreateStructuredTableCommand.NextTableId</c> now also floors its result against every
/// live <see cref="SlicerModel.SourceTableId"/> and <see cref="PivotCacheModel.SourceTableId"/>, not
/// just the (possibly-reset) in-memory watermark and the live-table scan -- re-deriving the correct
/// floor from what the file actually persisted.
///
/// These tests drive the real product entry point (<see cref="CreateStructuredTableCommand"/> and
/// <see cref="ConvertStructuredTableToRangeCommand"/>) end to end. A table-slicer binding has no
/// in-app "create" command of its own in this codebase (table slicers are only ever produced by the
/// XLSX/native-JSON loader -- see SlicerModel.SourceTableId's doc comment), so -- mirroring the
/// established pattern in R107_StructuredTableIdWatermarkPreventsReuseTests'
/// CreateFreshlyLoadedTableBackedPivot helper -- the slicer/pivot-cache state that a real reload would
/// restore is hand-built directly to represent "freshly loaded from a file", and the watermark reset
/// that a real reload also causes is applied directly to <see cref="Workbook.NextStructuredTableIdWatermark"/>
/// (which is exactly what NOT writing it in the IO layer amounts to: the property silently comes back
/// at its type default, 0, since nothing ever sets it on load).
/// </summary>
public sealed class R108_StructuredTableIdWatermarkSurvivesReloadTests
{
    [Fact]
    public void CreateStructuredTable_AfterSimulatedReloadWithDanglingSlicerBinding_DoesNotReuseTheFreedId()
    {
        var workbook = new Workbook("TableIdWatermarkReloadSlicerTest");
        var sheet = workbook.AddSheet("Data");
        for (var row = 1u; row <= 3; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"H{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue($"V{row}"));
        }

        var createFirst = new CreateStructuredTableCommand(
            sheet.Id, new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)));
        createFirst.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        var firstId = createFirst.CreatedTableId!.Value;
        firstId.Should().Be(1);

        // A table slicer bound to Table1 -- as it would look immediately after being restored from a
        // saved file (SourceTableId is a pure id-based binding with no in-app creation command; see
        // class doc comment).
        var slicer = new SlicerModel
        {
            Name = "Slicer1",
            CacheName = "Slicer_Slicer1",
            SourceTableId = firstId,
            SourceTableColumnId = 0,
        };
        workbook.Slicers.Add(slicer);

        // The table is converted to a range without ever removing the slicer (Excel allows a
        // slicer to dangle after its source table is gone). This ratchets the in-memory watermark to
        // firstId per CommandGuards.PinOrphanedPivotCacheSourceTableIds, and the slicer's
        // SourceTableId stays pinned at firstId by design -- it has no name fallback of its own.
        var convert = new ConvertStructuredTableToRangeCommand(sheet.Id, firstId);
        convert.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.StructuredTables.Should().BeEmpty("the only table on the sheet was just converted to a range");
        slicer.SourceTableId.Should().Be(firstId, "a dangling table slicer keeps pointing at the removed table's id by design");

        // Simulate save + reload: NextStructuredTableIdWatermark is never written to .fxl or .xlsx, so
        // a real reload leaves it at its type default, 0 -- exactly what resetting it here represents.
        // Everything else (the dangling slicer's SourceTableId above) is left untouched because it DOES
        // round-trip through the real file formats.
        workbook.NextStructuredTableIdWatermark = 0;

        for (var row = 10u; row <= 12; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 5), new TextValue($"H{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 6), new TextValue($"V{row}"));
        }

        var createSecond = new CreateStructuredTableCommand(
            sheet.Id, new GridRange(new CellAddress(sheet.Id, 10, 5), new CellAddress(sheet.Id, 12, 6)));
        createSecond.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        var secondId = createSecond.CreatedTableId!.Value;

        // Bug (before fix): with the watermark reset to 0 and no live tables, NextTableId's live-table
        // scan alone found max=0, so the brand-new table got firstId (1) right back -- silently
        // re-binding the dangling slicer above to this completely unrelated new table.
        secondId.Should().NotBe(firstId,
            "a table id a reloaded slicer still dangles from must never be reissued to a brand-new table");
    }

    [Fact]
    public void CreateStructuredTable_AfterSimulatedReloadWithDanglingPivotCacheBinding_DoesNotReuseTheFreedId()
    {
        var workbook = new Workbook("TableIdWatermarkReloadPivotCacheTest");
        var sheet = workbook.AddSheet("Data");
        for (var row = 1u; row <= 3; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"H{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue($"V{row}"));
        }

        var createFirst = new CreateStructuredTableCommand(
            sheet.Id, new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)));
        createFirst.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        var firstId = createFirst.CreatedTableId!.Value;

        // A pivot cache already pinned to the (about-to-be-removed) table's id, as it would look right
        // after being restored from a saved file post-CommandGuards.PinOrphanedPivotCacheSourceTableIds.
        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.Table,
            SourceTableName = "SomeOtherFreedName",
            SourceTableId = firstId,
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
        };
        workbook.PivotCaches.Add(cache);

        var convert = new ConvertStructuredTableToRangeCommand(sheet.Id, firstId);
        convert.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        // Simulate save + reload: the watermark resets to 0, but the pivot cache's SourceTableId (set
        // above) is left untouched, matching what the native-JSON pivot-cache DTO actually persists.
        workbook.NextStructuredTableIdWatermark = 0;

        for (var row = 10u; row <= 12; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 5), new TextValue($"H{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 6), new TextValue($"V{row}"));
        }

        var createSecond = new CreateStructuredTableCommand(
            sheet.Id, new GridRange(new CellAddress(sheet.Id, 10, 5), new CellAddress(sheet.Id, 12, 6)));
        createSecond.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        createSecond.CreatedTableId!.Value.Should().NotBe(firstId,
            "a table id a reloaded pivot cache still dangles from must never be reissued to a brand-new table");
    }

    // --- no-regression sibling: the ordinary, no-dangling-reference case must keep allocating plain
    // sequential ids, exactly as before this fix (the new scan must not spuriously inflate the floor
    // when every slicer/pivot-cache reference already points at a still-live table) ---

    [Fact]
    public void CreateStructuredTable_WithSlicerAndPivotCacheBoundToLiveTable_StillAllocatesNextSequentialId()
    {
        var workbook = new Workbook("TableIdWatermarkLiveReferencesTest");
        var sheet = workbook.AddSheet("Data");
        for (var row = 1u; row <= 3; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"H{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue($"V{row}"));
        }

        var createFirst = new CreateStructuredTableCommand(
            sheet.Id, new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)));
        createFirst.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        var firstId = createFirst.CreatedTableId!.Value;
        firstId.Should().Be(1);

        // Slicer and pivot cache both correctly bound to the still-live first table -- the ordinary,
        // healthy state, with no orphaning involved at all.
        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Slicer1",
            CacheName = "Slicer_Slicer1",
            SourceTableId = firstId,
            SourceTableColumnId = 0,
        });
        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.Table,
            SourceTableName = "Table1",
            SourceTableId = firstId,
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
        });

        for (var row = 10u; row <= 12; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 5), new TextValue($"H{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 6), new TextValue($"V{row}"));
        }

        var createSecond = new CreateStructuredTableCommand(
            sheet.Id, new GridRange(new CellAddress(sheet.Id, 10, 5), new CellAddress(sheet.Id, 12, 6)));
        createSecond.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        createSecond.CreatedTableId!.Value.Should().Be(firstId + 1,
            "with no dangling reference in play the allocator must still just count up from the live table, unaffected by the new scan");
    }
}
