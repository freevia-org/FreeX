using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R115-commands-pivot-sharedItems-refresh: R114 only recomputed a pivot cache field's SharedItems at
/// pivot creation, "Change Data Source", and (for a table-backed source) a column-count change on
/// refresh. An ORDINARY refresh -- the real F5 / Refresh-All entry point, <c>RefreshPivotTableCommand</c>
/// -- reused the existing field object verbatim for any header that still matched by name, so a field's
/// SharedItems (and therefore <c>SlicerItemResolver.ResolveAvailableItems</c>, the live-UI/render entry
/// point) stayed frozen at whatever it was when the pivot was created, even though the underlying cell
/// values kept changing. This covers BOTH source types the defect named: a plain worksheet-range pivot
/// cache (which never called the reconciliation at all) and a table-backed cache whose header set did
/// not change (which hit the old "already in sync" fast-path early return).
/// </summary>
public sealed class R115_PivotRefreshSharedItemsTests
{
    private static void SeedData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));
    }

    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    /// <summary>
    /// THE anchor test: a plain worksheet-range pivot (no structured table involved at all) whose
    /// header set never changes. Editing an existing cell to introduce a brand-new distinct value,
    /// then calling the real product entry point (<see cref="RefreshPivotTableCommand"/>, exactly what
    /// F5/Refresh-All runs), must make that new value show up in the cache field's SharedItems -- and
    /// therefore in a slicer resolved against it afterward via the same live-UI/render entry point
    /// (<see cref="SlicerItemResolver.ResolveAvailableItems"/>) the R114 tests exercise.
    /// </summary>
    [Fact]
    public void RefreshPivotTableCommand_RangeBackedCache_PicksUpNewDistinctValueForSurvivingField()
    {
        var workbook = new Workbook("R115RangeRefresh");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);

        var addPivot = new AddPivotTableCommand(
            sheet.Id,
            Range(sheet, "A1", "B4"),
            Range(sheet, "D3", "E6"),
            "PivotTable1",
            rowFieldIndexes: [0],
            dataFieldIndexes: [1]);
        addPivot.Apply(ctx).Success.Should().BeTrue();

        var cache = workbook.PivotCaches.Should().ContainSingle().Subject;
        cache.SourceType.Should().Be(PivotCacheSourceType.WorksheetRange);
        var categoryFieldBefore = cache.Fields.Should().ContainSingle(field => field.Name == "Category").Subject;
        categoryFieldBefore.SharedItems!.Should().Equal("A", "B");

        // Introduce a brand-new distinct value into the EXISTING range -- the header set and column
        // count are completely unchanged, exactly the "ordinary refresh" case the defect describes.
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("C"));

        var refresh = new RefreshPivotTableCommand(sheet.Id, "PivotTable1");
        refresh.Apply(ctx).Success.Should().BeTrue();

        var categoryFieldAfter = cache.Fields.Should().ContainSingle(field => field.Name == "Category").Subject;
        categoryFieldAfter.SharedItems.Should().NotBeNull();
        categoryFieldAfter.SharedItems!.Should().Contain("C");

        // Reachability: a slicer added AFTER the refresh (mirroring the real UI flow of refreshing
        // then inserting a filter) must offer the new value through the exact same live-UI/render
        // entry point R114 covered for pivot creation.
        var addSlicer = new AddSlicerCommand("Category Slicer", "PivotTable1", "Category");
        addSlicer.Apply(ctx).Success.Should().BeTrue();
        var slicer = workbook.Slicers.Should().ContainSingle().Subject;

        var resolved = SlicerItemResolver.ResolveAvailableItems(slicer, workbook);
        resolved.Should().Contain("C");
    }

    /// <summary>
    /// No-regression sibling: a table-backed pivot whose header set is unchanged on refresh must still
    /// pick up a new distinct value (the header-count-changed branch already had coverage from R114;
    /// this is the "same headers, values changed" branch of the SAME source type, which used to hit
    /// the old "already in sync" fast-path and skip reconciliation entirely). Constructs the
    /// table-backed cache/pivot by hand (mirroring R104_PivotRefreshTableIdentityTests' helper) since
    /// <see cref="AddPivotTableCommand"/> only ever creates a WorksheetRange-sourced cache.
    /// </summary>
    [Fact]
    public void RefreshPivotTableCommand_TableBackedCache_UnchangedHeaders_StillPicksUpNewDistinctValue()
    {
        var workbook = new Workbook("R115TableRefresh");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "CatTable",
            DisplayName = "CatTable",
            Range = Range(sheet, "A1", "B4"),
            HasAutoFilter = true,
            HeaderRowCount = 1,
        });

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.Table,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:B4",
            SourceTableName = "CatTable",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 3,
        };
        cache.Fields.Add(new PivotCacheFieldModel("Category", ContainsString: true, SharedItems: ["A", "B"], SharedItemKinds: ['s', 's']));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B4"),
            TargetRange = Range(sheet, "D3", "E6"),
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        // Establish the cache's baseline via a real refresh first, exactly like a file that has been
        // refreshed at least once since it was authored (mirrors R104's helper) -- this also pins down
        // cache.SourceTableId so the second refresh below resolves the SAME table.
        new RefreshPivotTableCommand(sheet.Id, "PivotTable1").Apply(ctx).Success.Should().BeTrue();
        cache.Fields.Should().ContainSingle(field => field.Name == "Category").Subject
            .SharedItems!.Should().Equal("A", "B");

        // Same column count, same header names -- only a cell value changes to a brand-new distinct
        // value. This is exactly the case the old fast-path short-circuited.
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("C"));

        var refresh = new RefreshPivotTableCommand(sheet.Id, "PivotTable1");
        refresh.Apply(ctx).Success.Should().BeTrue();

        var categoryFieldAfter = cache.Fields.Should().ContainSingle(field => field.Name == "Category").Subject;
        categoryFieldAfter.SharedItems.Should().NotBeNull();
        categoryFieldAfter.SharedItems!.Should().Contain("C");
    }

    /// <summary>
    /// No-regression sibling: an existing pivot-bound slicer's SharedItems-derived index positions
    /// must not be renumbered by a refresh that discovers a new distinct value -- a naive
    /// full-rebuild-from-scratch would silently shift "B"'s index whenever "C" is discovered earlier
    /// in row order, corrupting any slicer whose CacheItems already reference the old indices. The
    /// merge must APPEND new values, never reorder survivors.
    /// </summary>
    [Fact]
    public void Refresh_NewValueDiscoveredBeforeExistingValueInRowOrder_AppendsWithoutReordering()
    {
        var workbook = new Workbook("R115OrderStability");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);

        var addPivot = new AddPivotTableCommand(
            sheet.Id,
            Range(sheet, "A1", "B4"),
            Range(sheet, "D3", "E6"),
            "PivotTable1",
            rowFieldIndexes: [0],
            dataFieldIndexes: [1]);
        addPivot.Apply(ctx).Success.Should().BeTrue();

        var cache = workbook.PivotCaches.Should().ContainSingle().Subject;
        cache.Fields.Should().ContainSingle(field => field.Name == "Category").Subject
            .SharedItems!.Should().Equal("A", "B");

        // Introduce "C" in ROW 2 (i.e. it will be discovered by a fresh top-to-bottom scan BEFORE the
        // still-present "B" in row 3) -- a naive rebuild-from-scratch would put "C" ahead of "B" in the
        // new SharedItems list, renumbering "B" from index 1 to index 2.
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("C"));

        var refresh = new RefreshPivotTableCommand(sheet.Id, "PivotTable1");
        refresh.Apply(ctx).Success.Should().BeTrue();

        var categoryFieldAfter = cache.Fields.Should().ContainSingle(field => field.Name == "Category").Subject;
        // "A" stays index 0, "B" stays index 1 (both untouched from before) -- "C" is appended at the
        // end (index 2), regardless of where it was discovered in the live row scan.
        categoryFieldAfter.SharedItems!.Should().Equal("A", "B", "C");
    }
}
