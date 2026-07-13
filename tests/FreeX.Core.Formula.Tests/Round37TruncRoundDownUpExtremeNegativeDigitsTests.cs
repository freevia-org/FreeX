using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-37 regression test:
///  - R37-formula-math-precision-2-1: TRUNC/ROUNDDOWN/ROUNDUP must match ROUND's
///    behavior for a very-negative num_digits and return 0 rather than #NUM!.
///    Their double-fallback path (used once digits is out of DecimalPower10's
///    decimal-precision range) computed `factor = Math.Pow(10, digits)`, which for
///    a large-magnitude negative digits *underflows* to a finite 0.0 (not caught by
///    the `!double.IsFinite(factor)` guard), turning the final division into
///    0.0 / 0.0 = NaN -> #NUM!. ROUND's mirrored fallback uses
///    `factor = Math.Pow(10, -digits)`, which *overflows* to Infinity for the same
///    inputs and is correctly caught by the same guard, returning 0.
/// </summary>
public sealed class Round37TruncRoundDownUpExtremeNegativeDigitsTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet() => new(SheetId.New(), "S");

    [Fact]
    public void Trunc_ExtremeNegativeDigits_ReturnsZero_LikeRound()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=TRUNC(123,-400)", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Rounddown_ExtremeNegativeDigits_ReturnsZero_LikeRound()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=ROUNDDOWN(123,-400)", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Roundup_ExtremeNegativeDigits_ReturnsZero_LikeRound()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=ROUNDUP(123,-400)", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Round_ExtremeNegativeDigits_StillReturnsZero_ForComparison()
    {
        // ROUND already handled this correctly before the fix; pin it as a baseline
        // so a future regression in the sibling functions is caught against ROUND too.
        var sheet = MakeSheet();
        _eval.Evaluate("=ROUND(123,-400)", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Rounddown_NormalNegativeDigits_StillRoundsCorrectly()
    {
        // No-regression: an ordinary (non-extreme) negative num_digits must still
        // truncate toward zero at the requested power of ten.
        var sheet = MakeSheet();
        _eval.Evaluate("=ROUNDDOWN(12345,-2)", sheet).Should().Be(new NumberValue(12300));
    }

    [Fact]
    public void Roundup_NormalNegativeDigits_StillRoundsCorrectly()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=ROUNDUP(12345,-2)", sheet).Should().Be(new NumberValue(12400));
    }

    [Fact]
    public void Trunc_NormalPositiveDigits_StillTruncatesCorrectly()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=TRUNC(8.9,0)", sheet).Should().Be(new NumberValue(8));
    }
}
