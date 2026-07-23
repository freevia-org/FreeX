using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression test for R27-data-validation-eval-deep-2: WholeNumber/Decimal data validation
/// with the Equal or NotEqual operator compared the candidate value against its Formula1
/// bound using bit-exact double equality, even though the WholeNumber "is this a whole
/// number" gate one line earlier (IsEffectivelyWholeNumber) already tolerates the ordinary
/// floating-point noise inherent to double arithmetic (e.g. summing 0.1 ten times lands on
/// 0.9999999999999999, not exactly 1.0). A value that had just been accepted as "effectively
/// whole" could still be wrongly rejected as "not equal to 1" by the raw == comparison.
///
/// DataValidationService.ValidateNumeric now uses IsEffectivelyEqual (the same tolerance,
/// generalized to two arbitrary values) for the Equal/NotEqual branches, matching the way
/// Between/NotBetween/GreaterThan/etc. are already inherently tolerant via >=/<=.
///
/// Round 77 (precision-1-arithmetic-15sig) added Excel-matching 15-significant-digit rounding
/// to FreeX's own +,-,*,/,^ evaluator operators, so formulas like the ten-term 0.1 sum and
/// "=0.1+0.2" below now evaluate bit-exact through FreeX (matching real Excel) instead of
/// reproducing the raw IEEE-754 noise these tests originally relied on. That's the intended,
/// correct effect of the fix. The IsEffectivelyEqual/IsEffectivelyWholeNumber tolerance these
/// tests exist to cover is still a real requirement for values that reach DataValidationService
/// from anywhere other than FreeX's own rounded arithmetic operators, so the noisy values below
/// are now built with raw C# double arithmetic (bypassing FreeX's evaluator/rounding entirely)
/// instead of via _eval.Evaluate.
/// </summary>
public class R27_DvEqualOperatorToleranceTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void WholeNumberDv_Equal_FormulaResultWithOrdinaryFpNoise_IsAccepted()
    {
        // Raw C# double arithmetic (NOT run through FreeX's now-rounding evaluator)
        // reproduces the classic accumulated FP-noise artifact documented by
        // R20_DvWholeNumberToleranceTests: this sum does not land on bit-exact 1.0.
        double noisyOne = 0.1;
        for (var i = 0; i < 9; i++)
            noisyOne += 0.1;
        noisyOne.Should().NotBe(1.0, "the test must exercise real FP noise, not a coincidentally exact sum");

        var dv = new DataValidation { Type = DvType.WholeNumber, Operator = DvOperator.Equal, Formula1 = "1" };

        DataValidationService.Validate(dv, new NumberValue(noisyOne))
            .Should().BeNull("a value already accepted as effectively whole must not then be rejected as unequal to its own bound");
    }

    [Fact]
    public void WholeNumberDv_NotEqual_FormulaResultWithOrdinaryFpNoise_IsRejected()
    {
        double noisyOne = 0.1;
        for (var i = 0; i < 9; i++)
            noisyOne += 0.1;

        var dv = new DataValidation { Type = DvType.WholeNumber, Operator = DvOperator.NotEqual, Formula1 = "1" };

        DataValidationService.Validate(dv, new NumberValue(noisyOne))
            .Should().NotBeNull("the noisy value is effectively equal to 1, so NotEqual-to-1 must fail it, consistently with Equal accepting it");
    }

    [Fact]
    public void DecimalDv_Equal_FormulaResultWithOrdinaryFpNoise_IsAccepted()
    {
        // 0.1 + 0.2 famously evaluates to 0.30000000000000004 in raw IEEE-754 double
        // arithmetic, not bit-exact 0.3. The finding calls out this exact class of noise for
        // Decimal-type Equal/NotEqual bounds too. Computed directly in C# (not via
        // _eval.Evaluate) since FreeX's own "+" operator now rounds this exact sum to
        // bit-exact 0.3, matching Excel (see DecimalAddition_FreeXEvaluated_NowRoundsToBitExactValue below).
        double noisyPointThree = 0.1 + 0.2;
        noisyPointThree.Should().NotBe(0.3);

        var dv = new DataValidation { Type = DvType.Decimal, Operator = DvOperator.Equal, Formula1 = "0.3" };

        DataValidationService.Validate(dv, new NumberValue(noisyPointThree))
            .Should().BeNull("0.1 + 0.2 is effectively 0.3 modulo ordinary FP noise");
    }

    [Fact]
    public void DecimalAddition_FreeXEvaluated_NowRoundsToBitExactValue()
    {
        // Round 77: the exact formula the test above used to rely on for its FP-noise premise
        // now evaluates bit-exact through FreeX's own "+" operator, matching Excel's
        // 15-significant-digit rounding of arithmetic results. Covered in depth by
        // R77_ArithmeticResult15SigRoundingTests; asserted here too since it directly
        // documents why the sibling test above had to change its noise source.
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=0.1+0.2", sheet);

        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().Be(0.3, "Excel rounds arithmetic results to 15 significant digits");
    }

    [Fact]
    public void WholeNumberDv_Equal_GenuinelyDifferentValue_IsStillRejected()
    {
        // Sibling case that must keep working: a value that is genuinely not equal to the
        // bound (not just FP noise near it) must still fail.
        var dv = new DataValidation { Type = DvType.WholeNumber, Operator = DvOperator.Equal, Formula1 = "1" };

        DataValidationService.Validate(dv, new NumberValue(2.0))
            .Should().NotBeNull("2 is genuinely not equal to 1 and must still fail Equal-to-1 DV");
    }

    private static Sheet MakeSheet()
    {
        return new Sheet(SheetId.New(), "S");
    }
}
