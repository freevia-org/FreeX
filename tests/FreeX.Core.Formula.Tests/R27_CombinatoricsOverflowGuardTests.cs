using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R27-math-trig-remaining-1: COMBINA used a flawed overflow pre-check (`n > 1029` / `n > 1029 -
/// k + 1`) that rejected ordinary, easily double-representable results whenever n exceeded 1029,
/// even for tiny k. The correct guard mirrors what CombinPositiveIntegers itself checks internally
/// for CombinPositiveIntegers(n+k-1, k): the *minimized* k (min(k, n-1)), not n directly.
///
/// R27-math-trig-remaining-3: COMBIN/COMBINA/PERMUT/PERMUTATIONA all rejected any n above
/// int.MaxValue even for the mathematically trivial cases k=0 (result 1) and k=1 (result n),
/// which are well within double precision and real Excel's numeric range. Fixed by special-casing
/// k=0/k=1 before the int-range guard that only the general (k&gt;=2) computation actually needs.
/// </summary>
public sealed class R27_CombinatoricsOverflowGuardTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void Combina_LargeNSmallK_NoLongerFalselyOverflows()
    {
        // Bug case (R27-math-trig-remaining-1): n=1029 tripped the old `n > 1029 - k + 1` check
        // even though C(1030,2) is an ordinary, easily double-representable number.
        _eval.Evaluate("=COMBINA(1029,2)", MakeSheet()).Should().Be(new NumberValue(529935));
    }

    [Fact]
    public void Combina_ExistingSmallCases_StillMatchExcel()
    {
        // Sibling already-working cases: small n/k combinations must be unaffected.
        _eval.Evaluate("=COMBINA(4,3)", MakeSheet()).Should().Be(new NumberValue(20));
        _eval.Evaluate("=COMBINA(10,3)", MakeSheet()).Should().Be(new NumberValue(220));
        _eval.Evaluate("=COMBINA(1030,1)", MakeSheet()).Should().Be(new NumberValue(1030));
        _eval.Evaluate("=COMBINA(1030,0)", MakeSheet()).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Combina_TrulyOverflowingLargeK_StillReturnsNumError()
    {
        // Sibling already-working case: large minimized-k combinations that genuinely overflow
        // (or exceed the 1029 minimized-k limit) must still error, not silently succeed.
        _eval.Evaluate("=COMBINA(100000,50000)", MakeSheet()).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=COMBINA(0,1)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Combin_HugeN_TrivialK_ReturnsExcelResultInsteadOfNumError()
    {
        // Bug case (R27-math-trig-remaining-3): n far above int.MaxValue with trivial k=0/1 is
        // mathematically trivial (C(n,0)=1, PERMUT(n,1)=n) and within Excel's numeric range.
        _eval.Evaluate("=COMBIN(3000000000,0)", MakeSheet()).Should().Be(new NumberValue(1));
        _eval.Evaluate("=PERMUT(3000000000,1)", MakeSheet()).Should().Be(new NumberValue(3000000000));
    }

    [Fact]
    public void Combin_HugeN_NonTrivialK_StillReturnsNumError()
    {
        // Sibling already-working case: a huge n with a non-trivial k still can't be computed via
        // the int-indexed algorithm and must keep erroring rather than silently misbehaving.
        _eval.Evaluate("=COMBIN(3000000000,2)", MakeSheet()).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=PERMUT(3000000000,2)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Combin_ExistingCases_StillMatchExcel()
    {
        // Sibling already-working cases from the ordinary (int-range) path.
        _eval.Evaluate("=COMBIN(5,2)", MakeSheet()).Should().Be(new NumberValue(10));
        _eval.Evaluate("=COMBIN(1030,2)", MakeSheet()).Should().Be(new NumberValue(529935));
        _eval.Evaluate("=COMBIN(1030,515)", MakeSheet()).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=PERMUT(5,2)", MakeSheet()).Should().Be(new NumberValue(20));
        _eval.Evaluate("=PERMUT(171,171)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    private static Sheet MakeSheet(params (uint Row, uint Col, ScalarValue Value)[] values)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, value) in values)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
        return sheet;
    }
}
