using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for round-32 finding R32-formula-lookup-modern-1:
///
/// XLOOKUP eagerly evaluated/consulted if_not_found (arg[3]) and returned its error even when
/// the lookup itself SUCCEEDED -- e.g. XLOOKUP(1,{1,2,3},{10,20,30},NA()) returned #N/A instead
/// of 10, and a fallback chain =XLOOKUP(a,T1,R1,XLOOKUP(a,T2,R2)) surfaced the inner XLOOKUP's
/// result/error even when `a` WAS found in T1. if_not_found must only be consulted when the
/// lookup actually fails to find a match -- like IFNA's lazy value_if_na.
///
/// Both the slow path (BuiltInFunctions.Lookup.Modern.cs's Xlookup, reached for array-literal or
/// IF()-wrapped range arguments) and the fast path (FormulaEvaluator.LookupFastPaths.cs's
/// TryEvaluateXlookupDirectRanges, reached for bare cell-range arguments) had this bug. Fixed by:
///   - Slow path: removing the eager `if (args[3] is ErrorValue) return that error` check --
///     XlookupScalar/XlookupScalarLinear already only read `ifNotFound` on their not-found
///     branches, so simply not short-circuiting before the lookup runs is sufficient.
///   - Fast path: deferring evaluation of node.Arguments[3] entirely until after the match index
///     is computed, only evaluating/consulting it when the match is not found.
/// </summary>
public class R32_LookupLazyIfNotFoundTests
{
    private readonly FormulaEvaluator _eval = new();

    // ── Fast path (bare cell-range arguments) ───────────────────────────────────────────────

    [Fact]
    public void Xlookup_DirectRange_MatchFound_IgnoresErroringIfNotFound()
    {
        // A1:A3 = {1,2,3}; if_not_found is NA() -- must never be evaluated/consulted since 2 IS
        // found in A1:A3.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)));

        _eval.Evaluate("=XLOOKUP(2,A1:A3,A1:A3,NA())", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Xlookup_DirectRange_GenuineNotFound_ReturnsSuppliedIfNotFound()
    {
        // Sibling already-working case: a genuine miss must still consult if_not_found.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)));

        _eval.Evaluate("=XLOOKUP(99,A1:A3,A1:A3,\"NF\")", sheet).Should().Be(new TextValue("NF"));
        _eval.Evaluate("=XLOOKUP(99,A1:A3,A1:A3,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Xlookup_DirectRange_GenuineNotFound_OmittedIfNotFound_DefaultsToNA()
    {
        // Sibling already-working case: omitted if_not_found on a genuine miss still defaults to
        // #N/A.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)));

        _eval.Evaluate("=XLOOKUP(99,A1:A3,A1:A3)", sheet).Should().Be(ErrorValue.NA);
    }

    // ── Slow path (array literals / IF()-wrapped ranges) ────────────────────────────────────

    [Fact]
    public void Xlookup_ArrayLiteral_MatchFound_IgnoresErroringIfNotFound()
    {
        // The exact failure scenario from the finding: a found match must return its value even
        // though if_not_found (NA()) would itself evaluate to an error.
        var sheet = new Sheet(SheetId.New(), "S");

        _eval.Evaluate("=XLOOKUP(1,{1,2,3},{10,20,30},NA())", sheet).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Xlookup_WrappedRange_MatchFound_IgnoresErroringIfNotFound()
    {
        // Same fix, reached through an IF()-wrapped bare range instead of an array literal.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)),
            (1, 2, new NumberValue(10)), (2, 2, new NumberValue(20)), (3, 2, new NumberValue(30)));

        _eval.Evaluate("=XLOOKUP(2,IF(TRUE,A1:A3),IF(TRUE,B1:B3),NA())", sheet).Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Xlookup_FallbackChain_OuterFound_ReturnsOuterValueNotInnerResult()
    {
        // Fallback-chain scenario: =XLOOKUP(a,T1,R1,XLOOKUP(a,T2,R2)). `a` (2) IS in T1 (A1:A3),
        // so the outer lookup's found value (20, from B1:B3) must be returned -- not whatever the
        // inner XLOOKUP(2,D1:D3,E1:E3) (a genuine miss, T2 has no 2) would have produced.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)),
            (1, 2, new NumberValue(10)), (2, 2, new NumberValue(20)), (3, 2, new NumberValue(30)),
            (1, 4, new NumberValue(7)), (2, 4, new NumberValue(8)), (3, 4, new NumberValue(9)),
            (1, 5, new NumberValue(70)), (2, 5, new NumberValue(80)), (3, 5, new NumberValue(90)));

        _eval.Evaluate("=XLOOKUP(2,IF(TRUE,A1:A3),IF(TRUE,B1:B3),XLOOKUP(2,D1:D3,E1:E3))", sheet)
            .Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Xlookup_ArrayLiteral_GenuineNotFound_StillReturnsIfNotFound()
    {
        // Sibling already-working case: a genuine miss must still surface if_not_found (even an
        // error one), proving the fix didn't turn if_not_found into a no-op.
        var sheet = new Sheet(SheetId.New(), "S");

        _eval.Evaluate("=XLOOKUP(99,{1,2,3},{10,20,30},\"NF\")", sheet).Should().Be(new TextValue("NF"));
        _eval.Evaluate("=XLOOKUP(99,{1,2,3},{10,20,30},NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Xlookup_ArrayLiteral_GenuineNotFound_OmittedIfNotFound_DefaultsToNA()
    {
        // Sibling already-working case: omitted if_not_found on a genuine miss still defaults to
        // #N/A on the slow path too.
        var sheet = new Sheet(SheetId.New(), "S");

        _eval.Evaluate("=XLOOKUP(99,{1,2,3},{10,20,30})", sheet).Should().Be(ErrorValue.NA);
    }

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, val) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), val);
        return sheet;
    }
}
