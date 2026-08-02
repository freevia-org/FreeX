using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R117-io-slicer-cacheitem-growth: the Core.IO half of R117-commands-pivot-slicer-growth.
/// <c>PivotTableRefreshService.ExtendBoundSlicerCacheItems</c> now appends a new
/// <see cref="SlicerCacheItem"/> to an EXISTING slicer's <see cref="SlicerModel.CacheItems"/> when a
/// refresh discovers a brand-new distinct pivot-cache value -- but that in-memory growth has nowhere to
/// go on save unless the writer/rewriter also emits a new native <c>&lt;i x="N"/&gt;</c> entry.
/// <c>XlsxSlicerTimelineStateRewriter.RewriteNativeCacheItemSelection</c> used to only iterate the
/// ALREADY-PRESERVED <c>&lt;i&gt;</c> elements to patch their <c>s</c> selected flag -- it never appended
/// one for an index the model carries but the preserved XML doesn't, so even a Save-immediately-after-
/// Refresh silently dropped the new item, and it was invisible again on the very next reload.
///
/// ROUND-TRIP FIXTURE RULE: the fixture is built with the product's OWN save/load path (a fresh
/// <see cref="XlsxFileAdapter"/> save of a workbook wired through the real <see cref="AddPivotTableCommand"/>
/// / <see cref="AddSlicerCommand"/> entry points, then loaded back) -- never a hand-authored XML string --
/// so the reader/writer's own assumptions about the native <c>&lt;data&gt;&lt;tabular&gt;&lt;items&gt;</c>
/// shape are exercised on both sides, not baked into the fixture by hand.
/// </summary>
public sealed class R117_SlicerCacheItemGrowthRoundTripTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static Workbook BuildAndSaveThenLoad(out Sheet sheet, out TestCommandContext ctx)
    {
        var workbook = new Workbook("R117SlicerGrowthRoundTrip");
        var freshSheet = workbook.AddSheet("Data");

        // Source A1:B4 -- rows 2-3 filled (Category = A, B); row 4 left blank for later growth.
        freshSheet.SetCell(Addr(freshSheet, "A1"), new TextValue("Category"));
        freshSheet.SetCell(Addr(freshSheet, "B1"), new TextValue("Amount"));
        freshSheet.SetCell(Addr(freshSheet, "A2"), new TextValue("A"));
        freshSheet.SetCell(Addr(freshSheet, "B2"), new NumberValue(10));
        freshSheet.SetCell(Addr(freshSheet, "A3"), new TextValue("B"));
        freshSheet.SetCell(Addr(freshSheet, "B3"), new NumberValue(20));

        var buildCtx = new TestCommandContext(workbook);
        new AddPivotTableCommand(
            freshSheet.Id,
            Range(freshSheet, "A1", "B4"),
            Range(freshSheet, "G3", "H6"),
            "PivotTable1",
            rowFieldIndexes: [0],
            dataFieldIndexes: [1]).Apply(buildCtx).Success.Should().BeTrue();
        new AddSlicerCommand("Category Slicer", "PivotTable1", "Category").Apply(buildCtx).Success.Should().BeTrue();

        using var firstSave = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, firstSave);
        firstSave.Position = 0;

        var loaded = adapter.Load(firstSave);
        sheet = loaded.GetSheetAt(0);
        ctx = new TestCommandContext(loaded);
        return loaded;
    }

    [Fact]
    public void RefreshThenSaveThenReload_NewDistinctValue_PersistsAsNativeCacheItemEntry()
    {
        var loaded = BuildAndSaveThenLoad(out var sheet, out var ctx);
        var slicer = loaded.Slicers.Should().ContainSingle().Subject;
        slicer.CacheItems.Select(i => i.Index).Should().BeEquivalentTo([0, 1],
            "the first save/load round trip must carry one native cache item per original shared item");

        // A genuinely new distinct value appears in the previously-blank row.
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));

        new RefreshPivotTableCommand(sheet.Id, "PivotTable1").Apply(ctx).Success.Should().BeTrue();
        slicer.CacheItems.Select(i => i.Index).Should().BeEquivalentTo([0, 1, 2],
            "Refresh must have appended a CacheItems entry for the new index (in-memory half of the fix)");

        using var secondSave = new MemoryStream();
        new XlsxFileAdapter().Save(loaded, secondSave);
        secondSave.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(secondSave);
        var reloadedSlicer = reloaded.Slicers.Should().ContainSingle().Subject;
        reloadedSlicer.CacheItems.Select(i => i.Index).Should().BeEquivalentTo([0, 1, 2],
            "the new native <i x=\"2\"/> entry must survive a save + reload round trip, or the new value " +
            "is invisible again the moment the file is reopened");

        var cacheAfterReload = CommandGuards.FindPivotCache(reloaded, reloaded.GetSheetAt(0).PivotTables.Single())!;
        var fieldAfterReload = cacheAfterReload.Fields.Single(f => f.Name == "Category");
        fieldAfterReload.SharedItems.Should().Equal(["A", "B", "C"],
            "the preserved pivotCacheDefinition part's <sharedItems> must also carry the newly-appended " +
            "value, or the slicer's new CacheItems index points past the reloaded SharedItems list");

        var available = SlicerItemResolver.ResolveAvailableItems(reloadedSlicer, reloaded);
        available.Should().BeEquivalentTo(["A", "B", "C"]);
    }

    /// <summary>No-regression sibling: a resave with no refresh/growth at all leaves the native <c>&lt;items&gt;</c> list untouched (same two entries, same order).</summary>
    [Fact]
    public void ResaveWithoutRefresh_NoRegression_NativeCacheItemsUnchanged()
    {
        var loaded = BuildAndSaveThenLoad(out var sheet, out var ctx);
        var slicer = loaded.Slicers.Should().ContainSingle().Subject;

        using var resaved = new MemoryStream();
        new XlsxFileAdapter().Save(loaded, resaved);
        resaved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(resaved);
        var reloadedSlicer = reloaded.Slicers.Should().ContainSingle().Subject;
        reloadedSlicer.CacheItems.Select(i => i.Index).Should().BeEquivalentTo([0, 1],
            "an untouched resave must not spuriously add or drop native cache item entries");
    }
}
