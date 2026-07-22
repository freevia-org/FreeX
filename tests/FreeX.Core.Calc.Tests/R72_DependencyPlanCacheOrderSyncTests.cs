using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for R72-offbyone-newloop-sweep-1: <see cref="RecalcEngine.RetireWorkbook"/>
/// removed a closed workbook's dependency-plan cache dictionary entries but never removed the
/// matching keys from the companion FIFO order queue (<c>_dependencyPlanCacheOrder</c>), so the
/// queue desynced from the dictionary -- every retire left behind "phantom" queue entries for
/// keys no longer in the dictionary. <see cref="RecalcEngine.AddDependencyPlanToCache"/>'s eviction
/// path only ever dequeued/removed a single entry per insert, which was a no-op for a phantom key,
/// so the dictionary could grow past <c>MaxDependencyPlanCacheEntries</c> by roughly the number of
/// retired-but-still-queued keys across repeated open/close cycles. The fix (a) makes RetireWorkbook
/// rebuild the order queue to drop the retired sheets' keys, and (b) makes the eviction path loop
/// until it actually evicts a dictionary entry (skipping any phantom key), so the cache stays a
/// faithful, bounded mirror of the dictionary even if the two ever drifted again.
/// </summary>
public sealed class R72_DependencyPlanCacheOrderSyncTests
{
    private const int MaxDependencyPlanCacheEntries = 1024;

    private static RecalcEngine Engine() => new(new DependencyGraph(), new FormulaEvaluator());

    /// <summary>
    /// Fill the dependency-plan cache with distinct, non-volatile, non-self-excluding formula
    /// cells on one sheet. Each cell gets its own freshly parsed AST instance (the cache key is
    /// AST-reference-based, see DependencyPlanCacheKey), so distinct cell addresses are enough to
    /// produce distinct cache entries even when the literal formula text is reused.
    /// </summary>
    private static void FillWithDistinctFormulas(Sheet sheet, int count)
    {
        for (var row = 1; row <= count; row++)
            sheet.SetFormula(new CellAddress(sheet.Id, (uint)row, 1), $"{row}+1");
    }

    [Fact]
    public void RetireWorkbook_RemovesRetiredKeysFromOrderQueue_KeepingQueueAndDictionaryInSync()
    {
        var engine = Engine();

        var workbookA = new Workbook("A");
        var sheetA = workbookA.AddSheet("Sheet1");
        FillWithDistinctFormulas(sheetA, 900);
        engine.RecalculateAllFormulas(workbookA);

        engine.DependencyPlanCacheOrderCountForSheetForTests(sheetA.Id).Should().Be(900,
            "every distinct formula cell registered on A should have queued its own cache key");

        engine.RetireWorkbook(workbookA);

        // Fix (a): the order queue must no longer carry any of A's now-retired keys.
        engine.DependencyPlanCacheOrderCountForSheetForTests(sheetA.Id).Should().Be(0,
            "RetireWorkbook must drop the retired workbook's keys from the FIFO order queue, not just the dictionary");

        // The queue must stay a faithful mirror of the dictionary (same count), not merely smaller.
        engine.DependencyPlanCacheOrderCountForTests.Should().Be(engine.DependencyPlanCacheCountForTests,
            "the order queue must mirror the dictionary 1:1 after a retire");
    }

    [Fact]
    public void AddDependencyPlanToCache_AfterRetire_NeverExceedsMaxCacheEntriesAcrossRepeatedOpenClose()
    {
        // Sibling/no-regression: without the fix, phantom queue entries left behind by retiring A
        // would make the single-dequeue eviction a no-op for those slots, letting B's fill push
        // Count past MaxDependencyPlanCacheEntries. With either fix in place (queue resync, or the
        // loop-until-real-eviction eviction path), Count must stay bounded.
        var engine = Engine();

        var workbookA = new Workbook("A");
        var sheetA = workbookA.AddSheet("Sheet1");
        FillWithDistinctFormulas(sheetA, 1000);
        engine.RecalculateAllFormulas(workbookA);

        engine.RetireWorkbook(workbookA);

        var workbookB = new Workbook("B");
        var sheetB = workbookB.AddSheet("Sheet1");
        FillWithDistinctFormulas(sheetB, 1500);
        engine.RecalculateAllFormulas(workbookB);

        engine.DependencyPlanCacheCountForTests.Should().BeLessThanOrEqualTo(MaxDependencyPlanCacheEntries,
            "the dependency-plan cache must never grow past its cap even across retire/refill cycles");
        engine.DependencyPlanCacheOrderCountForTests.Should().Be(engine.DependencyPlanCacheCountForTests,
            "the order queue must still mirror the dictionary after heavy eviction churn");
    }

    [Fact]
    public void AddDependencyPlanToCache_SingleWorkbookFill_StillEvictsLruCorrectly()
    {
        // Sibling/no-regression: an ordinary single-workbook fill past the cap (no retire involved
        // at all) must still behave like a normal bounded LRU-ish FIFO cache.
        var engine = Engine();

        var workbook = new Workbook("Solo");
        var sheet = workbook.AddSheet("Sheet1");
        FillWithDistinctFormulas(sheet, MaxDependencyPlanCacheEntries + 200);
        engine.RecalculateAllFormulas(workbook);

        engine.DependencyPlanCacheCountForTests.Should().BeLessThanOrEqualTo(MaxDependencyPlanCacheEntries,
            "a normal single-workbook fill past the cap must still be bounded");
        engine.DependencyPlanCacheOrderCountForTests.Should().Be(engine.DependencyPlanCacheCountForTests,
            "the order queue must mirror the dictionary in the ordinary (no-retire) fill path too");
    }
}
