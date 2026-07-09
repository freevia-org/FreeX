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
/// </summary>
public class R20_dv_wholenumber_epsilon_Tests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void WholeNumberDv_FormulaResultWithOrdinaryFpNoise_IsAccepted()
    {
        // Summing 0.1 ten times via repeated double addition does not land on exactly
        // 1.0 — it lands on 0.9999999999999999 (a classic, well-documented IEEE-754
        // artifact). Excel still accepts this as a "whole number" for DV purposes.
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=0.1+0.1+0.1+0.1+0.1+0.1+0.1+0.1+0.1+0.1", sheet);

        result.Should().BeOfType<NumberValue>();
        var noisyOne = ((NumberValue)result).Value;

        // Sanity-check the premise: the raw double value must NOT be bit-exactly 1.0,
        // otherwise this test would not be exercising the tolerance fix at all.
        noisyOne.Should().NotBe(1.0);
        Math.Abs(noisyOne - 1.0).Should().BeGreaterThan(0);

        var dv = new DataValidation { Type = DvType.WholeNumber };

        DataValidationService.Validate(dv, new NumberValue(noisyOne))
            .Should().BeNull("a formula result that is mathematically whole, modulo ordinary FP noise, must pass WholeNumber DV");
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
