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
    private static (Workbook Workbook, Sheet[] Sheets) BuildWorkbookWithOneScopedNameEachSheet(int sheetCount)
    {
        var workbook = new Workbook("perf");
        var sheets = new Sheet[sheetCount];
        for (var i = 0; i < sheetCount; i++)
            sheets[i] = workbook.AddSheet($"S{i}");

        // One sheet-scoped named formula per sheet -- the formula text itself is irrelevant to
        // the sheet-lookup cost under test; the FirstOrDefault/GetSheet scan runs unconditionally
        // for every entry in ScopedNamedFormulas before the rewriter even inspects the text
        // (RowColumnShiftHelpers.NamedRanges.cs:48-56).
        for (var i = 0; i < sheetCount; i++)
            workbook.DefineNamedFormula($"ScopedName_{i}", "1+1", sheets[i].Id);

        return (workbook, sheets);
    }

    private static double MeasureRewriteNamedFormulasMsPerCall(int sheetCount, int iterations)
    {
        var (workbook, sheets) = BuildWorkbookWithOneScopedNameEachSheet(sheetCount);
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
        const int largeSheetCount = 3_000; // 30x more sheets AND 30x more scoped names
        const int iterations = 15;

        var smallMs = MeasureRewriteNamedFormulasMsPerCall(smallSheetCount, iterations);
        var largeMs = MeasureRewriteNamedFormulasMsPerCall(largeSheetCount, iterations);

        // Floor the small measurement so a near-zero denominator can't produce a spuriously huge
        // (or spuriously tiny) ratio on a very fast machine.
        var ratio = largeMs / Math.Max(smallMs, 0.01);

        // With an O(1) GetSheet lookup, total cost is O(scoped-name-count) -- it should scale
        // ~linearly with the 30x growth in sheet count (and scoped-name count, since this
        // workbook defines one name per sheet). With the old O(sheet-count) FirstOrDefault scan,
        // total cost is O(scoped-name-count * sheet-count) -- quadratic, i.e. ~900x (30 * 30) for
        // the same growth. A threshold of 150 sits comfortably above the expected ~30x linear
        // scaling and comfortably below the ~900x quadratic scaling, so it distinguishes the two
        // implementations without being sensitive to per-call measurement noise.
        ratio.Should().BeLessThan(150,
            because: $"GetSheet(SheetId) is an O(1) dictionary lookup, so RewriteNamedFormulas's " +
                     $"cost should scale ~linearly (~{largeSheetCount / smallSheetCount}x) with " +
                     $"sheet count, not quadratically like the old workbook.Sheets.FirstOrDefault " +
                     $"scan would (smallMs={smallMs:F3}, largeMs={largeMs:F3}, ratio={ratio:F1})");
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
