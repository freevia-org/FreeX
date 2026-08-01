using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R112-aggregate-union-ref1: AGGREGATE (any function_num 1-19) mishandled a parenthesized-union
/// reference argument (e.g. <c>=AGGREGATE(9,0,(A1:A5,C1:C5))</c>). Excel evaluates AGGREGATE's
/// ref1 as a genuine reference, so a union area-list is valid and must aggregate across every area
/// -- exactly like SUBTOTAL/SUM's own union support (BuiltInFunctions.Subtotal.cs, R97/R93). Before
/// this fix, AGGREGATE is deliberately excluded from FormulaEvaluator.FunctionClassification.cs's
/// UnionMaterializableRangeFunctions set (needs per-area sheet/row provenance for hidden-row and
/// nested-SUBTOTAL/AGGREGATE checks, same as SUBTOTAL), so the raw UnionValue reached AGGREGATE's
/// four data-collection helpers unmodified and matched none of their type checks:
///   - CollectAggregateNumbers (funcNum 12, 14-19): union silently contributed ZERO numbers.
///   - AggregateNumericStreaming (funcNum 1-11 except 3): union silently contributed nothing.
///   - AggregateCountA (funcNum 3): a UnionValue is "not BlankValue", so it incremented count by
///     exactly 1 (treating the whole multi-area union as one non-blank scalar) instead of counting
///     every real non-blank cell across all areas.
///   - AggregateModeSnglStreaming (funcNum 13): union silently contributed nothing.
///
/// This test class exercises all four helpers with a union ref1, plus hidden-row and nested-
/// AGGREGATE/error provenance (mirroring R97_UnionDeferredBacklogTests's SUBTOTAL coverage), plus
/// no-regression checks for plain-range ref1.
/// </summary>
public sealed class R112_AggregateUnionRef1Tests
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
    // AggregateNumericStreaming (funcNum 1-11 except 3) -- e.g. SUM (9)
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Sum_TwoAreaUnion_SumsAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),  // A1
            (2u, 1u, new NumberValue(2)),  // A2
            (1u, 3u, new NumberValue(3)),  // C1
            (2u, 3u, new NumberValue(4))   // C2
        );

        _eval.Evaluate("=AGGREGATE(9,0,(A1:A2,C1:C2))", sheet, workbook).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Sum_IgnoreHiddenRows_TwoAreaUnion_ExcludesHiddenRowInBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),    // A1
            (2u, 1u, new NumberValue(100)),  // A2 -- hidden
            (1u, 3u, new NumberValue(3)),    // C1
            (2u, 3u, new NumberValue(4))     // C2 -- hidden (same row)
        );
        sheet.HiddenRows.Add(2);

        // options=1: ignore hidden rows. Row 2 excluded in both areas -> 1+3 = 4.
        _eval.Evaluate("=AGGREGATE(9,1,(A1:A2,C1:C2))", sheet, workbook).Should().Be(new NumberValue(4));
        // options=0 must still include the hidden row, proving the exclusion is genuine.
        _eval.Evaluate("=AGGREGATE(9,0,(A1:A2,C1:C2))", sheet, workbook).Should().Be(new NumberValue(108));
    }

    [Fact]
    public void Sum_UnionWithErrorCellInAnArea_PropagatesError()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (1u, 3u, ErrorValue.DivByZero)
        );

        // options=0: do not ignore errors.
        _eval.Evaluate("=AGGREGATE(9,0,(A1:A1,C1:C1))", sheet, workbook).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Sum_UnionWithErrorCellInAnArea_Option6IgnoresError()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (1u, 3u, ErrorValue.DivByZero),
            (2u, 3u, new NumberValue(9))
        );

        // options=6: ignore error values -> only 1 + 9 = 10 count.
        _eval.Evaluate("=AGGREGATE(9,6,(A1:A1,C1:C2))", sheet, workbook).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Sum_NestedAggregateCellInsideUnionArea_ExcludedByDefault()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),  // A1
            (1u, 3u, new NumberValue(3))   // C1
        );
        // B1 holds a nested AGGREGATE formula -- Excel's anti-double-count rule excludes it from
        // an outer AGGREGATE that includes it, even inside a union area (options<=3 default).
        var b1 = new CellAddress(sheet.Id, 1u, 2u);
        sheet.SetCell(b1, new Cell { FormulaText = "AGGREGATE(9,0,A1:A1)", Value = new NumberValue(1) });

        // Union area (B1:B1,C1:C1): nested-AGGREGATE cell B1 excluded, only C1=3 counts.
        _eval.Evaluate("=AGGREGATE(9,0,(B1:B1,C1:C1))", sheet, workbook).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Sum_PlainRange_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (2u, 1u, new NumberValue(2)),
            (3u, 1u, new NumberValue(3))
        );

        _eval.Evaluate("=AGGREGATE(9,0,A1:A3)", sheet, workbook).Should().Be(new NumberValue(6));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // AggregateCountA (funcNum 3) -- the sharpest failure mode: a raw UnionValue "is not
    // BlankValue" so it silently counted as ONE non-blank scalar instead of iterating real cells.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CountA_TwoAreaUnion_CountsRealNonBlankCellsNotOne()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),  // A1
            (2u, 1u, new NumberValue(2)),  // A2
            (1u, 3u, new TextValue("x"))   // C1; C2 left blank
        );

        // Before the fix this returned 1 (the whole UnionValue treated as one non-blank scalar).
        // Real non-blank cells across both areas: A1, A2, C1 -> 3.
        _eval.Evaluate("=AGGREGATE(3,0,(A1:A2,C1:C2))", sheet, workbook).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void CountA_PlainRange_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (2u, 1u, new TextValue("x"))
        );

        _eval.Evaluate("=AGGREGATE(3,0,A1:A2)", sheet, workbook).Should().Be(new NumberValue(2));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // CollectAggregateNumbers (funcNum 12, 14-19) -- e.g. MEDIAN (12)
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Median_TwoAreaUnion_ComputesAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),  // A1
            (2u, 1u, new NumberValue(2)),  // A2
            (1u, 3u, new NumberValue(3)),  // C1
            (2u, 3u, new NumberValue(4))   // C2
        );

        // {1,2,3,4} -> median 2.5.
        _eval.Evaluate("=AGGREGATE(12,0,(A1:A2,C1:C2))", sheet, workbook).Should().Be(new NumberValue(2.5));
    }

    [Fact]
    public void Median_PlainRange_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(1)),
            (2u, 1u, new NumberValue(2)),
            (3u, 1u, new NumberValue(3))
        );

        _eval.Evaluate("=AGGREGATE(12,0,A1:A3)", sheet, workbook).Should().Be(new NumberValue(2));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // AggregateModeSnglStreaming (funcNum 13)
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ModeSngl_TwoAreaUnion_ComputesAcrossBothAreas()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(5)),  // A1
            (2u, 1u, new NumberValue(5)),  // A2
            (1u, 3u, new NumberValue(7)),  // C1
            (2u, 3u, new NumberValue(9))   // C2
        );

        // {5,5,7,9} -> mode 5.
        _eval.Evaluate("=AGGREGATE(13,0,(A1:A2,C1:C2))", sheet, workbook).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void ModeSngl_PlainRange_NoRegression()
    {
        var workbook = MakeWorkbook(out var sheet,
            (1u, 1u, new NumberValue(5)),
            (2u, 1u, new NumberValue(5)),
            (3u, 1u, new NumberValue(7))
        );

        _eval.Evaluate("=AGGREGATE(13,0,A1:A3)", sheet, workbook).Should().Be(new NumberValue(5));
    }
}
