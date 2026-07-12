using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression test for R32-commands-datavalidation-enforce-3: DataValidationService's
/// Between/NotBetween/GreaterThan/LessThan/GreaterOrEqual/LessOrEqual bound comparisons used raw,
/// untolerant double comparisons, while the WholeNumber "is this an integer" pre-check
/// (IsEffectivelyWholeNumber) already tolerates ordinary floating-point noise (e.g. a formula
/// result of 10.000000000000002 is accepted as "effectively" 10). That mismatch meant a value
/// already accepted as effectively whole could still be rejected at the bound itself, because
/// 10.000000000000002 &lt;= 10 is false under a raw comparison. The fix routes these operators
/// through CompareTolerant, which shares IsEffectivelyEqual's tolerance, so an effectively-in-range
/// value passes while a genuinely out-of-range value is still rejected.
/// </summary>
public sealed class R32_DataValidationBoundToleranceTests
{
    private static DataValidation MakeWholeNumberBetweenRule(string formula1, string formula2) => new()
    {
        Type = DvType.WholeNumber,
        Operator = DvOperator.Between,
        Formula1 = formula1,
        Formula2 = formula2,
    };

    [Fact]
    public void Validate_WholeNumberBetween_AcceptsValueEffectivelyAtUpperBound()
    {
        var dv = MakeWholeNumberBetweenRule("1", "10");

        // 10.000000000000002 is "effectively" 10 (within IsEffectivelyWholeNumber's tolerance), so
        // it must not be rejected by a raw <= 10 comparison at the bound itself.
        DataValidationService.Validate(dv, new NumberValue(10.000000000000002))
            .Should().BeNull("10.000000000000002 is effectively the upper bound 10");
    }

    [Fact]
    public void Validate_WholeNumberBetween_RejectsGenuinelyOutOfRangeValue()
    {
        var dv = MakeWholeNumberBetweenRule("1", "10");

        // Sibling already-working case: a genuinely out-of-range value (11, nowhere near the
        // tolerance band around 10) must still be rejected.
        DataValidationService.Validate(dv, new NumberValue(11))
            .Should().NotBeNull("11 is genuinely above the upper bound 10");
    }

    [Fact]
    public void Validate_WholeNumberBetween_AcceptsValueEffectivelyAtLowerBound()
    {
        var dv = MakeWholeNumberBetweenRule("1", "10");

        DataValidationService.Validate(dv, new NumberValue(0.999999999999999))
            .Should().BeNull("0.999999999999999 is effectively the lower bound 1");
    }

    [Fact]
    public void Validate_WholeNumberBetween_RejectsGenuinelyBelowLowerBound()
    {
        var dv = MakeWholeNumberBetweenRule("1", "10");

        DataValidationService.Validate(dv, new NumberValue(0))
            .Should().NotBeNull("0 is genuinely below the lower bound 1");
    }

    [Fact]
    public void Validate_WholeNumberGreaterThanOrEqual_AcceptsValueEffectivelyAtBound()
    {
        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.GreaterThanOrEqual,
            Formula1 = "10",
        };

        DataValidationService.Validate(dv, new NumberValue(10.000000000000002))
            .Should().BeNull("10.000000000000002 is effectively >= 10");

        // Sibling already-working case.
        DataValidationService.Validate(dv, new NumberValue(9))
            .Should().NotBeNull("9 is genuinely below 10");
    }

    [Fact]
    public void Validate_WholeNumberLessThan_StillRejectsValueEffectivelyAtBound()
    {
        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.LessThan,
            Formula1 = "10",
        };

        // A value effectively equal to the strict bound is not "less than" it.
        DataValidationService.Validate(dv, new NumberValue(10.000000000000002))
            .Should().NotBeNull("10.000000000000002 is effectively equal to 10, not less than it");

        // Sibling already-working case: genuinely below the bound still passes.
        DataValidationService.Validate(dv, new NumberValue(9))
            .Should().BeNull("9 is genuinely less than 10");
    }
}
