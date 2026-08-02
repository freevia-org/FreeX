using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R116-commands-pivot-slicer-changesource: r115's <c>PivotTableRefreshService.ReconcileCacheFields</c>
/// fix made the ORDINARY refresh path append newly-discovered distinct values to a surviving field's
/// <see cref="PivotCacheFieldModel.SharedItems"/> instead of rebuilding the list from scratch, so a
/// pivot-bound slicer's <see cref="SlicerModel.CacheItems"/>[].Index (a positional index into
/// SharedItems, resolved by <see cref="SlicerItemResolver.ResolveAvailableItems"/>) is never renumbered.
/// But <see cref="ChangePivotTableSourceCommand"/> -- the explicit "Change Data Source" command -- was
/// NOT updated to use the same merge in either of its branches (the same-SourceType in-place mutation,
/// or the cross-SourceType <c>BuildRedirectedCache</c> replacement): both did an unconditional
/// <c>cache.Fields.Clear()</c> + full rebuild from the new source's top-to-bottom scan, silently
/// renumbering SharedItems whenever the new source's row order surfaced a same-named field's distinct
/// values in a different order than before -- corrupting an existing slicer's selection to point at a
/// DIFFERENT value than the one the user actually selected, with no error or re-prompt.
///
/// These tests drive the real product entry points: <see cref="ChangePivotTableSourceCommand.Apply"/>
/// (the command an actual "Change Data Source" dialog invokes) followed by
/// <see cref="SlicerItemResolver.ResolveAvailableItems"/> (the real live-UI/render entry point that
/// projects a pivot slicer's cache-item selection onto <see cref="SlicerModel.SelectedItems"/>).
/// </summary>
public sealed class R116_PivotSlicerChangeSourcePreservesIndicesTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    /// <summary>
    /// THE anchor test (same-SourceType in-place mutation branch, ChangePivotTableSourceCommand.cs:200).
    /// Original source has Category values in first-appearance order A, B (rows 2-4: A, B, A), so the
    /// cache field's SharedItems come out as ["A", "B"] (index 0 = A, index 1 = B). A slicer is wired
    /// with a cache item selecting index 1 -- i.e. the user had selected "B" -- exactly the shape a
    /// loaded-from-file pivot slicer's selection takes (encoded purely as CacheItems[].Index +
    /// IsSelected, not yet projected onto SelectedItems). "Change Data Source" is redirected to a
    /// DIFFERENT plain range whose Category column surfaces the values in first-appearance order B, A, C
    /// (a newly-expanded/reordered range) -- before the fix, a full field rebuild would renumber
    /// SharedItems to ["B", "A", "C"], so the slicer's still-index-1 selection would now resolve to "A"
    /// instead of "B". After the fix, the surviving "Category" field keeps its existing SharedItems
    /// order (A, B) and only appends the genuinely new "C", so index 1 keeps meaning "B".
    /// </summary>
    [Fact]
    public void ChangeDataSource_SameSourceType_PreservesSlicerCacheItemIndexMeaning()
    {
        var workbook = new Workbook("R116ChangeSourceSameType");
        var sheet = workbook.AddSheet("Data");

        // Original source: A1:B4, Category first-appearance order = A, B.
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));

        // Redirect target: D1:E4, Category first-appearance order = B, A, C (reordered + expanded).
        sheet.SetCell(Addr(sheet, "D1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "E1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "D2"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "E2"), new NumberValue(99));
        sheet.SetCell(Addr(sheet, "D3"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "E3"), new NumberValue(88));
        sheet.SetCell(Addr(sheet, "D4"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "E4"), new NumberValue(77));

        var ctx = new TestCommandContext(workbook);
        var addPivot = new AddPivotTableCommand(
            sheet.Id,
            Range(sheet, "A1", "B4"),
            Range(sheet, "G3", "H6"),
            "PivotTable1",
            rowFieldIndexes: [0],
            dataFieldIndexes: [1]);
        addPivot.Apply(ctx).Success.Should().BeTrue();

        var cacheBefore = workbook.PivotCaches.Should().ContainSingle().Subject;
        var categoryFieldBefore = cacheBefore.Fields.Should().ContainSingle(f => f.Name == "Category").Subject;
        categoryFieldBefore.SharedItems.Should().Equal("A", "B");

        // Wire a pivot-bound slicer whose CacheItems selects index 1 ("B") -- the exact persisted shape
        // a loaded-from-file pivot slicer's selection takes (a live AddSlicerCommand+SetSlicerSelectionCommand
        // round trip always selects captions by name via SelectedItems, never leaves a single-index-only
        // CacheItems selection for this scenario to exercise, so this mirrors the load-from-file seam the
        // same way R104's tests directly construct a PivotCacheModel to mirror a loaded cache).
        var slicer = new SlicerModel
        {
            Name = "Category Slicer",
            CacheName = "Slicer_Category",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Category",
            CacheItems = [new SlicerCacheItem(0, false), new SlicerCacheItem(1, true)],
        };
        workbook.Slicers.Add(slicer);

        var changeSource = new ChangePivotTableSourceCommand(sheet.Id, "PivotTable1", Range(sheet, "D1", "E4"));
        changeSource.Apply(ctx).Success.Should().BeTrue();

        var cacheAfter = workbook.PivotCaches.Should().ContainSingle().Subject;
        var categoryFieldAfter = cacheAfter.Fields.Should().ContainSingle(f => f.Name == "Category").Subject;

        // The core assertion: index 1 must still mean "B" after the redirect, not get renumbered to "A"
        // purely because the new source's row order surfaced "B" before "A".
        categoryFieldAfter.SharedItems![1].Should().Be("B",
            "Change Data Source must preserve a surviving field's existing SharedItems order/index, " +
            "not renumber it from the new source's row order, or a bound slicer's selection silently " +
            "flips to a different value than the one the user selected");

        // Real live-UI/render entry point: resolving the slicer's items must project the selection onto
        // "B", never "A" -- proving the corruption is invisible nowhere else either. "C" is a genuinely
        // new distinct value surfaced by this redirect (R118-commands-pivot-slicer-changesource): Change
        // Data Source now extends the bound slicer's CacheItems for it too, same as an ordinary refresh
        // (R117). This slicer already has a manual filter applied ("A" explicitly deselected), and the
        // field carries no explicit includeNewItemsInFilter=true, so per Excel's default (ECMA-376
        // pivotField/@includeNewItemsInFilter, default false) the new "C" must NOT be auto-included --
        // it stays excluded alongside "A" until the user (or an explicit includeNewItemsInFilter=true)
        // opts in, so the user's deliberate filter is not silently widened.
        SlicerItemResolver.ResolveAvailableItems(slicer, workbook);
        slicer.SelectedItems.Should().Equal("B");
    }

    /// <summary>
    /// No-regression sibling (cross-SourceType <c>BuildRedirectedCache</c> branch): the same
    /// SharedItems-order-preservation must hold even when the redirect crosses the Table/WorksheetRange
    /// SourceType boundary and therefore swaps in a brand-new <see cref="PivotCacheModel"/> object
    /// (SourceType is init-only) -- a plain-range-backed pivot is redirected onto a live structured
    /// table covering different-ordered data for the same field name. The new cache object must still
    /// be built by reconciling against the ORIGINAL cache's fields (not a blind fresh build), so an
    /// existing slicer's CacheItems index survives the crossing exactly like the same-SourceType branch.
    /// </summary>
    [Fact]
    public void ChangeDataSource_CrossSourceType_PreservesSlicerCacheItemIndexMeaning()
    {
        var workbook = new Workbook("R116ChangeSourceCrossType");
        var sheet = workbook.AddSheet("Data");

        // Original plain-range source: N1:O3, Category first-appearance order = A, B.
        sheet.SetCell(Addr(sheet, "N1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "O1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "N2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "O2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "N3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "O3"), new NumberValue(20));

        // Redirect target: a live structured table H1:I4, Category first-appearance order = B, A, C.
        sheet.SetCell(Addr(sheet, "H1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "I1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "H2"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "I2"), new NumberValue(99));
        sheet.SetCell(Addr(sheet, "H3"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "I3"), new NumberValue(88));
        sheet.SetCell(Addr(sheet, "H4"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "I4"), new NumberValue(77));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 5,
            Name = "GrowTable",
            DisplayName = "GrowTable",
            Range = Range(sheet, "H1", "I4"),
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
        cache.Fields.Add(new PivotCacheFieldModel("Category", ContainsString: true, SharedItems: ["A", "B"], SharedItemKinds: ['s', 's']));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
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

        var slicer = new SlicerModel
        {
            Name = "Category Slicer",
            CacheName = "Slicer_Category",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Category",
            CacheItems = [new SlicerCacheItem(0, false), new SlicerCacheItem(1, true)],
        };
        workbook.Slicers.Add(slicer);

        var ctx = new TestCommandContext(workbook);
        var changeSource = new ChangePivotTableSourceCommand(sheet.Id, pivot.Name, Range(sheet, "H1", "I4"));
        changeSource.Apply(ctx).Success.Should().BeTrue();

        // This redirect crosses the WorksheetRange -> Table SourceType boundary, so the command must
        // have replaced the cache object -- re-fetch it rather than trusting the pre-Apply local `cache`.
        var cacheAfter = CommandGuards.FindPivotCache(workbook, pivot)!;
        cacheAfter.SourceType.Should().Be(PivotCacheSourceType.Table);
        var categoryFieldAfter = cacheAfter.Fields.Should().ContainSingle(f => f.Name == "Category").Subject;

        categoryFieldAfter.SharedItems![1].Should().Be("B",
            "the cross-SourceType redirect (BuildRedirectedCache) must reconcile against the ORIGINAL " +
            "cache's fields the same way the same-SourceType branch does, not blindly rebuild from the " +
            "new source's row order");

        // "C" is a genuinely new distinct value surfaced by this redirect
        // (R118-commands-pivot-slicer-changesource): Change Data Source now extends the bound slicer's
        // CacheItems for it too, same as an ordinary refresh (R117). This slicer already has a manual
        // filter applied ("A" explicitly deselected) and no explicit includeNewItemsInFilter=true, so
        // per Excel's default the new "C" must NOT be auto-included -- it stays excluded, preserving the
        // user's deliberate filter instead of silently widening it.
        SlicerItemResolver.ResolveAvailableItems(slicer, workbook);
        slicer.SelectedItems.Should().Equal("B");
    }
}
