using System.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R107-round2: r104/r106/r107 all fixed <see cref="PivotTableRefreshService.Refresh"/>'s null-id NAME
/// fallback by pinning an orphaned table's <see cref="PivotCacheModel.SourceTableId"/> the moment its
/// NAME is freed (Convert to Range / Delete Sheet / a row-or-column delete that consumes the table).
/// Those fixes all assume a <see cref="StructuredTableModel.Id"/>, once handed out, is never handed out
/// again for the lifetime of the in-memory workbook -- otherwise the very id-pinning that protects
/// against a freed NAME collides just as badly against a freed ID.
///
/// Before this fix, <c>CreateStructuredTableCommand.NextTableId</c> computed the next id purely as
/// "the current max id among LIVE tables, plus one". That silently REUSES a freed id the instant the
/// highest-numbered table is removed and a new table is then created: the removed table's id no longer
/// appears among any live table to raise the max, so the same id comes right back out for the new
/// table. A pivot cache that was deliberately pinned to the removed table's now-orphaned id (by any of
/// the r106/r107/R107-round2 fixes) then silently matches the brand-new, completely unrelated table the
/// instant it's created -- defeating the id-based identity those fixes exist to guarantee.
///
/// The fix: <see cref="Workbook.NextStructuredTableIdWatermark"/> tracks the highest id ever handed out
/// this session (never decremented, including on Undo), so <c>NextTableId</c> can never repeat one.
///
/// All tests drive the real product entry points (CreateStructuredTableCommand,
/// ConvertStructuredTableToRangeCommand, RenameStructuredTableCommand, RefreshPivotTableCommand) end to
/// end, never asserting on a hand-built model bypassing the commands.
/// </summary>
public sealed class R107_StructuredTableIdWatermarkPreventsReuseTests
{
    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Pivot, PivotCacheModel Cache) CreateFreshlyLoadedTableBackedPivot(string workbookName)
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
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        cache.Fields.Add(new PivotCacheFieldModel("Units", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 4)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 20, 1), new CellAddress(sheet.Id, 24, 3)),
            ReportLayout = PivotReportLayout.Tabular,
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        return (workbook, sheet, pivot, cache);
    }

    // --- direct watermark assertion: a freshly-created table's id must never repeat one that was
    // already handed out this session, even after the table that held it is gone ---

    [Fact]
    public void CreateStructuredTable_AfterRemovingHighestIdTable_NeverReusesTheFreedId()
    {
        var workbook = new Workbook("TableIdWatermarkDirectTest");
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

        var convert = new ConvertStructuredTableToRangeCommand(sheet.Id, firstId);
        convert.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.StructuredTables.Should().BeEmpty("the only table on the sheet was just converted to a range");

        for (var row = 10u; row <= 12; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 5), new TextValue($"H{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 6), new TextValue($"V{row}"));
        }

        var createSecond = new CreateStructuredTableCommand(
            sheet.Id, new GridRange(new CellAddress(sheet.Id, 10, 5), new CellAddress(sheet.Id, 12, 6)));
        createSecond.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        var secondId = createSecond.CreatedTableId!.Value;

        // Bug (before fix): NextTableId recomputed "max id among LIVE tables" each time -- with no
        // tables left after the convert, that max was 0, so the second table got firstId back.
        secondId.Should().NotBe(firstId, "a freed table id must never be handed out again in this session");
    }

    // --- end-to-end: without the watermark fix, a reused id would let a brand-new, completely
    // unrelated table silently inherit an orphaned pivot cache's id-based binding ---

    [Fact]
    public void Refresh_AfterConvertToRangeThenNewTableCreatedAndRenamedOntoFreedName_FromFreshlyLoadedCache_DoesNotRebindToNewTable()
    {
        var (workbook, sheet, pivot, cache) = CreateFreshlyLoadedTableBackedPivot(
            "PivotIdentityIdWatermarkCollisionTest");

        cache.SourceTableId.Should().BeNull("a cache just loaded from a file has never been refreshed, so no id has been established yet");

        // Step 1: Convert SalesTable (id=1) to a range without ever having refreshed the pivot. This
        // frees both SalesTable's id (1) and its name workbook-wide, and pins the orphaned cache's
        // SourceTableId to 1 per the r106 fix.
        var convert = new ConvertStructuredTableToRangeCommand(sheet.Id, tableId: 1);
        convert.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        cache.SourceTableId.Should().Be(1, "convert-to-range must pin the orphaned cache's id the moment the name is freed");

        // Step 2: the user creates a brand-new, completely unrelated table elsewhere on the sheet.
        sheet.SetCell(new CellAddress(sheet.Id, 10, 6), new TextValue("Widget"));
        sheet.SetCell(new CellAddress(sheet.Id, 10, 7), new TextValue("Color"));
        sheet.SetCell(new CellAddress(sheet.Id, 11, 6), new TextValue("Gadget"));
        sheet.SetCell(new CellAddress(sheet.Id, 11, 7), new TextValue("Blue"));

        var createDecoy = new CreateStructuredTableCommand(
            sheet.Id, new GridRange(new CellAddress(sheet.Id, 10, 6), new CellAddress(sheet.Id, 11, 7)));
        createDecoy.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        var decoyId = createDecoy.CreatedTableId!.Value;

        // Bug (before fix): decoyId would be 1 again (the freed id), immediately colliding with the
        // cache's just-pinned SourceTableId.
        decoyId.Should().NotBe(1, "the new table must not silently inherit the freed table's id");

        // Step 3: the user renames the new table onto the just-freed "SalesTable" name too, for good
        // measure -- realistic, since Excel's own auto-naming ("Table2") would otherwise never collide
        // by name either, but the id collision alone is already fatal without the watermark fix.
        var rename = new RenameStructuredTableCommand(sheet.Id, tableId: decoyId, newName: "SalesTable");
        rename.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        var decoyTable = sheet.StructuredTables.Single(t => t.Id == decoyId);
        decoyTable.Name.Should().Be("SalesTable");

        // Step 4: the FIRST ordinary refresh trigger since the file was opened.
        var refresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        refresh.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        // Bug (before fix): cache.SourceTableId (1) would resolve via FindStructuredTableById straight
        // to the new decoy table (which would also carry id 1), silently rebinding the pivot to its
        // unrelated 2x2 data instead of leaving the pivot's last-known SalesTable extent untouched.
        cache.SourceTableId.Should().Be(1, "the cache must keep pointing at the orphaned id, never the decoy's");
        pivot.SourceRange.Should().NotBe(decoyTable.Range, "the pivot must not silently graft onto the decoy table's unrelated range");
    }

    // --- no-regression sibling: Undo of the table creation must not roll the watermark back down,
    // since that would reopen exactly the collision this fix closes on a create/undo/create cycle ---

    [Fact]
    public void CreateStructuredTable_UndoThenCreateAgain_StillNeverReusesAnId()
    {
        var workbook = new Workbook("TableIdWatermarkUndoRedoTest");
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

        createFirst.Revert(new TestCommandContext(workbook));
        sheet.StructuredTables.Should().BeEmpty("undo must remove the created table");

        for (var row = 10u; row <= 12; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 5), new TextValue($"H{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 6), new TextValue($"V{row}"));
        }

        var createSecond = new CreateStructuredTableCommand(
            sheet.Id, new GridRange(new CellAddress(sheet.Id, 10, 5), new CellAddress(sheet.Id, 12, 6)));
        createSecond.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        createSecond.CreatedTableId!.Value.Should().NotBe(firstId, "the watermark must not roll back on Undo either");
    }
}
