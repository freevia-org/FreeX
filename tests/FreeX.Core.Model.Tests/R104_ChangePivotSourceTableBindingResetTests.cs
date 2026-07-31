using System.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R104-sibling: <see cref="ChangePivotTableSourceCommand"/> ("Change PivotTable Data Source") never
/// reset a pivot cache's table binding (<see cref="PivotCacheModel.SourceType"/> /
/// <see cref="PivotCacheModel.SourceTableName"/> / <see cref="PivotCacheModel.SourceTableId"/>) when the
/// user explicitly redirected a table-backed pivot to a plain worksheet range (or to a different table).
/// The cache stayed typed as Table with the OLD table's name/id, so the very next refresh -- including
/// the one this command itself triggers at the end of Apply -- re-derived pivot.SourceRange from that
/// stale table via <c>PivotTableRefreshService.Refresh</c>'s table-tracking block (R104), silently
/// discarding the user's explicit redirect and putting the OLD table's data back, with no warning.
///
/// This is worse after the R104 SourceTableId fix in one sense: the stale binding is now STICKIER,
/// since PivotTableRefreshService no longer drops it just because a name changed -- it keeps resolving
/// by id even through unrelated renames. Nothing in this class re-derives the pivot from that stale id
/// unless ChangePivotTableSourceCommand itself is left to leave it behind, which is exactly the defect
/// fixed here.
///
/// The fix makes ChangePivotTableSourceCommand always reconcile the cache's table binding to the NEW
/// source: cleared entirely when the new range isn't a live table's exact extent, established/updated
/// to the new table when it is. All tests drive the real product entry points end to end
/// (ChangePivotTableSourceCommand, then a real RefreshPivotTableCommand and/or ResizeStructuredTableCommand)
/// and assert the pivot's rendered DATA, not just the cache's fields.
/// </summary>
public sealed class R104_ChangePivotSourceTableBindingResetTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    /// <summary>
    /// A workbook with a table-backed pivot on "SalesTable" (A1:D5, Amount sums to 70), a second live
    /// "OtherTable" (H1:K3, Amount sums to 900) the pivot is NOT bound to, and a plain (non-table) range
    /// N1:Q3 (Amount sums to 300) that isn't part of any structured table. The pivot's cache has already
    /// been refreshed once so its SourceTableId is established (pinned to SalesTable's stable id),
    /// exactly like a real file that's been refreshed at least once since load.
    /// </summary>
    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Pivot, PivotCacheModel Cache) CreateTableBackedPivotWithAlternatives(string workbookName)
    {
        var workbook = new Workbook(workbookName);
        var sheet = workbook.AddSheet("Data");

        // SalesTable: A1:D5, Amount sums to 10+15+20+25 = 70.
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "D1"), new TextValue("Units"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "D2"), new NumberValue(2));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(15));
        sheet.SetCell(Addr(sheet, "D3"), new NumberValue(3));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B4"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C4"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "D4"), new NumberValue(4));
        sheet.SetCell(Addr(sheet, "A5"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B5"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C5"), new NumberValue(25));
        sheet.SetCell(Addr(sheet, "D5"), new NumberValue(5));

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "SalesTable",
            DisplayName = "SalesTable",
            Range = Range(sheet, "A1", "D5"),
            HasAutoFilter = true,
            HeaderRowCount = 1,
        });

        // OtherTable: H1:K3, Amount sums to 400+500 = 900.
        sheet.SetCell(Addr(sheet, "H1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "I1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "J1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "K1"), new TextValue("Units"));
        sheet.SetCell(Addr(sheet, "H2"), new TextValue("North"));
        sheet.SetCell(Addr(sheet, "I2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "J2"), new NumberValue(400));
        sheet.SetCell(Addr(sheet, "K2"), new NumberValue(40));
        sheet.SetCell(Addr(sheet, "H3"), new TextValue("South"));
        sheet.SetCell(Addr(sheet, "I3"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "J3"), new NumberValue(500));
        sheet.SetCell(Addr(sheet, "K3"), new NumberValue(50));

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 2,
            Name = "OtherTable",
            DisplayName = "OtherTable",
            Range = Range(sheet, "H1", "K3"),
            HasAutoFilter = true,
            HeaderRowCount = 1,
        });

        // A plain (non-table) range, N1:Q3, Amount sums to 100+200 = 300.
        sheet.SetCell(Addr(sheet, "N1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "O1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "P1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "Q1"), new TextValue("Units"));
        sheet.SetCell(Addr(sheet, "N2"), new TextValue("North"));
        sheet.SetCell(Addr(sheet, "O2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "P2"), new NumberValue(100));
        sheet.SetCell(Addr(sheet, "Q2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "N3"), new TextValue("South"));
        sheet.SetCell(Addr(sheet, "O3"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "P3"), new NumberValue(200));
        sheet.SetCell(Addr(sheet, "Q3"), new NumberValue(20));

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
            SourceRange = Range(sheet, "A1", "D5"),
            TargetRange = Range(sheet, "A20", "C25"),
            ReportLayout = PivotReportLayout.Tabular,
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        // Establish cache.SourceTableId via a real refresh, matching a file that's been refreshed at
        // least once since it was loaded/created.
        var initialRefresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        initialRefresh.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        return (workbook, sheet, pivot, cache);
    }

    private static double? ReadGrandTotalAmount(Sheet sheet, PivotTableModel pivot)
    {
        // Tabular layout, one row field + one data field: Grand Total sits on the last rendered row,
        // in the data column immediately to the right of the row-label column.
        var lastRenderedRow = pivot.LastRenderedRange!.Value.End.Row;
        var dataCol = pivot.TargetRange.Start.Col + 1;
        return sheet.GetCell(new CellAddress(sheet.Id, lastRenderedRow, dataCol))?.Value switch
        {
            NumberValue number => number.Value,
            _ => null,
        };
    }

    // --- direction 1 (the named defect): table-backed pivot -> plain range ---
    // Expected after: SourceType=WorksheetRange, SourceTableName=null, SourceTableId=null,
    // SourceRange/SourceReference = the new plain range.

    [Fact]
    public void Apply_RedirectsTableBackedPivotToPlainRange_ClearsTableBindingAndHonorsNewRangeData()
    {
        var (workbook, sheet, pivot, cache) = CreateTableBackedPivotWithAlternatives("PivotSourceToRangeTest");
        cache.SourceTableId.Should().Be(1, "the initial refresh must have pinned the cache to SalesTable's stable id");

        var newRange = Range(sheet, "N1", "Q3");
        var command = new ChangePivotTableSourceCommand(sheet.Id, pivot.Name, newRange);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        // This redirect crosses the Table -> WorksheetRange SourceType boundary, so (SourceType being
        // init-only) the command must have replaced the cache object rather than mutated it in place --
        // re-fetch it from the workbook rather than trusting the pre-Apply local reference.
        var cacheAfterApply = CommandGuards.FindPivotCache(workbook, pivot)!;

        // Bug (before fix): cache.SourceType/SourceTableName/SourceTableId stayed pointed at the OLD
        // SalesTable. Since SalesTable is still a live table, the table-tracking block inside the very
        // Refresh() this command's own Apply() triggers immediately re-derived pivot.SourceRange back
        // from SalesTable -- discarding the user's redirect within the very same Apply() call, before
        // it even returns.
        pivot.SourceRange.Should().Be(newRange, "the explicit redirect to the plain range must win, not get silently reverted by the pivot's own trailing refresh");
        cacheAfterApply.SourceType.Should().Be(PivotCacheSourceType.WorksheetRange);
        cacheAfterApply.SourceTableName.Should().BeNull("the stale SalesTable binding must be cleared, not left dangling");
        cacheAfterApply.SourceTableId.Should().BeNull("the stale SalesTable id must be cleared, not left dangling");
        cacheAfterApply.SourceReference.Should().Be("N1:Q3");
        cacheAfterApply.SourceSheetName.Should().Be(sheet.Name);
        cacheAfterApply.Fields.Select(f => f.Name).Should().Equal("Region", "Quarter", "Amount", "Units");

        // Prove wrong DATA, not merely a stale field: Grand Total must reflect the NEW range's Amount
        // sum (300), never SalesTable's (70).
        ReadGrandTotalAmount(sheet, pivot).Should().Be(300);

        // A subsequent, ordinary refresh (Data > Refresh) must keep honoring the new plain range --
        // the table-tracking block in PivotTableRefreshService must not fire at all now that
        // cache.SourceType is WorksheetRange.
        var refresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        refresh.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        pivot.SourceRange.Should().Be(newRange);
        ReadGrandTotalAmount(sheet, pivot).Should().Be(300);
    }

    // --- direction 4: table-backed pivot -> a DIFFERENT table ---
    // Expected after: SourceType=Table, SourceTableName/SourceTableId = the NEW table's identity
    // (old SalesTable identity fully discarded).

    [Fact]
    public void Apply_RedirectsTableBackedPivotToDifferentTable_RebindsToNewTableNotOldOne()
    {
        var (workbook, sheet, pivot, cache) = CreateTableBackedPivotWithAlternatives("PivotSourceToDifferentTableTest");
        cache.SourceTableId.Should().Be(1);

        var newRange = Range(sheet, "H1", "K3"); // exactly OtherTable's (id=2) extent
        var command = new ChangePivotTableSourceCommand(sheet.Id, pivot.Name, newRange);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        // Bug (before fix): same failure mode as direction 1 -- the cache stayed bound to SalesTable
        // (id=1), so the trailing refresh inside Apply() snapped pivot.SourceRange back to SalesTable
        // instead of the user's chosen OtherTable.
        pivot.SourceRange.Should().Be(newRange, "the explicit redirect to OtherTable must win");
        cache.SourceType.Should().Be(PivotCacheSourceType.Table);
        cache.SourceTableName.Should().Be("OtherTable");
        cache.SourceTableId.Should().Be(2, "the cache must now identify OtherTable, not SalesTable");
        cache.SourceReference.Should().Be("H1:K3");

        ReadGrandTotalAmount(sheet, pivot).Should().Be(900, "the pivot must show OtherTable's data, never SalesTable's stale 70");

        // Growing OtherTable and doing an ordinary refresh must now track OtherTable by its id (proving
        // the rebind is a REAL table binding, not just a one-off range copy) -- and must never resolve
        // back to SalesTable.
        sheet.SetCell(Addr(sheet, "H4"), new TextValue("Central"));
        sheet.SetCell(Addr(sheet, "I4"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "J4"), new NumberValue(600));
        sheet.SetCell(Addr(sheet, "K4"), new NumberValue(60));
        var resize = new ResizeStructuredTableCommand(sheet.Id, tableId: 2, newRange: Range(sheet, "H1", "K4"));
        resize.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        var refresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        refresh.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        pivot.SourceRange.Should().Be(Range(sheet, "H1", "K4"), "the grown OtherTable extent must be tracked");
        ReadGrandTotalAmount(sheet, pivot).Should().Be(1500, "900 + the new 600 row");
    }

    // --- direction 2: plain-range pivot -> table ---
    // Expected after: SourceType=Table, SourceTableName/SourceTableId = the new table's identity,
    // established for the first time.

    [Fact]
    public void Apply_RedirectsPlainRangePivotToTable_EstablishesTableBindingAndTracksSubsequentGrowth()
    {
        var workbook = new Workbook("PivotSourceRangeToTableTest");
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(Addr(sheet, "N1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "O1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "N2"), new TextValue("North"));
        sheet.SetCell(Addr(sheet, "O2"), new NumberValue(100));
        sheet.SetCell(Addr(sheet, "N3"), new TextValue("South"));
        sheet.SetCell(Addr(sheet, "O3"), new NumberValue(200));

        sheet.SetCell(Addr(sheet, "H1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "I1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "H2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "I2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "H3"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "I3"), new NumberValue(20));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 5,
            Name = "GrowTable",
            DisplayName = "GrowTable",
            Range = Range(sheet, "H1", "I3"),
            HasAutoFilter = true,
            HeaderRowCount = 1,
        });

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "N1:O3",
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount"));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "N1", "O3"),
            TargetRange = Range(sheet, "A20", "B25"),
            ReportLayout = PivotReportLayout.Tabular,
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var command = new ChangePivotTableSourceCommand(sheet.Id, pivot.Name, Range(sheet, "H1", "I3"));
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        // This redirect crosses the WorksheetRange -> Table SourceType boundary, so the command must
        // have replaced the cache object -- re-fetch it rather than trusting the pre-Apply local `cache`.
        var cacheAfterApply = CommandGuards.FindPivotCache(workbook, pivot)!;
        cacheAfterApply.SourceType.Should().Be(PivotCacheSourceType.Table);
        cacheAfterApply.SourceTableName.Should().Be("GrowTable");
        cacheAfterApply.SourceTableId.Should().Be(5);
        ReadGrandTotalAmount(sheet, pivot).Should().Be(30);

        // Grow GrowTable and refresh: without this fix ever establishing a Table binding here, the
        // pivot would never have picked up the growth (cache.SourceType would have stayed
        // WorksheetRange forever, so PivotTableRefreshService's table-tracking block would never fire).
        sheet.SetCell(Addr(sheet, "H4"), new TextValue("North"));
        sheet.SetCell(Addr(sheet, "I4"), new NumberValue(70));
        var resize = new ResizeStructuredTableCommand(sheet.Id, tableId: 5, newRange: Range(sheet, "H1", "I4"));
        resize.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        var refresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        refresh.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        pivot.SourceRange.Should().Be(Range(sheet, "H1", "I4"));
        ReadGrandTotalAmount(sheet, pivot).Should().Be(100, "10 + 20 + the new 70 row");
    }

    // --- no-regression sibling: plain range -> a different plain range, no table involved at all ---

    [Fact]
    public void Apply_RedirectsPlainRangePivotToDifferentPlainRange_NoRegression()
    {
        var workbook = new Workbook("PivotSourceRangeToRangeNoRegressionTest");
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));

        sheet.SetCell(Addr(sheet, "D1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "E1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "D2"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "E2"), new NumberValue(100));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:B3",
        };
        cache.Fields.Add(new PivotCacheFieldModel("Category"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount"));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "G3", "I6"),
            ReportLayout = PivotReportLayout.Tabular,
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var command = new ChangePivotTableSourceCommand(sheet.Id, pivot.Name, Range(sheet, "D1", "E2"));
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        cache.SourceType.Should().Be(PivotCacheSourceType.WorksheetRange);
        cache.SourceTableName.Should().BeNull();
        cache.SourceTableId.Should().BeNull();
        cache.SourceReference.Should().Be("D1:E2");
        ReadGrandTotalAmount(sheet, pivot).Should().Be(100);
    }

    // --- undo must restore the EXACT prior binding, including a null id vs an established one ---

    [Fact]
    public void Revert_AfterRedirectFromTableToRange_RestoresEstablishedTableBindingExactly()
    {
        var (workbook, sheet, pivot, cache) = CreateTableBackedPivotWithAlternatives("PivotSourceUndoEstablishedIdTest");
        cache.SourceTableId.Should().Be(1);
        var fieldsBeforeApply = cache.Fields.ToList();

        var command = new ChangePivotTableSourceCommand(sheet.Id, pivot.Name, Range(sheet, "N1", "Q3"));
        var ctx = new TestCommandContext(workbook);
        command.Apply(ctx).Success.Should().BeTrue();

        var cacheAfterApply = CommandGuards.FindPivotCache(workbook, pivot);
        cacheAfterApply!.SourceType.Should().Be(PivotCacheSourceType.WorksheetRange);

        command.Revert(ctx);

        var restoredCache = CommandGuards.FindPivotCache(workbook, pivot);
        restoredCache.Should().NotBeNull();
        restoredCache!.SourceType.Should().Be(PivotCacheSourceType.Table, "undo must restore the exact prior binding, not just clear fields back to a guess");
        restoredCache.SourceTableName.Should().Be("SalesTable");
        restoredCache.SourceTableId.Should().Be(1, "the established id must come back exactly, not be re-derived or left null");
        restoredCache.SourceReference.Should().Be("A1:D5");
        restoredCache.Fields.Select(f => f.Name).Should().Equal(fieldsBeforeApply.Select(f => f.Name));
        pivot.SourceRange.Should().Be(Range(sheet, "A1", "D5"));

        // The restored pivot must render the ORIGINAL SalesTable data again, not the abandoned redirect.
        var refresh = new RefreshPivotTableCommand(sheet.Id, pivot.Name);
        refresh.Apply(ctx).Success.Should().BeTrue();
        ReadGrandTotalAmount(sheet, pivot).Should().Be(70);
    }

    [Fact]
    public void Revert_AfterRedirectFromRangeToTable_RestoresNullSourceTableIdExactly()
    {
        var workbook = new Workbook("PivotSourceUndoNullIdTest");
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(Addr(sheet, "N1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "O1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "N2"), new TextValue("North"));
        sheet.SetCell(Addr(sheet, "O2"), new NumberValue(100));
        sheet.SetCell(Addr(sheet, "N3"), new TextValue("South"));
        sheet.SetCell(Addr(sheet, "O3"), new NumberValue(200));

        sheet.SetCell(Addr(sheet, "H1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "I1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "H2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "I2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "H3"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "I3"), new NumberValue(20));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 5,
            Name = "GrowTable",
            DisplayName = "GrowTable",
            Range = Range(sheet, "H1", "I3"),
            HasAutoFilter = true,
            HeaderRowCount = 1,
        });

        // This cache has NEVER been refreshed since creation -- SourceTableId is null, matching a
        // freshly-loaded file's cache before its first refresh. This is the state undo must restore
        // exactly (null, not merely "unset because we didn't bother restoring it").
        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "N1:O3",
        };
        cache.SourceTableId.Should().BeNull();
        cache.Fields.Add(new PivotCacheFieldModel("Region"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount"));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "N1", "O3"),
            TargetRange = Range(sheet, "A20", "B25"),
            ReportLayout = PivotReportLayout.Tabular,
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        // No initial refresh -- deliberately leaving SourceTableId unestablished, unlike the other tests.

        var command = new ChangePivotTableSourceCommand(sheet.Id, pivot.Name, Range(sheet, "H1", "I3"));
        var ctx = new TestCommandContext(workbook);
        command.Apply(ctx).Success.Should().BeTrue();

        var cacheAfterApply = CommandGuards.FindPivotCache(workbook, pivot);
        cacheAfterApply!.SourceType.Should().Be(PivotCacheSourceType.Table);
        cacheAfterApply.SourceTableId.Should().Be(5);

        command.Revert(ctx);

        var restoredCache = CommandGuards.FindPivotCache(workbook, pivot);
        restoredCache.Should().NotBeNull();
        restoredCache!.SourceType.Should().Be(PivotCacheSourceType.WorksheetRange);
        restoredCache.SourceTableName.Should().BeNull();
        restoredCache.SourceTableId.Should().BeNull("the prior null id (never established) must come back as null, not as some other sentinel or the new table's id");
        restoredCache.SourceReference.Should().Be("N1:O3");
        pivot.SourceRange.Should().Be(Range(sheet, "N1", "O3"));
    }
}
