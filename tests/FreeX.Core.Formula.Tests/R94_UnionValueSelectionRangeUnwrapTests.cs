using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R94-formula-union-selection-range: R93 unwrapped a parenthesized union argument (e.g. the
/// "(A1:A2,B1:B2)" in MIN((A1:A2,B1:B2))) into flattened cell values ONLY for functions in
/// AggregateFunctions (see R93_UnionValueAggregateUnwrapTests). LARGE, SMALL, RANK/RANK.EQ/
/// RANK.AVG, PERCENTILE(.INC/.EXC), QUARTILE(.INC/.EXC), TRIMMEAN, PERCENTRANK(.INC/.EXC), and
/// COUNTBLANK are classified as StructuredRangeFunctions instead (isAggregate is false for them),
/// so the aggregate-flatten branches never ran and a UnionValue argument reached each function's
/// own "args[i] is RangeValue r ? r : wrap-as-1x1" fallback, which silently misread the whole
/// UnionValue object as one opaque scalar cell -- CollectRangeNumbersForSelection/
/// CollectRangeNumbers's cell-type switch doesn't recognize UnionValue, so it contributed zero
/// numbers instead of every cell across every area. Before this fix, e.g. LARGE((A1:A5,C1:C5),1)
/// returned #NUM! (k=1 > nums.Count=0) instead of the largest value across both areas.
///
/// THE FIX: FormulaEvaluator.Functions.cs's per-argument expansion loop now also materializes a
/// UnionValue argument into one synthetic Nx1 RangeValue (MaterializeUnionRangeValue in
/// FormulaEvaluator.References.cs, reusing FlattenUnionAreas) for the functions named in
/// FormulaEvaluator.FunctionClassification.cs's new UnionMaterializableRangeFunctions set --
/// exactly the StructuredRangeFunctions whose range argument is consumed as a flat, shape-agnostic
/// bag of numbers, so collapsing every area into a single column is safe. Unlike the aggregate
/// case, the union is materialized into ONE RangeValue argument (not spread across expandedArgs as
/// individual scalars), because LARGE/RANK/etc. are fixed-arity functions, not variadic.
/// </summary>
public sealed class R94_UnionValueSelectionRangeUnwrapTests
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

    // === THE GAP, closed: LARGE is the headline case from the finding =========================

    [Fact]
    public void Large_TwoAreaUnion_ReturnsLargestAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(5)),  // A1
            (2u, 1u, new NumberValue(2)),  // A2
            (1u, 2u, new NumberValue(9)),  // B1
            (2u, 2u, new NumberValue(1))   // B2
        );

        // Before the fix this returned #NUM! (k=1 > nums.Count=0); Excel-correct answer is 9.
        _eval.Evaluate("=LARGE((A1:A2,B1:B2),1)", sheet, workbook).Should().Be(new NumberValue(9));
    }

    [Fact]
    public void Large_ThirdLargestAcrossTwoAreaUnion_ReturnsCorrectRank()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(5)),  // A1
            (2u, 1u, new NumberValue(2)),  // A2
            (1u, 2u, new NumberValue(9)),  // B1
            (2u, 2u, new NumberValue(1))   // B2
        );

        // Combined {5,2,9,1} sorted desc: 9,5,2,1 -> 3rd largest = 2.
        _eval.Evaluate("=LARGE((A1:A2,B1:B2),3)", sheet, workbook).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Small_TwoAreaUnion_ReturnsSmallestAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(5)),  // A1
            (2u, 1u, new NumberValue(2)),  // A2
            (1u, 2u, new NumberValue(9)),  // B1
            (2u, 2u, new NumberValue(1))   // B2
        );

        _eval.Evaluate("=SMALL((A1:A2,B1:B2),1)", sheet, workbook).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Rank_TwoAreaUnion_ComputesRankAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(10)),  // A1
            (2u, 1u, new NumberValue(20)),  // A2
            (1u, 2u, new NumberValue(30)),  // B1
            (2u, 2u, new NumberValue(40)),  // B2
            (1u, 4u, new NumberValue(20))   // D1 -- the number being ranked
        );

        // Descending rank of 20 within {10,20,30,40} is 3rd (40,30,20,10).
        _eval.Evaluate("=RANK(D1,(A1:A2,B1:B2))", sheet, workbook).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void RankEq_TwoAreaUnion_MatchesPlainRank()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(10)),
            (2u, 1u, new NumberValue(20)),
            (1u, 2u, new NumberValue(30)),
            (2u, 2u, new NumberValue(40)),
            (1u, 4u, new NumberValue(20))
        );

        _eval.Evaluate("=RANK.EQ(D1,(A1:A2,B1:B2))", sheet, workbook).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void RankAvg_TwoAreaUnionWithTie_AveragesTiedRanks()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(10)),  // A1
            (2u, 1u, new NumberValue(20)),  // A2
            (1u, 2u, new NumberValue(20)),  // B1 -- ties D1
            (2u, 2u, new NumberValue(40)),  // B2
            (1u, 4u, new NumberValue(20))   // D1
        );

        // Descending: 40 (1), 20/20 (tied 2nd/3rd -> 2.5), 10 (4).
        _eval.Evaluate("=RANK.AVG(D1,(A1:A2,B1:B2))", sheet, workbook).Should().Be(new NumberValue(2.5));
    }

    [Fact]
    public void PercentileInc_TwoAreaUnion_ComputesAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),  // A1
            (2u, 1u, new NumberValue(2)),  // A2
            (1u, 2u, new NumberValue(3)),  // B1
            (2u, 2u, new NumberValue(4))   // B2
        );

        // {1,2,3,4}: PERCENTILE.INC at 1.0 is the max = 4.
        _eval.Evaluate("=PERCENTILE.INC((A1:A2,B1:B2),1)", sheet, workbook).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void QuartileInc_TwoAreaUnion_ComputesAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),  // A1
            (2u, 1u, new NumberValue(2)),  // A2
            (1u, 2u, new NumberValue(3)),  // B1
            (2u, 2u, new NumberValue(4))   // B2
        );

        // {1,2,3,4}: QUARTILE.INC quart=4 is the max = 4.
        _eval.Evaluate("=QUARTILE.INC((A1:A2,B1:B2),4)", sheet, workbook).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Trimmean_TwoAreaUnion_ComputesAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)), (2u, 1u, new NumberValue(2)),
            (3u, 1u, new NumberValue(3)), (4u, 1u, new NumberValue(4)),
            (5u, 1u, new NumberValue(5)),
            (1u, 2u, new NumberValue(6)), (2u, 2u, new NumberValue(7)),
            (3u, 2u, new NumberValue(8)), (4u, 2u, new NumberValue(9)),
            (5u, 2u, new NumberValue(10))
        );

        // Combined {1..10}, trim 20% -> floor(10*0.2/2)=1 from each end -> {2..9} average = 5.5.
        _eval.Evaluate("=TRIMMEAN((A1:A5,B1:B5),0.2)", sheet, workbook).Should().Be(new NumberValue(5.5));
    }

    [Fact]
    public void PercentrankInc_TwoAreaUnion_ComputesAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),  // A1
            (2u, 1u, new NumberValue(2)),  // A2
            (1u, 2u, new NumberValue(3)),  // B1
            (2u, 2u, new NumberValue(4)),  // B2
            (1u, 4u, new NumberValue(3))   // D1
        );

        // {1,2,3,4}: percent rank of 3 is (3-1)/(4-1) = 2/3, truncated to the default 3
        // significant digits (matching this engine's PERCENTRANK.INC truncation convention).
        _eval.Evaluate("=PERCENTRANK.INC((A1:A2,B1:B2),D1)", sheet, workbook)
            .Should().Be(new NumberValue(0.666));
    }

    [Fact]
    public void Countblank_TwoAreaUnion_CountsBlanksAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),   // A1 non-blank
            (2u, 1u, BlankValue.Instance),  // A2 blank
            (1u, 2u, BlankValue.Instance),  // B1 blank
            (2u, 2u, new NumberValue(4))    // B2 non-blank
        );

        _eval.Evaluate("=COUNTBLANK((A1:A2,B1:B2))", sheet, workbook).Should().Be(new NumberValue(2));
    }

    // === No-regression: the same functions still work on a plain (non-union) range ==============

    [Fact]
    public void Large_PlainRange_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(5)),
            (2u, 1u, new NumberValue(2)),
            (3u, 1u, new NumberValue(9))
        );

        _eval.Evaluate("=LARGE(A1:A3,1)", sheet, workbook).Should().Be(new NumberValue(9));
    }

    [Fact]
    public void Small_PlainRange_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(5)),
            (2u, 1u, new NumberValue(2)),
            (3u, 1u, new NumberValue(9))
        );

        _eval.Evaluate("=SMALL(A1:A3,1)", sheet, workbook).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Rank_PlainRange_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(10)),
            (2u, 1u, new NumberValue(20)),
            (3u, 1u, new NumberValue(30)),
            (1u, 4u, new NumberValue(20))
        );

        _eval.Evaluate("=RANK(D1,A1:A3)", sheet, workbook).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void PercentileInc_PlainRange_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (2u, 1u, new NumberValue(2)),
            (3u, 1u, new NumberValue(3)),
            (4u, 1u, new NumberValue(4))
        );

        _eval.Evaluate("=PERCENTILE.INC(A1:A4,1)", sheet, workbook).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Countblank_PlainRange_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (2u, 1u, BlankValue.Instance)
        );

        _eval.Evaluate("=COUNTBLANK(A1:A2)", sheet, workbook).Should().Be(new NumberValue(1));
    }
}
