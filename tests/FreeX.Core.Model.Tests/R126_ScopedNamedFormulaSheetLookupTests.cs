using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round 126 LOW finding: RowColumnShiftHelpers.RewriteNamedFormulas (and SheetCommands'
/// RemoveSheetCommand delete-sheet rewrite pass) resolved a sheet-scoped defined name's owning
/// sheet via <c>workbook.Sheets.FirstOrDefault(s =&gt; s.Id == sheetId)</c> -- an O(sheet-count)
/// linear scan -- even though <see cref="Workbook.GetSheet(SheetId)"/> already gives an O(1)
/// lookup backed by the workbook's internal <c>_sheetById</c> dictionary. This function runs on
/// every InsertRows/DeleteRows/InsertDeleteColumns/InsertDeleteCells Apply and on RemoveSheet, so
/// a single structural edit re-scanned the sheet list once per sheet-scoped defined name --
/// O(scoped-name-count * sheet-count) work where O(scoped-name-count) already suffices.
///
/// This is a pure algorithmic-complexity fix: <c>_sheetById</c> is kept in lockstep with
/// <c>Sheets</c> on every add/insert/remove path (Workbook.cs AddSheet/InsertSheet/RemoveSheet),
/// so <c>GetSheet</c> and the old <c>FirstOrDefault</c> scan always agree on which <see cref="Sheet"/>
/// they return -- there is no behavioral divergence to assert on. The regression test below
/// instead proves the complexity claim directly: it measures RewriteNamedFormulas's wall-clock
/// cost at two sheet counts (30x apart) and asserts the cost scales roughly linearly with sheet
/// count, not quadratically. The O(N) FirstOrDefault scan makes total cost O(S*N) (quadratic when
/// S and N grow together, as they do here -- one scoped name per sheet), which blows well past
/// the linear-scaling threshold; the O(1) GetSheet lookup keeps total cost O(S) (linear).
/// </summary>
public class R126_ScopedNamedFormulaSheetLookupTests
{
    /// <summary>
    /// Ratio above which the sheet lookup is judged to scale with sheet count. Sized from measured
    /// values, not from theory: with this harness the O(1) dictionary measures 1.0-1.3 and the O(N)
    /// scan it guards against measures 6.8-18.1, so 4.0 sits ~3x above the former and below the
    /// latter's worst observed run. See BuildWorkbook's remarks for why the previous design could
    /// not be separated by any threshold at all.
    /// </summary>
    private const double ScanRegressionRatioThreshold = 4.0;

    /// <summary>
    /// Builds a workbook with <paramref name="sheetCount"/> sheets carrying exactly
    /// <paramref name="scopedNameCount"/> sheet-scoped names, so the two can be varied
    /// independently.
    /// </summary>
    /// <remarks>
    /// Holding the name count fixed is what makes this test discriminate. RewriteNamedFormulas does
    /// two things per scoped name: look the owning sheet up, and rewrite the formula text. Only the
    /// lookup depends on sheet count. An earlier version of this test grew names and sheets together,
    /// so the text-rewriting term grew 30x alongside and swamped the signal -- measured here, the
    /// O(N)-scan regression it exists to catch produced a ratio of ~80 while the correct O(1) code
    /// produced ~30, and the same correct code reached ~152 under a loaded gate. The bug's signature
    /// sat *below* the noise floor of the fix, so no threshold could separate them: 150 was
    /// simultaneously too loose to catch the regression and too tight to avoid false failures. With
    /// the name count pinned, the rewriting cost is identical on both sides and cancels out of the
    /// ratio, leaving only the lookup: ~1x for the O(1) dictionary, ~30x for the O(N) scan.
    /// </remarks>
    private static (Workbook Workbook, Sheet[] Sheets) BuildWorkbook(int sheetCount, int scopedNameCount)
    {
        // Names are scoped to the tail sheets (below), so asking for more names than sheets would
        // index off the front of the array. Fail with the reason rather than an IndexOutOfRange.
        scopedNameCount.Should().BeLessThanOrEqualTo(
            sheetCount,
            "each scoped name needs its own sheet to own it");

        var workbook = new Workbook("perf");
        var sheets = new Sheet[sheetCount];
        for (var i = 0; i < sheetCount; i++)
            sheets[i] = workbook.AddSheet($"S{i}");

        // Scope the names to the LAST sheets, not the first. A linear FirstOrDefault scan stops at
        // the match, so names owned by early sheets are found after a few steps no matter how many
        // sheets follow -- with the names on sheets[0..n) the scan measured exactly as fast as the
        // dictionary and the regression was invisible. Owning the tail forces the scan to walk the
        // whole list, which is the cost this test exists to detect. The O(1) lookup is indifferent
        // to the position either way.
        //
        // The formula text is irrelevant to the sheet-lookup cost under test; the
        // FirstOrDefault/GetSheet call runs unconditionally for every entry in ScopedNamedFormulas
        // before the rewriter even inspects the text.
        for (var i = 0; i < scopedNameCount; i++)
            workbook.DefineNamedFormula($"ScopedName_{i}", "1+1", sheets[sheetCount - 1 - i].Id);

        return (workbook, sheets);
    }

    /// <summary>
    /// Best-of-<paramref name="rounds"/> per-call cost. Interference (GC, scheduling, a noisy test
    /// host) only ever ADDS time, so the minimum observation is the closest estimate of the true
    /// cost and is the one measurement a loaded machine cannot inflate.
    /// </summary>
    /// <remarks>
    /// The mean was not robust enough here. This test divides the large-workbook cost by the small
    /// one, and the small side is only ~0.2ms per call -- a denominator small enough that ordinary
    /// scheduler jitter on the large side swings the ratio wildly. It failed a full-suite gate run
    /// at ratio 185.7 against a threshold of 150 while passing in isolation, which is a measurement
    /// artifact, not a regression: a genuinely quadratic implementation would sit near 900x and
    /// could never get lucky enough to slip under the bound, so best-of-N loses no discriminating
    /// power while removing the false failures.
    /// </remarks>
    private static double BestOfMeasureRewriteNamedFormulasMsPerCall(
        int sheetCount, int scopedNameCount, int iterations, int rounds = 5)
    {
        var best = double.MaxValue;
        for (var round = 0; round < rounds; round++)
            best = Math.Min(best, MeasureRewriteNamedFormulasMsPerCall(sheetCount, scopedNameCount, iterations));
        return best;
    }

    private static double MeasureRewriteNamedFormulasMsPerCall(
        int sheetCount, int scopedNameCount, int iterations)
    {
        var (workbook, sheets) = BuildWorkbook(sheetCount, scopedNameCount);
        var op = new InsertRowsOp(sheets[0].Name, BeforeRow: 1, Count: 1);

        // Warm up JIT / first-call allocation costs outside the timed region.
        var warmupSnapshot = new Dictionary<string, string>();
        var warmupScoped = new Dictionary<(string, SheetId), string>();
        RowColumnShiftHelpers.RewriteNamedFormulas(workbook, op, warmupSnapshot, warmupScoped);

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var snapshot = new Dictionary<string, string>();
            var scopedSnapshot = new Dictionary<(string, SheetId), string>();
            RowColumnShiftHelpers.RewriteNamedFormulas(workbook, op, snapshot, scopedSnapshot);
        }
        sw.Stop();

        return sw.Elapsed.TotalMilliseconds / iterations;
    }

    [Fact]
    public void R126_RewriteNamedFormulas_ScopedSheetLookupScalesLinearlyWithSheetCount()
    {
        const int smallSheetCount = 100;
        const int largeSheetCount = 3_000; // 30x more sheets, with the scoped-name count held fixed
        const int scopedNameCount = 100;   // constant on both sides -- see BuildWorkbook's remarks
        const int iterations = 15;

        // Best-of-N on BOTH sides: see BestOfMeasureRewriteNamedFormulasMsPerCall. Taking the mean
        // here made the gate report a scaling regression that did not exist.
        var smallMs = BestOfMeasureRewriteNamedFormulasMsPerCall(
            smallSheetCount, scopedNameCount, iterations);
        var largeMs = BestOfMeasureRewriteNamedFormulasMsPerCall(
            largeSheetCount, scopedNameCount, iterations);

        // Floor the small measurement so a near-zero denominator can't produce a spuriously huge
        // (or spuriously tiny) ratio on a very fast machine.
        var ratio = largeMs / Math.Max(smallMs, 0.01);

        // With the scoped-name count fixed, the only sheet-count-dependent work left is the owning
        // sheet lookup. An O(1) GetSheet keeps the total cost flat as sheets grow 30x, so the ratio
        // sits near 1. The old O(sheet-count) FirstOrDefault scan makes it grow with sheet count, so
        // the ratio tracks the 30x growth.
        ratio.Should().BeLessThan(ScanRegressionRatioThreshold,
            because: $"GetSheet(SheetId) is an O(1) dictionary lookup, so with the scoped-name " +
                     $"count held at {scopedNameCount} the cost must not grow as sheets go " +
                     $"{smallSheetCount} -> {largeSheetCount}; the old workbook.Sheets.FirstOrDefault " +
                     $"scan grows it ~{largeSheetCount / smallSheetCount}x " +
                     $"(smallMs={smallMs:F3}, largeMs={largeMs:F3}, ratio={ratio:F1})");
    }

    // ── No-regression siblings: the O(1) lookup must still resolve the SAME sheet as the old
    // O(N) scan in every real call path that reaches RewriteNamedFormulas / the delete-sheet
    // scoped-formula rewrite. ─────────────────────────────────────────────────────────────────

    [Fact]
    public void InsertRows_ShiftsScopedNamedFormulaReferenceDown()
    {
        // Sheet1-scoped formula "Local" = Sheet1!$A$5*0.2. Inserting 3 rows above row 3 must
        // shift $A$5 -> $A$8, exercising the RowColumnShiftHelpers.RewriteNamedFormulas scoped
        // pass (RowColumnShiftHelpers.NamedRanges.cs:46-60) whose sheet lookup this round fixed.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        wb.DefineNamedFormula("Local", "Sheet1!$A$5*0.2", sheet.Id);

        new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 3).Apply(ctx);

        wb.ScopedNamedFormulas[("Local", sheet.Id)].Should().Contain("$A$8",
            because: "inserting 3 rows above row 5 must shift the scoped formula's reference from $A$5 to $A$8");
        wb.ScopedNamedFormulas[("Local", sheet.Id)].Should().NotContain("$A$5",
            because: "the original row-5 reference must have been updated");
    }

    [Fact]
    public void InsertRowsRevert_RestoresScopedNamedFormula()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        wb.DefineNamedFormula("Local", "Sheet1!$A$5*0.2", sheet.Id);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 3);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        wb.ScopedNamedFormulas[("Local", sheet.Id)].Should().Be("Sheet1!$A$5*0.2",
            because: "undo must restore the original scoped named formula text");
    }

    [Fact]
    public void RemoveSheet_RewritesSurvivingScopedNamedFormulaReferencingDeletedSheet()
    {
        // "Local" is scoped to (owned by) Sheet1 and references Sheet2, which is deleted.
        // Exercises SheetCommands.cs's RewriteScopedNamedFormulasForDeletedSheet, whose sheet
        // lookup (SheetCommands.cs:1504) this round also fixed.
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);
        wb.DefineNamedFormula("Local", "Sheet2!B1*2", sheet1.Id);

        new RemoveSheetCommand(sheet2.Id).Apply(ctx);

        wb.ScopedNamedFormulas[("Local", sheet1.Id)].Should().Be("#REF!*2",
            because: "deleting the referenced sheet must rewrite the surviving scoped formula to #REF!");
    }
}
