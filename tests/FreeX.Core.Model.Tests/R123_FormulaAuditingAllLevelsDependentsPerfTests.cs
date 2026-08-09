using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R123-core-commands-formula-auditing-all-levels-perf: Trace Dependents / Go To Special
/// "All Levels" used to call <c>FormulaAuditingService.GetDirectDependents(Workbook, GridRange)</c>
/// once per BFS/recursion level -- and that method fully re-scans every sheet and, for any formula
/// containing a range/cross-sheet reference, fully re-lexes/re-parses its text
/// (<c>ExtractPrecedents</c>) to test whether it references the target. Driven once per newly
/// discovered dependent, across a chain of length L in a workbook with N "noisy" range-referencing
/// formula cells, the old code paid O(L * N) full re-parses. The fix builds a reverse-dependency
/// index once per multi-level trace (<c>FormulaAuditingService.BuildDependentsIndex</c>) and reuses
/// it for every level, paying O(N) parses total instead of O(L * N).
///
/// Both real entry points exercised through the actual product surface (not a hand-built model):
///   - GoToSpecialService.Find(..., GoToSpecialKind.Dependents, AllLevels: true) -- backs the
///     Go To Special dialog's Dependents "All Levels" option.
///   - FormulaAuditingService.GetDependentTraceArrows -- backs the ribbon's Trace Dependents
///     command and the keyboard "select all-level dependents" shortcut in both shells.
/// </summary>
public sealed class R123_FormulaAuditingAllLevelsDependentsPerfTests
{
    // Chain length (number of BFS/recursion levels) and workbook-wide "noise" formula count (cells
    // whose formula text contains ':' so they hit FormulaAuditingService's expensive re-parse
    // fallback, TryFormulaContainsLocalReferenceInRange's fast-path bailout, on every scan). Sized
    // so the O(L*N) pre-fix cost is clearly multiple seconds (proven manually via the cp-backup
    // revert technique) while the O(N) post-fix cost stays a small fraction of a second -- well
    // under this test's budget even on a loaded CI box.
    private const int ChainLength = 300;
    private const int NoiseFormulaCount = 10000;

    private static (Workbook workbook, Sheet sheet, CellAddress root) BuildChainWithNoise()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        // Noise formulas: each references an unrelated range (forces the full re-parse fallback in
        // GetDirectDependents on every call, but never matches the actual chain so it never adds a
        // spurious dependent).
        for (var i = 0; i < NoiseFormulaCount; i++)
        {
            var address = new CellAddress(sheet.Id, (uint)(1000 + i), 26); // column Z, rows 1000+
            sheet.SetCell(address, Cell.FromFormula("SUM(ZZ1:ZZ2)"));
        }

        // A strict one-dependent-per-level chain down column A: A1 -> A2 -> A3 -> ... -> A(L+1).
        var root = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(root, new NumberValue(1));
        for (var level = 1; level <= ChainLength; level++)
        {
            var current = new CellAddress(sheet.Id, (uint)(level + 1), 1);
            var previousRef = CellAddress.NumberToColumnName(1) + level;
            sheet.SetCell(current, Cell.FromFormula($"{previousRef}+1"));
        }

        return (workbook, sheet, root);
    }

    [Fact]
    public void GoToSpecialAllLevelsDependents_CompletesQuicklyOnLongChainWithManyNoiseFormulas()
    {
        var (workbook, sheet, root) = BuildChainWithNoise();
        var range = new GridRange(root, root);

        var stopwatch = Stopwatch.StartNew();
        var result = GoToSpecialService.Find(
            workbook,
            sheet,
            range,
            GoToSpecialKind.Dependents,
            activeCell: root,
            options: new GoToSpecialOptions(AllLevels: true));
        stopwatch.Stop();

        // Correctness: every cell in the chain must still be discovered.
        result.Should().HaveCount(ChainLength);

        // Performance: this is the assertion that fails before the fix (O(L*N) full re-parses --
        // multiple seconds for L=150, N=3000) and passes after it (O(N) parses total, well under a
        // second). Proven both ways with the cp-backup revert technique; see the round report.
        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(2),
            "the reverse-dependency index should be built once and reused across every BFS level, " +
            "not re-scanned/re-parsed from scratch per level");
    }

    [Fact]
    public void GetDependentTraceArrows_CompletesQuicklyOnLongChainWithManyNoiseFormulas()
    {
        var (workbook, _, root) = BuildChainWithNoise();

        var stopwatch = Stopwatch.StartNew();
        var arrows = FormulaAuditingService.GetDependentTraceArrows(workbook, root);
        stopwatch.Stop();

        arrows.Should().HaveCount(ChainLength);

        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(2),
            "the ribbon/keyboard Trace Dependents recursive collector should build the reverse-" +
            "dependency index once and reuse it at every recursion step, not re-scan/re-parse the " +
            "whole workbook per step");
    }

    /// <summary>
    /// No-regression sibling: the perf fix's region-overlap index must still be exactly as precise
    /// as the old flattened-cell containment check across the paths that matter most --
    /// range-precedent dependents AND cross-sheet dependents, combined in the SAME multi-level
    /// trace (the combination neither the pure-perf test above nor the pre-existing single-hop
    /// R91 tests exercise).
    /// </summary>
    [Fact]
    public void AllLevelsDependents_ReachesRangeAndCrossSheetDependentsAcrossMultipleLevels()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        var a1 = new CellAddress(sheet1.Id, 1, 1);
        // Level 1: a RANGE precedent dependent (SUM(A1:A3) depends on A1 via a range, not an exact
        // ref) -- exercises the index's range-bucket path, not just the exact-cell path.
        var rangeDependent = new CellAddress(sheet1.Id, 5, 1);
        // Level 2: a CROSS-SHEET dependent of the range dependent.
        var crossSheetDependent = new CellAddress(sheet2.Id, 1, 1);
        // Level 3: back on Sheet1, depending on the cross-sheet cell.
        var finalDependent = new CellAddress(sheet1.Id, 6, 1);

        sheet1.SetCell(a1, new NumberValue(1));
        sheet1.SetCell(rangeDependent, Cell.FromFormula("SUM(A1:A3)"));
        sheet2.SetCell(crossSheetDependent, Cell.FromFormula("Sheet1!A5*2"));
        sheet1.SetCell(finalDependent, Cell.FromFormula("Sheet2!A1+1"));

        var range = new GridRange(a1, a1);
        var result = GoToSpecialService.Find(
            workbook,
            sheet1,
            range,
            GoToSpecialKind.Dependents,
            activeCell: a1,
            options: new GoToSpecialOptions(AllLevels: true));

        // GoToSpecialService.Find only returns matches on the ACTIVE sheet (Sheet1), matching
        // Excel's own Go To Special semantics, but the traversal must still walk THROUGH the
        // cross-sheet hop to reach the level-3 dependent back on Sheet1.
        result.Should().Contain(rangeDependent).And.Contain(finalDependent);
        result.Should().NotContain(crossSheetDependent, "Find only returns matches on the active sheet");

        // The ribbon/keyboard trace-arrow path must reach the same full chain, including the
        // cross-sheet hop itself (trace arrows are not sheet-filtered).
        var arrows = FormulaAuditingService.GetDependentTraceArrows(workbook, a1);
        arrows.Should().Equal(
            new FormulaTraceArrow(a1, rangeDependent, FormulaTraceArrowKind.Dependent),
            new FormulaTraceArrow(rangeDependent, crossSheetDependent, FormulaTraceArrowKind.Dependent),
            new FormulaTraceArrow(crossSheetDependent, finalDependent, FormulaTraceArrowKind.Dependent));
    }
}
