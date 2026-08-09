using System.Diagnostics;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-129 (task 2): SUMPRODUCT (BuiltInFunctions.MathCore.Aggregates.cs) and MMULT
/// (BuiltInFunctions.Matrix.cs -&gt; TryRangeToMatrix) both consume a <c>RangeValue</c> built by
/// <c>FormulaEvaluator.References.BuildRangeValue</c>, which materializes a dense
/// <c>new ScalarValue[rows, cols]</c> sized to the REFERENCED rectangle (after
/// <c>ClampOpenEndedRangeToUsed</c> clamps an open-ended A:A/1:1 reference's open end down to the
/// sheet's WHOLE-SHEET used range — not the specific referenced column/row's own populated extent).
///
/// DECISION (measured, not assumed): unlike the LARGE/SMALL/PERCENTILE/QUARTILE/MEDIAN/
/// AGGREGATE(13)/MODE.SNGL "bag of numbers" family fixed in round 127
/// (FormulaEvaluator.SelectionFastPaths.cs), SUMPRODUCT/MMULT's dense positional array is NOT safe
/// to shrink via a PER-ARGUMENT sparse clamp: every consumer of BuildRangeValue (INDEX, VLOOKUP/
/// HLOOKUP/MATCH/XLOOKUP fallback paths, MMULT, structured-table refs, OFFSET, ISFORMULA/
/// FORMULATEXT's multi-cell path, INDIRECT, ISREF's 2-D path, and SUMPRODUCT's own sibling-shape
/// matching) needs positional/dimensional integrity — see the extended reasoning on
/// <c>BuildRangeValue</c> itself. The measured cost of the newly-reachable case (round 126 removed
/// the #REF! that used to reject A:A/B:B outright) is real but BOUNDED: a single full-column
/// reference can never exceed CellAddress.MaxRow (1,048,576) rows no matter how far away the stray
/// data that inflates the sheet's used range sits, and this is the exact scenario
/// FormulaSafetyLimits.MaxMaterializedRangeCells (16,777,216 cells, ~134MB worst case) was already
/// sized to bound for the equivalent explicit-range case. Accepted per option (d): documented, with
/// this test file pinning the measured numbers so a future change that makes the allocation
/// materially worse (e.g. a regression that drops the sheet-wide clamp entirely) is caught.
/// </summary>
public sealed class R129_SumproductMmultFullColumnAllocationBoundTests
{
    private readonly ITestOutputHelper _output;
    public R129_SumproductMmultFullColumnAllocationBoundTests(ITestOutputHelper output) => _output = output;

    private static (Workbook Workbook, Sheet Sheet) MakeSparseTopWithFarUnrelatedCell()
    {
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        for (uint r = 1; r <= 10; r++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));
            sheet.SetCell(new CellAddress(sheet.Id, r, 2), new NumberValue(r * 2));
        }
        // One stray value far down an UNRELATED column (Z), simulating a real workbook footer/summary
        // cell hundreds of thousands of rows below the actual data this formula cares about. This is
        // exactly the case round126 made newly reachable (previously an immediate #REF!).
        sheet.SetCell(new CellAddress(sheet.Id, 900_000, 26), new NumberValue(1));
        return (wb, sheet);
    }

    [Fact]
    public void Sumproduct_FullColumn_BenignSheet_NoFarData_AllocatesNegligibly()
    {
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        for (uint r = 1; r <= 10; r++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));
            sheet.SetCell(new CellAddress(sheet.Id, r, 2), new NumberValue(r * 2));
        }
        var eval = new FormulaEvaluator();
        eval.Evaluate("=SUMPRODUCT(A1:A10,B1:B10)", sheet, wb); // warm up JIT/parse caches

        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = eval.Evaluate("=SUMPRODUCT(A:A,B:B)", sheet, wb);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        result.Should().Be(new NumberValue(770));
        // No-regression sibling: when the sheet's used range genuinely matches the populated data
        // (no far-away stray cell), the clamp keeps this cheap -- orders of magnitude below the
        // ~29,000,000-byte adversarial figure measured in the sibling test below. The ceiling here is
        // deliberately generous (test-harness/JIT overhead observed up to ~135,000 bytes for the
        // surrounding parse/evaluate machinery, not the range materialization itself) so this stays a
        // reliable "still cheap" signal rather than a flaky exact-byte pin.
        allocated.Should().BeLessThan(2_000_000,
            "a full-column SUMPRODUCT over a sheet whose used range matches the populated data should stay cheap, nowhere near the ~29MB adversarial figure");
    }

    [Fact]
    public void Sumproduct_FullColumn_WithFarUnrelatedData_StillComputesTheCorrectResult()
    {
        var (wb, sheet) = MakeSparseTopWithFarUnrelatedCell();
        var eval = new FormulaEvaluator();

        var result = eval.Evaluate("=SUMPRODUCT(A:A,B:B)", sheet, wb);

        // sum(r * 2r) for r in 1..10 = 2 * sum(r^2) = 2 * 385 = 770. The far stray cell in column Z
        // must not perturb the result -- correctness survives the sheet-wide clamp even though the
        // clamp pulls in a lot of genuinely-blank rows.
        result.Should().Be(new NumberValue(770));
    }

    [Fact]
    public void Sumproduct_FullColumn_WithFarUnrelatedData_AllocationStaysWithinTheDesignedCeiling()
    {
        var (wb, sheet) = MakeSparseTopWithFarUnrelatedCell();
        var eval = new FormulaEvaluator();
        eval.Evaluate("=SUMPRODUCT(A1:A10,B1:B10)", sheet, wb); // warm up

        var sw = Stopwatch.StartNew();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = eval.Evaluate("=SUMPRODUCT(A:A,B:B)", sheet, wb);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        sw.Stop();

        _output.WriteLine($"SUMPRODUCT(A:A,B:B) with far unrelated data: allocated={allocated:N0} bytes, elapsed={sw.Elapsed.TotalMilliseconds:F1} ms");
        result.Should().Be(new NumberValue(770));

        // Measured today: ~29,000,000 bytes / ~150ms (two ~900,000-row BuildRangeValue arrays, one
        // per SUMPRODUCT argument). This ceiling is deliberately generous (2x the measured figure)
        // -- it exists to catch a REGRESSION that removes the sheet-wide clamp (which would make this
        // approach the full CellAddress.MaxRow=1,048,576 height, or worse, revert to the pre-cap
        // unbounded behavior), not to assert the current cost is optimal. See BuildRangeValue's own
        // doc comment for why a tighter per-argument clamp is unsafe here.
        allocated.Should().BeLessThan(70_000_000,
            "the clamp must keep this bounded near the sheet's actual used-range row count, not balloon toward CellAddress.MaxRow or beyond");
    }

    [Fact]
    public void Mmult_FullColumnOperand_WithFarUnrelatedData_StillReturnsTheSameOutcomeAsAnExplicitClampedRange()
    {
        var (wb, sheet) = MakeSparseTopWithFarUnrelatedCell();
        var eval = new FormulaEvaluator();

        // A:A (10 populated rows, clamped by the far Z cell) times a 1x1 B1 selector isolates
        // BuildRangeValue's full-column materialization on a single operand. This codebase's MMULT
        // (TryRangeToMatrix) requires every cell in a matrix operand to be numeric (TryCellNumber
        // rejects blanks), so a full-column operand whose clamped tail is mostly blank rows already
        // returns #VALUE! regardless of this round's decision -- that's pre-existing MMULT behavior,
        // not something round126/129 changed. The point of this test is that A:A produces the exact
        // same outcome as the equivalent explicit clamped range (A1:A<clampedRowCount>), proving the
        // sheet-wide clamp doesn't silently corrupt/truncate the operand differently than an explicit
        // reference would.
        var fullColumnResult = eval.Evaluate("=MMULT(A:A,B1:B1)", sheet, wb);
        var explicitClampedResult = eval.Evaluate("=MMULT(A1:A900000,B1:B1)", sheet, wb);

        fullColumnResult.Should().Be(ErrorValue.Value, "MMULT rejects a matrix operand with blank cells beyond A's populated rows");
        fullColumnResult.Should().Be(explicitClampedResult, "the full-column shorthand must clamp to the same extent as the equivalent explicit range");
    }

    [Fact]
    public void Mmult_FullColumnOperand_WithFarUnrelatedData_AllocationStaysWithinTheDesignedCeiling()
    {
        var (wb, sheet) = MakeSparseTopWithFarUnrelatedCell();
        var eval = new FormulaEvaluator();
        eval.Evaluate("=MMULT(A1:A10,B1:B1)", sheet, wb); // warm up

        var sw = Stopwatch.StartNew();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = eval.Evaluate("=MMULT(A:A,B1:B1)", sheet, wb);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        sw.Stop();

        _output.WriteLine($"MMULT(A:A,B1:B1) with far unrelated data: allocated={allocated:N0} bytes, elapsed={sw.Elapsed.TotalMilliseconds:F1} ms");
        result.Should().Be(ErrorValue.Value);

        // Measured today: ~14,400,000 bytes / ~57ms (one ~900,000-row BuildRangeValue array, PLUS
        // TryRangeToMatrix's own separate double[,] copy) even though the result is #VALUE! -- the
        // allocation happens before the blank-cell rejection is discovered. Same rationale as the
        // SUMPRODUCT ceiling above -- generous but present to catch a real regression.
        allocated.Should().BeLessThan(40_000_000,
            "MMULT's full-column operand allocation must stay bounded near the sheet's actual used-range row count");
    }

    [Fact]
    public void Sumproduct_ExplicitHugeBoundedRange_PreExistingBehavior_StillComputesTheCorrectResult()
    {
        // No-regression sibling for the PRE-EXISTING (not round-126-introduced) case: an explicit
        // bounded range like A1:A900000 was never touched by ClampOpenEndedRangeToUsed (it only
        // clamps ranges whose end reaches CellAddress.MaxRow/MaxCol) and has always materialized the
        // full nominal rectangle regardless of the sheet's used range. Confirms this round's
        // decision didn't change (and didn't need to change) that existing, already-accepted cost.
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        for (uint r = 1; r <= 10; r++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));
            sheet.SetCell(new CellAddress(sheet.Id, r, 2), new NumberValue(r * 2));
        }
        var eval = new FormulaEvaluator();

        var result = eval.Evaluate("=SUMPRODUCT(A1:A900000,B1:B900000)", sheet, wb);

        result.Should().Be(new NumberValue(770));
    }
}
