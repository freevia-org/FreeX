using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression test for R20-data-validation-eval-3: WholeNumber data validation compared
/// the value against Math.Round(value) using a bit-exact double.Epsilon tolerance. Excel
/// itself is far more forgiving — a formula result that is mathematically a whole number
/// but carries the ordinary floating-point noise inherent to double arithmetic (e.g.
/// summing 0.1 ten times lands on 0.9999999999999999, not exactly 1.0) is accepted as a
/// whole number by Excel's data validation. The double.Epsilon check rejected such values,
/// since double.Epsilon is the smallest positive double (~4.9E-324) and only tolerates a
/// handful of ULPs near zero — nowhere near enough to absorb real accumulated FP noise.
///
/// DataValidationService.ValidateNumeric now uses a small absolute/relative tolerance
/// (1e-9, scaled by magnitude) instead, via the new IsEffectivelyWholeNumber helper.
///
/// Round 77 (precision-1-arithmetic-15sig) added Excel-matching 15-significant-digit rounding
/// to FreeX's own +,-,*,/,^ evaluator operators (FormulaEvaluator.Operators.cs's
/// RoundTo15SignificantDigits), so a formula evaluated *through FreeX* like the ten-term sum
/// below now lands bit-exact on 1.0, same as real Excel — it no longer reproduces the noisy
/// 0.9999999999999999 this test originally relied on to exercise the tolerance path. That's
/// the intended, correct effect of the precision fix (asserted directly below). The
/// IsEffectivelyWholeNumber tolerance itself is still a real requirement though:
/// DataValidationService.Validate accepts a bare NumberValue from any source, not only
/// FreeX's own rounded-arithmetic operators — a value pasted in, imported from an external
/// file, or produced by an aggregate function can still carry ordinary un-rounded FP noise —
/// so the noisy value below is now built with raw C# double arithmetic (bypassing FreeX's
/// evaluator entirely) instead of via _eval.Evaluate.
/// </summary>
public class R20_dv_wholenumber_epsilon_Tests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void WholeNumberDv_FormulaResultWithOrdinaryFpNoise_IsAccepted()
    {
        // Raw C# double arithmetic (NOT run through FreeX's evaluator, which now rounds to 15
        // significant digits) reproduces the classic accumulated FP-noise artifact: summing
        // 0.1 ten times lands on 0.9999999999999999, not exactly 1.0. This models a value
        // that reaches DataValidationService from a source other than FreeX's own rounded
        // arithmetic operators.
        double noisyOne = 0.1;
        for (var i = 0; i < 9; i++)
            noisyOne += 0.1;

        // Sanity-check the premise: the raw double value must NOT be bit-exactly 1.0,
        // otherwise this test would not be exercising the tolerance fix at all.
        noisyOne.Should().NotBe(1.0);
        Math.Abs(noisyOne - 1.0).Should().BeGreaterThan(0);

        var dv = new DataValidation { Type = DvType.WholeNumber };

        DataValidationService.Validate(dv, new NumberValue(noisyOne))
            .Should().BeNull("a formula result that is mathematically whole, modulo ordinary FP noise, must pass WholeNumber DV");
    }

    [Fact]
    public void WholeNumberDv_FreeXEvaluatedNoiseProneFormula_NowRoundsToBitExactWholeNumber()
    {
        // Round 77: the exact formula the test above used to rely on for its FP-noise
        // premise now evaluates bit-exact through FreeX's own operators, matching Excel's
        // 15-significant-digit rounding of arithmetic results. Covered in depth by
        // R77_ArithmeticResult15SigRoundingTests; asserted here too since it directly
        // documents why the sibling test above had to change its noise source.
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=0.1+0.1+0.1+0.1+0.1+0.1+0.1+0.1+0.1+0.1", sheet);

        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().Be(1.0, "Excel rounds arithmetic results to 15 significant digits");
    }

    [Fact]
    public void WholeNumberDv_GenuineHalfValue_IsStillRejected()
    {
        var dv = new DataValidation { Type = DvType.WholeNumber };

        DataValidationService.Validate(dv, new NumberValue(5.5))
            .Should().NotBeNull("5.5 is genuinely not a whole number and must still fail WholeNumber DV");
    }

    [Fact]
    public void WholeNumberDv_ExactIntegerValue_IsAccepted()
    {
        var dv = new DataValidation { Type = DvType.WholeNumber };

        DataValidationService.Validate(dv, new NumberValue(5.0))
            .Should().BeNull();
    }

    private static Sheet MakeSheet()
    {
        return new Sheet(SheetId.New(), "S");
    }
}
