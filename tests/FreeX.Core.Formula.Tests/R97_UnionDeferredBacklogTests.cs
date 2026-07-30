using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R97-union-deferred-backlog: closes out the four functions R93/R94 deliberately left unhandled
/// when they wired union-reference support (the "(A1:B2,D5)" comma-operator syntax) into
/// FormulaEvaluator.Functions.cs's per-argument choke point.
///
/// DISPOSITION PER FUNCTION (see the code comments at each fix site for the full reasoning):
///  - DEVSQ: FIXED. Its variadic loop only ever flattens arguments into a numeric bag (same shape
///    contract as LARGE/SMALL/COUNTBLANK, R94's UnionMaterializableRangeFunctions members) -- added
///    to that set in FormulaEvaluator.FunctionClassification.cs. Before the fix, a union argument
///    matched none of Devsq's per-arg-type switch arms and was silently SKIPPED (contributed zero
///    numbers) rather than misread -- a different failure shape from LARGE/SMALL's #NUM!.
///  - FREQUENCY: FIXED. data_array and bins_array are two INDEPENDENT flat-bag arguments (never
///    shape-paired against each other), so each is safe to materialize independently -- also added
///    to UnionMaterializableRangeFunctions, which runs its check once per argument position.
///  - SUBTOTAL: FIXED, but NOT via the shared choke point. SUBTOTAL needs real per-cell sheet/row
///    provenance for hidden-row-skip and nested-SUBTOTAL/AGGREGATE exclusion, which the choke
///    point's MaterializeUnionRangeValue (one synthetic Nx1 RangeValue, IsSheetReference=false)
///    would destroy. Fixed instead with a bespoke union-aware loop inside Subtotal() itself
///    (BuiltInFunctions.Subtotal.cs) that processes each union area as its own genuine RangeValue
///    (which DOES carry real per-area provenance -- see EvaluateUnionNode in
///    FormulaEvaluator.References.cs) through the exact same row/col logic a plain range argument
///    uses. This mirrors Excel's native SUBTOTAL(fn,ref1,ref2,...) variadic-range support.
///  - MAXIFS/MINIFS: REASONED NON-FIX. max_range/min_range must be pairwise SHAPE-ALIGNED with one
///    or more criteria_range arguments (TryCreateConditionalCriteriaSet iterates matching (r,c)
///    positions). A per-argument independent materialization has no way to guarantee two
///    separately-materialized unions used the same area boundaries/order, so it could silently
///    misalign max_range's row N against a criteria_range row N from a different original area --
///    genuinely unsafe without a bespoke union-aware pairing loop (out of scope here). The current
///    #VALUE! (a raw UnionValue argument fails the `is RangeValue` guard) is kept and pinned by
///    test, matching Microsoft's documented "same size and shape" contract for these functions.
///
/// See also R97_UnionValueContainmentAndDeadCodeTests for the item-2 (dead-branch) and item-3
/// (containment) parts of this backlog.
/// </summary>
public sealed class R97_UnionDeferredBacklogTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Workbook MakeWorkbook(out Sheet sheet, params (uint row, uint col, ScalarValue val)[] cells)
    {
        var workbook = new Workbook("Test");
        sheet = workbook.AddSheet("S");
        foreach (var (row, col, val) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), val);
        return workbook;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // DEVSQ
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Devsq_TwoAreaUnion_ComputesAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),  // A1
            (2u, 1u, new NumberValue(2)),  // A2
            (1u, 2u, new NumberValue(3)),  // B1
            (2u, 2u, new NumberValue(4))   // B2
        );

        // {1,2,3,4}: mean=2.5, sum of squared deviations = 2.25+0.25+0.25+2.25 = 5.
        _eval.Evaluate("=DEVSQ((A1:A2,B1:B2))", sheet, workbook).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Devsq_ThreeAreaUnionWithBareSingleCell_ComputesAcrossAllAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),  // A1
            (2u, 1u, new NumberValue(2)),  // A2
            (1u, 2u, new NumberValue(3)),  // B1
            (5u, 4u, new NumberValue(4))   // D5 -- bare single-cell area
        );

        // Same {1,2,3,4} set as above, split across a two-cell area, a one-cell area, and a
        // bare single-cell reference -> DEVSQ = 5.
        _eval.Evaluate("=DEVSQ((A1:A2,B1:B1,D5))", sheet, workbook).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Devsq_OverlappingUnionAreas_DoubleCountsSharedCells()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(2)),
            (2u, 1u, new NumberValue(4))
        );

        // (A1:A2,A1:A2): {2,4,2,4}, mean=3, deviations sum = 1+1+1+1 = 4.
        _eval.Evaluate("=DEVSQ((A1:A2,A1:A2))", sheet, workbook).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Devsq_UnionWithOneEntirelyBlankArea_IgnoresIt()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(5)),  // A1; Z1:Z2 is never populated (blank)
            (2u, 1u, new NumberValue(5))   // A2
        );

        // {5,5}: DEVSQ = 0 regardless of the blank area contributing nothing.
        _eval.Evaluate("=DEVSQ((A1:A2,Z1:Z2))", sheet, workbook).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Devsq_UnionWithErrorCellInAnArea_PropagatesError()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (1u, 2u, ErrorValue.DivByZero)
        );

        _eval.Evaluate("=DEVSQ((A1:A1,B1:B1))", sheet, workbook).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Devsq_DefinedNameResolvingToUnion_ComputesAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (2u, 1u, new NumberValue(2)),
            (1u, 2u, new NumberValue(3)),
            (2u, 2u, new NumberValue(4))
        );
        workbook.NamedFormulas["U"] = "(A1:A2,B1:B2)";

        _eval.Evaluate("=DEVSQ(U)", sheet, workbook).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Devsq_SingleRange_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(2)),
            (2u, 1u, new NumberValue(4)),
            (3u, 1u, new NumberValue(6))
        );

        // {2,4,6}: mean=4, deviations sum = 4+0+4 = 8.
        _eval.Evaluate("=DEVSQ(A1:A3)", sheet, workbook).Should().Be(new NumberValue(8));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // FREQUENCY
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Frequency_DataArrayIsTwoAreaUnion_CountsAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),   // A1 (data)
            (2u, 1u, new NumberValue(5)),   // A2 (data)
            (1u, 2u, new NumberValue(9)),   // B1 (data)
            (1u, 4u, new NumberValue(3)),   // D1 (bin)
            (2u, 4u, new NumberValue(7))    // D2 (bin)
        );

        // data {1,5,9} vs bins {3,7}: 1<=3 -> bin0; 5 in (3,7] -> bin1; 9>7 -> overflow bin2.
        var result = _eval.Evaluate("=FREQUENCY((A1:A2,B1:B1),D1:D2)", sheet, workbook);
        var range = result.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(3);
        range.Cells[0, 0].Should().Be(new NumberValue(1));
        range.Cells[1, 0].Should().Be(new NumberValue(1));
        range.Cells[2, 0].Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Frequency_BinsArrayIsTwoAreaUnion_BinsAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),   // A1 (data)
            (2u, 1u, new NumberValue(5)),   // A2 (data)
            (3u, 1u, new NumberValue(9)),   // A3 (data)
            (1u, 4u, new NumberValue(3)),   // D1 (bin area 1)
            (1u, 5u, new NumberValue(7))    // E1 (bin area 2)
        );

        var result = _eval.Evaluate("=FREQUENCY(A1:A3,(D1:D1,E1:E1))", sheet, workbook);
        var range = result.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(3);
        range.Cells[0, 0].Should().Be(new NumberValue(1));
        range.Cells[1, 0].Should().Be(new NumberValue(1));
        range.Cells[2, 0].Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Frequency_UnionWithErrorCellInDataArray_PropagatesError()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (1u, 2u, ErrorValue.DivByZero),
            (1u, 4u, new NumberValue(3))
        );

        _eval.Evaluate("=FREQUENCY((A1:A1,B1:B1),D1)", sheet, workbook).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Frequency_PlainRanges_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (2u, 1u, new NumberValue(5)),
            (3u, 1u, new NumberValue(9)),
            (1u, 4u, new NumberValue(3)),
            (2u, 4u, new NumberValue(7))
        );

        var result = _eval.Evaluate("=FREQUENCY(A1:A3,D1:D2)", sheet, workbook);
        var range = result.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(3);
        range.Cells[0, 0].Should().Be(new NumberValue(1));
        range.Cells[1, 0].Should().Be(new NumberValue(1));
        range.Cells[2, 0].Should().Be(new NumberValue(1));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // SUBTOTAL
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Subtotal_Sum_TwoAreaUnion_SumsAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),  // A1
            (2u, 1u, new NumberValue(2)),  // A2
            (1u, 2u, new NumberValue(3)),  // B1
            (2u, 2u, new NumberValue(4))   // B2
        );

        _eval.Evaluate("=SUBTOTAL(9,(A1:A2,B1:B2))", sheet, workbook).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Subtotal_Sum_ThreeAreaUnionWithBareSingleCell_SumsAcrossAllAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),  // A1
            (2u, 1u, new NumberValue(2)),  // A2
            (1u, 2u, new NumberValue(3)),  // B1
            (5u, 4u, new NumberValue(4))   // D5 -- bare single-cell area
        );

        _eval.Evaluate("=SUBTOTAL(9,(A1:A2,B1:B1,D5))", sheet, workbook).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Subtotal_Sum_OverlappingUnionAreas_DoubleCountsSharedCells()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (2u, 1u, new NumberValue(2))
        );

        // (A1:A2,A1:A2) double-counts: 1+2+1+2 = 6, matching Excel's own overlap behavior
        // (and the identical R93 SUM((A1:A2,A1:A2)) overlap test).
        _eval.Evaluate("=SUBTOTAL(9,(A1:A2,A1:A2))", sheet, workbook).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Subtotal_Sum_UnionWithOneEntirelyBlankArea_IgnoresIt()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(10)) // A1; Z1:Z2 never populated (blank)
        );

        _eval.Evaluate("=SUBTOTAL(9,(A1:A1,Z1:Z2))", sheet, workbook).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Subtotal_Sum_UnionWithErrorCellInAnArea_PropagatesError()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (1u, 2u, ErrorValue.DivByZero)
        );

        _eval.Evaluate("=SUBTOTAL(9,(A1:A1,B1:B1))", sheet, workbook).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Subtotal_Sum_DefinedNameResolvingToUnion_SumsAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (2u, 1u, new NumberValue(2)),
            (1u, 2u, new NumberValue(3)),
            (2u, 2u, new NumberValue(4))
        );
        workbook.NamedFormulas["U"] = "(A1:A2,B1:B2)";

        _eval.Evaluate("=SUBTOTAL(9,U)", sheet, workbook).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Subtotal_SkipHidden_TwoAreaUnion_ExcludesHiddenRowInOneArea()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),    // A1
            (2u, 1u, new NumberValue(100)),  // A2 -- hidden
            (1u, 2u, new NumberValue(3)),    // B1
            (2u, 2u, new NumberValue(4))     // B2
        );
        sheet.GroupHiddenRows.Add(2);

        // funcNum 109 (100+9) skips hidden rows: row 2 is hidden sheet-wide, so BOTH A2's hidden
        // 100 (first area) and B2's 4 (second area, also row 2) are excluded from the union sum,
        // leaving 1+3 = 4. Proves per-area hidden-row provenance survives the union split
        // (compare against Subtotal_Sum_TwoAreaUnion_SumsAcrossBothAreas's 9-based analog).
        _eval.Evaluate("=SUBTOTAL(109,(A1:A2,B1:B2))", sheet, workbook).Should().Be(new NumberValue(4));
        // The non-hidden-aware sibling (9) must still include the hidden row, proving the
        // exclusion is genuine row-hidden detection, not the whole check being disabled.
        _eval.Evaluate("=SUBTOTAL(9,(A1:A2,B1:B2))", sheet, workbook).Should().Be(new NumberValue(108));
    }

    [Fact]
    public void Subtotal_SkipHidden_HiddenRowInSecondArea_IsExcluded()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),   // A1
            (1u, 2u, new NumberValue(3)),   // B1
            (2u, 2u, new NumberValue(100))  // B2 -- hidden, in the SECOND area
        );
        sheet.GroupHiddenRows.Add(2);

        _eval.Evaluate("=SUBTOTAL(109,(A1:A1,B1:B2))", sheet, workbook).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Subtotal_NestedSubtotalCell_ExcludedInsideUnionArea()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),  // A1
            (1u, 2u, new NumberValue(3))   // B1
        );
        // C1 holds a nested SUBTOTAL formula over A1:A1 -- Excel's anti-double-count rule must
        // exclude it from an outer SUBTOTAL that includes it, even inside a union area.
        var c1 = new CellAddress(sheet.Id, 1u, 3u);
        sheet.SetCell(c1, new Cell { FormulaText = "SUBTOTAL(9,A1:A1)", Value = new NumberValue(1) });

        // Union area (C1:C1,B1:B1): nested SUBTOTAL cell C1 excluded, only B1=3 counts.
        _eval.Evaluate("=SUBTOTAL(9,(C1:C1,B1:B1))", sheet, workbook).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Subtotal_Sum_PlainRange_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (2u, 1u, new NumberValue(2)),
            (3u, 1u, new NumberValue(3))
        );

        _eval.Evaluate("=SUBTOTAL(9,A1:A3)", sheet, workbook).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Subtotal_SkipHidden_PlainRange_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (2u, 1u, new NumberValue(100)),
            (3u, 1u, new NumberValue(3))
        );
        sheet.GroupHiddenRows.Add(2);

        _eval.Evaluate("=SUBTOTAL(109,A1:A3)", sheet, workbook).Should().Be(new NumberValue(4));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // MAXIFS / MINIFS -- reasoned non-fix, pinned current (Excel-consistent) behavior
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Maxifs_UnionMaxRange_ReturnsValueError()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(5)),   // A1
            (2u, 1u, new NumberValue(9)),   // A2
            (1u, 2u, new NumberValue(3)),   // B1
            (2u, 2u, new NumberValue(7)),   // B2
            (1u, 4u, new TextValue("x")),   // D1
            (2u, 4u, new TextValue("x")),   // D2
            (1u, 5u, new TextValue("x")),   // E1
            (2u, 5u, new TextValue("x"))    // E2
        );

        _eval.Evaluate("=MAXIFS((A1:A2,B1:B2),(D1:D2,E1:E2),\"x\")", sheet, workbook)
            .Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Minifs_UnionMinRange_ReturnsValueError()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(5)),
            (2u, 1u, new NumberValue(9)),
            (1u, 2u, new NumberValue(3)),
            (2u, 2u, new NumberValue(7)),
            (1u, 4u, new TextValue("x")),
            (2u, 4u, new TextValue("x")),
            (1u, 5u, new TextValue("x")),
            (2u, 5u, new TextValue("x"))
        );

        _eval.Evaluate("=MINIFS((A1:A2,B1:B2),(D1:D2,E1:E2),\"x\")", sheet, workbook)
            .Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Maxifs_PlainRange_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(5)),
            (2u, 1u, new NumberValue(9)),
            (1u, 2u, new TextValue("x")),
            (2u, 2u, new TextValue("x"))
        );

        _eval.Evaluate("=MAXIFS(A1:A2,B1:B2,\"x\")", sheet, workbook).Should().Be(new NumberValue(9));
    }

    [Fact]
    public void Minifs_PlainRange_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(5)),
            (2u, 1u, new NumberValue(9)),
            (1u, 2u, new TextValue("x")),
            (2u, 2u, new TextValue("x"))
        );

        _eval.Evaluate("=MINIFS(A1:A2,B1:B2,\"x\")", sheet, workbook).Should().Be(new NumberValue(5));
    }
}
