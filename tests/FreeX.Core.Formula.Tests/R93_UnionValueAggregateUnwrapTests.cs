using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R93-AREAS-union-value-model follow-up: the round that introduced <see cref="UnionNode"/>/
/// <see cref="UnionValue"/> (see R93_AreasUnionValueModelTests) wired union support into AREAS,
/// SUM, AVERAGE, and the CollectNumbers helper only, and explicitly flagged that MIN/MAX/COUNT/
/// COUNTA/PRODUCT/MEDIAN and other aggregates with their own inline loops did not yet unwrap a
/// UnionValue argument -- silently falling through to their scalar default (0 for
/// MIN/MAX/PRODUCT-with-no-numeric-args, 0 for COUNT/COUNTA) instead of erroring OR computing the
/// right answer. That is a silent-wrong-answer bug, not a crash.
///
/// THE FIX (single choke point, not per-function patches): FormulaEvaluator.Functions.cs's
/// per-argument expansion loop already flattens a bare RangeValue argument into individual
/// (optionally ReferencedScalarValue-wrapped) cell values for every aggregate function before the
/// function body ever runs -- see the "!isStructured &amp;&amp; isAggregate &amp;&amp; value is
/// RangeValue" checks. A UnionValue argument fell through that same check untouched (UnionValue is
/// not a RangeValue). Three sites (the generic EvaluateNode fallback, the LET/LAMBDA-bound-name
/// path, and the named-formula path) now also match "value is UnionValue" and flatten via the new
/// FormulaEvaluator.References.cs helper FlattenUnionAreas (concatenates each area's Flatten() in
/// order, un-deduplicated, so overlapping areas are counted twice like Excel). Every aggregate
/// function in FormulaEvaluator.FunctionClassification.cs's AggregateFunctions set -- not just the
/// six named in the gap report -- gets union support from this one change, because they all pass
/// through this same expansion loop.
/// </summary>
public sealed class R93_UnionValueAggregateUnwrapTests
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

    // === THE GAP: named functions from the report, each with a union argument ===================
    // Reasoning per function (Excel ground truth):
    //  - MIN/MAX: numeric-only, ignore text/blank, an error cell anywhere in scope propagates.
    //  - COUNT: counts only numeric cells (numbers/dates/bools), ignores text/blank, ignores an
    //    error cell (does NOT propagate) -- COUNT is deliberately error-tolerant in Excel.
    //  - COUNTA: counts every non-blank cell (numbers, text, bools, errors all count).
    //  - PRODUCT: multiplies numeric cells, ignores text/blank; with no numeric cell anywhere
    //    returns 0 (Excel's PRODUCT() with only blanks is 0).
    //  - MEDIAN: numeric cells only, ignores text/blank.

    [Fact]
    public void Min_TwoAreaUnion_ReturnsMinimumAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(5)),  // A1
            (2u, 1u, new NumberValue(2)),  // A2
            (1u, 2u, new NumberValue(9)),  // B1
            (2u, 2u, new NumberValue(1))   // B2
        );

        _eval.Evaluate("=MIN((A1:A2,B1:B2))", sheet, workbook).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Max_TwoAreaUnion_ReturnsMaximumAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(5)),
            (2u, 1u, new NumberValue(2)),
            (1u, 2u, new NumberValue(9)),
            (2u, 2u, new NumberValue(1))
        );

        _eval.Evaluate("=MAX((A1:A2,B1:B2))", sheet, workbook).Should().Be(new NumberValue(9));
    }

    [Fact]
    public void Count_TwoAreaUnion_CountsNumericCellsAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),   // A1 numeric
            (2u, 1u, new TextValue("x")),   // A2 text (not counted)
            (1u, 2u, new NumberValue(3)),   // B1 numeric
            (2u, 2u, BlankValue.Instance)   // B2 blank (not counted)
        );

        _eval.Evaluate("=COUNT((A1:A2,B1:B2))", sheet, workbook).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void CountA_TwoAreaUnion_CountsEveryNonBlankCellAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),  // A1
            (2u, 1u, new TextValue("x")),  // A2 -- COUNTA counts text too
            (1u, 2u, new NumberValue(3)),  // B1
            (2u, 2u, BlankValue.Instance)  // B2 blank -- not counted
        );

        _eval.Evaluate("=COUNTA((A1:A2,B1:B2))", sheet, workbook).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Product_TwoAreaUnion_MultipliesAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(2)),  // A1
            (1u, 2u, new NumberValue(3)),  // B1
            (1u, 3u, new NumberValue(4))   // C1
        );

        _eval.Evaluate("=PRODUCT((A1:A1,B1:C1))", sheet, workbook).Should().Be(new NumberValue(24));
    }

    [Fact]
    public void Median_TwoAreaUnion_ComputesMedianAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),  // A1
            (2u, 1u, new NumberValue(2)),  // A2
            (1u, 2u, new NumberValue(3)),  // B1
            (2u, 2u, new NumberValue(100)) // B2
        );

        // Combined set {1,2,3,100} -> median of 4 values = (2+3)/2 = 2.5
        _eval.Evaluate("=MEDIAN((A1:A2,B1:B2))", sheet, workbook).Should().Be(new NumberValue(2.5));
    }

    // === Error propagation through the gap-closed functions =====================================

    [Fact]
    public void Min_UnionWithErrorCellInAnArea_PropagatesError()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (1u, 2u, ErrorValue.DivByZero)
        );

        _eval.Evaluate("=MIN((A1:A1,B1:B1))", sheet, workbook).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Count_UnionWithErrorCellInAnArea_IgnoresErrorLikePlainRange()
    {
        // COUNT is error-tolerant in Excel even for a plain range (COUNT(A1:A2) with an erroring
        // cell simply doesn't count it, it does not propagate) -- a union must match.
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (1u, 2u, ErrorValue.DivByZero)
        );

        _eval.Evaluate("=COUNT((A1:A1,B1:B1))", sheet, workbook).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void CountA_UnionWithErrorCellInAnArea_CountsTheErrorAsNonBlank()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (1u, 2u, ErrorValue.DivByZero)
        );

        _eval.Evaluate("=COUNTA((A1:A1,B1:B1))", sheet, workbook).Should().Be(new NumberValue(2));
    }

    // === Overlapping areas: Excel double-counts a cell covered by two areas ======================

    [Fact]
    public void Sum_OverlappingUnionAreas_DoubleCountsSharedCells()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),  // A1
            (2u, 1u, new NumberValue(2))   // A2
        );

        // (A1:A2,A1:A2) -- the same area listed twice -- must double-count: 1+2+1+2 = 6.
        _eval.Evaluate("=SUM((A1:A2,A1:A2))", sheet, workbook).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Count_OverlappingUnionAreas_DoubleCountsSharedCells()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (2u, 1u, new NumberValue(2))
        );

        _eval.Evaluate("=COUNT((A1:A2,A1:A2))", sheet, workbook).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Max_OverlappingUnionAreas_SameResultAsSingleArea_NoRegression()
    {
        // MAX isn't sensitive to double-counting (max of a multiset with duplicates is unchanged)
        // -- this pins that the overlap-handling change doesn't corrupt MAX's actual answer.
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(7)),
            (2u, 1u, new NumberValue(3))
        );

        _eval.Evaluate("=MAX((A1:A2,A1:A2))", sheet, workbook).Should().Be(new NumberValue(7));
    }

    // === Bare single-cell area inside a union ====================================================

    [Fact]
    public void Min_UnionWithBareSingleCellArea_TreatsItAsOneByOneRange()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(5)),  // A1
            (5u, 4u, new NumberValue(1))   // D5
        );

        _eval.Evaluate("=MIN((A1:A1,D5))", sheet, workbook).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void CountA_UnionWithBareSingleCellArea_CountsIt()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(5)),  // A1
            (5u, 4u, new TextValue("z"))   // D5
        );

        _eval.Evaluate("=COUNTA((A1:A1,D5))", sheet, workbook).Should().Be(new NumberValue(2));
    }

    // === An area that is entirely empty/blank ====================================================

    [Fact]
    public void Sum_UnionWithOneEntirelyBlankArea_IgnoresIt()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(10)) // A1; Z1:Z2 is never populated (blank)
        );

        _eval.Evaluate("=SUM((A1:A1,Z1:Z2))", sheet, workbook).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Count_UnionWithOneEntirelyBlankArea_ContributesZero()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(10)) // A1; Z1:Z2 is never populated (blank)
        );

        _eval.Evaluate("=COUNT((A1:A1,Z1:Z2))", sheet, workbook).Should().Be(new NumberValue(1));
    }

    // === No-regression: the same functions still work on a plain (non-union) range/args ==========

    [Fact]
    public void Min_PlainRange_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(5)),
            (2u, 1u, new NumberValue(2))
        );

        _eval.Evaluate("=MIN(A1:A2)", sheet, workbook).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Max_PlainRange_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(5)),
            (2u, 1u, new NumberValue(2))
        );

        _eval.Evaluate("=MAX(A1:A2)", sheet, workbook).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Count_PlainRange_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(5)),
            (2u, 1u, new TextValue("x"))
        );

        _eval.Evaluate("=COUNT(A1:A2)", sheet, workbook).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void CountA_PlainRange_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(5)),
            (2u, 1u, new TextValue("x"))
        );

        _eval.Evaluate("=COUNTA(A1:A2)", sheet, workbook).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Product_PlainRange_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(2)),
            (2u, 1u, new NumberValue(3))
        );

        _eval.Evaluate("=PRODUCT(A1:A2)", sheet, workbook).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Median_PlainRange_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (2u, 1u, new NumberValue(2)),
            (3u, 1u, new NumberValue(3))
        );

        _eval.Evaluate("=MEDIAN(A1:A3)", sheet, workbook).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Sum_PlainRange_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (2u, 1u, new NumberValue(2))
        );

        _eval.Evaluate("=SUM(A1:A2)", sheet, workbook).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Average_PlainRange_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(2)),
            (2u, 1u, new NumberValue(4))
        );

        _eval.Evaluate("=AVERAGE(A1:A2)", sheet, workbook).Should().Be(new NumberValue(3));
    }

    // === Sibling aggregate not explicitly named in the gap report, also routed through the same
    // choke point (STDEV.P is in AggregateFunctions and ReferenceProvenanceAggregates but has its
    // own body, not CollectNumbers) -- confirms the fix is a true choke point, not a per-name list.

    [Fact]
    public void StdevP_TwoAreaUnion_ComputesAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(2)),  // A1
            (1u, 2u, new NumberValue(4)),  // B1
            (1u, 3u, new NumberValue(4)),  // C1
            (1u, 4u, new NumberValue(4)),  // D1
            (1u, 5u, new NumberValue(5)),  // E1
            (1u, 6u, new NumberValue(5)),  // F1
            (1u, 7u, new NumberValue(7)),  // G1
            (1u, 8u, new NumberValue(9))   // H1
        );

        // Population stdev of {2,4,4,4,5,5,7,9} is exactly 2 -- split across two union areas
        // (A1:D1, E1:H1) must give the identical result to the plain contiguous range.
        _eval.Evaluate("=STDEV.P((A1:D1,E1:H1))", sheet, workbook).Should().Be(new NumberValue(2));
    }
}
